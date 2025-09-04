using ForensicWhisperDeskZH.Transcription;
using ForensicWhisperDeskZH.Common;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading;
using WebRtcVadSharp;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Handles audio capture and processing for transcription
    /// </summary>
    internal class AudioProcessor : IDisposable
    {
        #region Fields
        private readonly TranscriptionSettings _settings;
        private readonly WaveFormat _waveFormat;
        private IAudioCapture _audioCapture;
        private AudioBufferProcessor _audioProcessor;
        private bool _isCapturing = false;
        private bool _isDisposed = false;
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a processed audio chunk is ready for transcription
        /// </summary>
        public event EventHandler<ProcessedAudioEventArgs> AudioChunkReady;

        /// <summary>
        /// Occurs when an audio processing error occurs
        /// </summary>
        public event EventHandler<ErrorEventArgs> AudioError;
        #endregion

        #region Properties
        /// <summary>
        /// Gets whether audio capture is currently active
        /// </summary>
        public bool IsCapturing => _isCapturing;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new audio processor with the specified settings
        /// </summary>
        /// <param name="settings">Transcription settings containing audio configuration</param>
        public AudioProcessor(TranscriptionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _waveFormat = _settings.WaveFormat;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts audio capture from the specified device
        /// </summary>
        /// <param name="deviceNumber">Audio device number to capture from</param>
        public void StartCapture(int deviceNumber = 0)
        {
            ThrowIfDisposed();

            if (_isCapturing)
                return;

            try
            {
                CreateAudioCapture(deviceNumber);
                
                // Start audio processing and capture
                _audioProcessor.Start();
                _audioCapture.StartCapture();
                _isCapturing = true;

                LoggingService.LogMessage($"AudioProcessor: Started capture on device {deviceNumber}", "AudioProcessor_StartCapture");
            }
            catch (Exception ex)
            {
                _isCapturing = false;
                OnAudioError(new ErrorEventArgs(ex));
                throw;
            }
        }

        /// <summary>
        /// Stops audio capture
        /// </summary>
        public void StopCapture()
        {
            ThrowIfDisposed();

            if (!_isCapturing)
                return;

            try
            {
                _isCapturing = false;

                // Stop audio capture and processing
                _audioCapture?.StopCapture();
                _audioProcessor?.Stop();

                LoggingService.LogMessage("AudioProcessor: Stopped capture", "AudioProcessor_StopCapture");
            }
            catch (Exception ex)
            {
                OnAudioError(new ErrorEventArgs(ex));
            }
        }

        /// <summary>
        /// Saves audio chunk to a WAV file for processing
        /// </summary>
        /// <param name="tempFile">Path to save the WAV file</param>
        /// <param name="audioBuffer">Audio data to save</param>
        public void SaveAudioChunkToFile(string tempFile, MemoryStream audioBuffer)
        {
            System.Diagnostics.Debug.WriteLine($"AudioProcessor: Writing audio to temp file: {tempFile}");
            System.Diagnostics.Debug.WriteLine($"AudioProcessor: Wave format: {_waveFormat}");

            // Write the WAV file to disk
            using (WaveFileWriter writer = new WaveFileWriter(tempFile, _waveFormat))
            {
                audioBuffer.Position = 0;
                byte[] audioData = audioBuffer.ToArray();

                System.Diagnostics.Debug.WriteLine($"AudioProcessor: Writing {audioData.Length} bytes to WAV file");

                // Analyze audio content for debugging
                AnalyzeAudioSamples(audioData, "Before WAV write");

                writer.Write(audioData, 0, audioData.Length);
                writer.Flush();
            }
        }

        /// <summary>
        /// Checks if a WAV file contains voice using WebRTC VAD
        /// </summary>
        /// <param name="tempFilePath">Path to the WAV file</param>
        /// <returns>True if voice is detected, false otherwise</returns>
        public bool CheckWavFileForVoice(string tempFilePath)
        {
            try
            {
                using (var reader = new WaveFileReader(tempFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"AudioProcessor: Analyzing audio for voice using WebRTC VAD - Duration: {reader.TotalTime.TotalSeconds:F2}s");

                    // WebRTC VAD works with specific sample rates: 8000, 16000, 32000, or 48000 Hz
                    // and requires specific frame sizes
                    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
                    {
                        System.Diagnostics.Debug.WriteLine($"AudioProcessor: Audio format not compatible with WebRTC VAD, falling back to energy-based detection");
                        return FallbackEnergyBasedDetection(tempFilePath);
                    }

                    using (var vad = new WebRtcVad())
                    {
                        // Set aggressiveness level (0-3, where 3 is most aggressive)
                        vad.OperatingMode = OperatingMode.Aggressive;

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

                        System.Diagnostics.Debug.WriteLine($"AudioProcessor: WebRTC VAD Results:");
                        System.Diagnostics.Debug.WriteLine($"  Total frames analyzed: {totalFrames}");
                        System.Diagnostics.Debug.WriteLine($"  Voice frames detected: {voiceFrames}");
                        System.Diagnostics.Debug.WriteLine($"  Voice percentage: {voicePercentage:F2}%");

                        // Consider it voice if at least 5% of frames contain speech
                        bool containsVoice = voicePercentage >= 5.0;

                        System.Diagnostics.Debug.WriteLine($"AudioProcessor: Voice detection result: {containsVoice}");
                        LoggingService.LogMessage($"AudioProcessor: Voice detection result: {containsVoice}", "AudioProcessor_CheckWavFileForVoice");

                        return containsVoice;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioProcessor: Error in WebRTC VAD voice detection: {ex.Message}");
                return FallbackEnergyBasedDetection(tempFilePath);
            }
        }

        /// <summary>
        /// Cleans temporary audio files from the temp directory
        /// </summary>
        public void CleanTempDirectory()
        {
            try
            {
                var tempPath = Path.GetTempPath();
                var tempFiles = Directory.GetFiles(tempPath, "WhisperDesk_audio_*.wav");
                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                        System.Diagnostics.Debug.WriteLine($"AudioProcessor: Deleted temp file: {file}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AudioProcessor: Failed to delete temp file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioProcessor: Error cleaning temp directory: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Creates and configures audio capture from the specified device
        /// </summary>
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

        /// <summary>
        /// Handles audio data available from capture device
        /// </summary>
        private void OnAudioDataAvailable(object sender, AudioDataEventArgs e)
        {
            _audioProcessor.AddAudioData(e.AudioData.Span);
        }

        /// <summary>
        /// Handles processed audio chunks ready for transcription
        /// </summary>
        private void OnAudioChunkReady(object sender, ProcessedAudioEventArgs e)
        {
            try
            {
                AudioChunkReady?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                OnAudioError(new ErrorEventArgs(ex));
            }
        }

        /// <summary>
        /// Fallback energy-based voice detection when WebRTC VAD can't be used
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

                    System.Diagnostics.Debug.WriteLine($"AudioProcessor: Fallback detection - RMS: {rms:F2}, Energy %: {energyPercentage:F2}");

                    return rms > 150 && energyPercentage > 2.0;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioProcessor: Fallback detection failed: {ex.Message}");
                return true; // Assume voice if all detection methods fail
            }
        }

        /// <summary>
        /// Analyzes audio samples for debugging purposes
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

                System.Diagnostics.Debug.WriteLine($"AudioProcessor: {context} - Audio Analysis:");
                System.Diagnostics.Debug.WriteLine($"  Total samples: {totalSamples}");
                System.Diagnostics.Debug.WriteLine($"  Non-zero samples: {nonZeroSamples} ({audioPercentage:F2}%)");
                System.Diagnostics.Debug.WriteLine($"  Max sample: {maxSample}");
                System.Diagnostics.Debug.WriteLine($"  RMS level: {rms:F2}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioProcessor: Error analyzing audio samples: {ex.Message}");
            }
        }

        private void OnAudioError(ErrorEventArgs e)
        {
            AudioError?.Invoke(this, e);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(AudioProcessor));
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (!_isDisposed)
            {
                StopCapture();

                try
                {
                    _audioCapture?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioProcessor: Error disposing audio capture: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"AudioProcessor: Error disposing audio processor: {ex.Message}");
                }
                finally
                {
                    _audioProcessor = null;
                }

                _isDisposed = true;
            }
        }
        #endregion
    }
}