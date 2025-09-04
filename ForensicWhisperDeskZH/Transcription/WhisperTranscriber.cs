using ForensicWhisperDeskZH.Common;
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
        public WhisperTranscriber(TranscriptionSettings settings)
        {
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
                RuntimeLibrary.Cpu
            };

            InitializeWhisperFactory();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Transcribes an audio file and returns the result
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file</param>
        /// <param name="sessionId">Session ID for tracking</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Transcription result</returns>
        public async Task<TranscriptionResult> TranscribeAudioFileAsync(string audioFilePath, string sessionId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                var resultSegments = new List<TranscriptionSegment>();
                var segmentTexts = new List<string>();

                using (var fileStream = File.OpenRead(audioFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Reading WAV file for processing - Size: {fileStream.Length} bytes");
                    System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Starting Whisper processing...");

                    await foreach (var segment in _transcriptor.ProcessAsync(fileStream, cancellationToken))
                    {
                        System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Received segment: '{segment.Text}' ({segment.Start} - {segment.End})");

                        if (string.IsNullOrWhiteSpace(segment.Text))
                        {
                            System.Diagnostics.Debug.WriteLine("WhisperTranscriber: Empty segment received from Whisper");
                            continue;
                        }

                        // Process the text
                        string processedText = _textProcessor.ProcessTranscribedText(segment.Text);
                        segmentTexts.Add(processedText);

                        // Create result segment
                        resultSegments.Add(new TranscriptionSegment(
                            processedText,
                            segment.Start,
                            segment.End,
                            sessionId));
                    }
                }

                string fullText = string.Join(" ", segmentTexts);
                System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Completed processing - Full text: '{fullText}'");

                return new TranscriptionResult(fullText, fullText, resultSegments);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WhisperTranscriber: Error transcribing audio: {ex.Message}");
                OnTranscriptionError(new ErrorEventArgs(ex));
                throw new InvalidOperationException("Failed to transcribe audio file", ex);
            }
        }

        /// <summary>
        /// Changes the model type used for transcription
        /// </summary>
        /// <param name="modelType">New model type</param>
        public void ChangeModelType(GgmlType modelType)
        {
            ThrowIfDisposed();

            if (_transcriptor != null && modelType != default(GgmlType))
            {
                _settings.ModelType = modelType;
                InitializeWhisperFactory();
                _transcriptor = CreateWhisperProcessor();
            }
        }

        /// <summary>
        /// Changes the language used for transcription
        /// </summary>
        /// <param name="language">Language code (e.g., "en", "de")</param>
        public void ChangeLanguage(string language)
        {
            ThrowIfDisposed();

            if (_transcriptor != null && !string.IsNullOrEmpty(language))
            {
                _settings.Language = language;
                _transcriptor.ChangeLanguage(language);
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
                LoggingService.LogMessage($"WhisperTranscriber: Using Whisper model path: {whisperModelPath}", "WhisperTranscriber_init");

                string modelPath = _modelManager.EnsureModelExistsAsync(
                    whisperModelPath,
                    _settings.ModelType == default(GgmlType) ? GgmlType.Base : _settings.ModelType
                ).Result;

                LoggingService.LogMessage($"WhisperTranscriber: Model path resolved to: {modelPath}", "WhisperTranscriber_init");
                _transcriptorFactory = _modelManager.CreateFactory(modelPath);

                // Create builder and processor
                _transcriptorBuilder = _transcriptorFactory.CreateBuilder();
                _transcriptor = CreateWhisperProcessor();
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
            _transcriptorBuilder
                .WithDuration(_settings.ChunkDuration)
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

        private void OnTranscriptionError(ErrorEventArgs e)
        {
            TranscriptionError?.Invoke(this, e);
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
            if (!_isDisposed)
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

                _isDisposed = true;
            }
        }
        #endregion
    }
}