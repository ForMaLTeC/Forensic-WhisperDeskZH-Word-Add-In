using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public string SessionId { get; }

        public TranscriptionSegment(string text, TimeSpan start, TimeSpan end, string sessionId)
        {
            Text = text;
            Start = start;
            End = end;
            SessionId = sessionId;
        }
    }
}
