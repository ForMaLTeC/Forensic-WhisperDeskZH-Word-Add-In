using System;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Represents event arguments for transcription events.
    /// </summary>
    public class TranscriptionEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the session ID associated with the transcription event.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Gets the transcript associated with the transcription event.
        /// </summary>
        public string Transcript { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TranscriptionEventArgs"/> class.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="transcript">The transcript.</param>
        public TranscriptionEventArgs(string sessionId, string transcript)
        {
            SessionId = sessionId;
            Transcript = transcript;
        }
    }
}