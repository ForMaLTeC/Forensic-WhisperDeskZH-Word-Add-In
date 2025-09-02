using ForensicWhisperDeskZH.Common;
using ForensicWhisperDeskZH.Document;
using System;

namespace ForensicWhisperDeskZH
{
    public partial class ThisAddIn
    {
        internal AddInViewModel AddInViewModel { get; private set; }

        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            try
            {
                LoggingService.LogMessage("Add-in starting", "ThisAddIn_Startup");

                // Create WordDocumentService with the Word Application
                var documentService = new WordDocumentService(Application);

                // Create the view model with appropriate dependencies
                AddInViewModel = AddInViewModel.CreateAsync(
                    new Transcription.WhisperTranscriptionServiceProvider(),
                    documentService,
                    ConfigurationManager.LoadTranscriptionSettings(),
                    ConfigurationManager.LoadKeywordReplacements()).Result;

                // Subscribe to view model events
                AddInViewModel.ErrorOccurred += (s, message) =>
                {
                    LoggingService.LogError(message, null, "AddInViewModel");
                };

                LoggingService.LogMessage("Add-in started successfully", "ThisAddIn_Startup");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Failed to initialize add-in", ex, "ThisAddIn_Startup");
                throw;
            }
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
            try
            {
                // Save settings on shutdown
                ConfigurationManager.SaveTranscriptionSettings(AddInViewModel._transcriptionSettings);

                // Cleanup resources
                AddInViewModel?.Dispose();

                LoggingService.LogMessage("Add-in shut down successfully", "ThisAddIn_Shutdown");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Error during add-in shutdown", ex, "ThisAddIn_Shutdown");
            }
        }

        // Legacy logging methods for backward compatibility
        public void LogException(Exception ex, string location = "ThisAddIn")
        {
            LoggingService.LogError("Exception occurred", ex, location);
        }

        public void LogMessage(string message, string location = "ThisAddIn")
        {
            LoggingService.LogMessage(message, location);
        }

        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new EventHandler(ThisAddIn_Startup);
            this.Shutdown += new EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}