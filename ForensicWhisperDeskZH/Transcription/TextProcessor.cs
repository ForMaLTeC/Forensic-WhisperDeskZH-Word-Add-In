using ForensicWhisperDeskZH.Utils;
using Microsoft.Office.Interop.Word;
using NHunspell; // Add this NuGet package
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Processes transcribed text according to formatting settings
    /// </summary>
    public class TextProcessor : IDisposable
    {
        private readonly TranscriptionSettings _settings;
        private readonly Dictionary<string, string> _keywordReplacements;
        private Hunspell _hunspell;
        private readonly HashSet<string> _germanNouns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _disposed = false;
        private string lastWordInPreviousChunk = "";

        public TextProcessor(TranscriptionSettings settings, Dictionary<string, string> keywordReplacements = null)
        {
            _settings = settings;
            _keywordReplacements = keywordReplacements ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            InitializeGermanDictionary();
        }

        private void InitializeGermanDictionary()
        {
            try
            {
                // Initialize Hunspell with German dictionary
                _hunspell = new Hunspell("de_CH_frami.aff", "de_CH_frami.dic");
                LoggingService.LogMessage("hunspell initialized", "TextProcessor", true);
                
                // Load common German nouns for faster lookup
                LoadCommonGermanNouns();
            }
            catch (Exception ex)
            {
                // Log error but continue without spell checking
                System.Diagnostics.Debug.WriteLine($"Failed to initialize German dictionary: {ex.Message}");
                LoggingService.LogMessage("Failed to load hunspell", "TextProcessor", true);
            }
        }

        private void LoadCommonGermanNouns()
        {
            // Add common German nouns that should always be capitalized
            var commonNouns = new[]
            {
                "Haus", "Auto", "Mensch", "Zeit", "Jahr", "Tag", "Leben", "Hand", "Welt", "Frau",
                "Mann", "Kind", "Arbeit", "Geld", "Familie", "Problem", "Beispiel", "Frage",
                "Stadt", "Land", "Staat", "Gruppe", "Unternehmen", "System", "Programm",
                "Projekt", "Computer", "Internet", "Telefon", "Email", "Brief", "Dokument"
                // Add more as needed
            };

            foreach (var noun in commonNouns)
            {
                _germanNouns.Add(noun);
            }
        }

        /// <summary>
        /// Processes transcribed text with all transformations (keyword replacement, capitalization, punctuation)
        /// </summary>
        public string ProcessTranscribedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            string[] words = text.Split(' ');
            for (int i = 0; i < words.Length; i++) {
                words[i] = Regex.Replace(words[i], @"[^\w]", "").ToLower(); ;
            }
            text = string.Join(" ", words);
            // Apply keyword replacements
            text = ApplyKeywordReplacements(text);

            text = text.Trim();

            // Apply German capitalization rules if language is German
            if (_settings.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase))
            {
                text = ApplyGermanCapitalization(text);
            }

            return text;
        }

        /// <summary>
        /// Applies keyword replacements to the text
        /// </summary>
        private string ApplyKeywordReplacements(string text)
        {
            if (_keywordReplacements.Count == 0)
                return text;

            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                // Handle special case for line breaks
                if (_keywordReplacements.TryGetValue(words[i], out var replacement))
                {
                    if (replacement == "/n/r")
                    {
                        words[i] = Environment.NewLine;
                    }
                    else
                    {
                        words[i] = replacement;
                    }
                }
                else
                {
                    words[i] = " " + words[i];
                }
            }
            return string.Join("", words);
        }


        private string ApplyGermanCapitalization(string text)
        {
            if (_hunspell == null)
                return ApplyBasicGermanCapitalization(text);

            string[] words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                string cleanWord = Regex.Replace(word, @"[^\w]", ""); // Remove punctuation for analysis
                
                if (string.IsNullOrEmpty(word))
                    continue;

                // Check if it's a German noun using Hunspell
                if (IsGermanNoun(word))
                {
                    words[i] = CapitalizeWord(word, cleanWord);
                }
                else if (IsStartOfSentence(words, i))
                {
                    words[i] = CapitalizeFirstLetter(cleanWord);
                }

            }
            lastWordInPreviousChunk = words[words.Length - 1];

            return string.Join(" ", words);
        }

        private string ApplyBasicGermanCapitalization(string text)
        {
            // Fallback method when Hunspell is not available
            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                var cleanWord = Regex.Replace(word, @"[^\w]", "");
                
                if (string.IsNullOrEmpty(cleanWord))
                    continue;

                // Check against known nouns list
                if (_germanNouns.Contains(cleanWord))
                {
                    words[i] = CapitalizeWord(word, cleanWord);
                }
                // Apply basic heuristics for German nouns
                else if (IsLikelyGermanNoun(cleanWord))
                {
                    words[i] = CapitalizeWord(word, cleanWord);
                }
                // Capitalize first word of sentence
                else if (i == 0 || IsStartOfSentence(words, i))
                {
                    words[i] = CapitalizeFirstLetter(word);
                }
            }

            return string.Join(" ", words);
        }

        private bool IsGermanNoun(string word)
        {
            if (_hunspell == null)
                return false;

            try
            {
                // Check if the capitalized version is correct
                var capitalizedWord = char.ToUpper(word[0]) + word.Substring(1).ToLower();
                var lowercaseWord = word.ToLower();
                
                // If capitalized version is correct but lowercase is not, it's likely a noun
                return _hunspell.Spell(capitalizedWord) && !_hunspell.Spell(lowercaseWord);
            }
            catch
            {
                return false;
            }
        }

        private bool IsLikelyGermanNoun(string word)
        {
            // Basic heuristics for German nouns
            // Words ending in typical German noun suffixes
            var nounSuffixes = new[] { "ung", "heit", "keit", "schaft", "tum", "nis", "sal", "sel" };
            var lowercaseWord = word.ToLower();
            
            return nounSuffixes.Any(suffix => lowercaseWord.EndsWith(suffix)) ||
                   _germanNouns.Contains(word);
        }

        private bool IsStartOfSentence(string[] words, int index)
        {  
            if (index == 0 && (lastWordInPreviousChunk.Contains(".") || lastWordInPreviousChunk == "")){
                return true;
            }
            // Check if previous word ends with sentence-ending punctuation
            else if (index == 0)
            {
                return false;
            }
            var previousWord = words[index - 1];
            return previousWord.EndsWith(".") || previousWord.EndsWith("!") || previousWord.EndsWith("?");
        }

        private string CapitalizeWord(string originalWord, string cleanWord)
        {
            if (string.IsNullOrEmpty(cleanWord))
                return originalWord;

            var capitalizedClean = char.ToUpper(cleanWord[0]) + cleanWord.Substring(1).ToLower();
            return originalWord.Replace(cleanWord, capitalizedClean);
        }

        private string CapitalizeFirstLetter(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            return char.ToUpper(word[0]) + word.Substring(1);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _hunspell?.Dispose();
                _disposed = true;
            }
        }
    }
}