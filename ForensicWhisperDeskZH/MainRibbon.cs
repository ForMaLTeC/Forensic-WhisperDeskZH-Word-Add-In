using ForensicWhisperDeskZH.Audio;
using ForensicWhisperDeskZH.Common;
using Microsoft.Office.Tools.Ribbon;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH
{
    public partial class MainRibbon
    {
        private readonly double _minSilenceThreshold = 0.1; // Minimum silence threshold in seconds
        private readonly double _maxSilenceThreshold = 10.0; // Maximum silence threshold in seconds

        private readonly double _minChunkSizeInSeconds = 1.0; // Minimum chunk size in seconds
        private readonly double _maxChunkSizeInSeconds = 30.0; // Maximum chunk size in seconds
        private AddInViewModel ViewModel => Globals.ThisAddIn.AddInViewModel;
        private static bool _isTranscribing = false;
        private bool _isInitialized = false;

        private void TestRibbon_Load(object sender, RibbonUIEventArgs e)
        {
            // Don't initialize immediately - wait for ViewModel to be ready
            System.Diagnostics.Debug.WriteLine("MainRibbon: Load event fired, deferring initialization...");

            // Start a background task to wait for ViewModel initialization
            Task.Run(async () => await WaitForViewModelAndInitialize());
        }

        private async Task WaitForViewModelAndInitialize()
        {
            // Wait for the ViewModel to be available with timeout
            int attempts = 0;
            const int maxAttempts = 50; // 5 seconds max wait

            while (ViewModel == null && attempts < maxAttempts)
            {
                await Task.Delay(100); // Wait 100ms between checks
                attempts++;
                System.Diagnostics.Debug.WriteLine($"FennecRibbon: Waiting for ViewModel... Attempt {attempts}");
            }

            if (ViewModel == null)
            {
                System.Diagnostics.Debug.WriteLine("FennecRibbon: ERROR - ViewModel not available after timeout!");
                return;
            }

            // Initialize on the main thread
            await Task.Run(() =>
            {
                // Use Invoke to run on the main thread

                // With this line:
                // Replace this line:
                // Globals.Ribbons.MainRibbon.RibbonUI?.Invalidate();

                // With this line:
                Globals.Ribbons.MainRibbon.RibbonUI?.Invalidate();

                InitializeRibbonControls();
            });
        }

        private void InitializeRibbonControls()
        {
            if (_isInitialized || ViewModel == null) return;

            try
            {
                System.Diagnostics.Debug.WriteLine("FennecRibbon: Starting ribbon initialization...");

                // Add diagnostic logging
                AudioDiagnosticTool.ListAudioDevices();

                InitalizeUserInterface();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FennecRibbon: Error initializing ribbon controls: {ex.Message}");
            }
        }

        private void InitalizeUserInterface()
        {
            // load available microphones
            LoadMicrophones();

            // load available model types
            LoadModelTypes();

            // load available languages
            LoadLanguages();

            // Set UI values from the settings
            MinChunkSizeInSeconds.Text = ViewModel._transcriptionSettings.ChunkDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            SilenceThreshold.Text = ViewModel._transcriptionSettings.SilenceThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);

            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("FennecRibbon: Initialization completed successfully!");
        }

        private void UpdateSettingsView()
        {
            // Set UI values from the settings
            MinChunkSizeInSeconds.Text = ViewModel._transcriptionSettings.ChunkDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            SilenceThreshold.Text = ViewModel._transcriptionSettings.SilenceThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        }

        private void LoadMicrophones(bool isRefresh = false)
        {
            if (ViewModel == null) return;

            try
            {
                // Clear existing items first
                MicrophoneCheckBox.Items.Clear();

                // Populate microphone list
                var microphones = ViewModel.GetMicrophoneList();
                foreach (var mic in microphones)
                {
                    RibbonDropDownItem dropDownItem = Globals.Factory.GetRibbonFactory().CreateRibbonDropDownItem();
                    dropDownItem.Label = mic.Name;
                    dropDownItem.Tag = mic.DeviceNumber;
                    MicrophoneCheckBox.Items.Add(dropDownItem);
                    LoggingService.LogMessage($"FennecRibbon: Found microphone: {mic.Name} (Device Number: {mic.DeviceNumber})");
                }

                // Set the default selected item to the first microphone in the list
                if (MicrophoneCheckBox.Items.Count > 0 && !isRefresh)
                {
                    MicrophoneCheckBox.Text = MicrophoneCheckBox.Items[0].Label;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FennecRibbon: Error loading microphones: {ex.Message}");
            }
        }

        private void LoadModelTypes()
        {
            if (ViewModel == null) return;

            try
            {
                // Clear existing items first
                ModelSelection.Items.Clear();

                var modelTypes = ViewModel.GetModelTypeList();
                foreach (var model in modelTypes)
                {
                    RibbonDropDownItem dropDownItem = Globals.Factory.GetRibbonFactory().CreateRibbonDropDownItem();
                    dropDownItem.Label = model.ToString();
                    dropDownItem.Tag = model;
                    ModelSelection.Items.Add(dropDownItem);
                    if (model == ViewModel._transcriptionSettings.ModelType)
                    {
                        ModelSelection.Text = dropDownItem.Label; // Set default selection
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FennecRibbon: Error loading model types: {ex.Message}");
            }
        }

        private void LoadLanguages()
        {
            if (ViewModel == null) return;

            try
            {
                // Clear existing items first
                LanguageSelection.Items.Clear();

                List<string> languages = ViewModel.GetLanguageList();
                foreach (string lang in languages)
                {
                    RibbonDropDownItem dropDownItem = Globals.Factory.GetRibbonFactory().CreateRibbonDropDownItem();
                    dropDownItem.Label = lang;
                    dropDownItem.Tag = lang;
                    LanguageSelection.Items.Add(dropDownItem);
                    if (lang == ViewModel._transcriptionSettings.Language)
                    {
                        LanguageSelection.Text = dropDownItem.Label;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FennecRibbon: Error loading languages: {ex.Message}");
            }
        }

        // Add null checks to all event handlers
        private void StartTranscriptionButton_Click(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null)
            {
                MessageBox.Show("Transcription service is not ready yet. Please wait a moment and try again.",
                    "Service Not Ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ViewModel.ToggleTranscription())
            {
                MessageBox.Show("Failed to toggle transcription. Check the logs for more details.",
                    "Transcription Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            ToggleInteractability();
            ToggleDictationButton();
        }

        private void MaxChunkSizeInSeconds_TextChanged(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null) return;

            try
            {
                double seconds = double.Parse(MinChunkSizeInSeconds.Text, CultureInfo.InvariantCulture);
                // Validate the chunk size value
                if (seconds < _minChunkSizeInSeconds)
                {
                    MessageBox.Show($"Chunk Size must be longer than {_minChunkSizeInSeconds} seconds",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ViewModel._transcriptionSettings.ChunkDuration = TimeSpan.FromSeconds(_minChunkSizeInSeconds);
                    MinChunkSizeInSeconds.Text = _minChunkSizeInSeconds.ToString(CultureInfo.InvariantCulture);
                    return;
                }
                if (seconds > _maxChunkSizeInSeconds)
                {
                    MessageBox.Show($"Chunk Size must be shorter than {_maxChunkSizeInSeconds} seconds.",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ViewModel._transcriptionSettings.ChunkDuration = TimeSpan.FromSeconds(_maxChunkSizeInSeconds);
                    MinChunkSizeInSeconds.Text = _maxChunkSizeInSeconds.ToString(CultureInfo.InvariantCulture);
                    return;
                }
                ViewModel._transcriptionSettings.ChunkDuration = TimeSpan.FromSeconds(seconds);
            }
            catch
            {
                // Reset to current value on parse error
                MinChunkSizeInSeconds.Text = ViewModel._transcriptionSettings.ChunkDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                MessageBox.Show("Please enter a valid number for Chunk Size in Seconds.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SilenceThreshold_TextChanged(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null) return;
            try
            {
                double threshold = double.Parse(SilenceThreshold.Text, CultureInfo.InvariantCulture);
                // Validate the threshold value
                if (threshold < _minSilenceThreshold)
                {
                    MessageBox.Show($"Silence Threshold must be longer than {_minSilenceThreshold} seconds",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    threshold = _minSilenceThreshold;
                }
                if (threshold > _maxSilenceThreshold)
                {
                    MessageBox.Show($"Silence Threshold must be shorter than {_maxSilenceThreshold} seconds.",
                        "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    threshold = _maxSilenceThreshold;
                }
                SilenceThreshold.Text = threshold.ToString(CultureInfo.InvariantCulture);
                ViewModel.ChangeSilenceThreshold((int)threshold);
            }
            catch
            {
                // Reset to current value on parse error
                SilenceThreshold.Text = ViewModel._transcriptionSettings.SilenceThreshold.TotalSeconds.ToString(CultureInfo.InvariantCulture);
                MessageBox.Show("Please enter a valid number for Silence Threshold.",
                    "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MicrophoneCheckBox_TextChanged(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null) return;

            var selectedMicrophone = MicrophoneCheckBox.Items.FirstOrDefault(item => item.Label == MicrophoneCheckBox.Text);
            if (selectedMicrophone?.Tag is int deviceNumber)
            {
                ViewModel.SetDeviceNumber(deviceNumber);
                LoadMicrophones(true);
            }
        }

        private void ModelSelection_TextChanged(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null) return;

            var selectedModel = ModelSelection.Items.FirstOrDefault(item => item.Label == ModelSelection.Text);
            if (selectedModel?.Tag is GgmlType modelType)
            {
                ViewModel._transcriptionSettings.ModelType = modelType;
            }
            else
            {
                MessageBox.Show("Invalid model type selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LanguageSelection_TextChanged(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel == null) return;

            var selectedLanguage = LanguageSelection.Items.FirstOrDefault(item => item.Label == LanguageSelection.Text);
            if (selectedLanguage?.Tag is string language)
            {
                ViewModel._transcriptionSettings.Language = language;
            }
        }

        private void ToggleInteractability()
        {
            // Toggle the transcription state
            _isTranscribing = !_isTranscribing;


            MicrophoneCheckBox.Enabled = !_isTranscribing;
            ModelSelection.Enabled = !_isTranscribing;
            LanguageSelection.Enabled = !_isTranscribing;
            MinChunkSizeInSeconds.Enabled = !_isTranscribing;
            SilenceThreshold.Enabled = !_isTranscribing;
        }

        private void ToggleDictationButton()
        {
            // Enable or disable controls based on transcription state
            StartTranscriptionButton.Label = _isTranscribing ? "Diktat Beenden" : "Diktat Starten";
            StartTranscriptionButton.Enabled = true;
        }

        // Fix typo: change 'privtae' to 'private'
        private void ToggleListeningModeButton()
        {
            ListenModeButton.Label = _isTranscribing ? "Hörmodus Beenden" : "Hörmodus Starten";
            ListenModeButton.Enabled = true;

        }

        private void ResetButton_Click(object sender, RibbonControlEventArgs e)
        {
            if (ViewModel.ResetSettings())
            {
                UpdateSettingsView();
            }
        }

        private void ListenModeButton_Click(object sender, RibbonControlEventArgs e)
        {
            ViewModel.StartListeningMode();

            ToggleInteractability();
            ToggleListeningModeButton();
        }
    }
}