using System;
using System.Text.RegularExpressions;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Processes transcribed text according to formatting settings
    /// </summary>
    public class TextProcessor
    {
        private readonly TranscriptionSettings _settings;
        
        public TextProcessor(TranscriptionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }
        
        /// <summary>
        /// Formats transcribed text according to settings (capitalization, punctuation)
        /// </summary>
        public string ProcessTranscribedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Apply text processing based on settings
            string result = text.Trim();

            return result;
        }
        
        /// <summary>
        /// Extracts only the new text portion compared to previous text
        /// </summary>
        public string GetIncrementalText(string previousText, string newText)
        {
            // If the new text is shorter, it's probably a correction - return it
            if (newText.Length < previousText.Length)
                return newText;

            // If the new text starts with the previous text, just return the additional part
            if (newText.StartsWith(previousText, StringComparison.OrdinalIgnoreCase))
                return newText.Substring(previousText.Length);

            // Find overlap between the end of previous text and start of new text
            int overlap = FindMaxOverlap(previousText, newText);
            
            // If we found an overlap, return the non-overlapping part
            if (overlap > 0)
                return newText.Substring(overlap);

            // If no overlap, it's completely new text
            return newText;
        }
        
        /// <summary>
        /// Finds the maximum overlap between the end of string1 and start of string2
        /// </summary>
        private int FindMaxOverlap(string string1, string string2)
        {
            int maxOverlap = 0;
            
            for (int i = 1; i <= Math.Min(string1.Length, string2.Length); i++)
            {
                string prevEnd = string1.Substring(string1.Length - i);
                string newStart = string2.Substring(0, i);

                if (string.Equals(prevEnd, newStart, StringComparison.OrdinalIgnoreCase))
                    maxOverlap = i;
            }
            
            return maxOverlap;
        }
    }
}