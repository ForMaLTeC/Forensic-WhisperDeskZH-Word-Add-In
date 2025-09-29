using System;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Represents event arguments for transcription events.
    /// </summary>
    public class TranscriptionEventArgs : EventArgs
    {

        /// <summary>
        /// Gets the transcript associated with the transcription event.
        /// </summary>
        public string Transcript { get; }

        /// <param name="transcript">The transcript.</param>
        public TranscriptionEventArgs(string transcript)
        {
            Transcript = transcript;
        }
    }
}