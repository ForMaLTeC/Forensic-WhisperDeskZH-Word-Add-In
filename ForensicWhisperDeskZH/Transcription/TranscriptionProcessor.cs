using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForensicWhisperDeskZH.Common;
using NAudio.Wave;
using Whisper.net;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Processes audio data and manages the transcription workflow
    /// </summary>
    public class TranscriptionProcessor
    {
        private readonly WhisperProcessor _transcriptor;
        private readonly WhisperProcessorBuilder _processorBuilder;
        private readonly TextProcessor _textProcessor;
        private readonly WaveFormat _waveFormat;
        private readonly HashSet<string> _processedSegmentIds = new HashSet<string>();
        private string _lastTranscribedText = string.Empty;

        public TranscriptionProcessor(
            WhisperProcessor transcriptor,
            WhisperProcessorBuilder processorBuilder,
            TextProcessor textProcessor,
            WaveFormat waveFormat)
        {
            _transcriptor = transcriptor ?? throw new ArgumentNullException(nameof(transcriptor));
            _processorBuilder = processorBuilder ?? throw new ArgumentNullException(nameof(processorBuilder));
            _textProcessor = textProcessor ?? throw new ArgumentNullException(nameof(textProcessor));
            _waveFormat = waveFormat ?? throw new ArgumentNullException(nameof(waveFormat));
        }

        /// <summary>
        /// Transcribes an audio buffer and returns processed text
        /// </summary>
        public async Task<TranscriptionResult> TranscribeBufferAsync(
            MemoryStream audioBuffer,
            string sessionId,
            CancellationToken cancellationToken)
        {
            // Validate input buffer
            if (audioBuffer == null || audioBuffer.Length == 0)
            {
                Debug.WriteLine("TranscriptionProcessor: Empty or null audio buffer received");
                return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
            }

            Debug.WriteLine($"TranscriptionProcessor: Processing audio buffer with {audioBuffer.Length} bytes");

            // Analyze audio content before processing
            bool hasAudioContent = ValidateAudioContent(audioBuffer);
            if (!hasAudioContent)
            {
                Debug.WriteLine("TranscriptionProcessor: WARNING - Audio buffer contains only silence!");
                // Don't return early - let's still try to process and see what WAV file looks like
            }

            // Create a temporary file for the WAV data - but don't delete it immediately for debugging
            string tempFile = Path.Combine(Path.GetTempPath(), $"debug_audio_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
            
            try
            {
                Debug.WriteLine($"TranscriptionProcessor: Writing audio to temp file: {tempFile}");
                Debug.WriteLine($"TranscriptionProcessor: Wave format: {_waveFormat}");

                // Write the WAV file to disk with enhanced validation
                using (WaveFileWriter writer = new WaveFileWriter(tempFile, _waveFormat))
                {
                    audioBuffer.Position = 0;
                    byte[] audioData = audioBuffer.ToArray();

                    Debug.WriteLine($"TranscriptionProcessor: Writing {audioData.Length} bytes to WAV file");
                    
                    // Add detailed sample analysis before writing
                    AnalyzeAudioSamples(audioData, "Before WAV write");
                    
                    writer.Write(audioData, 0, audioData.Length);
                    writer.Flush(); // Ensure data is written to disk
                }

                // Validate the created WAV file
                var fileInfo = new FileInfo(tempFile);
                Debug.WriteLine($"TranscriptionProcessor: WAV file created - Size: {fileInfo.Length} bytes");

                if (fileInfo.Length < 100)
                {
                    Debug.WriteLine("TranscriptionProcessor: WARNING - WAV file is very small, may be empty!");
                    return new TranscriptionResult(string.Empty, string.Empty, new List<TranscriptionSegment>());
                }

                // Quick validation of WAV file content
                ValidateWavFile(tempFile);

                var segmentBuilder = new StringBuilder();
                var incrementalBuilder = new StringBuilder();
                var resultSegments = new List<TranscriptionSegment>();

                // Process the WAV file
                using (var fileStream = File.OpenRead(tempFile))
                {
                    Debug.WriteLine($"TranscriptionProcessor: Reading WAV file for processing - Size: {fileStream.Length} bytes");

                    await foreach (var segment in _transcriptor.ProcessAsync(fileStream, cancellationToken))
                    {
                        if (string.IsNullOrWhiteSpace(segment.Text))
                        {
                            Debug.WriteLine("TranscriptionProcessor: Empty segment received from Whisper");
                            continue;
                        }

                        Debug.WriteLine($"TranscriptionProcessor: Received segment: '{segment.Text}' ({segment.Start} - {segment.End})");

                        // Generate a unique ID for this segment
                        string segmentId = $"{segment.Start.TotalMilliseconds}-{segment.End.TotalMilliseconds}-{segment.Text.GetHashCode()}";

                        // Skip already processed segments
                        if (_processedSegmentIds.Contains(segmentId))
                        {
                            Debug.WriteLine($"TranscriptionProcessor: Skipping duplicate segment: {segmentId}");
                            continue;
                        }

                        _processedSegmentIds.Add(segmentId);

                        // Process the text (capitalize, add punctuation, etc.)
                        string processedText = _textProcessor.ProcessTranscribedText(segment.Text);

                        // Add to the full segment text for this buffer
                        segmentBuilder.Append(processedText);

                        // Extract incremental text compared to what we've seen before
                        string incrementalText = _textProcessor.GetIncrementalText(_lastTranscribedText, processedText);

                        // CRITICAL FIX: Don't dispose the transcriptor! This was breaking everything
                        if (!string.IsNullOrEmpty(incrementalText))
                        {
                            Debug.WriteLine($"TranscriptionProcessor: Processing incremental text: '{incrementalText}'");

                            // Just append without rebuilding the processor
                            incrementalBuilder.Append(incrementalText);

                            // Add to result segments
                            resultSegments.Add(new TranscriptionSegment(
                                incrementalText,
                                segment.Start,
                                segment.End,
                                sessionId));

                            // Update text record
                            _lastTranscribedText += incrementalText;
                        }
                    }
                }

                Debug.WriteLine($"TranscriptionProcessor: Completed processing - Full text: '{segmentBuilder}', Incremental: '{incrementalBuilder}'");

                return new TranscriptionResult(
                    segmentBuilder.ToString(),
                    incrementalBuilder.ToString(),
                    resultSegments);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranscriptionProcessor: Error processing audio: {ex.Message}");
                Debug.WriteLine($"TranscriptionProcessor: Stack trace: {ex.StackTrace}");
                LoggingService.LogError($"TranscriptionProcessor: Error processing audio: {ex.Message}\n Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException("Failed to transcribe audio buffer", ex);
            }
            finally
            {
                // For debugging, don't delete the temp file immediately
                Debug.WriteLine($"TranscriptionProcessor: Temp file saved for debugging: {tempFile}");
                
                // Uncomment this after debugging:
                // try 
                // { 
                //     if (File.Exists(tempFile))
                //     {
                //         File.Delete(tempFile);
                //         Debug.WriteLine($"TranscriptionProcessor: Cleaned up temp file: {tempFile}");
                //     }
                // } 
                // catch (Exception ex)
                // {
                //     Debug.WriteLine($"TranscriptionProcessor: Failed to delete temp file: {ex.Message}");
                // }
            }
        }

        /// <summary>
        /// Analyzes audio samples and provides detailed information
        /// </summary>
        private void AnalyzeAudioSamples(byte[] audioData, string context)
        {
            try
            {
                if (audioData.Length < 2) return;

                int totalSamples = audioData.Length / 2; // 16-bit samples
                int nonZeroSamples = 0;
                short minSample = short.MaxValue;
                short maxSample = short.MinValue;
                long sumSquares = 0;

                for (int i = 0; i < audioData.Length - 1; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    
                    if (sample != 0) nonZeroSamples++;
                    if (sample < minSample) minSample = sample;
                    if (sample > maxSample) maxSample = sample;
                    sumSquares += (long)sample * sample;
                }

                double rms = Math.Sqrt((double)sumSquares / totalSamples);
                double audioPercentage = totalSamples > 0 ? (double)nonZeroSamples / totalSamples * 100 : 0;

                Debug.WriteLine($"TranscriptionProcessor: {context} - Audio Analysis:");
                Debug.WriteLine($"  Total samples: {totalSamples}");
                Debug.WriteLine($"  Non-zero samples: {nonZeroSamples} ({audioPercentage:F2}%)");
                Debug.WriteLine($"  Sample range: {minSample} to {maxSample}");
                Debug.WriteLine($"  RMS level: {rms:F2}");
                Debug.WriteLine($"  First 10 samples: {string.Join(", ", Enumerable.Range(0, Math.Min(10, totalSamples)).Select(i => BitConverter.ToInt16(audioData, i * 2)))}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranscriptionProcessor: Error analyzing audio samples: {ex.Message}");
                LoggingService.LogError($"TranscriptionProcessor: Error analyzing audio samples: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that the audio buffer contains actual audio data (not just silence)
        /// </summary>
        private bool ValidateAudioContent(MemoryStream audioBuffer)
        {
            try
            {
                audioBuffer.Position = 0;
                byte[] data = audioBuffer.ToArray();
                audioBuffer.Position = 0; // Reset position

                int nonSilentSamples = 0;
                int totalSamples = 0;

                // Check 16-bit samples
                for (int i = 0; i < data.Length - 1; i += 2)
                {
                    short sample = BitConverter.ToInt16(data, i);
                    totalSamples++;

                    if (Math.Abs(sample) > 100) // Threshold for non-silence
                    {
                        nonSilentSamples++;
                    }
                }

                double audioPercentage = totalSamples > 0 ? (double)nonSilentSamples / totalSamples * 100 : 0;
                Debug.WriteLine($"TranscriptionProcessor: Audio analysis - {nonSilentSamples}/{totalSamples} samples have audio content ({audioPercentage:F1}%)");

                return audioPercentage > 0.1; // Lower threshold - even 0.1% should indicate some audio
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranscriptionProcessor: Error validating audio content: {ex.Message}");
                LoggingService.LogError($"TranscriptionProcessor: Error validating audio content: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates the created WAV file
        /// </summary>
        private void ValidateWavFile(string filePath)
        {
            try
            {
                using (var reader = new WaveFileReader(filePath))
                {
                    Debug.WriteLine($"TranscriptionProcessor: WAV validation - Format: {reader.WaveFormat}");
                    Debug.WriteLine($"TranscriptionProcessor: WAV validation - Duration: {reader.TotalTime}");
                    Debug.WriteLine($"TranscriptionProcessor: WAV validation - Samples: {reader.SampleCount}");

                    // Read a small portion to check for audio content
                    var buffer = new byte[Math.Min(8192, (int)reader.Length)]; // Read up to 8KB or entire file
                    int bytesRead = reader.Read(buffer, 0, buffer.Length);

                    int nonZeroSamples = 0;
                    int totalSamples = bytesRead / 2; // 16-bit samples

                    for (int i = 0; i < bytesRead - 1; i += 2)
                    {
                        short sample = BitConverter.ToInt16(buffer, i);
                        if (sample != 0)
                        {
                            nonZeroSamples++;
                        }
                    }

                    Debug.WriteLine($"TranscriptionProcessor: WAV validation - {nonZeroSamples}/{totalSamples} samples contain audio ({(totalSamples > 0 ? (double)nonZeroSamples / totalSamples * 100 : 0):F2}%)");
                    
                    // Show first few samples for debugging
                    if (bytesRead >= 20)
                    {
                        var firstSamples = new short[10];
                        for (int i = 0; i < 10; i++)
                        {
                            firstSamples[i] = BitConverter.ToInt16(buffer, i * 2);
                        }
                        Debug.WriteLine($"TranscriptionProcessor: WAV validation - First 10 samples: {string.Join(", ", firstSamples)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TranscriptionProcessor: Error validating WAV file: {ex.Message}");
                LoggingService.LogError($"TranscriptionProcessor: Error validating WAV file: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a transcription segment with timing information
    /// </summary>
    public class TranscriptionSegment
    {
        public string Text { get; }
        public System.TimeSpan Start { get; }
        public TimeSpan End { get; }
        public string SessionId { get; }

        public TranscriptionSegment(string text, TimeSpan start, TimeSpan end, string sessionId)
        {
            Text = text;
            Start = start;
            End = end;
            SessionId = sessionId;
        }
    }

    /// <summary>
    /// Represents the result of a transcription operation
    /// </summary>
    public class TranscriptionResult
    {
        public string FullText { get; }
        public string IncrementalText { get; }
        public IReadOnlyList<TranscriptionSegment> Segments { get; }

        public TranscriptionResult(
            string fullText,
            string incrementalText,
            IReadOnlyList<TranscriptionSegment> segments)
        {
            FullText = fullText;
            IncrementalText = incrementalText;
            Segments = segments;
        }
    }
}