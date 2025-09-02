using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForensicWhisperDeskZH.Transcription
{

    /// <summary>
    /// Represents the result of a transcription operation
    /// </summary>
    public class TranscriptionResult
    {
        public string FullText { get; }
        public string IncrementalText { get; }
        public IReadOnlyList<TranscriptionSegment> Segments { get; }

        public TranscriptionResult(
            string fullText,
            string incrementalText,
            IReadOnlyList<TranscriptionSegment> segments)
        {
            FullText = fullText;
            IncrementalText = incrementalText;
            Segments = segments;
        }
    }
}
