using System;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Processes transcribed text according to formatting settings
    /// </summary>
    public class TextProcessor
    {

        public TextProcessor(TranscriptionSettings settings)
        {

        }

        /// <summary>
        /// Formats transcribed text according to settings (capitalization, punctuation)
        /// </summary>
        public string ProcessTranscribedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Apply text processing based on settings
            text = text.ToLower();
            text = text.Trim();
                        
            return text;
        }
    }
}