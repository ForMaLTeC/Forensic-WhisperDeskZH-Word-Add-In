using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using WebRtcVadSharp;
using System.Collections.Generic;
using System.Linq;
using ForensicWhisperDeskZH.Common;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Processes audio buffers, handling overlapping chunks
    /// </summary>
    public class AudioBufferProcessor : IDisposable
    {
        private readonly TimeSpan _chunkDuration;
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
        private readonly Queue<byte[]> _voiceFrames = new Queue<byte[]>();
        private readonly int _silenceThresholdMs = 300; // 300ms of silence indicates word boundary
        private const int FRAME_SIZE_SAMPLES = 320; // 20ms at 16kHz
        private const int FRAME_SIZE_BYTES = FRAME_SIZE_SAMPLES * 2;

        /// <summary>
        /// Occurs when a processed audio chunk is available
        /// </summary>
        public event EventHandler<ProcessedAudioEventArgs> ChunkReady;

        /// <summary>
        /// Creates a new audio buffer processor
        /// </summary>
        /// <param name="bytesPerMillisecond">Bytes per millisecond based on audio format</param>
        /// <param name="chunkDuration">Duration of each processed chunk</param>
        /// <param name="silenceThreshold">Duration of overlap between chunks</param>
        public AudioBufferProcessor(
            int bytesPerMillisecond,
            TimeSpan chunkDuration,
            TimeSpan silenceThreshold)
        {
            _bytesPerMillisecond = bytesPerMillisecond;
            _chunkDuration = chunkDuration;
            _silenceThresholdMs = (int)silenceThreshold.TotalMilliseconds;

            // Initialize both buffers from the pool
            _activeBuffer = _streamPool.GetStream();
            _processingBuffer = _streamPool.GetStream();

            // Set up timer to process chunks at regular intervals
            _chunkTimer = new Timer(ProcessChunk, null, Timeout.Infinite, Timeout.Infinite);

            // Start the consumer task
            Task.Run(ConsumeChunksAsync);
            
            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Initialized with chunk duration: {chunkDuration.TotalMilliseconds}ms, overlap: {silenceThreshold.TotalMilliseconds}ms, bytes/ms: {bytesPerMillisecond}");
            LoggingService.LogMessage($"AudioBufferProcessor: Initialized with chunk duration: {chunkDuration.TotalMilliseconds}ms, overlap: {silenceThreshold.TotalMilliseconds}ms, bytes/ms: {bytesPerMillisecond}", "AudioBufferProcessor_init");
            // DON'T initialize VAD here - do it lazily when first needed
        }

        /// <summary>
        /// Starts processing audio chunks at the specified interval
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            int interval = (int)_chunkDuration.TotalMilliseconds;
            _chunkTimer.Change(interval, interval);
            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Started with {interval}ms interval");
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
                
            lock (_bufferLock)
            {
                // Convert ReadOnlySpan<byte> to byte array and write to active buffer
                byte[] audioDataArray = audioData.ToArray();
                _activeBuffer.Write(audioDataArray, 0, audioDataArray.Length);
                
                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Added {audioData.Length} bytes, total buffer size: {_activeBuffer.Length}");
            }
        }

        private void ProcessChunk(object state)
        {
            MemoryStream chunk = _streamPool.GetStream();
            
            MemoryStream bufferToProcess;
            lock (_bufferLock)
            {
                if (_activeBuffer.Length < FRAME_SIZE_BYTES * 10) // At least 200ms of audio
                {
                    _streamPool.ReturnStream(chunk);
                    return;
                }
                
                // Swap active and processing buffers
                bufferToProcess = _activeBuffer;
                _activeBuffer = _processingBuffer;
                _processingBuffer = bufferToProcess;
                
                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Processing chunk with {bufferToProcess.Length} bytes");
            }

            try
            {
                // Process buffer for word boundaries using VAD
                var wordBoundaryChunks = DetectWordBoundaries(bufferToProcess);
                
                foreach (var wordChunk in wordBoundaryChunks)
                {
                    if (wordChunk.Length > 0)
                    {
                        var chunkStream = new MemoryStream(wordChunk);
                        _processedChunks.Enqueue(chunkStream);
                        _chunkAvailableSemaphore.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Error processing chunk: {ex.Message}");
                LoggingService.LogError($"AudioBufferProcessor: Error processing chunk: {ex.Message}", ex, "AudioBufferProcessor_ProcessChunk");
            }
            finally
            {
                bufferToProcess.SetLength(0);
                bufferToProcess.Position = 0;
                _streamPool.ReturnStream(chunk);
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
                    LoggingService.LogError($"AudioBufferProcessor: Failed to initialize VAD: {ex.Message}", ex, "AudioBufferProcessor_EnsureVadInitialized");
                    _vadInitialized = false;
                    return false;
                }
            }
        }

        private List<byte[]> DetectWordBoundaries(MemoryStream audioBuffer)
        {
            // Try to initialize VAD if not already done
            if (!EnsureVadInitialized())
            {
                // Fall back to energy-based detection if VAD fails
                return DetectWordBoundariesByEnergy(audioBuffer);
            }

            var chunks = new List<byte[]>();
            var currentChunk = new List<byte>();
            
            audioBuffer.Position = 0;
            byte[] frameBuffer = new byte[FRAME_SIZE_BYTES];
            int consecutiveSilenceFrames = 0;
            
            // Calculate minimum chunk size in bytes based on _chunkDuration
            int minChunkSizeBytes = (int)(_chunkDuration.TotalMilliseconds * _bytesPerMillisecond);
            
            System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Minimum chunk size: {minChunkSizeBytes} bytes ({_chunkDuration.TotalMilliseconds}ms)");
            
            while (audioBuffer.Position < audioBuffer.Length - FRAME_SIZE_BYTES)
            {
                int bytesRead = audioBuffer.Read(frameBuffer, 0, FRAME_SIZE_BYTES);
                
                if (bytesRead == FRAME_SIZE_BYTES)
                {
                    // Convert to samples for VAD
                    short[] samples = new short[FRAME_SIZE_SAMPLES];
                    for (int i = 0; i < FRAME_SIZE_SAMPLES; i++)
                    {
                        samples[i] = BitConverter.ToInt16(frameBuffer, i * 2);
                    }
                    
                    bool hasVoice;
                    try
                    {
                        hasVoice = _vad.HasSpeech(samples);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: VAD error: {ex.Message}");
                        LoggingService.LogError($"AudioBufferProcessor: VAD error: {ex.Message}", ex, "AudioBufferProcessor_DetectWordBoundaries");
                        // Fall back to energy-based detection for this frame
                        hasVoice = CalculateRMSEnergy(frameBuffer) > 500.0;
                    }
                    
                    // Always add frame to current chunk first
                    currentChunk.AddRange(frameBuffer);
                    
                    if (hasVoice)
                    {
                        consecutiveSilenceFrames = 0;
                    }
                    else
                    {
                        consecutiveSilenceFrames++;
                        
                        // Only consider cutting the chunk if we've reached minimum duration AND have sustained silence
                        bool hasMinimumDuration = currentChunk.Count >= minChunkSizeBytes;
                        bool hasSufficientSilence = consecutiveSilenceFrames >= (_silenceThresholdMs / 20); // 20ms per frame
                        
                        if (hasMinimumDuration && hasSufficientSilence)
                        {
                            // End current chunk if it has content
                            if (currentChunk.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Creating chunk with {currentChunk.Count} bytes (duration: {(double)currentChunk.Count / _bytesPerMillisecond:F0}ms) after {consecutiveSilenceFrames * 20}ms silence");
                                chunks.Add(currentChunk.ToArray());
                                currentChunk.Clear();
                            }
                            consecutiveSilenceFrames = 0;
                        }
                        // If we haven't reached minimum duration yet, continue adding frames regardless of silence
                    }
                }
            }
            
            // Add remaining audio as final chunk only if it meets minimum size or is the only chunk
            if (currentChunk.Count > 0)
            {
                if (currentChunk.Count >= minChunkSizeBytes || chunks.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Creating final chunk with {currentChunk.Count} bytes (duration: {(double)currentChunk.Count / _bytesPerMillisecond:F0}ms)");
                    chunks.Add(currentChunk.ToArray());
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Discarding final chunk - too short: {currentChunk.Count} bytes (duration: {(double)currentChunk.Count / _bytesPerMillisecond:F0}ms)");
                    
                    // If we have previous chunks, merge this small chunk with the last one
                    if (chunks.Count > 0)
                    {
                        var lastChunk = chunks[chunks.Count - 1].ToList();
                        lastChunk.AddRange(currentChunk);
                        chunks[chunks.Count - 1] = lastChunk.ToArray();
                        System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Merged small chunk with previous, new size: {chunks[chunks.Count - 1].Length} bytes");
                    }
                    else
                    {
                        // If it's the only chunk, keep it anyway
                        chunks.Add(currentChunk.ToArray());
                        System.Diagnostics.Debug.WriteLine($"AudioBufferProcessor: Keeping small chunk as it's the only one");
                    }
                }
            }
            
            return chunks;
        }

        private List<byte[]> DetectWordBoundariesByEnergy(MemoryStream audioBuffer)
        {
            var chunks = new List<byte[]>();
            var currentChunk = new List<byte>();
            
            audioBuffer.Position = 0;
            byte[] frameBuffer = new byte[FRAME_SIZE_BYTES];
            int consecutiveLowEnergyFrames = 0;
            const double energyThreshold = 500.0;
            const int silenceFrameThreshold = 15; // ~300ms of silence
            
            while (audioBuffer.Position < audioBuffer.Length - FRAME_SIZE_BYTES)
            {
                int bytesRead = audioBuffer.Read(frameBuffer, 0, FRAME_SIZE_BYTES);
                
                if (bytesRead == FRAME_SIZE_BYTES)
                {
                    double energy = CalculateRMSEnergy(frameBuffer);
                    
                    if (energy > energyThreshold)
                    {
                        currentChunk.AddRange(frameBuffer);
                        consecutiveLowEnergyFrames = 0;
                    }
                    else
                    {
                        consecutiveLowEnergyFrames++;
                        
                        if (consecutiveLowEnergyFrames >= silenceFrameThreshold)
                        {
                            if (currentChunk.Count > 0)
                            {
                                chunks.Add(currentChunk.ToArray());
                                currentChunk.Clear();
                            }
                            consecutiveLowEnergyFrames = 0;
                        }
                        else
                        {
                            currentChunk.AddRange(frameBuffer);
                        }
                    }
                }
            }
            
            if (currentChunk.Count > 0)
            {
                chunks.Add(currentChunk.ToArray());
            }
            
            return chunks;
        }

        private double CalculateRMSEnergy(byte[] frameBuffer)
        {
            long sumSquares = 0;
            int sampleCount = frameBuffer.Length / 2;
            
            for (int i = 0; i < frameBuffer.Length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(frameBuffer, i);
                sumSquares += (long)sample * sample;
            }
            
            return Math.Sqrt((double)sumSquares / sampleCount);
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
                catch (OperationCanceledException ex)
                {
                    System.Diagnostics.Debug.WriteLine("AudioBufferProcessor: Consumer task cancelled");
                    LoggingService.LogError("AudioBufferProcessor: Consumer task cancelled",ex , "AudioBufferProcessor_ConsumeChunksAsync");
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
                
                _chunkTimer?.Dispose();
                _cts?.Cancel();
                _cts?.Dispose();
                _chunkAvailableSemaphore?.Dispose();
                _vad?.Dispose(); // Dispose VAD if it was initialized

                // Return buffers to pool
                _streamPool.ReturnStream(_activeBuffer);
                _streamPool.ReturnStream(_processingBuffer);

                // Clear and dispose queued chunks
                while (_processedChunks.TryDequeue(out var chunk))
                {
                    chunk?.Dispose();
                }

                _isDisposed = true;
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