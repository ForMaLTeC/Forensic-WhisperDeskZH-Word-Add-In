using System;
using System.Text;
using System.Threading;

namespace ForensicWhisperDeskZH.Text
{
    /// <summary>
    /// Manages buffering and timed insertion of text
    /// </summary>
    public class TextBufferService : IDisposable
    {
        private readonly StringBuilder _textBuffer = new StringBuilder();
        private readonly object _bufferLock = new object();
        private readonly Timer _insertionTimer;
        private readonly TimeSpan _insertionInterval;
        private readonly Action<string> _insertAction;
        private bool _isRunning = false;
        private bool _isDisposed = false;
        
        public event EventHandler<string> TextReady;
        
        /// <summary>
        /// Creates a new text buffer service
        /// </summary>
        /// <param name="insertionInterval">How often to insert buffered text</param>
        /// <param name="insertAction">Optional action to call when text is ready</param>
        public TextBufferService(TimeSpan insertionInterval, Action<string> insertAction = null)
        {
            _insertionInterval = insertionInterval;
            _insertAction = insertAction;
            _insertionTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }
        
        /// <summary>
        /// Starts the buffer service
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(TextBufferService));
                
            if (_isRunning)
                return;
                
            _isRunning = true;
            _insertionTimer.Change(0, (int)_insertionInterval.TotalMilliseconds);
        }
        
        /// <summary>
        /// Stops the buffer service and flushes any pending text
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;
                
            _isRunning = false;
            _insertionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            
            // Process any remaining text
            ProcessBuffer();
        }
        
        /// <summary>
        /// Adds text to the buffer
        /// </summary>
        public void AddText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // remove Punctuation
            text = text.Trim().TrimEnd('.', ',', '!', '?', ';', ':');

            lock (_bufferLock)
            {
                _textBuffer.Append(text + " ");
            }
        }
        
        private void OnTimerElapsed(object state)
        {
            if (_isRunning)
            {
                ProcessBuffer();
            }
        }
        
        private void ProcessBuffer()
        {
            string textToProcess;
            
            lock (_bufferLock)
            {
                if (_textBuffer.Length == 0)
                    return;
                    
                textToProcess = _textBuffer.ToString();
                _textBuffer.Clear();
            }
            
            // Notify listeners
            TextReady?.Invoke(this, textToProcess);
            
            // Call the insertion action if provided
            _insertAction?.Invoke(textToProcess);
        }
        
        public void Dispose()
        {
            if (_isDisposed)
                return;
                
            Stop();
            _insertionTimer?.Dispose();
            _isDisposed = true;
        }
    }
}