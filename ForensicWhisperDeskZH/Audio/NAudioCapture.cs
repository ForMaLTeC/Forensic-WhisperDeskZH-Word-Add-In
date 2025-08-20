using ForensicWhisperDeskZH.Common;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using System;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Implements audio capture using NAudio WASAPI library for improved reliability
    /// </summary>
    public class NAudioCapture : IAudioCapture
    {
        private WasapiCapture _wasapiCapture;
        private readonly int _deviceNumber;
        private bool _isCapturing = false;
        private bool _isDisposed = false;
        private readonly WaveFormat _desiredFormat;
        private MediaFoundationResampler _resampler;
        private BufferedWaveProvider _bufferedProvider; // Store reference to the BufferedWaveProvider
        
        public bool IsCapturing => _isCapturing;
        
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
        {
            _deviceNumber = deviceNumber;
            _desiredFormat = new WaveFormat(sampleRate, bitsPerSample, channels);
            
            InitializeWasapiCapture();
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
                    throw new ArgumentException($"Invalid device number: {_deviceNumber}. Available devices: 0-{devices.Count - 1}");
                }
                
                var selectedDevice = devices[_deviceNumber];
                
                // Create WASAPI capture with the selected device - use shared mode with smaller buffer
                _wasapiCapture = new WasapiCapture(selectedDevice, false, 20); // Use exclusive mode=false, 20ms buffer
                
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
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Device format: {_wasapiCapture.WaveFormat}");
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Desired format: {_desiredFormat}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Failed to initialize WASAPI: {ex.Message}");
                LoggingService.LogError("Failed to initialize WASAPI capture", ex, "NAudioCapture_Initialize");
                throw;
            }
        }
        
        public void StartCapture()
        {
            ThrowIfDisposed();
            
            if (_isCapturing)
                return;
                
            try
            {
                System.Diagnostics.Debug.WriteLine("NAudioCapture: Starting WASAPI recording...");
                _wasapiCapture.StartRecording();
                _isCapturing = true;
                System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI recording started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Failed to start WASAPI recording: {ex.Message}");
                LoggingService.LogError("Failed to start WASAPI audio capture", ex, "NAudioCapture_StartCapture");
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
                _wasapiCapture.StopRecording();
                _isCapturing = false;
                System.Diagnostics.Debug.WriteLine("NAudioCapture: WASAPI recording stopped successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NAudioCapture: Failed to stop WASAPI recording: {ex.Message}");
                LoggingService.LogError("Failed to stop WASAPI audio capture", ex, "NAudioCapture_StopCapture");
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

        public static void TestMicrophoneAccess(int deviceNumber)
        {
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                
                System.Diagnostics.Debug.WriteLine($"Testing microphone access for device {deviceNumber}");
                System.Diagnostics.Debug.WriteLine($"Total capture devices: {devices.Count}");
                
                if (deviceNumber < devices.Count)
                {
                    var selectedDevice = devices[deviceNumber];
                    System.Diagnostics.Debug.WriteLine($"Device {deviceNumber}: {selectedDevice.FriendlyName}");
                    System.Diagnostics.Debug.WriteLine($"Device State: {selectedDevice.State}");
                    System.Diagnostics.Debug.WriteLine($"Device Format: {selectedDevice.AudioClient.MixFormat}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error testing microphone: {ex.Message}");
            }
        }
    }
}