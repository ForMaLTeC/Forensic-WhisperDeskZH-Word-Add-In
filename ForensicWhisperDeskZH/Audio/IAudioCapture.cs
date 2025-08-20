using System;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Defines the interface for audio capture services
    /// </summary>
    public interface IAudioCapture : IDisposable
    {
        /// <summary>
        /// Occurs when audio data is available
        /// </summary>
        event EventHandler<AudioDataEventArgs> AudioDataAvailable;

        /// <summary>
        /// Occurs when an error happens during audio capture
        /// </summary>
        event EventHandler<AudioCaptureErrorEventArgs> Error;
        
        /// <summary>
        /// Gets whether the audio capture is currently active
        /// </summary>
        bool IsCapturing { get; }
        
        /// <summary>
        /// Starts audio capture
        /// </summary>
        void StartCapture();
        
        /// <summary>
        /// Stops audio capture
        /// </summary>
        void StopCapture();

        void Dispose();
    }
    
    public class AudioDataEventArgs : EventArgs
    {
        public ReadOnlyMemory<byte> AudioData { get; }
        
        public AudioDataEventArgs(ReadOnlyMemory<byte> audioData)
        {
            AudioData = audioData;
        }
    }
    
    public class AudioCaptureErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        
        public AudioCaptureErrorEventArgs(Exception exception)
        {
            Exception = exception;
        }
    }
}