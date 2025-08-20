using System;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Represents the arguments for transcription result events.
    /// </summary>
    public class TranscriptionResultEventArgs : EventArgs
    {
        public string Text { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }
        public string SessionId { get; }

        public TranscriptionResultEventArgs(string text, TimeSpan start, TimeSpan end, string sessionId)
        {
            Text = text;
            Start = start;
            End = end;
            SessionId = sessionId;
        }
    }
}