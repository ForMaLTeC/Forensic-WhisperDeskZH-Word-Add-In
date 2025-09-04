using ForensicWhisperDeskZH.Audio;
using ForensicWhisperDeskZH.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Service that handles audio transcription using Whisper models
    /// Orchestrates the transcription process by coordinating audio processing and Whisper transcription
    /// </summary>
    internal class TranscriptionService : ITranscriptionService
    {
        #region Fields
        private readonly AudioProcessor _audioProcessor;
        private readonly WhisperTranscriber _whisperTranscriber;
        private TranscriptionSettings _settings;
        private CancellationTokenSource _cancellationTokenSource;
        private StringBuilder _fullTranscriptBuilder = new StringBuilder();
        
        private readonly Queue<Task<TranscriptionResult>> _transcriptionTasks = new Queue<Task<TranscriptionResult>>();
        private readonly object _taskLock = new object();
        private int _errorCount = 0;
        private const int MaxConsecutiveErrors = 3;
        
        private bool _isTranscribing = false;
        private bool _isDisposed = false;
        private string _sessionId;
        #endregion

        #region Events
                public event EventHandler<TranscriptionEventArgs> TranscriptionStarted;
        public event EventHandler<TranscriptionEventArgs> TranscriptionStopped;
        public event EventHandler<TranscriptionResultEventArgs> TranscriptionResult;
        public event EventHandler<ErrorEventArgs> TranscriptionError;
        #endregion

        #region Properties
        public bool IsTranscribing => _isTranscribing;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new transcription service
        /// </summary>
        /// <param name="settings">Transcription settings</param>
        public TranscriptionService(TranscriptionSettings settings = null)
        {
            try
            {
                _settings = settings ?? TranscriptionSettings.Default;
                
                // Create the specialized components
                _audioProcessor = new AudioProcessor(_settings);
                _whisperTranscriber = new WhisperTranscriber(_settings);
                
                // Wire up events
                _audioProcessor.AudioChunkReady += OnAudioChunkReady;
                _audioProcessor.AudioError += OnAudioError;
                _whisperTranscriber.TranscriptionError += OnWhisperError;
                
                LoggingService.LogMessage("TranscriptionService: Initialized successfully", "TranscriptionService_init");
            }
            catch (Exception ex)
            {
                OnTranscriptionError(new ErrorEventArgs(ex));
                throw;
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts transcription from the specified microphone
        /// </summary>
        public void StartTranscription(Action<string> textHandler, int deviceNumber = 0, CultureInfo language = null)
        {
            ThrowIfDisposed();

            if (_isTranscribing)
                return;

            try
            {
                LoggingService.PlayTranscriptionStateChangeSound();

                LoggingService.LogMessage("TranscriptionService: Starting transcription", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using device number {deviceNumber}", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using language '{language?.Name ?? _settings.Language}'", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using model type '{_settings.ModelType}'", "TranscriptionService_StartTranscription");

                _isTranscribing = true;
                _errorCount = 0;
                _sessionId = Guid.NewGuid().ToString();

                // Change language if specified
                if (language != null && language.Name != _settings.Language)
                {
                    ChangeLanguage(language.Name);
                }

                _cancellationTokenSource = new CancellationTokenSource();

                // Notify listeners that transcription started
                OnTranscriptionStarted(new TranscriptionEventArgs(_sessionId, string.Empty));

                // Start the background task to process completed transcriptions
                Task.Run(() => ProcessCompletedTranscriptionsAsync(textHandler, _cancellationTokenSource.Token));

                // Start audio processing
                _audioProcessor.StartCapture(deviceNumber);
            }
            catch (Exception ex)
            {
                _isTranscribing = false;
                OnTranscriptionError(new ErrorEventArgs(ex));
            }
        }

        /// <summary>
        /// Stops the active transcription
        /// </summary>
        public void StopTranscription()
        {
            Task.Run(async () => await StopTranscriptionInternalAsync());
            LoggingService.PlayTranscriptionStateChangeSound();
            _audioProcessor.CleanTempDirectory();
        }

        /// <summary>
        /// Toggles transcription on or off
        /// </summary>
        public void ToggleTranscription(Action<string> textHandler, TranscriptionSettings transcriptionSettings, int deviceNumber = 0)
        {
            if (_isTranscribing)
            {
                StopTranscription();
            }
            else
            {
                _settings = transcriptionSettings ?? TranscriptionSettings.Default;
                StartTranscription(textHandler, deviceNumber);
            }
        }

        /// <summary>
        /// Changes the language used for transcription
        /// </summary>
        public void ChangeLanguage(string language)
        {
            ThrowIfDisposed();
            _whisperTranscriber.ChangeLanguage(language);
        }

        /// <summary>
        /// Changes the model type used for transcription
        /// </summary>
        public void ChangeModelType(GgmlType modelType)
        {
            ThrowIfDisposed();
            _whisperTranscriber.ChangeModelType(modelType);
        }

        /// <summary>
        /// Changes the silence threshold
        /// </summary>
        public void ChangeSilenceThreshold(int silenceThreshold)
        {
            ThrowIfDisposed();
            _settings.SilenceThreshold = TimeSpan.FromSeconds(silenceThreshold);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Handles audio chunks ready for transcription
        /// </summary>
        private void OnAudioChunkReady(object sender, ProcessedAudioEventArgs e)
        {
            if (_cancellationTokenSource?.IsCancellationRequested == true)
                return;

            try
            {
                var transcriptionTask = TranscribeChunkAsync(
                    e.AudioData,
                    _sessionId,
                    _cancellationTokenSource?.Token ?? CancellationToken.None);

                lock (_taskLock)
                {
                    _transcriptionTasks.Enqueue(transcriptionTask);
                }
            }
            catch (Exception ex)
            {
                HandleTranscriptionError(ex);
            }
        }

        /// <summary>
        /// Transcribes an audio chunk
        /// </summary>
        private async Task<TranscriptionResult> TranscribeChunkAsync(MemoryStream audioBuffer, string sessionId, CancellationToken cancellationToken)
        {
            // Validate input buffer
            if (audioBuffer == null || audioBuffer.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("TranscriptionService: Empty or null audio buffer received");
                return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
            }

            System.Diagnostics.Debug.WriteLine($"TranscriptionService: Processing audio buffer with {audioBuffer.Length} bytes");

            // Create a temporary file for the WAV data
            string tempFile = Path.Combine(Path.GetTempPath(), $"WhisperDesk_audio_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");

            try
            {
                _audioProcessor.SaveAudioChunkToFile(tempFile, audioBuffer);

                // Validate the created WAV file
                var fileInfo = new FileInfo(tempFile);
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: WAV file created - Size: {fileInfo.Length} bytes");

                if (fileInfo.Length < 100)
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: WARNING - WAV file is very small, may be empty!");
                    return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
                }

                if (!_audioProcessor.CheckWavFileForVoice(tempFile))
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: INFORMATION - WAV File does not contain a voice");
                    return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
                }

                // Transcribe using Whisper
                var result = await _whisperTranscriber.TranscribeAudioFileAsync(tempFile, sessionId, cancellationToken);

                if (result.Segments.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: WARNING - No segments returned from Whisper!");
                    _whisperTranscriber.DiagnoseWhisperIssue(tempFile);
                }

                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Completed processing - Full text: '{result.FullText}'");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error processing audio: {ex.Message}");
                throw new InvalidOperationException("Failed to transcribe audio buffer", ex);
            }
            finally
            {
                // Keep temp file for debugging
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Temp file saved for debugging: {tempFile}");
            }
        }

        /// <summary>
        /// Processes completed transcription tasks
        /// </summary>
        private async Task ProcessCompletedTranscriptionsAsync(Action<string> textHandler, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task<TranscriptionResult> currentTask = null;

                lock (_taskLock)
                {
                    if (_transcriptionTasks.Count > 0)
                    {
                        currentTask = _transcriptionTasks.Peek();
                    }
                }

                if (currentTask != null)
                {
                    try
                    {
                        // Wait for this task to complete
                        var result = await currentTask;

                        // Remove the task from the queue
                        lock (_taskLock)
                        {
                            _transcriptionTasks.Dequeue();
                        }

                        // Process the results
                        if (!string.IsNullOrWhiteSpace(result.FullText))
                        {
                            // Add to full transcript
                            _fullTranscriptBuilder.Append(result.FullText + " ");

                            // Send text to callback
                            textHandler?.Invoke(result.IncrementalText);

                            // Raise events for each segment
                            foreach (var segment in result.Segments)
                            {
                                OnTranscriptionResult(new TranscriptionResultEventArgs(
                                    segment.Text,
                                    segment.Start,
                                    segment.End));
                            }
                        }

                        // Reset error count on success
                        _errorCount = 0;
                    }
                    catch (Exception ex)
                    {
                        // Remove the failed task
                        lock (_taskLock)
                        {
                            _transcriptionTasks.Dequeue();
                        }

                        HandleTranscriptionError(ex);
                    }
                }
                else
                {
                    // No tasks to process right now, wait a bit
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Internal implementation of stop transcription
        /// </summary>
        private async Task StopTranscriptionInternalAsync()
        {
            if (!_isTranscribing)
                return;

            _isTranscribing = false;

            try
            {
                // Cancel ongoing operations
                _cancellationTokenSource?.Cancel();

                // Stop audio processing
                _audioProcessor.StopCapture();

                // Wait for pending tasks with timeout
                var pendingTasks = new List<Task<TranscriptionResult>>();
                lock (_taskLock)
                {
                    while (_transcriptionTasks.Count > 0)
                    {
                        pendingTasks.Add(_transcriptionTasks.Dequeue());
                    }
                }

                if (pendingTasks.Count > 0)
                {
                    try
                    {
                        var allTasksCompletion = Task.WhenAll(pendingTasks);
                        var timeout = Task.Delay(3000);
                        var completedTask = await Task.WhenAny(allTasksCompletion, timeout);

                        if (completedTask == timeout)
                        {
                            System.Diagnostics.Debug.WriteLine("TranscriptionService: Timeout waiting for tasks to complete");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error waiting for tasks: {ex.Message}");
                    }
                }

                // Notify that transcription has stopped
                OnTranscriptionStopped(new TranscriptionEventArgs(_sessionId, _fullTranscriptBuilder.ToString()));
            }
            catch (Exception ex)
            {
                OnTranscriptionError(new ErrorEventArgs(ex));
            }
            finally
            {
                // Reset state
                _fullTranscriptBuilder.Clear();
                
                try
                {
                    _cancellationTokenSource?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error disposing cancellation token: {ex.Message}");
                }
                finally
                {
                    _cancellationTokenSource = null;
                }
            }
        }

        private void OnAudioError(object sender, ErrorEventArgs e)
        {
            OnTranscriptionError(e);
        }

        private void OnWhisperError(object sender, ErrorEventArgs e)
        {
            OnTranscriptionError(e);
        }

        private void HandleTranscriptionError(Exception ex)
        {
            _errorCount++;
            OnTranscriptionError(new ErrorEventArgs(ex));

            // If we have too many consecutive errors, stop transcription
            if (_errorCount >= MaxConsecutiveErrors)
            {
                StopTranscription();
            }
        }

        // Event raising methods
        protected virtual void OnTranscriptionStarted(TranscriptionEventArgs e)
        {
            TranscriptionStarted?.Invoke(this, e);
        }

        protected virtual void OnTranscriptionStopped(TranscriptionEventArgs e)
        {
            TranscriptionStopped?.Invoke(this, e);
        }

        protected virtual void OnTranscriptionResult(TranscriptionResultEventArgs e)
        {
            TranscriptionResult?.Invoke(this, e);
        }

        protected virtual void OnTranscriptionError(ErrorEventArgs e)
        {
            TranscriptionError?.Invoke(this, e);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TranscriptionService));
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (!_isDisposed)
            {
                StopTranscription();

                _audioProcessor?.Dispose();
                _whisperTranscriber?.Dispose();

                _isDisposed = true;
            }
        }
        #endregion
    }
}