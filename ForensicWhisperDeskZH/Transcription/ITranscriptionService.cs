using System;
using System.Globalization;
using System.IO;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Defines core functionality for a speech-to-text transcription service
    /// </summary>
    public interface ITranscriptionService : IDisposable
    {
        /// <summary>
        /// Occurs when transcription begins
        /// </summary>
        event EventHandler<TranscriptionEventArgs> TranscriptionStarted;
        
        /// <summary>
        /// Occurs when transcription ends
        /// </summary>
        event EventHandler<TranscriptionEventArgs> TranscriptionStopped;
        
        /// <summary>
        /// Occurs when a new piece of transcription text is available
        /// </summary>
        event EventHandler<TranscriptionResultEventArgs> TranscriptionResult;
        
        /// <summary>
        /// Occurs when an error happens during transcription
        /// </summary>
        event EventHandler<ErrorEventArgs> TranscriptionError;
        
        /// <summary>
        /// Gets whether the service is actively transcribing
        /// </summary>
        bool IsTranscribing { get; }
        
        /// <summary>
        /// Starts transcription using the specified microphone
        /// </summary>
        /// <param name="textHandler">Handler for transcribed text</param>
        /// <param name="deviceNumber">Microphone device number</param>
        /// <param name="language">Optional language to use</param>
        void StartTranscription(Action<string> textHandler, int deviceNumber = 0, CultureInfo language = null);
        
        /// <summary>
        /// Stops active transcription
        /// </summary>
        void StopTranscription();
        
        /// <summary>
        /// Toggles transcription on/off
        /// </summary>
        /// <param name="textHandler">Handler for transcribed text</param>
        /// <param name="deviceNumber">Microphone device number</param>
        void ToggleTranscription(Action<string> textHandler, TranscriptionSettings transcriptionSettings, int deviceNumber = 0);
        
        /// <summary>
        /// Changes the language used for transcription
        /// </summary>
        /// <param name="language">Two-letter language code</param>
        void ChangeLanguage(string language);

        void ChangeModelType(GgmlType modelType);

    }
}