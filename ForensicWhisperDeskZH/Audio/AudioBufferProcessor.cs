using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebRtcVadSharp;
using ForensicWhisperDeskZH.Utils;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Processes audio buffers, handling overlapping chunks with improved responsiveness
    /// </summary>
    public class AudioBufferProcessor : IDisposable
    {
        #region Fields
        private readonly TimeSpan _minChunkDuration;
        private readonly int _bytesPerMillisecond;
        private MemoryStream _activeBuffer;
        private MemoryStream _processingBuffer;
        private readonly object _bufferLock = new object();
        private readonly ConcurrentQueue<MemoryStream> _processedChunks = new ConcurrentQueue<MemoryStream>();
        private readonly SemaphoreSlim _chunkAvailableSemaphore = new SemaphoreSlim(0);
        private readonly Timer _chunkTimer;
        private readonly MemoryStreamPool _streamPool = new MemoryStreamPool();
        private bool _isDisposed;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private WebRtcVad _vad;
        private bool _vadInitialized = false;
        private readonly object _vadLock = new object();
        private readonly int _silenceThresholdMs;
        private const int FRAME_SIZE_SAMPLES = 320; // 20ms at 16kHz
        private const int FRAME_SIZE_BYTES = FRAME_SIZE_SAMPLES * 2;
        private const int MAX_CHUNK_DURATION_MS = 30000; // 30 seconds maximum
        private const int MIN_PROCESSING_DURATION_MS = 1000; // 1 second minimum for processing
        private const int OVERLAP_DURATION_MS = 500; // 500ms overlap for word boundary detection
        #endregion

        #region Events
        /// <summary>
        /// Occurs when a processed audio chunk is available
        /// </summary>
        public event EventHandler<ProcessedAudioEventArgs> ChunkReady;
        #endregion

        /// <summary>
        /// Creates a new audio buffer processor
        /// </summary>
        /// <param name="bytesPerMillisecond">Bytes per millisecond based on audio format</param>
        /// <param name="minChunkDuration">Duration of each processed chunk (minimum duration)</param>
        /// <param name="silenceThreshold">Duration of silence required for word boundary detection</param>
        public AudioBufferProcessor(
            int bytesPerMillisecond,
            TimeSpan minChunkDuration,
            TimeSpan silenceThreshold)
        {
            _bytesPerMillisecond = bytesPerMillisecond;
            _minChunkDuration = minChunkDuration;
            _silenceThresholdMs = (int)silenceThreshold.TotalMilliseconds;

            // Initialize both buffers from the pool
            _activeBuffer = _streamPool.GetStream();
            _processingBuffer = _streamPool.GetStream();

            // Set up timer to process chunks at regular intervals
            _chunkTimer = new Timer(ProcessChunk, null, Timeout.Infinite, Timeout.Infinite);

            // Start the consumer task
            Task.Run(ConsumeChunksAsync);

            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Initialized with min chunk duration: {minChunkDuration.TotalMilliseconds}ms, silence threshold: {silenceThreshold.TotalMilliseconds}ms, bytes/ms: {bytesPerMillisecond}");
            LoggingService.LogMessage($"AudioBufferProcessor: Initialized with min chunk duration: {minChunkDuration.TotalMilliseconds}ms, silence threshold: {silenceThreshold.TotalMilliseconds}ms, bytes/ms: {bytesPerMillisecond}", "AudioBufferProcessor_init");
        }

        /// <summary>
        /// Starts processing audio chunks at the specified interval
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            // Use a more frequent timer for better responsiveness
            int interval = Math.Max(1000, (int)_minChunkDuration.TotalMilliseconds / 2);
            _chunkTimer.Change(interval, interval);
            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Started with {interval}ms timer interval");
        }

        /// <summary>
        /// Stops processing audio chunks
        /// </summary>
        public void Stop()
        {
            System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Stopping...");
            _chunkTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _cts.Cancel();

            // Process any remaining audio
            ProcessChunk(null);
            System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Stopped");
        }

        /// <summary>
        /// Adds audio data to the buffer for processing
        /// </summary>
        public void AddAudioData(ReadOnlySpan<byte> audioData)
        {
            if (audioData.Length == 0)
                return;

            if (_isDisposed)
                return; // Silently ignore if disposed

            lock (_bufferLock)
            {
                if (_isDisposed) // Check again inside lock
                    return;

                try
                {
                    // Convert ReadOnlySpan<byte> to byte array and write to active buffer
                    byte[] audioDataArray = audioData.ToArray();
                    _activeBuffer.Write(audioDataArray, 0, audioDataArray.Length);

                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Added {audioData.Length} bytes, total buffer size: {_activeBuffer.Length}");
                }
                catch (ObjectDisposedException)
                {
                    // Buffer was disposed between checks - ignore silently
                    System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Buffer disposed while adding audio data");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error adding audio data: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Improved ProcessChunk method that respects minimum duration and silence threshold
        /// </summary>
        private void ProcessChunk(object state)
        {
            if (_isDisposed)
                return;

            MemoryStream bufferToProcess;
            lock (_bufferLock)
            {
                if (_isDisposed)
                    return;

                // Only process if we have at least the minimum chunk duration worth of audio
                int minChunkBytes = (int)(_minChunkDuration.TotalMilliseconds * _bytesPerMillisecond);
                
                if (_activeBuffer.Length < minChunkBytes)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Buffer too small ({_activeBuffer.Length} bytes < {minChunkBytes} bytes), waiting for more audio");
                    return;
                }

                // Check if we have reached maximum duration - force processing if so
                int maxProcessingBytes = _bytesPerMillisecond * MAX_CHUNK_DURATION_MS;
                bool forceProcessing = _activeBuffer.Length >= maxProcessingBytes;

                if (!forceProcessing)
                {
                    // For normal processing, only process what we have up to max duration
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Normal processing - have {_activeBuffer.Length} bytes, min required: {minChunkBytes} bytes");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Force processing - buffer reached max size ({_activeBuffer.Length} bytes >= {maxProcessingBytes} bytes)");
                }

                // Process all available audio (up to max duration)
                int bytesToProcess = Math.Min((int)_activeBuffer.Length, maxProcessingBytes);
                
                // Create buffer with the audio to process
                bufferToProcess = _streamPool.GetStream();
                _activeBuffer.Position = 0;
                
                byte[] tempBuffer = new byte[bytesToProcess];
                _activeBuffer.Read(tempBuffer, 0, bytesToProcess);
                bufferToProcess.Write(tempBuffer, 0, bytesToProcess);
                
                // Clear the active buffer since we're processing all of it
                _activeBuffer.SetLength(0);
                _activeBuffer.Position = 0;

                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Processing {bytesToProcess} bytes ({(double)bytesToProcess / _bytesPerMillisecond:F0}ms of audio)");
            }

            try
            {
                if (_isDisposed)
                    return;

                // Process buffer for word boundaries using VAD - this will respect minimum duration + silence threshold
                var wordBoundaryChunks = DetectWordBoundaries(bufferToProcess);

                foreach (var wordChunk in wordBoundaryChunks)
                {
                    if (_isDisposed)
                        break;

                    if (wordChunk.Length > 0)
                    {
                        var chunkStream = new MemoryStream(wordChunk);
                        _processedChunks.Enqueue(chunkStream);
                        _chunkAvailableSemaphore.Release();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Object disposed during chunk processing");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error processing chunk: {ex.Message}");
                LoggingService.LogError($"AudioBufferProcessor: Error processing chunk: {ex.Message}", ex, "AudioBufferProcessor_ProcessChunk");
            }
            finally
            {
                try
                {
                    bufferToProcess.SetLength(0);
                    bufferToProcess.Position = 0;
                    _streamPool.ReturnStream(bufferToProcess);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error returning stream to pool: {ex.Message}");
                }
            }
        }

        private bool EnsureVadInitialized()
        {
            if (_vadInitialized) return true;

            lock (_vadLock)
            {
                if (_vadInitialized) return true;

                try
                {                    
                    _vad = new WebRtcVad();
                    _vad.OperatingMode = OperatingMode.Aggressive;
                    _vadInitialized = true;
                    System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: VAD initialized successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Failed to initialize VAD: {ex.Message}");
                    LoggingService.LogError($"AudioBufferProcessor: Failed to initialize VAD: {ex.Message} \n" + 
                                    $"DLL Load Path: {System.Reflection.Assembly.GetExecutingAssembly().Location}\n" + 
                                    $"Inner Exception: {ex.InnerException}\n StackTrace {ex.StackTrace}", 
                                    ex, "AudioBufferProcessor_EnsureVadInitialized");

                    _vadInitialized = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Energy-based silence detection as fallback when VAD is not available
        /// </summary>
        private bool DetectSilenceWithoutVad(byte[] frameBuffer)
        {
            if (frameBuffer.Length < 2) return true;
            
            long sumSquares = 0;
            int sampleCount = frameBuffer.Length / 2;
            
            for (int i = 0; i < frameBuffer.Length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(frameBuffer, i);
                sumSquares += (long)sample * sample;
            }
            
            double rms = Math.Sqrt((double)sumSquares / sampleCount);
            
            // Consider it silence if RMS is below threshold (adjust as needed)
            return rms < 500; // Threshold for 16-bit audio
        }

        /// <summary>
        /// Improved word boundary detection that properly respects minimum duration + silence threshold
        /// </summary>
        private List<byte[]> DetectWordBoundaries(MemoryStream audioBuffer)
        {
            if (_isDisposed)
                return new List<byte[]>();

            // Try to initialize VAD if not already done
            bool vadAvailable = EnsureVadInitialized();
            if (!vadAvailable)
            {
                LoggingService.LogMessage("AudioBufferProcessor: VAD not available, using energy-based detection", "AudioBufferProcessor_DetectWordBoundaries");
            }

            var chunks = new List<byte[]>();
            var currentChunk = new List<byte>();

            try
            {
                audioBuffer.Position = 0;
                byte[] frameBuffer = new byte[FRAME_SIZE_BYTES];
                int consecutiveSilenceFrames = 0;
                int consecutiveVoiceFrames = 0;
                
                // Calculate minimum chunk size in bytes based on _minChunkDuration
                int minChunkSizeBytes = (int)(_minChunkDuration.TotalMilliseconds * _bytesPerMillisecond);
                
                // Calculate maximum chunk size in bytes (30 seconds)
                int maxChunkSizeBytes = (int)(MAX_CHUNK_DURATION_MS * _bytesPerMillisecond);
                
                // Calculate silence threshold in frames (20ms per frame)
                int silenceThresholdFrames = _silenceThresholdMs / 20;
                
                // Require at least 2 consecutive voice frames to end silence
                const int minVoiceFramesToEndSilence = 2;

                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Min chunk: {minChunkSizeBytes} bytes ({_minChunkDuration.TotalMilliseconds}ms), Max chunk: {maxChunkSizeBytes} bytes ({MAX_CHUNK_DURATION_MS}ms), Silence threshold: {silenceThresholdFrames} frames ({_silenceThresholdMs}ms)");

                while (audioBuffer.Position < audioBuffer.Length - FRAME_SIZE_BYTES && !_isDisposed)
                {
                    int bytesRead = audioBuffer.Read(frameBuffer, 0, FRAME_SIZE_BYTES);

                    if (bytesRead == FRAME_SIZE_BYTES)
                    {
                        bool hasVoice = false;
                        
                        if (vadAvailable && _vad != null)
                        {
                            try
                            {
                                // Convert to samples for VAD
                                short[] samples = new short[FRAME_SIZE_SAMPLES];
                                for (int i = 0; i < FRAME_SIZE_SAMPLES; i++)
                                {
                                    samples[i] = BitConverter.ToInt16(frameBuffer, i * 2);
                                }

                                hasVoice = _vad.HasSpeech(samples);
                            }
                            catch (ObjectDisposedException)
                            {
                                vadAvailable = false;
                                System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: VAD disposed during processing");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: VAD error: {ex.Message}");
                                LoggingService.LogError($"AudioBufferProcessor: VAD error: {ex.Message}", ex, "AudioBufferProcessor_DetectWordBoundaries");
                                
                                // Disable VAD for this session if it's consistently failing
                                vadAvailable = false;
                                lock (_vadLock)
                                {
                                    try
                                    {
                                        _vad?.Dispose();
                                    }
                                    catch { }
                                    _vad = null;
                                    _vadInitialized = false;
                                }
                            }
                        }
                        else
                        {
                            // Fallback to energy-based detection
                            hasVoice = !DetectSilenceWithoutVad(frameBuffer);
                        }

                        // Always add frame to current chunk first
                        currentChunk.AddRange(frameBuffer);

                        // Update voice and silence counters based on voice activity
                        if (hasVoice)
                        {
                            consecutiveVoiceFrames++;
                            
                            // Only reset silence counter if we have enough consecutive voice frames
                            if (consecutiveVoiceFrames >= minVoiceFramesToEndSilence)
                            {
                                consecutiveSilenceFrames = 0;
                            }
                        }
                        else
                        {
                            consecutiveVoiceFrames = 0;
                            consecutiveSilenceFrames++;
                        }

                        // Check if we should cut the chunk
                        bool hasMinimumDuration = currentChunk.Count >= minChunkSizeBytes;
                        bool hasMaximumDuration = currentChunk.Count >= maxChunkSizeBytes;
                        bool hasSufficientSilence = consecutiveSilenceFrames >= silenceThresholdFrames;

                        // CORRECTED LOGIC: Cut chunk ONLY if:
                        // (minimum duration is met AND sufficient silence is detected) OR maximum duration is reached
                        if ((hasMinimumDuration && hasSufficientSilence) || hasMaximumDuration)
                        {
                            if (currentChunk.Count > 0)
                            {
                                double durationMs = (double)currentChunk.Count / _bytesPerMillisecond;
                                string cutReason = hasMaximumDuration ? "max duration" : "min duration + silence threshold";
                                
                                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Creating chunk with {currentChunk.Count} bytes (duration: {durationMs:F0}ms) - cut due to {cutReason}");
                                
                                chunks.Add(currentChunk.ToArray());
                                currentChunk.Clear();
                                consecutiveSilenceFrames = 0;
                                consecutiveVoiceFrames = 0;
                            }
                        }
                    }
                }

                // Handle remaining audio - return it to the buffer if it doesn't meet minimum duration
                if (currentChunk.Count > 0 && !_isDisposed)
                {
                    double finalDurationMs = (double)currentChunk.Count / _bytesPerMillisecond;
                    bool meetsMinimumDuration = currentChunk.Count >= minChunkSizeBytes;
                    
                    if (meetsMinimumDuration || chunks.Count == 0)
                    {
                        // Create final chunk if it meets minimum duration OR if it's the only audio we have
                        System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Creating final chunk with {currentChunk.Count} bytes (duration: {finalDurationMs:F0}ms)");
                        chunks.Add(currentChunk.ToArray());
                    }
                    else
                    {
                        // Return the remaining audio to the active buffer for future processing
                        System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Returning {currentChunk.Count} bytes (duration: {finalDurationMs:F0}ms) to buffer - doesn't meet minimum duration");
                        
                        lock (_bufferLock)
                        {
                            if (!_isDisposed)
                            {
                                // Insert the remaining audio at the beginning of the active buffer
                                byte[] remainingAudio = currentChunk.ToArray();
                                byte[] existingBuffer = _activeBuffer.ToArray();
                                
                                _activeBuffer.SetLength(0);
                                _activeBuffer.Position = 0;
                                _activeBuffer.Write(remainingAudio, 0, remainingAudio.Length);
                                _activeBuffer.Write(existingBuffer, 0, existingBuffer.Length);
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Created {chunks.Count} chunks from boundary detection");
            }
            catch (ObjectDisposedException)
            {
                System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Audio buffer disposed during boundary detection");
                return new List<byte[]>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error in DetectWordBoundaries: {ex.Message}");
                LoggingService.LogError($"AudioBufferProcessor: Error in DetectWordBoundaries: {ex.Message}", ex, "AudioBufferProcessor_DetectWordBoundaries");
                
                // Return what we have so far
                if (currentChunk.Count > 0)
                {
                    chunks.Add(currentChunk.ToArray());
                }
            }

            return chunks;
        }

        private async Task ConsumeChunksAsync()
        {
            System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Consumer task started");

            while (!_isDisposed)
            {
                try
                {
                    await _chunkAvailableSemaphore.WaitAsync(1000, _cts.Token);

                    while (_processedChunks.TryDequeue(out MemoryStream chunk))
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Processing chunk with {chunk.Length} bytes");

                            // Create a copy that will be owned by the event receiver
                            MemoryStream chunkCopy = new MemoryStream();
                            chunk.Position = 0;
                            await chunk.CopyToAsync(chunkCopy);
                            chunkCopy.Position = 0;

                            // Return the original chunk to the pool
                            _streamPool.ReturnStream(chunk);

                            // Raise the event with the copy
                            ChunkReady?.Invoke(this, new ProcessedAudioEventArgs(chunkCopy));
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error in consume chunk: {ex.Message}");
                            LoggingService.LogError($"AudioBufferProcessor: Error in consume chunk: {ex.Message}", ex, "AudioBufferProcessor_ConsumeChunksAsync");
                            _streamPool.ReturnStream(chunk);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Consumer task cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error in consumer task: {ex.Message}");
                    LoggingService.LogError($"AudioBufferProcessor: Error in consumer task: {ex.Message}", ex, "AudioBufferProcessor_ConsumeChunksAsync");
                    await Task.Delay(100);
                }
            }

            System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Consumer task finished");
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Disposing...");

                _isDisposed = true;
                
                _chunkTimer?.Dispose();
                _cts?.Cancel();
                _cts?.Dispose();
                _chunkAvailableSemaphore?.Dispose();
                _vad?.Dispose();

                // Return buffers to pool
                _streamPool.ReturnStream(_activeBuffer);
                _streamPool.ReturnStream(_processingBuffer);

                // Clear and dispose queued chunks
                while (_processedChunks.TryDequeue(out var chunk))
                {
                    chunk?.Dispose();
                }

                System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Disposed");
            }
        }
    }

    public class ProcessedAudioEventArgs : EventArgs
    {
        public MemoryStream AudioData { get; }

        public ProcessedAudioEventArgs(MemoryStream audioData)
        {
            AudioData = audioData;
        }
    }
}