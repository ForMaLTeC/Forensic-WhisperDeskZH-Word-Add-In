using ForensicWhisperDeskZH.Audio;
using ForensicWhisperDeskZH.Common;
using ForensicWhisperDeskZH.Transcription;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebRtcVadSharp;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Service that handles audio transcription using Whisper models
    /// </summary>
    internal class TranscriptionService : ITranscriptionService
    {
        private readonly WhisperModelManager _modelManager;
        private static WhisperFactory _transcriptorFactory;
        private static TranscriptionSettings _settings;
        private readonly WaveFormat _waveFormat;
        private readonly TextProcessor _textProcessor;

        private IAudioCapture _audioCapture;
        private AudioBufferProcessor _audioProcessor;
        private WhisperProcessor _transcriptor;
        private WhisperProcessorBuilder _transcriptorBuilder;
        private CancellationTokenSource _cancellationTokenSource;
        private StringBuilder _fullTranscriptBuilder = new StringBuilder();

        private readonly Queue<Task<TranscriptionResult>> _transcriptionTasks = new Queue<Task<TranscriptionResult>>();
        private readonly object _taskLock = new object();
        private int _errorCount = 0;
        private const int MaxConsecutiveErrors = 3;

        private bool _isTranscribing = false;
        private bool _isDisposed = false;
        private string _sessionId;

        // Events for notifying clients about transcription events
        public event EventHandler<TranscriptionEventArgs> TranscriptionStarted;
        public event EventHandler<TranscriptionEventArgs> TranscriptionStopped;
        public event EventHandler<TranscriptionResultEventArgs> TranscriptionResult;
        public event EventHandler<ErrorEventArgs> TranscriptionError; // Fix: Ensure the event matches the interface definition

        /// <summary>
        /// Creates a new instance of the transcription service
        /// </summary>
        public TranscriptionService(TranscriptionSettings settings = null)
        {
            try
            {
                // Initialize settings
                _settings = settings ?? TranscriptionSettings.Default;
                _waveFormat = _settings.WaveFormat; // 16kHz, 16-bit, mono
                _textProcessor = new TextProcessor(_settings);

                // Configure runtime libraries order
                RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary>
                    {
                        RuntimeLibrary.OpenVino,  // Intel hardware
                        RuntimeLibrary.Cuda,      // NVIDIA GPUs
                        RuntimeLibrary.Vulkan,    // Generic GPU acceleration
                        RuntimeLibrary.Cpu
                    };

                // Initialize model and factory
                _modelManager = new WhisperModelManager();

                // if in debug mode, download all models
#if DEBUG
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
                _modelManager.DownloadAllModelsAsync().Wait();
#endif
                //CreateWhisperFactory();

            }
            catch (Exception ex)
            {
                OnTranscriptionError(new ErrorEventArgs(ex));
                throw;
            }
        }

        private void CreateWhisperFactory()
        {
            try
            {
                string whisperModelPath = $"ggml-{_settings.ModelType.ToString().ToLower()}.bin";
                LoggingService.LogMessage($"TranscriptionService: Using Whisper model path: {whisperModelPath}", "TranscriptionService_init");

                string modelPath = _modelManager.EnsureModelExistsAsync(
                    whisperModelPath,
                    _settings.ModelType == default(GgmlType) ? GgmlType.Base : _settings.ModelType
                ).Result;

                LoggingService.LogMessage($"TranscriptionService: Model path resolved to: {modelPath}", "TranscriptionService_init");
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
        /// Gets whether the service is currently transcribing
        /// </summary>
        public bool IsTranscribing => _isTranscribing;

        /// <summary>
        /// Creates a configured WhisperProcessor based on settings
        /// </summary>
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

            if(lastTranscribedText != null)
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

        /// <summary>
        /// Changes the language used for transcription
        /// </summary>
        public void ChangeLanguage(string language)
        {
            if (_transcriptor != null && !string.IsNullOrEmpty(language))
            {
                _settings.Language = language;
                _transcriptor.ChangeLanguage(language);
            }
        }

        /// <summary>
        /// Toggles transcription on or off
        /// </summary>
        public void ToggleTranscription(Action<string> action, TranscriptionSettings transcriptionSettings, int deviceNumber = 0)
        {
            if (_isTranscribing)
            {
                StopTranscription();
            }
            else
            {
                _settings = transcriptionSettings ?? TranscriptionSettings.Default;
                StartTranscription(action, deviceNumber);
            }
        }


        /// <summary>
        /// Starts transcription from the specified microphone
        /// </summary>
        public void StartTranscription(Action<string> action, int deviceNumber = 0, CultureInfo language = null)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TranscriptionService));

            if (_isTranscribing)
                return;

            try
            {
                // Play warning sound instead of inserting text
                LoggingService.PlayTranscriptionStateChangeSound();
                
                LoggingService.LogMessage("TranscriptionService: Starting transcription", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using device number {deviceNumber}", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using language '{language?.Name ?? _settings.Language}'", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using model type '{_settings.ModelType}'", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using chunk duration '{_settings.ChunkDuration.TotalSeconds} seconds'", "TranscriptionService_StartTranscription");
                LoggingService.LogMessage($"TranscriptionService: Using silence threshold '{_settings.SilenceThreshold.TotalSeconds} seconds'", "TranscriptionService_StartTranscription");

                _isTranscribing = true;
                _errorCount = 0;
                _sessionId = Guid.NewGuid().ToString();

                // Change language if specified
                if (language != null && language.Name != _settings.Language)
                {
                    ChangeLanguage(language.Name);
                }

                CreateWhisperFactory();
                CreateAudioCapture(deviceNumber);
#if DEBUG
                LoggingService.LogMessage($"TranscriptionService: Transcriptor Loaded: {_transcriptor.GetType()}", "TranscriptionService_StartTranscription");
#endif

                _cancellationTokenSource = new CancellationTokenSource();

                // Notify listeners that transcription started
                OnTranscriptionStarted(new TranscriptionEventArgs(_sessionId, string.Empty)); // Fix: Provide the required 'transcript' argument

                // Start the background task to process completed transcriptions
                Task.Run(() => ProcessCompletedTranscriptionsAsync(action, _cancellationTokenSource.Token));

                // Start audio capture and processing
                _audioProcessor.Start();
                _audioCapture.StartCapture();
            }
            catch (Exception ex)
            {
                _isTranscribing = false;
                OnTranscriptionError(new ErrorEventArgs(ex));
            }
        }

        private void CreateAudioCapture(int deviceNumber)
        {
            // Initialize audio capture and processing
            _audioCapture = new NAudioCapture(deviceNumber);

            // Create buffer processor
            var bytesPerMs = _waveFormat.AverageBytesPerSecond / 1000;
            _audioProcessor = new AudioBufferProcessor(
                bytesPerMs,
                _settings.ChunkDuration,
                _settings.SilenceThreshold);

            // Connect events
            _audioCapture.AudioDataAvailable += OnAudioDataAvailable;
            _audioProcessor.ChunkReady += OnAudioChunkReady;
        }

        public void ChangeChunkDuration(double chunkDuration)
        {
            _settings.ChunkDuration = TimeSpan.FromSeconds(chunkDuration);
        }

        public void ChangeSilenceThreshold(double overlapDuration)
        {
            _settings.SilenceThreshold = TimeSpan.FromSeconds(overlapDuration);
        }

        private void OnAudioDataAvailable(object sender, AudioDataEventArgs e)
        {
            _audioProcessor.AddAudioData(e.AudioData.Span);
        }

        private void OnAudioChunkReady(object sender, ProcessedAudioEventArgs e)
        {
            if (_cancellationTokenSource?.IsCancellationRequested == true)
                return;

            try
            {
                // PROBLEM: Creating new processor instances for each chunk
                // This creates new state and can confuse Whisper

                // FIXED: Reuse the existing processor or create a simpler approach
                var transcriptionTask = TranscribeChunkDirectlyAsync(
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
        /// Transcribes an audio chunk directly without creating new processor instances
        /// </summary>
        private async Task<TranscriptionResult> TranscribeChunkDirectlyAsync(
            MemoryStream audioBuffer,
            string sessionId,
            CancellationToken cancellationToken)
        {
            // Validate input buffer
            if (audioBuffer == null || audioBuffer.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("TranscriptionService: Empty or null audio buffer received");
                return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
            }

            System.Diagnostics.Debug.WriteLine($"TranscriptionService: Processing audio buffer with {audioBuffer.Length} bytes");

            // Create a temporary file for the WAV data
            string tempFile = Path.Combine(Path.GetTempPath(), $"debug_audio_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");

            try
            {
                SaveAudioChunkToFile(tempFile, audioBuffer);

                // Validate the created WAV file
                var fileInfo = new FileInfo(tempFile);
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: WAV file created - Size: {fileInfo.Length} bytes");

                if (fileInfo.Length < 100)
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: WARNING - WAV file is very small, may be empty!");
                    return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
                }

                if (!CheckWavFileForVoice(tempFile))
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: INFORMATION - WAV File does not contain a voice");
                    return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
                }
                var resultSegments = new List<TranscriptionSegment>();
                var segmentTexts = new List<string>();

                // Process the WAV file using the main transcriptor
                int segmentCount = 0;

                await ProcessAudioChunk(tempFile, segmentCount, segmentTexts, resultSegments, sessionId);

                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Whisper processing completed. Total segments: {segmentCount}");

                if (segmentCount == 0)
                {
                    System.Diagnostics.Debug.WriteLine("TranscriptionService: WARNING - No segments returned from Whisper!");

                    // Try to diagnose why no segments were returned
                    DiagnoseWhisperIssue(tempFile);
                }


                string fullText = string.Join(" ", segmentTexts);
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Completed processing - Full text: '{fullText}'");
                //_transcriptor = CreateWhisperProcessor(fullText);

                return new TranscriptionResult(fullText, fullText, resultSegments);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error processing audio: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to transcribe audio buffer", ex);
            }
            finally
            {
                // Keep temp file for debugging
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Temp file saved for debugging: {tempFile}");
            }
        }

        private async Task<int> ProcessAudioChunk(string tempFile, int segmentCount, List<string> segmentTexts, List<TranscriptionSegment> resultSegments, string sessionId)
        {
            using (var fileStream = File.OpenRead(tempFile))
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Reading WAV file for processing - Size: {fileStream.Length} bytes");
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Starting Whisper processing...");


                await foreach (var segment in _transcriptor.ProcessAsync(fileStream))
                {
                    segmentCount++;
                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Received segment #{segmentCount}: '{segment.Text}' ({segment.Start} - {segment.End})");

                    if (string.IsNullOrWhiteSpace(segment.Text))
                    {
                        System.Diagnostics.Debug.WriteLine("TranscriptionService: Empty segment received from Whisper");
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
            return 1;
        }

        /// <summary>
        /// This Method detects if a audio file contains a voice or not using WebRTC VAD.
        /// </summary>
        /// <param name="tempFilePath"></param>
        /// <returns></returns>
        private bool CheckWavFileForVoice(string tempFilePath)
        {
            try
            {
                using (var reader = new WaveFileReader(tempFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Analyzing audio for voice using WebRTC VAD - Duration: {reader.TotalTime.TotalSeconds:F2}s");
                   
                    // WebRTC VAD works with specific sample rates: 8000, 16000, 32000, or 48000 Hz
                    // and requires specific frame sizes
                    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
                    {
                        System.Diagnostics.Debug.WriteLine($"TranscriptionService: Audio format not compatible with WebRTC VAD, falling back to energy-based detection");
                        return FallbackEnergyBasedDetection(tempFilePath);
                    }

                    using (var vad = new WebRtcVad())
                    {
                        // Set aggressiveness level (0-3, where 3 is most aggressive)
                        // 0 = least aggressive (more likely to detect speech)
                        // 3 = most aggressive (less likely to detect speech)
                        vad.OperatingMode = OperatingMode.Aggressive; // Moderate aggressiveness

                        // WebRTC VAD requires specific frame sizes for 16kHz: 160, 320, or 480 samples
                        const int FRAME_SIZE_SAMPLES = 320; // 20ms at 16kHz
                        const int FRAME_SIZE_BYTES = FRAME_SIZE_SAMPLES * 2; // 16-bit samples

                        byte[] frameBuffer = new byte[FRAME_SIZE_BYTES];
                        int voiceFrames = 0;
                        int totalFrames = 0;

                        reader.Position = 0;

                        while (reader.Position < reader.Length - FRAME_SIZE_BYTES)
                        {
                            int bytesRead = reader.Read(frameBuffer, 0, FRAME_SIZE_BYTES);

                            if (bytesRead == FRAME_SIZE_BYTES)
                            {
                                totalFrames++;

                                // Convert bytes to short array for WebRTC VAD
                                short[] samples = new short[FRAME_SIZE_SAMPLES];
                                for (int i = 0; i < FRAME_SIZE_SAMPLES; i++)
                                {
                                    samples[i] = BitConverter.ToInt16(frameBuffer, i * 2);
                                }

                                // Check if this frame contains voice
                                bool hasVoice = vad.HasSpeech(samples);

                                if (hasVoice)
                                {
                                    voiceFrames++;
                                }
                            }
                        }

                        double voicePercentage = totalFrames > 0 ? (double)voiceFrames / totalFrames * 100 : 0;

                        System.Diagnostics.Debug.WriteLine($"TranscriptionService: WebRTC VAD Results:");
                        System.Diagnostics.Debug.WriteLine($"  Total frames analyzed: {totalFrames}");
                        System.Diagnostics.Debug.WriteLine($"  Voice frames detected: {voiceFrames}");
                        System.Diagnostics.Debug.WriteLine($"  Voice percentage: {voicePercentage:F2}%");

                        // Consider it voice if at least 5% of frames contain speech
                        bool containsVoice = voicePercentage >= 5.0;

                        System.Diagnostics.Debug.WriteLine($"TranscriptionService: Voice detection result: {containsVoice}");
                        LoggingService.LogMessage($"TranscriptionService: Voice detection result: {containsVoice}", "TranscriptionService_CheckWavFileForVoice");

                        return containsVoice;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error in WebRTC VAD voice detection: {ex.Message}");
                // Fall back to energy-based detection if WebRTC VAD fails
                return FallbackEnergyBasedDetection(tempFilePath);
            }
        }

        /// <summary>
        /// Fallback energy-based voice detection for when WebRTC VAD can't be used
        /// </summary>
        private bool FallbackEnergyBasedDetection(string tempFilePath)
        {
            try
            {
                using (var reader = new WaveFileReader(tempFilePath))
                {
                    var audioData = new byte[reader.Length];
                    reader.Read(audioData, 0, audioData.Length);

                    int totalSamples = audioData.Length / 2;
                    int energySamples = 0;
                    long sumSquares = 0;

                    for (int i = 0; i < audioData.Length - 1; i += 2)
                    {
                        short sample = BitConverter.ToInt16(audioData, i);
                        sumSquares += (long)sample * sample;

                        if (Math.Abs(sample) > 500) // Energy threshold
                        {
                            energySamples++;
                        }
                    }

                    double rms = Math.Sqrt((double)sumSquares / totalSamples);
                    double energyPercentage = (double)energySamples / totalSamples * 100;

                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Fallback detection - RMS: {rms:F2}, Energy %: {energyPercentage:F2}");

                    return rms > 150 && energyPercentage > 2.0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Fallback detection failed: {ex.Message}");
                return true; // Assume voice if all detection methods fail
            }
        }

        /// <summary>
        /// Saves the audio chunk to a temporary WAV file for later processing
        /// The current Version of the Whisper.net library requires a WAV file input,
        /// including a complete header. This method ensures the audio data is correctly formatted
        /// and saved to disk before processing.
        /// </summary>
        /// <param name="tempFile"></param>
        /// <param name="audioBuffer"></param>
        private void SaveAudioChunkToFile(string tempFile, MemoryStream audioBuffer)
        {
            System.Diagnostics.Debug.WriteLine($"TranscriptionService: Writing audio to temp file: {tempFile}");
            System.Diagnostics.Debug.WriteLine($"TranscriptionService: Wave format: {_waveFormat}");

            // Write the WAV file to disk
            using (WaveFileWriter writer = new WaveFileWriter(tempFile, _waveFormat))
            {
                audioBuffer.Position = 0;
                byte[] audioData = audioBuffer.ToArray();

                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Writing {audioData.Length} bytes to WAV file");

                // Analyze audio content
                AnalyzeAudioSamples(audioData, "Before WAV write");

                writer.Write(audioData, 0, audioData.Length);
                writer.Flush();
            }
        }

        /// <summary>
        /// Analyzes audio samples for debugging
        /// </summary>
        private void AnalyzeAudioSamples(byte[] audioData, string context)
        {
            try
            {
                if (audioData.Length < 2) return;

                int totalSamples = audioData.Length / 2;
                int nonZeroSamples = 0;
                short maxSample = 0;
                long sumSquares = 0;

                for (int i = 0; i < audioData.Length - 1; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    if (sample != 0) nonZeroSamples++;
                    if (Math.Abs(sample) > Math.Abs(maxSample)) maxSample = sample;
                    sumSquares += (long)sample * sample;
                }

                double rms = Math.Sqrt((double)sumSquares / totalSamples);
                double audioPercentage = totalSamples > 0 ? (double)nonZeroSamples / totalSamples * 100 : 0;

                System.Diagnostics.Debug.WriteLine($"TranscriptionService: {context} - Audio Analysis:");
                System.Diagnostics.Debug.WriteLine($"  Total samples: {totalSamples}");
                System.Diagnostics.Debug.WriteLine($"  Non-zero samples: {nonZeroSamples} ({audioPercentage:F2}%)");
                System.Diagnostics.Debug.WriteLine($"  Max sample: {maxSample}");
                System.Diagnostics.Debug.WriteLine($"  RMS level: {rms:F2}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error analyzing audio samples: {ex.Message}");
            }
        }

        public void ChangeModelType(GgmlType modelType)
        {
            if (_transcriptor != null && modelType != default(GgmlType))
            {
                _settings.ModelType = modelType;
                _transcriptor = CreateWhisperProcessor();
            }
        }

        /// <summary>
        /// Diagnoses potential Whisper processing issues
        /// </summary>
        private void DiagnoseWhisperIssue(string tempFile)
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

        private async Task ProcessCompletedTranscriptionsAsync(Action<string> action, CancellationToken cancellationToken)
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
                            action?.Invoke(result.IncrementalText);

                            // Raise events for each segment
                            foreach (var segment in result.Segments)
                            {
                                OnTranscriptionResult(new TranscriptionResultEventArgs(
                                    segment.Text,
                                    segment.Start,
                                    segment.End,
                                    segment.SessionId));
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

        private void HandleTranscriptionError(Exception ex)
        {
            _errorCount++;
            OnTranscriptionError(new ErrorEventArgs(ex)); // Pass only the exception object

            // If we have too many consecutive errors, stop transcription
            if (_errorCount >= MaxConsecutiveErrors)
            {
                StopTranscription();
            }
        }

        /// <summary>
        /// Stops the active transcription
        /// </summary>
        public void StopTranscription()
        {
            Task.Run(async () => await StopTranscriptionInternalAsync());
            LoggingService.PlayTranscriptionStateChangeSound(); 
        }

        /// <summary>
        /// Internal implementation of stop transcription
        /// </summary>
        private async Task StopTranscriptionInternalAsync()
        {
            if (!_isTranscribing)
                return;

            // Set the flag to prevent new tasks
            _isTranscribing = false;

            try
            {
                // Cancel ongoing operations first
                _cancellationTokenSource?.Cancel();

                // Stop audio capture immediately
                _audioCapture?.StopCapture();
                _audioProcessor?.Stop();

                // Wait for pending tasks with timeout - make this more robust
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
                        var timeout = Task.Delay(3000); // Reduced timeout
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

                // Clean up resources with proper disposal order
                try
                {
                    _audioCapture?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error disposing audio capture: {ex.Message}");
                }
                finally
                {
                    _audioCapture = null;
                }

                try
                {
                    _audioProcessor?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"TranscriptionService: Error disposing audio processor: {ex.Message}");
                }
                finally
                {
                    _audioProcessor = null;
                }

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

        /// <summary>
        /// Disposes resources used by the service
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            StopTranscription();

            _transcriptor?.Dispose();
            _transcriptorFactory?.Dispose();

            _isDisposed = true;
        }

    }
}