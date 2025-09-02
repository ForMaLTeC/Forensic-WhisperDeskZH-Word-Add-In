using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using System; 

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Provider for Whisper-based transcription services
    /// </summary>
    public class WhisperTranscriptionServiceProvider : ITranscriptionServiceProvider
    {
        /// <summary>
        /// Gets a list of available microphone devices using WASAPI (consistent with NAudioCapture)
        /// </summary>
        public List<MicrophoneDevice> GetAvailableMicrophones()
        {
            var devices = new List<MicrophoneDevice>();
            
            try
            {
                // Use WASAPI enumeration to match NAudioCapture behavior
                var deviceEnumerator = new MMDeviceEnumerator();
                var audioDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                
                for (int i = 0; i < audioDevices.Count; i++)
                {
                    try
                    {
                        var device = audioDevices[i];
                        devices.Add(new MicrophoneDevice(i, device.FriendlyName));
                        
                        // Debug logging to verify consistency
                        System.Diagnostics.Debug.WriteLine($"WASAPI Device {i}: {device.FriendlyName} (ID: {device.ID})");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing WASAPI device {i}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating WASAPI devices: {ex.Message}");
                
                // Fallback to WinMM if WASAPI fails
                int deviceCount = WaveIn.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    try
                    {
                        var capabilities = WaveIn.GetCapabilities(i);
                        devices.Add(new MicrophoneDevice(i, capabilities.ProductName));
                        System.Diagnostics.Debug.WriteLine($"WinMM Fallback Device {i}: {capabilities.ProductName}");
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing WinMM device {i}: {fallbackEx.Message}");
                    }
                }
            }
            
            return devices;
        }
        
        /// <summary>
        /// Creates a new transcription service with the specified settings
        /// </summary>
        public async Task<ITranscriptionService> CreateTranscriptionServiceAsync(TranscriptionSettings settings)
        {
            // Create and return the service
            // This may take time if the model needs to be downloaded
            var service = new TranscriptionService(settings);
            return service;
        }
    }
}