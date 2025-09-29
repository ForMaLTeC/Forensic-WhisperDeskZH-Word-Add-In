using ForensicWhisperDeskZH.Utils;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using System;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Implements audio capture using NAudio 
    /// library for improved reliability
    /// </summary>
    public class NAudioCapture : IAudioCapture
    {
        private WasapiCapture _wasapiCapture;
        private readonly int _deviceNumber;
        private readonly string _expectedDeviceId;
        private readonly string _expectedDeviceName;
        private bool _isCapturing = false;
        private bool _isDisposed = false;
        private readonly WaveFormat _desiredFormat;
        private MediaFoundationResampler _resampler;
        private BufferedWaveProvider _bufferedProvider;

        public bool IsCapturing => _isCapturing;
        public int DeviceNumber => _deviceNumber;
        public string ActualDeviceId { get; private set; }
        public string ActualDeviceName { get; private set; }

        public event EventHandler<AudioDataEventArgs> AudioDataAvailable;
        public event EventHandler<AudioCaptureErrorEventArgs> Error;

        /// <summary>
        /// Initializes a new instance of NAudio capture using WASAPI
        /// </summary>
        /// <param name="deviceNumber">The device number of the microphone to use</param>
        /// <param name="sampleRate">Sample rate to use (default 16000Hz)</param>
        /// <param name="bitsPerSample">Bits per sample (default 16)</param>
        /// <param name="channels">Number of channels (default 1 = mono)</param>
        public NAudioCapture(int deviceNumber, int sampleRate = 16000, int bitsPerSample = 16, int channels = 1)
            : this(deviceNumber, sampleRate, bitsPerSample, channels, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of NAudio capture using WASAPI with device validation
        /// </summary>
        /// <param name="deviceNumber">The device number of the microphone to use</param>
        /// <param name="sampleRate">Sample rate to use (default 16000Hz)</param>
        /// <param name="bitsPerSample">Bits per sample (default 16)</param>
        /// <param name="channels">Number of channels (default 1 = mono)</param>
        /// <param name="expectedDeviceId">Expected device ID for validation</param>
        /// <param name="expectedDeviceName">Expected device name for validation</param>
        public NAudioCapture(int deviceNumber, int sampleRate, int bitsPerSample, int channels, string expectedDeviceId, string expectedDeviceName)
        {
            _deviceNumber = deviceNumber;
            _expectedDeviceId = expectedDeviceId;
            _expectedDeviceName = expectedDeviceName;
            _desiredFormat = new WaveFormat(sampleRate, bitsPerSample, channels);

            InitializeWasapiCapture();
        }

        /// <summary>
        /// Creates NAudioCapture from a MicrophoneDevice for enhanced device tracking
        /// </summary>
        public static NAudioCapture FromMicrophoneDevice(MicrophoneDevice device, int sampleRate = 16000, int bitsPerSample = 16, int channels = 1)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            LoggingService.LogMessage($"Creating NAudioCapture from MicrophoneDevice: {device.Name} (Index: {device.DeviceNumber}, ID: {device.DeviceId})", "NAudioCapture", true);

            return new NAudioCapture(device.DeviceNumber, sampleRate, bitsPerSample, channels, device.DeviceId, device.Name);
        }

        private void InitializeWasapiCapture()
        {
            try
            {
                // Initialize MediaFoundation for resampling
                MediaFoundationApi.Startup();

                // Get the device using MMDeviceEnumerator
                var deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                if (_deviceNumber >= devices.Count || _deviceNumber < 0)
                {
                    string errorMessage = $"Invalid device number: {_deviceNumber}. Available devices: 0-{devices.Count - 1}";
                    LoggingService.LogMessage(errorMessage, "NAudioCapture", true);
                    throw new ArgumentException(errorMessage);
                }

                var selectedDevice = devices[_deviceNumber];

                // Store actual device information
                ActualDeviceId = selectedDevice.ID;
                ActualDeviceName = selectedDevice.FriendlyName;

                // Validate device selection if expected values were provided
                ValidateDeviceSelection(selectedDevice);

                // Create WASAPI capture with the selected device - use shared mode with smaller buffer
                _wasapiCapture = new WasapiCapture(selectedDevice, true, 20); // Use exclusive mode=false, 20ms buffer

                // Set up event handlers
                _wasapiCapture.DataAvailable += OnDataAvailable;
                _wasapiCapture.RecordingStopped += OnRecordingStopped;

                // Initialize resampler if formats don't match
                if (!_wasapiCapture.WaveFormat.Equals(_desiredFormat))
                {
                    System.Diagnostics.Debug.WriteLine($"NAudioCapture: Format conversion needed from {_wasapiCapture.WaveFormat} to {_desiredFormat}");
                    // Create a BufferedWaveProvider that we'll reuse for all conversions
                    _bufferedProvider = new BufferedWaveProvider(_wasapiCapture.WaveFormat)
                    {
                        BufferLength = _wasapiCapture.WaveFormat.AverageBytesPerSecond * 2, // 2 seconds buffer
                        DiscardOnBufferOverflow = true
                    };
                    _resampler = new MediaFoundationResampler(_bufferedProvider, _desiredFormat);
                }

                // Log the selected device for debugging
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Using WASAPI device {_deviceNumber}: {selectedDevice.FriendlyName}");
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Device ID: {selectedDevice.ID}");
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Device format: {_wasapiCapture.WaveFormat}");
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Desired format: {_desiredFormat}");

                LoggingService.LogMessage($"NAudioCapture initialized successfully:\nDevice: {selectedDevice.FriendlyName}\nIndex: {_deviceNumber}\nID: {selectedDevice.ID}", "NAudioCapture", true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Failed to initialize WASAPI: {ex.Message}");
                LoggingService.LogError("Failed to initialize WASAPI capture", ex, "NAudioCapture_Initialize");
                throw;
            }
        }

        private void ValidateDeviceSelection(MMDevice selectedDevice)
        {
            bool validationFailed = false;
            string validationErrors = "";

            // Validate device ID if expected
            if (!string.IsNullOrEmpty(_expectedDeviceId))
            {
                if (!string.Equals(selectedDevice.ID, _expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    validationFailed = true;
                    validationErrors += $"Device ID mismatch. Expected: {_expectedDeviceId}, Actual: {selectedDevice.ID}\n";
                }
            }

            // Validate device name if expected
            if (!string.IsNullOrEmpty(_expectedDeviceName))
            {
                if (!string.Equals(selectedDevice.FriendlyName, _expectedDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    validationFailed = true;
                    validationErrors += $"Device name mismatch. Expected: {_expectedDeviceName}, Actual: {selectedDevice.FriendlyName}\n";
                }
            }

            if (validationFailed)
            {
                string warningMessage = $"Device selection validation failed for device {_deviceNumber}:\n{validationErrors}" +
                                      "This may indicate that the device list has changed since device enumeration. " +
                                      "The application will continue with the actual device found.";

                LoggingService.LogMessage(warningMessage, "NAudioCapture Device Validation", true);
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: {warningMessage}");

                // Log this as a warning but don't throw - the device might still work
                // In a production environment, you might want to throw an exception here
            }
            else if (!string.IsNullOrEmpty(_expectedDeviceId) || !string.IsNullOrEmpty(_expectedDeviceName))
            {
                LoggingService.LogMessage($"Device selection validation passed for device {_deviceNumber}: {selectedDevice.FriendlyName}", "NAudioCapture Device Validation", true);
            }
        }

        /// <summary>
        /// Gets information about the currently selected device
        /// </summary>
        public (string DeviceId, string DeviceName, int DeviceNumber) GetDeviceInfo()
        {
            return (ActualDeviceId, ActualDeviceName, _deviceNumber);
        }

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (_isCapturing)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine("NAudioCapture: Starting WASAPI recording...");
                LoggingService.LogMessage($"Starting audio capture on device: {ActualDeviceName} (Index: {_deviceNumber})", "NAudioCapture", true);

                _wasapiCapture.StartRecording();
                _isCapturing = true;

                System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI recording started successfully");
                LoggingService.LogMessage("Audio capture started successfully", "NAudioCapture", true);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to start WASAPI recording on device: {ActualDeviceName} (Index: {_deviceNumber})";
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: {errorMessage}: {ex.Message}");
                LoggingService.LogError(errorMessage, ex, "NAudioCapture_StartCapture");
                OnError(ex);
            }
        }

        public void StopCapture()
        {
            ThrowIfDisposed();

            if (!_isCapturing)
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine("NAudioCapture: Stopping WASAPI recording...");
                LoggingService.LogMessage($"Stopping audio capture on device: {ActualDeviceName} (Index: {_deviceNumber})", "NAudioCapture", true);

                _wasapiCapture.StopRecording();
                _isCapturing = false;

                System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI recording stopped successfully");
                LoggingService.LogMessage("Audio capture stopped successfully", "NAudioCapture", true);
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to stop WASAPI recording on device: {ActualDeviceName} (Index: {_deviceNumber})";
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: {errorMessage}: {ex.Message}");
                LoggingService.LogError(errorMessage, ex, "NAudioCapture_StopCapture");
                OnError(ex);
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                if (e.BytesRecorded > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"NAudioCapture: Raw audio received - {e.BytesRecorded} bytes");

                    // Process audio format if necessary
                    byte[] processedBuffer = ProcessAudioFormat(e.Buffer, e.BytesRecorded);

                    if (processedBuffer != null && processedBuffer.Length > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"NAudioCapture: Processed buffer size: {processedBuffer.Length} bytes");

                        // Check for actual audio content (not just silence)
                        bool hasAudio = false;
                        int nonZeroSamples = 0;
                        short maxSample = 0;

                        for (int i = 0; i < Math.Min(processedBuffer.Length - 1, 1000); i += 2) // Check first 500 samples max
                        {
                            if (i + 1 < processedBuffer.Length)
                            {
                                short sample = BitConverter.ToInt16(processedBuffer, i);
                                if (sample != 0) nonZeroSamples++;
                                if (Math.Abs(sample) > Math.Abs(maxSample)) maxSample = sample;
                                if (Math.Abs(sample) > 50) // Lower threshold for better sensitivity
                                {
                                    hasAudio = true;
                                }
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"NAudioCapture: Audio analysis - Non-zero samples: {nonZeroSamples}, Max sample: {maxSample}, Has audio: {hasAudio}");

                        AudioDataAvailable?.Invoke(this, new AudioDataEventArgs(processedBuffer));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("NAudioCapture: ProcessAudioFormat returned null or empty buffer");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("NAudioCapture: Received event with 0 bytes recorded");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Error in WASAPI OnDataAvailable: {ex.Message}");
                LoggingService.LogError("Error processing WASAPI audio data", ex, "NAudioCapture_OnDataAvailable");
                OnError(ex);
            }
        }

        private byte[] ProcessAudioFormat(byte[] buffer, int bytesRecorded)
        {
            try
            {
                // If no resampler needed, return buffer as-is
                if (_resampler == null)
                {
                    byte[] result = new byte[bytesRecorded];
                    Buffer.BlockCopy(buffer, 0, result, 0, bytesRecorded);
                    return result;
                }

                // Use MediaFoundation resampler for format conversion
                return ConvertAudioFormatWithMF(buffer, bytesRecorded);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Error processing audio format: {ex.Message}");
                LoggingService.LogError("Error processing audio format", ex, "NAudioCapture_ProcessAudioFormat");

                // Fallback: return original buffer truncated to recorded bytes if no conversion needed
                if (_resampler == null)
                {
                    byte[] fallback = new byte[bytesRecorded];
                    Buffer.BlockCopy(buffer, 0, fallback, 0, bytesRecorded);
                    return fallback;
                }

                return null;
            }
        }

        private byte[] ConvertAudioFormatWithMF(byte[] inputBuffer, int bytesRecorded)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Converting audio format - Input: {bytesRecorded} bytes");

                // Check if we have the buffered provider
                if (_bufferedProvider == null)
                {
                    System.Diagnostics.Debug.WriteLine("NAudioCapture: ERROR - BufferedWaveProvider is null");
                    return null;
                }

                // Add the input data to the buffered provider
                _bufferedProvider.AddSamples(inputBuffer, 0, bytesRecorded);

                // Calculate expected output size
                int outputSizeEstimate = (int)((bytesRecorded / (float)_wasapiCapture.WaveFormat.AverageBytesPerSecond) * _desiredFormat.AverageBytesPerSecond) + _desiredFormat.BlockAlign;
                var outputBuffer = new byte[outputSizeEstimate];

                // Read from the resampler
                int bytesRead = _resampler.Read(outputBuffer, 0, outputSizeEstimate);

                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Resampler output: {bytesRead} bytes from {outputSizeEstimate} estimated");

                if (bytesRead > 0)
                {
                    // Return only the actual bytes read
                    var result = new byte[bytesRead];
                    Buffer.BlockCopy(outputBuffer, 0, result, 0, bytesRead);
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: MediaFoundation resampling failed: {ex.Message}");
                throw;
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI recording stopped event received");
            _isCapturing = false;

            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: WASAPI recording stopped with exception: {e.Exception.Message}");
                OnError(e.Exception);
            }
        }

        private void OnError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NAudioCapture: WASAPI error occurred: {ex.Message}");
            Error?.Invoke(this, new AudioCaptureErrorEventArgs(ex));
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NAudioCapture));
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                System.Diagnostics.Debug.WriteLine("NAudioCapture: Disposing WASAPI capture...");

                try
                {
                    if (_isCapturing)
                    {
                        StopCapture();
                        // Wait for capture to fully stop
                        System.Threading.Thread.Sleep(100);
                    }

                    // Dispose in correct order to avoid access violations
                    _resampler?.Dispose();
                    _resampler = null;

                    _wasapiCapture?.Dispose();
                    _wasapiCapture = null;

                    _bufferedProvider = null;

                    // Cleanup MediaFoundation
                    try
                    {
                        MediaFoundationApi.Shutdown();
                    }
                    catch
                    {
                        // Ignore shutdown errors
                    }

                    _isDisposed = true;

                    System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI capture disposed successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NAudioCapture: Error during disposal: {ex.Message}");
                }
            }
        }
    }
}