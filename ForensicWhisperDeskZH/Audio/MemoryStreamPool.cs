using System;
using System.Collections.Concurrent;
using System.IO;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Provides pooling for MemoryStream objects to reduce garbage collection pressure
    /// </summary>
    public class MemoryStreamPool
    {
        private readonly ConcurrentBag<MemoryStream> _pool = new ConcurrentBag<MemoryStream>();
        private readonly int _initialCapacity;
        
        /// <summary>
        /// Creates a new MemoryStreamPool
        /// </summary>
        /// <param name="initialCapacity">Initial capacity for memory streams</param>
        public MemoryStreamPool(int initialCapacity = 16384)
        {
            _initialCapacity = initialCapacity;
        }
        
        /// <summary>
        /// Gets a MemoryStream from the pool or creates a new one
        /// </summary>
        public MemoryStream GetStream()
        {
            if (_pool.TryTake(out MemoryStream stream))
            {
                stream.Position = 0;
                return stream;
            }
            
            return new MemoryStream(_initialCapacity);
        }
        
        /// <summary>
        /// Returns a MemoryStream to the pool
        /// </summary>
        public void ReturnStream(MemoryStream stream)
        {
            if (stream == null) return;
            
            try
            {
                stream.Position = 0;
                stream.SetLength(0);
                _pool.Add(stream);
            }
            catch
            {
                // If we can't reset the stream, just dispose it
                try { stream.Dispose(); } catch { }
            }
        }
    }
}