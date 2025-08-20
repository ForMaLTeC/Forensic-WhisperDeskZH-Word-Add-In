using System.Collections.Generic;

namespace ForensicWhisperDeskZH.Text
{
    /// <summary>
    /// Represents a keyword replacement mapping
    /// </summary>
    public class KeywordReplacement
    {
        public string Word { get; set; }
        public string Symbol { get; set; }

        public KeywordReplacement() { }

        public KeywordReplacement(string word, string symbol)
        {
            Word = word;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// Container for keyword replacements loaded from XML
    /// </summary>
    public class KeywordReplacements
    {
        public List<KeywordReplacement> Replacements { get; set; } = new List<KeywordReplacement>();
    }
}