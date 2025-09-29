using System;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Represents a transcription segment with timing information
    /// </summary>
    public class TranscriptionSegment
    {
        public string Text { get; }
        public System.TimeSpan Start { get; }
        public TimeSpan End { get; }

        public TranscriptionSegment(string text, TimeSpan start, TimeSpan end)
        {
            Text = text;
            Start = start;
            End = end;
        }
    }
}
