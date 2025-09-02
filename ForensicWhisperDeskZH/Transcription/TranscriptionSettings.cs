using NAudio.Wave;
using System;
using Whisper.net.Ggml;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Configuration settings for the transcription service, including audio processing parameters,
    /// Whisper model settings, and text processing options.
    /// </summary>
    public class TranscriptionSettings
    {
        /// <summary>
        /// Gets or sets the interval at which transcribed text is inserted into the document.
        /// Default is 100 milliseconds.
        /// </summary>
        public TimeSpan InsertionInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Gets or sets the duration of silence required to consider a word boundary.
        /// Default is 500 milliseconds.
        /// </summary>
        public TimeSpan SilenceThreshold { get; set; } = TimeSpan.FromMilliseconds(500);


        /// <summary>
        /// Gets or sets the duration of each audio chunk processed by the transcription engine.
        /// Default is 3 seconds.
        /// </summary>
        public TimeSpan ChunkDuration
        {
            get => _chunkDuration;
            set
            {
                _chunkDuration = value;
            }
        }
        private TimeSpan _chunkDuration = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the audio wave format for recording and processing.
        /// Default is 16kHz, 16-bit, mono.
        /// </summary>
        public WaveFormat WaveFormat { get; set; } = new WaveFormat(16000, 16, 1);

        /// <summary>
        /// Gets or sets the language code for transcription (e.g., "en-US", "de-DE").
        /// Default is "de-DE" (German).
        /// </summary>
        public string Language { get; set; } = "de-DE";

        /// <summary>
        /// Gets or sets the number of CPU threads to use for transcription processing.
        /// Default is the number of processor cores available.
        /// </summary>
        public int Threads { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets whether to translate the transcribed text to English.
        /// Default is false.
        /// </summary>
        public bool TranslateToEnglish { get; set; } = false;

        /// <summary>
        /// Gets or sets the temperature parameter for Whisper model sampling.
        /// Higher values (e.g., 1.0) make output more random, lower values (e.g., 0.0) make it more deterministic.
        /// Default is 0.0.
        /// </summary>
        public float Temperature { get; set; } = 0.0f;

        /// <summary>
        /// Gets or sets the beam size for beam search decoding strategy.
        /// Larger values may improve accuracy but increase processing time.
        /// Default is 5.
        /// </summary>
        public int BeamSize { get; set; } = 5;

        /// <summary>
        /// Gets or sets whether to use greedy sampling strategy instead of beam search.
        /// Greedy is faster but may be less accurate than beam search.
        /// Default is false (uses beam search).
        /// </summary>
        public bool UseGreedyStrategy { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to automatically add a period at the end of paragraphs.
        /// Default is false.
        /// </summary>
        public bool EndParagraphWithPeriod { get; set; } = false;

        /// <summary>
        /// Gets or sets the file path to the Whisper model.
        /// Default is "ggml-turbo.bin".
        /// </summary>
        public string ModelPath { get; set; } = "ggml-turbo.bin";

        /// <summary>
        /// Gets or sets the type of Whisper model to use for transcription.
        /// Different models offer trade-offs between speed and accuracy.
        /// Default is LargeV3Turbo.
        /// </summary>
        public GgmlType ModelType { get; set; } = GgmlType.LargeV3Turbo;

        /// <summary>
        /// Gets or sets whether to automatically capitalize the first letter of transcribed text.
        /// Default is false.
        /// </summary>
        public bool CapitalizeFirstLetter { get; set; } = false;

        /// <summary>
        /// Gets or sets whether to automatically add punctuation to transcribed text.
        /// Default is false.
        /// </summary>
        public bool AutoPunctuation { get; set; } = false;

        /// <summary>
        /// Gets a new instance of TranscriptionSettings with default values.
        /// </summary>
        public static TranscriptionSettings Default => new TranscriptionSettings();

        public int ListeningModeBufferSize { get; internal set; } = 1000;

        /// <summary>
        /// Resets all transcription settings to their default values.
        /// </summary>
        public void ResetTranscriptionSettings()
        {
            InsertionInterval = TimeSpan.FromMilliseconds(100);
            SilenceThreshold = TimeSpan.FromSeconds(500);
            ChunkDuration = TimeSpan.FromSeconds(3);
            WaveFormat = new WaveFormat(16000, 16, 1);
            Threads = Environment.ProcessorCount;
            TranslateToEnglish = false;
            Temperature = 0.0f;
            BeamSize = 5;
            UseGreedyStrategy = false;
            EndParagraphWithPeriod = false;
            CapitalizeFirstLetter = false;
            AutoPunctuation = false;
        }
    }
}