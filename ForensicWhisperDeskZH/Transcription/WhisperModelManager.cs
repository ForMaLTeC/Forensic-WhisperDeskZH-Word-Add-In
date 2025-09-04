using ForensicWhisperDeskZH.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Manages Whisper model downloading and loading
    /// </summary>
    public class WhisperModelManager
    {
        private readonly HttpClient _httpClient;

        public WhisperModelManager(HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Ensures the model exists, first checking local content files, then downloading if necessary
        /// </summary>
        /// <param name="modelPath">Path where the model should be located</param>
        /// <param name="modelType">Model type to download if needed</param>
        /// <returns>The path to the model file</returns>
        public async Task<string> EnsureModelExistsAsync(string modelPath, GgmlType modelType = GgmlType.Base)
        {
            // First, check for local content files (Models folder in output directory)
            string localModelPath = GetLocalModelPath(modelType);
            if (File.Exists(localModelPath))
            {
                LoggingService.LogMessageAsync($"Using local model at {localModelPath}").Wait();
                return localModelPath;
            }

            // If the originally requested path exists, return it
            if (File.Exists(modelPath))
            {
                return modelPath;
            }

            // Handle directory and filename determination for download
            string directory = Path.GetDirectoryName(modelPath);
            string fileName = Path.GetFileName(modelPath);

            if (string.IsNullOrEmpty(fileName))
            {
                // Generate a filename based on model type
                fileName = $"ggml-{modelType.ToString().ToLower()}.bin";
                modelPath = Path.Combine(directory ?? ".", fileName);

                // Check if the generated path exists
                if (File.Exists(modelPath))
                {
                    return modelPath;
                }
            }

            // Create the directory if it doesn't exist
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Download the model as fallback
            try
            {
                var downloader = new WhisperGgmlDownloader(_httpClient);
                using (var modelStream = await downloader.GetGgmlModelAsync(modelType))
                using (var fileStream = File.Create(modelPath))
                {
                    await modelStream.CopyToAsync(fileStream);
                }
                return modelPath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download model: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the path to a local model file in the Models directory
        /// </summary>
        /// <param name="modelType">The model type</param>
        /// <returns>Full path to the local model file</returns>
        private string GetLocalModelPath(GgmlType modelType)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDirectory = Path.Combine(baseDirectory, "Models");
            string fileName = $"ggml-{GetStringFromGgmlType(modelType)}.bin";
            return Path.Combine(modelsDirectory, fileName);
        }

        /// <summary>
        /// Creates a WhisperFactory from the specified model path
        /// </summary>
        public WhisperFactory CreateFactory(string modelPath)
        {
            return WhisperFactory.FromPath(modelPath);
        }

        private static string GetStringFromGgmlType(GgmlType modelType)
        {
            return modelType switch
            {
                GgmlType.Tiny => "tiny",
                GgmlType.Base => "base",
                GgmlType.Small => "small",
                GgmlType.Medium => "medium",
                GgmlType.LargeV3 => "large",
                GgmlType.LargeV3Turbo => "turbo",
                _ => throw new ArgumentException($"Unknown model type: {modelType}"),
            };
        }
    }
}