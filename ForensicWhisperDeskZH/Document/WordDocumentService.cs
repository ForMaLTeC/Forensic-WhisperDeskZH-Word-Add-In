using Microsoft.Office.Interop.Word;
using System;

namespace ForensicWhisperDeskZH.Document
{
    /// <summary>
    /// Service for interacting with Word documents through the Word API
    /// </summary>
    public class WordDocumentService : IDocumentService
    {
        private readonly Application _application;

        public event EventHandler<DocumentErrorEventArgs> Error;

        public WordDocumentService(Application application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        /// <summary>
        /// Checks if a document is available for text insertion
        /// </summary>
        public bool IsDocumentAvailable
        {
            get
            {
                try
                {
                    return _application.Documents.Count > 0 && _application.ActiveDocument != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Inserts text at the current cursor position
        /// </summary>
        public bool InsertText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            try
            {
                if (!IsDocumentAvailable)
                    return false;

                _application.Selection.TypeText(text);
                return true;
            }
            catch (Exception ex)
            {
                OnError(new DocumentErrorEventArgs("Error inserting text into document", ex));

                return false;
            }
        }

        protected virtual void OnError(DocumentErrorEventArgs e)
        {
            Error?.Invoke(this, e);
        }
    }
}