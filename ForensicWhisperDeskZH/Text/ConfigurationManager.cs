using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using ForensicWhisperDeskZH.Transcription;
using Newtonsoft.Json;

namespace ForensicWhisperDeskZH.Common
{
    /// <summary>
    /// Manages application configuration settings
    /// </summary>
    public class ConfigurationManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForensicWhisperDeskZH",
            "config.json");

        private static readonly string KeywordPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForensicWhisperDeskZH",
            "Keywords.xml");

        private static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForensicWhisperDeskZH");

        /// <summary>
        /// Loads settings from disk or creates default settings
        /// </summary>
        public static TranscriptionSettings LoadTranscriptionSettings()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonConvert.DeserializeObject<TranscriptionSettings>(json)
                        ?? TranscriptionSettings.Default;
                }
            }
            catch (Exception ex)
            {
                // Log but continue with defaults
                LoggingService.LogError(ex.Message, ex, "ConfigurationManager.LoadTranscriptionSettings");
            }

            return TranscriptionSettings.Default;
        }

        /// <summary>
        /// Loads keyword replacements from XML file
        /// </summary>
        public static Dictionary<string, string> LoadKeywordReplacements()
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Ensure the app data directory exists
                Directory.CreateDirectory(AppDataDirectory);

                // Copy the Keywords.xml from the solution if it doesn't exist
                EnsureKeywordsFileExists();

                if (File.Exists(KeywordPath))
                {
                    var doc = XDocument.Load(KeywordPath);
                    var keywordReplacements = doc.Root?.Elements("Replacement");

                    if (keywordReplacements != null)
                    {
                        foreach (var replacement in keywordReplacements)
                        {
                            var word = replacement.Element("Word")?.Value?.Trim();
                            var symbol = replacement.Element("Symbol")?.Value;

                            if (!string.IsNullOrEmpty(word) && symbol != null)
                            {
                                replacements[word] = symbol;
                                LoggingService.LogMessage($"Loaded keyword replacement: '{word}' -> '{symbol}'",
                                    "ConfigurationManager.LoadKeywordReplacements");
                            }
                        }
                    }
                }

                LoggingService.LogMessage($"Loaded {replacements.Count} keyword replacements",
                    "ConfigurationManager.LoadKeywordReplacements");
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.Message, ex, "ConfigurationManager.LoadKeywordReplacements");
            }

            return replacements;
        }

        /// <summary>
        /// Ensures the Keywords.xml file exists in the app data directory
        /// </summary>
        private static void EnsureKeywordsFileExists()
        {
            try
            {
                if (!File.Exists(KeywordPath))
                {
                    // Try to find the Keywords.xml in the add-in directory
                    string addinDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string sourceKeywordPath = Path.Combine(addinDirectory, "Keywords.xml");

                    if (File.Exists(sourceKeywordPath))
                    {
                        File.Copy(sourceKeywordPath, KeywordPath);
                        LoggingService.LogMessage($"Copied Keywords.xml from {sourceKeywordPath} to {KeywordPath}",
                            "ConfigurationManager.EnsureKeywordsFileExists");
                    }
                    else
                    {
                        // Create a default Keywords.xml file
                        CreateDefaultKeywordsFile();
                        LoggingService.LogMessage($"Created default Keywords.xml at {KeywordPath}",
                            "ConfigurationManager.EnsureKeywordsFileExists");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.Message, ex, "ConfigurationManager.EnsureKeywordsFileExists");
            }
        }

        /// <summary>
        /// Creates a default Keywords.xml file
        /// </summary>
        private static void CreateDefaultKeywordsFile()
        {
            var defaultXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<KeywordReplacements>
  <Replacement>
    <Word>Punkt</Word>
    <Symbol>.</Symbol>
  </Replacement>
  <Replacement>
    <Word>Komma</Word>
    <Symbol>,</Symbol>
  </Replacement>
  <Replacement>
    <Word>Ausrufezeichen</Word>
    <Symbol>!</Symbol>
  </Replacement>
  <Replacement>
    <Word>Fragezeichen</Word>
    <Symbol>?</Symbol>
  </Replacement>
  <Replacement>
    <Word>semicolon</Word>
    <Symbol>;</Symbol>
  </Replacement>
  <Replacement>
    <Word>Doppelpunkt</Word>
    <Symbol>:</Symbol>
  </Replacement>
  <Replacement>
    <Word>Bindestrich</Word>
    <Symbol>-</Symbol>
  </Replacement>
  <Replacement>
    <Word>Neue Linie</Word>
    <Symbol>/n</Symbol>
  </Replacement>
  <Replacement>
    <Word>Abstand</Word>
    <Symbol>/n/r</Symbol>
  </Replacement>
</KeywordReplacements>";

            File.WriteAllText(KeywordPath, defaultXml);
        }

        /// <summary>
        /// Saves settings to disk
        /// </summary>
        public static void SaveTranscriptionSettings(TranscriptionSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError(ex.Message, ex, "ConfigurationManager.SaveTranscriptionSettings");
            }
        }
    }
}