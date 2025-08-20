using System.Collections.Generic;
using System.Threading.Tasks;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Provider for transcription services
    /// </summary>
    public interface ITranscriptionServiceProvider
    {
        /// <summary>
        /// Gets a list of available microphone devices
        /// </summary>
        List<MicrophoneDevice> GetAvailableMicrophones();
        
        /// <summary>
        /// Creates a transcription service with the specified settings
        /// </summary>
        Task<ITranscriptionService> CreateTranscriptionServiceAsync(TranscriptionSettings settings);
    }
    
    /// <summary>
    /// Represents a microphone device
    /// </summary>
    public class MicrophoneDevice
    {
        public int DeviceNumber { get; }
        public string Name { get; }
        
        public MicrophoneDevice(int deviceNumber, string name)
        {
            DeviceNumber = deviceNumber;
            Name = name;
        }
    }
}