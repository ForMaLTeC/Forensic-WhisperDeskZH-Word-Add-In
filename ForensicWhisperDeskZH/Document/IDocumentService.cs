using System;

namespace ForensicWhisperDeskZH.Document
{
    /// <summary>
    /// Service for interacting with Word documents
    /// </summary>
    public interface IDocumentService
    {
        /// <summary>
        /// Inserts text at the current cursor position in the active document
        /// </summary>
        /// <param name="text">Text to insert</param>
        /// <returns>True if successful, false otherwise</returns>
        bool InsertText(string text);

        /// <summary>
        /// Checks if a document is available for text insertion
        /// </summary>
        bool IsDocumentAvailable { get; }

        /// <summary>
        /// Occurs when an error happens during document operations
        /// </summary>
        event EventHandler<DocumentErrorEventArgs> Error;
    }

    /// <summary>
    /// Arguments for document error events
    /// </summary>
    public class DocumentErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception Exception { get; }

        public DocumentErrorEventArgs(string message, Exception exception = null)
        {
            Message = message;
            Exception = exception;
        }
    }
}