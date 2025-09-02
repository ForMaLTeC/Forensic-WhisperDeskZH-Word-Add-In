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
        private readonly TextBufferService _textBufferService;
        private readonly SynchronizationContext _uiContext;

        private static Dictionary<string, string> _keywordsToReplace;

        private ITranscriptionService _transcriptionService;
        private int _selectedDeviceNumber = 0;
        private bool _isDisposed = false;
        private bool _isInListeningMode = false;
        private bool _triggerPhraseDetected = false;
        private string _lastTextAdded = string.Empty;

        public TranscriptionSettings _transcriptionSettings { get; }
        public bool IsTranscribing => _transcriptionService?.IsTranscribing ?? false;

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

            // Capture UI context for later marshaling
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            // Create text buffer service
            _textBufferService = new TextBufferService(_transcriptionSettings.InsertionInterval, InsertTextIntoDocument);
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

            Globals.ThisAddIn.LogMessage("AddInViewModel initialized successfully.", "AddInViewModel_init");
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
                task.Wait(5000); // 5 second timeout
                
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
                _transcriptionService.ToggleTranscription(HandleTranscribedTextListeningMode,_transcriptionSettings, _selectedDeviceNumber);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error starting listening mode: {ex.Message}", ex);
                return false;
            }
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

                if (_transcriptionService.IsTranscribing)
                {
                    // Start the text buffer service
                    _textBufferService.Start();
                }
                else
                {
                    // Stop the text buffer service
                    _textBufferService.Stop();
                }

                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error toggling transcription: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Handles new transcribed text from the transcription service
        /// </summary>
        private void HandleTranscribedText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Add text to the buffer service
            _textBufferService.AddText(text);
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

                var currentBuffer = _listeningBuffer.ToString().ToLowerInvariant();

                if (currentBuffer.Contains("diktat starten"))
                {
                    _listeningBuffer.Clear();
                    // Play Confirmation sound
                    LoggingService.PlayDictationModeChangeSound();
                    _triggerPhraseDetected = true;
                }
                if(currentBuffer.Contains("diktat beenden"))
                {
                    _listeningBuffer.Clear();
                    LoggingService.PlayDictationModeChangeSound();
                    _triggerPhraseDetected = false;
                }

                if (TriggerPhraseDetected)
                {
                    _textBufferService.AddText(text);
                }
            }
        }

        /// <summary>
        /// Inserts text into the document on the UI thread
        /// </summary>
        private void InsertTextIntoDocument(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // replace keywords in text
            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (_keywordsToReplace.TryGetValue(words[i], out var replacement))
                {
                    words[i] = replacement;
                }
            }

            // recombine words into text
            text = string.Join(" ", words);
            // add space at the end to avoid merging with next word
            text += " ";

            // call overlap algorithm to find overlapping words

            // Marshal to UI thread
            _uiContext.Post(_ =>
            {
                try
                {
                    if (!_documentService.InsertText(text))
                    {
                        OnErrorOccurred("Failed to insert text: No active document.");
                    }
                    else
                    {
                        _lastTextAdded = text;
                    }
                }
                catch (Exception ex)
                {
                    OnErrorOccurred($"Error inserting text: {ex.Message}", ex);
                }
            }, null);
        }

        /// <summary>
        /// Gets a list of available microphones
        /// </summary>
        public System.Collections.Generic.List<MicrophoneDevice> GetMicrophoneList()
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
            Globals.ThisAddIn.LogMessage($"ERROR: {message}", "AddInViewModel_OnErrorOccurred");
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
                // Stop transcription first to ensure proper cleanup
                if (_transcriptionService?.IsTranscribing == true)
                {
                    _transcriptionService.StopTranscription();
                }

                // Wait a moment for transcription to stop completely
                System.Threading.Thread.Sleep(500);

                _textBufferService?.Dispose();
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
    }
}