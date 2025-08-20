using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace ModelDownloader
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("Whisper Model Downloader - Downloads Whisper models for build processes");

            var modelsOption = new Option<string[]>(
                "--models",
                description: "Model types to download (e.g., base,large,tiny)")
            {
                AllowMultipleArgumentsPerToken = true
            };
            modelsOption.SetDefaultValue(new[] { "base", "large" });

            var targetOption = new Option<string>(
                "--target",
                description: "Target directory for downloaded models");
            targetOption.SetDefaultValue("Models");

            var forceOption = new Option<bool>(
                "--force",
                description: "Force re-download even if models exist");

            rootCommand.AddOption(modelsOption);
            rootCommand.AddOption(targetOption);
            rootCommand.AddOption(forceOption);

            rootCommand.SetHandler(async (models, target, force) =>
            {
                try
                {
                    Console.WriteLine("=== Whisper Model Downloader ===");
                    Console.WriteLine($"Target Directory: {target}");
                    Console.WriteLine($"Models to Download: {string.Join(", ", models)}");
                    Console.WriteLine($"Force Re-download: {force}");
                    Console.WriteLine();

                    var downloader = new ModelDownloaderService();
                    await downloader.DownloadModelsAsync(models, target, force);

                    Console.WriteLine();
                    Console.WriteLine("✅ Model download process completed successfully!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error: {ex.Message}");
                    Environment.Exit(1);
                }
            }, modelsOption, targetOption, forceOption);

            return await rootCommand.InvokeAsync(args);
        }
    }

    public class ModelDownloaderService
    {
        private readonly WhisperGgmlDownloader _downloader;

        public ModelDownloaderService()
        {
            _downloader = WhisperGgmlDownloader.Default;
        }

        public async Task DownloadModelsAsync(string[] modelTypes, string targetDirectory, bool force = false)
        {
            // Ensure target directory exists
            Directory.CreateDirectory(targetDirectory);
            Console.WriteLine($"📁 Created/verified target directory: {Path.GetFullPath(targetDirectory)}");

            foreach (string modelType in modelTypes)
            {
                try
                {
                    var ggmlType = GetGgmlType(modelType);
                    string fileName = $"ggml-{modelType}.bin";
                    string modelPath = Path.Combine(targetDirectory, fileName);

                    // Check if model already exists
                    if (File.Exists(modelPath) && !force)
                    {
                        var existingSize = new FileInfo(modelPath).Length / (1024 * 1024);
                        Console.WriteLine($"⏭️  Model {modelType} already exists ({existingSize:F1} MB) - skipping");
                        continue;
                    }

                    Console.WriteLine($"⬇️  Downloading {modelType} model...");

                    // Download the model using WhisperGgmlDownloader
                    using (var modelStream = await _downloader.GetGgmlModelAsync(ggmlType))
                    using (var fileStream = File.Create(modelPath))
                    {
                        await modelStream.CopyToAsync(fileStream);
                    }

                    // Verify download
                    if (File.Exists(modelPath))
                    {
                        var fileSize = new FileInfo(modelPath).Length / (1024 * 1024);
                        Console.WriteLine($"✅ Successfully downloaded {modelType} model ({fileSize:F1} MB)");
                    }
                    else
                    {
                        throw new Exception("Model file was not created after download");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Failed to download {modelType} model: {ex.Message}");
                    throw new Exception($"Critical failure downloading model {modelType}: {ex.Message}", ex);
                }
            }

            // Display summary
            Console.WriteLine();
            Console.WriteLine("📋 Download Summary:");
            if (Directory.Exists(targetDirectory))
            {
                var modelFiles = Directory.GetFiles(targetDirectory, "ggml-*.bin");
                foreach (var file in modelFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var fileSize = new FileInfo(file).Length / (1024 * 1024);
                    Console.WriteLine($"   📄 {fileName} - {fileSize:F1} MB");
                }
                Console.WriteLine($"   📊 Total models: {modelFiles.Length}");
            }
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
                _ => throw new ArgumentException($"Unknown model type: {modelType}. Valid types: tiny, base, small, medium, large, turbo"),
            };
        }
    }
}
