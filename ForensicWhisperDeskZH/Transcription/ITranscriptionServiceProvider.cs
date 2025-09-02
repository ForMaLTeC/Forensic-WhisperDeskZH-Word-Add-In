using System.Collections.Generic;
using System.Threading.Tasks;
using ForensicWhisperDeskZH.Audio;

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

}