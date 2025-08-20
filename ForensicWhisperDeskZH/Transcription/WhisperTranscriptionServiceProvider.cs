using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Provider for Whisper-based transcription services
    /// </summary>
    public class WhisperTranscriptionServiceProvider : ITranscriptionServiceProvider
    {
        /// <summary>
        /// Gets a list of available microphone devices
        /// </summary>
        public List<MicrophoneDevice> GetAvailableMicrophones()
        {
            var devices = new List<MicrophoneDevice>();
            
            // Get all microphones seen by the system
            int deviceCount = WaveIn.DeviceCount;
            for (int i = 0; i < deviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                devices.Add(new MicrophoneDevice(i, capabilities.ProductName));
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