using ForensicWhisperDeskZH.Audio;
using ForensicWhisperDeskZH.Common;
using ForensicWhisperDeskZH.Document;
using ForensicWhisperDeskZH.Text;
using ForensicWhisperDeskZH.Transcription;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH
{
    /// <summary>
    /// ViewModel that coordinates the transcription and document interaction
    /// </summary>
    internal class AddInViewModel : IDisposable
    {
        private readonly ITranscriptionServiceProvider _transcriptionProvider;
        private readonly IDocumentService _documentService;

        private static Dictionary<string, string> _keywordsToReplace;

        private ITranscriptionService _transcriptionService;
        private int _selectedDeviceNumber = 0;
        private bool _isDisposed = false;
        private bool _isInListeningMode = false;
        private bool _triggerPhraseDetected = false;
        private readonly StringBuilder _textAccumulator = new StringBuilder();
        private readonly object _accumulatorLock = new object();
        private System.Timers.Timer _insertionTimer;
        private const int INSERTION_INTERVAL_MS = 500; // 500ms batching
        public TranscriptionSettings _transcriptionSettings { get; }

        public event EventHandler<bool> OnDictationStateChanged;
        public event EventHandler<string> ErrorOccurred;

        private StringBuilder _listeningBuffer = new StringBuilder();
        private readonly object _listeningLock = new object();

        /// <summary>
        /// Gets whether the system is currently in listening mode
        /// </summary>
        public bool IsInListeningMode => _isInListeningMode;

        /// <summary>
        /// Gets whether the trigger phrase has been detected
        /// </summary>
        public bool TriggerPhraseDetected => _triggerPhraseDetected;

        /// <summary>
        /// Creates a new ViewModel with the specified dependencies
        /// </summary>
        private AddInViewModel(
            ITranscriptionServiceProvider transcriptionProvider,
            IDocumentService documentService,
            TranscriptionSettings settings = null,
            Dictionary<string, string> keywordReplacements = null)
        {
            _transcriptionProvider = transcriptionProvider ?? throw new ArgumentNullException(nameof(transcriptionProvider));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _transcriptionSettings = settings ?? TranscriptionSettings.Default;
            _keywordsToReplace = keywordReplacements ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Remove TextBufferService dependency
            InitializeTextInsertion();
        }

        /// <summary>
        /// Creates a new ViewModel with the specified dependencies asynchronously
        /// </summary>
        public static async Task<AddInViewModel> CreateAsync(
            ITranscriptionServiceProvider transcriptionProvider,
            IDocumentService documentService,
            TranscriptionSettings settings = null,
            Dictionary<string, string> keywordReplacements = null)
        {
            var viewModel = new AddInViewModel(transcriptionProvider, documentService, settings, keywordReplacements);
            await viewModel.InitializeAsync();

            LoggingService.LogMessage("AddInViewModel initialized successfully.", "AddInViewModel_init");
            return viewModel;
        }


        private async Task InitializeAsync()
        {
            try
            {
                _transcriptionService = await _transcriptionProvider.CreateTranscriptionServiceAsync(_transcriptionSettings);

                if (_transcriptionService != null)
                {
                    // Set up event handlers
                    _transcriptionService.TranscriptionError += (sender, e) =>
                        OnErrorOccurred($"Transcription error: {e.ToString()}");
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to initialize transcription service: {ex.Message}", ex);
                throw; // Re-throw for CreateAsync callers
            }
        }

        public bool ResetSettings()
        {
            try
            {
                // Stop transcription first if running
                if (_transcriptionService?.IsTranscribing == true)
                {
                    _transcriptionService.StopTranscription();
                    // Wait for stop to complete
                    System.Threading.Thread.Sleep(1000);
                }

                // Reset transcription settings to default
                _transcriptionSettings.ResetTranscriptionSettings();

                // Dispose old service properly
                _transcriptionService?.Dispose();
                _transcriptionService = null;

                // Wait before creating new service
                System.Threading.Thread.Sleep(500);

                // Create new service asynchronously but wait for completion
                var task = _transcriptionProvider.CreateTranscriptionServiceAsync(_transcriptionSettings);

                task.Wait(500); // 0.5 second timeout

                if (task.IsCompleted)
                {
                    _transcriptionService = task.Result;
                    ConfigurationManager.SaveTranscriptionSettings(_transcriptionSettings);
                    return true;
                }
                else
                {
                    OnErrorOccurred("Failed to recreate transcription service within timeout");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Failed to reset transcription settings: {ex.Message}", ex);
                return false;
            }

        }

        public bool ToggleListeningMode(bool isListening)
        {
            if (isListening)
            {
                return StopListeningMode();
            }
            else
            {
                return StartListeningMode();
            }
        }

        /// <summary>
        /// Starts listening mode transcription that waits for trigger phrase
        /// </summary>
        public bool StartListeningMode()
        {
            if (_transcriptionService == null)
            {
                OnErrorOccurred("Transcription service is not initialized.");
                return false;
            }

            try
            {
                _isInListeningMode = true;
                _triggerPhraseDetected = false;

                lock (_listeningLock)
                {
                    _listeningBuffer.Clear();
                }
                // Start transcription in listening mode
                _transcriptionService.ToggleTranscription(HandleTranscribedTextListeningMode, _transcriptionSettings, _selectedDeviceNumber);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error starting listening mode: {ex.Message}", ex);
                return false;
            }
        }

        public bool StopListeningMode()
        {
            try
            {
                _isInListeningMode = false;
                _triggerPhraseDetected = false;
                OnDictationStateChanged.Invoke(this, _triggerPhraseDetected);
                _listeningBuffer.Clear();
                if (_transcriptionService?.IsTranscribing == true)
                {
                    _transcriptionService.StopTranscription();
                    return true;
                }
                return false;
                
            }
            catch (Exception e)
            {
                LoggingService.LogError(e.Message, e, "AddInViewModel.StopTranscription");
                return false;
            }
            //_textBufferService.Stop();
        }

        /// <summary>
        /// Toggles transcription on or off
        /// </summary>
        public bool ToggleTranscription()
        {
            if (_transcriptionService == null)
            {
                OnErrorOccurred("Transcription service is not initialized.");
                return false;
            }

            try
            {
                // Start or stop transcription
                _transcriptionService.ToggleTranscription(HandleTranscribedText, _transcriptionSettings, _selectedDeviceNumber);

                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error toggling transcription: {ex.Message}", ex);
                return false;
            }
        }

        private void InitializeTextInsertion()
        {
            _insertionTimer = new System.Timers.Timer(INSERTION_INTERVAL_MS);
            _insertionTimer.Elapsed += (sender, e) => FlushAccumulatedText();
            _insertionTimer.AutoReset = true;
            _insertionTimer.Start();
        }

        /// <summary>
        /// Optimized transcribed text handler with reduced overhead
        /// </summary>
        private void HandleTranscribedText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Apply keyword replacements immediately
            text = ApplyKeywordReplacements(text);

            lock (_accumulatorLock)
            {
                _textAccumulator.Append(text).Append(" ");
            }
        }

        private void FlushAccumulatedText()
        {
            string textToInsert;

            lock (_accumulatorLock)
            {
                if (_textAccumulator.Length == 0)
                    return;

                textToInsert = _textAccumulator.ToString().Trim();
                _textAccumulator.Clear();
            }

            // Direct UI thread execution without marshaling overhead
            if (_documentService.IsDocumentAvailable)
            {
                try
                {
                    _documentService.InsertText(textToInsert + " ");
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"Error inserting text: {ex.Message}", ex);
                }
            }
        }

        private string ApplyKeywordReplacements(string text)
        {
            if (_keywordsToReplace.Count == 0)
                return text;

            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (_keywordsToReplace.TryGetValue(words[i], out var replacement))
                {
                    words[i] = replacement;
                }
            }

            return string.Join(" ", words);
        }

        private void HandleTranscribedTextListeningMode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            lock (_listeningLock)
            {
                // Add text to listening buffer
                _listeningBuffer.Append(text + " ");

                // Trim buffer if it gets too large
                if (_listeningBuffer.Length > _transcriptionSettings.ListeningModeBufferSize * 10) // Rough estimate of 10 chars per word
                {
                    var bufferText = _listeningBuffer.ToString();
                    var words = bufferText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    if (words.Length > _transcriptionSettings.ListeningModeBufferSize)
                    {
                        var keepWords = words.Skip(words.Length - _transcriptionSettings.ListeningModeBufferSize);
                        _listeningBuffer.Clear();
                        _listeningBuffer.Append(string.Join(" ", keepWords) + " ");
                    }
                }

                var currentBuffer = _listeningBuffer.ToString().ToLowerInvariant().Replace(",", "").Replace(".", "");

                if (currentBuffer.Contains("diktat start"))
                {
                    _listeningBuffer.Clear();
                    LoggingService.PlayDictationModeChangeSound();
                    _triggerPhraseDetected = true;
                    OnDictationStateChanged.Invoke(this, _triggerPhraseDetected);
                    return;
                }
                if (currentBuffer.Contains("diktat beenden"))
                {
                    _listeningBuffer.Clear();
                    LoggingService.PlayDictationModeChangeSound();
                    _triggerPhraseDetected = false;
                    OnDictationStateChanged.Invoke(this, _triggerPhraseDetected);
                    return;
                }

                if (_triggerPhraseDetected)
                {
                    HandleTranscribedText(text);
                }
            }
        }

        /// <summary>
        /// Gets a list of available microphones
        /// </summary>
        public List<MicrophoneDevice> GetMicrophoneList()
        {
            return _transcriptionProvider.GetAvailableMicrophones();
        }

        /// <summary>
        /// Sets the microphone device to use for transcription
        /// </summary>
        public void SetDeviceNumber(int deviceNumber)
        {
            _selectedDeviceNumber = deviceNumber;
        }

        /// <summary>
        /// Raises the ErrorOccurred event
        /// </summary>
        protected virtual void OnErrorOccurred(string message, Exception ex = null)
        {
            LoggingService.LogError(message, ex, "AddInViewModel_OnErrorOccurred");
            ErrorOccurred?.Invoke(this, message);
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            try
            {
                _insertionTimer?.Stop();
                _insertionTimer?.Dispose();

                // Flush any remaining text
                FlushAccumulatedText();

                if (_transcriptionService?.IsTranscribing == true)
                {
                    _transcriptionService.StopTranscription();
                }

                System.Threading.Thread.Sleep(500);
                _transcriptionService?.Dispose();
                _isDisposed = true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error during disposal: {ex.Message}", ex);
            }
        }

        public void SetModelType(GgmlType modelType)
        {
            if (_transcriptionService != null)
            {
                _transcriptionService.ChangeModelType(modelType);
            }
        }

        internal List<GgmlType> GetModelTypeList()
        {
            return new List<GgmlType>
            {
                GgmlType.LargeV3Turbo,
                GgmlType.Base,
                GgmlType.Tiny,
                GgmlType.Small,
                GgmlType.Medium,
                GgmlType.LargeV3,
            };
        }

        internal List<string> GetLanguageList()
        {
            return new List<string>
            {
                "de-DE",
                "en-US",
                "fr-FR",
                "it-IT",
                "es-ES",
            };
        }

        internal void ChangeSilenceThreshold(int threshold)
        {
            _transcriptionSettings.SilenceThreshold = TimeSpan.FromSeconds(threshold);
            _transcriptionService.ChangeSilenceThreshold(threshold);
        }
    }
}