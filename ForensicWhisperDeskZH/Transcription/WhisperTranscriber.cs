using ForensicWhisperDeskZH.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Handles Whisper-specific transcription operations
    /// </summary>
    internal class WhisperTranscriber : IDisposable
    {
        #region Fields
        private readonly WhisperModelManager _modelManager;
        private readonly TranscriptionSettings _settings;
        private readonly TextProcessor _textProcessor;
        private readonly WaveFormat _waveFormat;
        private WhisperFactory _transcriptorFactory;
        private WhisperProcessor _transcriptor;
        private WhisperProcessorBuilder _transcriptorBuilder;
        private bool _isDisposed = false;
        private readonly object _disposeLock = new object();
        private readonly object _processorLock = new object();
        private volatile bool _isTranscribing = false;

        private string fullText = "";
        private string incrementalText = "";
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a transcription error occurs
        /// </summary>
        public event EventHandler<ErrorEventArgs> TranscriptionError;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new Whisper transcriber with the specified settings
        /// </summary>
        /// <param name="settings">Transcription settings</param>
        public WhisperTranscriber(TranscriptionSettings settings, TranscriptionService transcriptionService = null)
        {

            if (transcriptionService != null)
            {
                transcriptionService.TranscriptionStopped += (s, e) => ResetFullText();
                transcriptionService.TranscriptionStarted += (s, e) => ResetFullText();
            }
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _waveFormat = _settings.WaveFormat;
            _textProcessor = new TextProcessor(_settings);
            _modelManager = new WhisperModelManager();

            // Configure runtime libraries order
            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
            {
                RuntimeLibrary.OpenVino,  // Intel hardware
                RuntimeLibrary.Cuda,      // NVIDIA GPUs
                RuntimeLibrary.Vulkan,    // Generic GPU acceleration
                RuntimeLibrary.Cpu,
            };

            InitializeWhisperFactory();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Transcribes an audio file and returns the result
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transcription result</returns>
        public async Task<TranscriptionResult> TranscribeAudioFileAsync(string audioFilePath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // Use a semaphore-like approach with the existing _processorLock to ensure only one transcription happens at a time
            // This prevents concurrent access to Whisper.net objects which are not thread-safe
            TranscriptionResult result;
            
            lock (_processorLock)
            {
                ThrowIfDisposed(); // Check again inside the lock
                result = TranscribeAudioFileInternal(audioFilePath, cancellationToken);
            }

            return result;
        }

        /// <summary>
        /// Internal synchronous transcription method that runs within the lock
        /// </summary>
        private TranscriptionResult TranscribeAudioFileInternal(string audioFilePath, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            try
            {
                _isTranscribing = true;
                var resultSegments = new List<TranscriptionSegment>();
                var segmentTexts = new List<string>();
                
                WhisperProcessor transcriptor = null;
                transcriptor = CreateWhisperProcessor(incrementalText);

                if (transcriptor == null)
                {
                    throw new InvalidOperationException("Failed to create WhisperProcessor");
                }

                try
                {
                    using (var fileStream = File.OpenRead(audioFilePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Reading WAV file for processing - Size: {fileStream.Length} bytes");
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Starting Whisper processing...");

                        // Use synchronous enumeration to avoid async within lock
                        var segments = new List<Whisper.net.SegmentData>();
                        
                        // We need to process this synchronously within the lock
                        // Create a task and wait for it synchronously to maintain thread safety
                        var transcriptionTask = Task.Run(async () =>
                        {
                            var tempSegments = new List<Whisper.net.SegmentData>();
                            await foreach (var segment in transcriptor.ProcessAsync(fileStream, cancellationToken))
                            {
                                tempSegments.Add(segment);
                            }
                            return tempSegments;
                        });

                        // Wait synchronously for the task to complete within the lock
                        segments = transcriptionTask.GetAwaiter().GetResult();

                        foreach (var segment in segments)
                        {
                            // Check for cancellation and disposal frequently
                            cancellationToken.ThrowIfCancellationRequested();
                            ThrowIfDisposed();

                            System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Received segment: '{segment.Text}' ({segment.Start} - {segment.End})");

                            if (string.IsNullOrWhiteSpace(segment.Text))
                            {
                                System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Empty segment received from Whisper");
                                continue;
                            }

                            // Process the text
                            string processedText = _textProcessor.ProcessTranscribedText(segment.Text);
                            segmentTexts.Add(processedText);

                            // Create result segment with empty sessionId since it's not passed to this method
                            resultSegments.Add(new TranscriptionSegment(
                                processedText,
                                segment.Start,
                                segment.End,
                                string.Empty));
                        }
                    }
                }
                finally
                {
                    // Always dispose the processor when done to free GPU resources
                    try
                    {
                        transcriptor?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing transcriptor after use: {ex.Message}");
                    }
                }

                incrementalText = string.Join(" ", segmentTexts);
                fullText = string.Join(incrementalText, segmentTexts);
                System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Completed processing - Full text: '{fullText}'");

                return new TranscriptionResult(fullText, fullText, resultSegments);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Transcription was cancelled");
                return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
            }
            catch (ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Object was disposed during transcription");
                return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error transcribing audio: {ex.Message}");
                OnTranscriptionError(new ErrorEventArgs(ex));
                throw new InvalidOperationException("Failed to transcribe audio file", ex);
            }
            finally
            {
                _isTranscribing = false;
            }
        }

        /// <summary>
        /// Changes the model type used for transcription
        /// </summary>
        /// <param name="modelType">New model type</param>
        public void ChangeModelType(GgmlType modelType)
        {
            ThrowIfDisposed();

            if (modelType != default(GgmlType))
            {
                lock (_processorLock)
                {
                    ThrowIfDisposed(); // Check again inside the lock
                    
                    // Wait for any ongoing transcription to complete
                    while (_isTranscribing)
                    {
                        Thread.Sleep(50);
                        ThrowIfDisposed();
                    }

                    _settings.ModelType = modelType;
                    
                    // Dispose existing processor safely
                    try
                    {
                        _transcriptor?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing old processor: {ex.Message}");
                    }
                    finally
                    {
                        _transcriptor = null;
                    }
                    
                    InitializeWhisperFactory();
                }
            }
        }

        /// <summary>
        /// Changes the language used for transcription
        /// </summary>
        /// <param name="language">Language code (e.g., "en", "de")</param>
        public void ChangeLanguage(string language)
        {
            ThrowIfDisposed();

            if (!string.IsNullOrEmpty(language))
            {
                lock (_processorLock)
                {
                    ThrowIfDisposed(); // Check again inside the lock
                    
                    // Wait for any ongoing transcription to complete
                    while (_isTranscribing)
                    {
                        Thread.Sleep(50);
                        ThrowIfDisposed();
                    }
                    
                    _settings.Language = language;
                    
                    // Dispose existing processor safely
                    try
                    {
                        _transcriptor?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing processor for language change: {ex.Message}");
                    }
                    finally
                    {
                        _transcriptor = null;
                    }
                }
            }
        }

        /// <summary>
        /// Diagnoses potential Whisper processing issues with an audio file
        /// </summary>
        /// <param name="tempFile">Path to the audio file to diagnose</param>
        public void DiagnoseWhisperIssue(string tempFile)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== WHISPER DIAGNOSTIC ===");

                // Check file size and duration
                var fileInfo = new FileInfo(tempFile);
                System.Diagnostics.Debug.WriteLine($"File size: {fileInfo.Length} bytes");

                using (var reader = new WaveFileReader(tempFile))
                {
                    System.Diagnostics.Debug.WriteLine($"WAV Format: {reader.WaveFormat}");
                    System.Diagnostics.Debug.WriteLine($"Duration: {reader.TotalTime}");
                    System.Diagnostics.Debug.WriteLine($"Sample count: {reader.SampleCount}");

                    // Check if duration is too short
                    if (reader.TotalTime.TotalSeconds < 0.1)
                    {
                        System.Diagnostics.Debug.WriteLine("ISSUE: Audio duration is too short for Whisper processing!");
                    }

                    // Check if format matches expected
                    if (reader.WaveFormat.SampleRate != _waveFormat.SampleRate)
                    {
                        System.Diagnostics.Debug.WriteLine($"ISSUE: Sample rate mismatch! Expected: {_waveFormat.SampleRate}, Got: {reader.WaveFormat.SampleRate}");
                    }

                    if (reader.WaveFormat.Channels != _waveFormat.Channels)
                    {
                        System.Diagnostics.Debug.WriteLine($"ISSUE: Channel count mismatch! Expected: {_waveFormat.Channels}, Got: {reader.WaveFormat.Channels}");
                    }

                    if (reader.WaveFormat.BitsPerSample != _waveFormat.BitsPerSample)
                    {
                        System.Diagnostics.Debug.WriteLine($"ISSUE: Bits per sample mismatch! Expected: {_waveFormat.BitsPerSample}, Got: {reader.WaveFormat.BitsPerSample}");
                    }
                }

                // Check transcriptor configuration
                System.Diagnostics.Debug.WriteLine($"Whisper Settings:");
                System.Diagnostics.Debug.WriteLine($"  Language: {_settings.Language}");
                System.Diagnostics.Debug.WriteLine($"  Model Type: {_settings.ModelType}");
                System.Diagnostics.Debug.WriteLine($"  No Speech Threshold: 0.6f");
                System.Diagnostics.Debug.WriteLine($"  Temperature: {_settings.Temperature}");

                System.Diagnostics.Debug.WriteLine("=== END DIAGNOSTIC ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Whisper diagnosis: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Initializes the Whisper factory and model loading
        /// </summary>
        private void InitializeWhisperFactory()
        {
            try
            {
                string whisperModelPath = $"ggml-{_settings.ModelType.ToString().ToLower()}.bin";
                LoggingService.LogMessage($"WhisperTranscriber: Using Whisper model path: {whisperModelPath}", "WhisperTranscriber_init", true);

                string modelPath = _modelManager.EnsureModelExistsAsync(
                    whisperModelPath,
                    _settings.ModelType == default(GgmlType) ? GgmlType.Base : _settings.ModelType
                ).Result;

                LoggingService.LogMessage($"WhisperTranscriber: Model path resolved to: {modelPath}", "WhisperTranscriber_init");
                
                // Dispose old factory if exists
                try
                {
                    _transcriptorFactory?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing old factory: {ex.Message}");
                }
                
                _transcriptorFactory = _modelManager.CreateFactory(modelPath);

                // Create builder and processor
                _transcriptorBuilder = _transcriptorFactory.CreateBuilder();
            }
            catch (Exception ex)
            {
                OnTranscriptionError(new ErrorEventArgs(ex));
                throw;
            }
        }

        /// <summary>
        /// Creates a configured WhisperProcessor based on settings
        /// </summary>
        /// <param name="lastTranscribedText">Optional prompt text from previous transcription</param>
        /// <returns>Configured WhisperProcessor</returns>
        private WhisperProcessor CreateWhisperProcessor(string lastTranscribedText = null)
        {
            ThrowIfDisposed();
            
            if (_transcriptorBuilder == null)
            {
                throw new InvalidOperationException("WhisperProcessor builder not initialized");
            }

            try
            {
                _transcriptorBuilder
                    .WithDuration(_settings.minChunkDuration)
                    .WithThreads(_settings.Threads)
                    .WithLanguage(_settings.Language)
                    .WithPrintProgress()
                    .WithPrintResults();

                if (_settings.TranslateToEnglish)
                {
                    _transcriptorBuilder.WithTranslate();
                }

                if (_settings.Temperature > 0)
                {
                    _transcriptorBuilder.WithTemperature(_settings.Temperature);
                }

                if (lastTranscribedText != null)
                {
                    _transcriptorBuilder.WithPrompt(lastTranscribedText);
                }

                // Configure sampling strategy
                if (_settings.UseGreedyStrategy)
                {
                    _transcriptorBuilder.WithGreedySamplingStrategy();
                }
                else
                {
                    var beamSearchBuilder = (BeamSearchSamplingStrategyBuilder)_transcriptorBuilder.WithBeamSearchSamplingStrategy();
                    beamSearchBuilder.WithBeamSize(_settings.BeamSize);
                }

                // Add additional options for better quality
                _transcriptorBuilder
                    .WithNoSpeechThreshold(0.6f)
                    .WithTokenTimestamps()
                    .WithProbabilities();

                return _transcriptorBuilder.Build();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error creating processor: {ex.Message}");
                throw;
            }
        }

        private void OnTranscriptionError(ErrorEventArgs e)
        {
            TranscriptionError?.Invoke(this, e);
        }

        public void ResetFullText()
        {
            fullText = "";
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(WhisperTranscriber));
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (!_isDisposed)
                {
                    System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Starting disposal...");
                    _isDisposed = true;

                    // Wait for any ongoing transcription to complete with timeout
                    int waitTimeMs = 0;
                    const int maxWaitMs = 5000; // 5 seconds max wait
                    while (_isTranscribing && waitTimeMs < maxWaitMs)
                    {
                        Thread.Sleep(100);
                        waitTimeMs += 100;
                    }

                    if (_isTranscribing)
                    {
                        System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Forced disposal while transcription was active");
                    }

                    lock (_processorLock)
                    {
                        try
                        {
                            _transcriptor?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing transcriptor: {ex.Message}");
                        }
                        finally
                        {
                            _transcriptor = null;
                        }

                        try
                        {
                            _transcriptorFactory?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error disposing factory: {ex.Message}");
                        }
                        finally
                        {
                            _transcriptorFactory = null;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Disposal completed");
                }
            }
        }
        #endregion
    }
}