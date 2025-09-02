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
        /// This method downloads all Available Whisper models and adds them to the Models Directory.
        /// It Should only ever be called in a debug environment and not in production.
        /// </summary>
        public async Task DownloadAllModelsAsync()
        {
            // Create Models directory relative to the project/application directory
            string projectDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDirectory = Path.Combine(projectDirectory, "Models");
            //modelsDirectory = modelsDirectory.Replace("bin\\Debug\\", "");

            // Ensure the Models directory exists
            Directory.CreateDirectory(modelsDirectory);

            // List of all available model types from GetGgmlType method
            string[] modelTypes = { "tiny", "base", "small", "medium", "large", "turbo" };

            var downloader = new WhisperGgmlDownloader(_httpClient);

            foreach (string modelType in modelTypes)
            {
                try
                {
                    string fileName = $"ggml-{modelType}.bin";
                    string modelPath = Path.Combine(modelsDirectory, fileName);

                    // Skip if model already exists
                    if (File.Exists(modelPath))
                    {
                        Console.WriteLine($"Model {modelType} already exists at {modelPath}");
                        continue;
                    }

                    Console.WriteLine($"Downloading {modelType} model...");

                    // Download the model
                    using (var modelStream = await downloader.GetGgmlModelAsync(GetGgmlType(modelType)))
                    using (var fileStream = File.Create(modelPath))
                    {
                        await modelStream.CopyToAsync(fileStream);
                    }

                    Console.WriteLine($"Successfully downloaded {modelType} model to {modelPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to download {modelType} model: {ex.Message}");
                    // Continue with other models even if one fails
                }
            }

            Console.WriteLine("Model download process completed.");
        }

        /// <summary>
        /// Downloads specific models for build/deployment purposes
        /// This method is not restricted to debug mode and can be used in CI/CD pipelines
        /// </summary>
        /// <param name="modelTypes">Array of model types to download. If null, downloads base and large models.</param>
        /// <param name="targetDirectory">Target directory for models. If null, uses default Models directory.</param>
        /// <returns>Task representing the download operation</returns>
        public async Task DownloadModelsForBuildAsync(string[] modelTypes = null, string targetDirectory = null)
        {
            // Default to essential models if none specified
            modelTypes = modelTypes ?? new[] { "base", "large" };

            // Use provided directory or default to Models subdirectory
            string modelsDirectory = targetDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

            // Ensure the Models directory exists
            Directory.CreateDirectory(modelsDirectory);

            Console.WriteLine($"Downloading {modelTypes.Length} model(s) to: {modelsDirectory}");

            var downloader = new WhisperGgmlDownloader(_httpClient);

            foreach (string modelType in modelTypes)
            {
                try
                {
                    string fileName = $"ggml-{modelType}.bin";
                    string modelPath = Path.Combine(modelsDirectory, fileName);

                    // Skip if model already exists
                    if (File.Exists(modelPath))
                    {
                        Console.WriteLine($"Model {modelType} already exists at {modelPath}");
                        continue;
                    }

                    Console.WriteLine($"Downloading {modelType} model...");

                    // Download the model
                    using (var modelStream = await downloader.GetGgmlModelAsync(GetGgmlType(modelType)))
                    using (var fileStream = File.Create(modelPath))
                    {
                        await modelStream.CopyToAsync(fileStream);
                    }

                    Console.WriteLine($"Successfully downloaded {modelType} model to {modelPath}");

                    // Log file size for verification
                    var fileInfo = new FileInfo(modelPath);
                    Console.WriteLine($"Model {modelType} size: {fileInfo.Length / (1024 * 1024):F1} MB");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to download {modelType} model: {ex.Message}");
                    throw new Exception($"Critical failure downloading model {modelType}: {ex.Message}", ex);
                }
            }

            Console.WriteLine("Build model download process completed successfully.");
        }

        /// <summary>
        /// Downloads models using direct HTTP download for CI/CD environments
        /// This is a backup method if the Whisper.net downloader doesn't work in CI
        /// </summary>
        /// <param name="modelTypes">Array of model types to download</param>
        /// <param name="targetDirectory">Target directory for models</param>
        /// <returns>Task representing the download operation</returns>
        public async Task DownloadModelsDirectAsync(string[] modelTypes = null, string targetDirectory = null)
        {
            modelTypes = modelTypes ?? new[] { "base", "large" };
            string modelsDirectory = targetDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");

            Directory.CreateDirectory(modelsDirectory);

            // HuggingFace direct download URLs
            const string baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

            Console.WriteLine($"Direct downloading {modelTypes.Length} model(s) to: {modelsDirectory}");

            foreach (string modelType in modelTypes)
            {
                try
                {
                    string fileName = $"ggml-{modelType}.bin";
                    string modelPath = Path.Combine(modelsDirectory, fileName);
                    string downloadUrl = $"{baseUrl}/{fileName}";

                    if (File.Exists(modelPath))
                    {
                        Console.WriteLine($"Model {modelType} already exists at {modelPath}");
                        continue;
                    }

                    Console.WriteLine($"Direct downloading {modelType} model from {downloadUrl}...");

                    using (var response = await _httpClient.GetAsync(downloadUrl))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var fileStream = File.Create(modelPath))
                        {
                            await response.Content.CopyToAsync(fileStream);
                        }
                    }

                    var fileInfo = new FileInfo(modelPath);
                    Console.WriteLine($"Successfully downloaded {modelType} model ({fileInfo.Length / (1024 * 1024):F1} MB)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to directly download {modelType} model: {ex.Message}");
                    throw new Exception($"Critical failure downloading model {modelType}: {ex.Message}", ex);
                }
            }

            Console.WriteLine("Direct model download process completed successfully.");
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
        /// Gets a list of available local models
        /// </summary>
        /// <returns>Array of available model types</returns>
        public string[] GetAvailableLocalModels()
        {
            var availableModels = new List<string>();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDirectory = Path.Combine(baseDirectory, "Models");

            if (Directory.Exists(modelsDirectory))
            {
                var modelFiles = Directory.GetFiles(modelsDirectory, "ggml-*.bin");
                foreach (var file in modelFiles)
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.StartsWith("ggml-") && fileName.EndsWith(".bin"))
                    {
                        // Extract model type from filename
                        var modelType = fileName.Substring("ggml-".Length, fileName.Length - "ggml-.bin".Length);
                        availableModels.Add(modelType);
                    }
                }
            }

            return availableModels.ToArray();
        }

        /// <summary>
        /// Creates a WhisperFactory from the specified model path
        /// </summary>
        public WhisperFactory CreateFactory(string modelPath)
        {
            return WhisperFactory.FromPath(modelPath);
        }

        private static GgmlType GetGgmlType(string modelType)
        {
            return modelType.ToLowerInvariant() switch
            {
                "tiny" => GgmlType.Tiny,
                "base" => GgmlType.Base,
                "small" => GgmlType.Small,
                "medium" => GgmlType.Medium,
                "large" => GgmlType.LargeV3,
                "turbo" => GgmlType.LargeV3Turbo,

                _ => throw new ArgumentException($"Unknown model type: {modelType}"),
            };
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