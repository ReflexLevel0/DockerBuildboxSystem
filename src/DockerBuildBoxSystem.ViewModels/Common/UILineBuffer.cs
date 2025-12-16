using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    /// <summary>
    /// Represents a console line, containing metadata
    /// </summary>
    /// <param name="Timestamp">The time at which the line was produced.</param>
    /// <param name="Text">The line text.</param>
    /// <param name="IsError">True if the line represents an error output.</param>
    /// <param name="IsImportant">True if the line is considered important (e.g., should trigger auto-scroll).</param>
    public sealed record ConsoleLine(DateTime Timestamp, string Text, bool IsError, bool IsImportant = false);

    public sealed class UILineBuffer : IAsyncDisposable
    {
        /// <summary>
        /// Repreents a batch for the UI, comprised of two subsets: general lines that are posted to the UI, and important lines
        /// that might need some special handling (e.g., auto-scroll in the view). Only defined internally 
        /// </summary>
        public sealed record UiBatch(IReadOnlyList<ConsoleLine> Lines, IReadOnlyList<ConsoleLine> Important);

        public event EventHandler<string>? OutputChunk;
        public event EventHandler? OutputCleared;
        public event EventHandler<int>? OutputTrimmed;
        /// <summary>
        /// Raised whenever an important line is added to the UI, ex a error line that needs attention.
        /// Used for view-specific behaviors (e.g., auto-scroll).
        /// </summary>
        public event EventHandler<ConsoleLine>? ImportantLineArrived;
        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        private readonly RangeObservableCollection<ConsoleLine> _lines;
        private readonly SynchronizationContext? _uiContext;

        private readonly StringBuilder _buffer = new();
        private readonly object _bufferLock = new();
        public string Output { get { lock (_bufferLock) return _buffer.ToString(); } }

        private readonly Queue<int> _lineTextLengths = new();
        //global UI update
        private ConcurrentQueue<ConsoleLine> _outputQueue = new();

        private CancellationTokenSource? _uiUpdateCts;
        private Task? _uiUpdateTask;
        private int _started;

        // ANSI escape code, RegexOptions.Compiled for better performance compiling regex 
        //https://github.com/chalk/ansi-regex/blob/main/index.js
        //https://www.npmjs.com/package/ansi-regex
        private static readonly Regex AnsiRegex = new(
            @"(?:\u001B\][\s\S]*?(?:\u0007|\u001B\u005C|\u009C))" +
            @"|" +
            @"(?:[\u001B\u009B][\[\]\(\)#;?]*(?:\d{1,4}(?:[;:]\d{0,4})*)?[\dA-PR-TZcf-nq-uy=><~])",
            RegexOptions.Compiled
        );

        public int MaxLinesPerTick { get; set; } = 500;
        public int MaxLines { get; set; } = 1000;
        public int MaxQueueSize { get; set; } = 2000;
        public TimeSpan Interval { get; }

        private int _queueCount = 0;

        public UILineBuffer(
            RangeObservableCollection<ConsoleLine> lines,
            TimeSpan? interval = null,
            SynchronizationContext? uiContext = null)
        {
            _lines = lines;
            Interval = interval ?? TimeSpan.FromMilliseconds(100);
            _uiContext = uiContext ?? SynchronizationContext.Current;
        }

        /// <summary>
        /// Starts the global UI update task that processes the output queue.
        /// Separated to avoid multiple concurrent UI updates.
        /// </summary>
        public void Start()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
                return;

            _uiUpdateCts = new CancellationTokenSource();
            var ct = _uiUpdateCts.Token;

            _uiUpdateTask = Task.Run(async () =>
            {
                //Restrict the number of lines per tick
                //If we dequeue everything at once it might overwhelm the UI
                var batch = new List<ConsoleLine>(MaxLinesPerTick);
                var important = new List<ConsoleLine>(Math.Min(MaxLinesPerTick, 8));

                try
                {
                    using var timer = new PeriodicTimer(Interval);

                    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                    {
                        DrainOnce(batch, important);
                        if (batch.Count > 0)
                            PostBatchToUI(new UiBatch(batch.ToArray(), important.ToArray())); // stable snapshot
                    }
                }
                catch (OperationCanceledException)
                {
                    DiscardPending();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UILineBuffer UI task error: {ex}");
                }
            }, ct);
        }

        public async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _started, 0) == 0)
                return;

            var cts = Interlocked.Exchange(ref _uiUpdateCts, null);
            var task = Interlocked.Exchange(ref _uiUpdateTask, null);

            try
            {
                cts?.Cancel();

                if (task != null)
                {
                    try { await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (TimeoutException)
                    {
                        // If stop times out, just drop pending.
                        DiscardPending();
                    }
                }
            }
            finally
            {
                cts?.Dispose();
            }
        }

        private void DrainOnce(List<ConsoleLine> batch, List<ConsoleLine> important)
        {
            batch.Clear();
            important.Clear();

            var q = _outputQueue;
            while (batch.Count < MaxLinesPerTick && q.TryDequeue(out var line))
            {
                Interlocked.Decrement(ref _queueCount);
                batch.Add(line);
                if (line.IsImportant) important.Add(line);
            }
        }
        /// <summary>
        /// Posts a batch to the UI thread (OR directly if no synchronization context is available).
        /// </summary>
        /// <param name="batch">The batch of console lines to append to the UI.</param>
        private void PostBatchToUI(UiBatch batch)
        {
            if (_uiContext is null || SynchronizationContext.Current == _uiContext)
                AddLinesToUI(batch);
            else
                _uiContext.Post(_ => AddLinesToUI(batch), null);
        }
        /// <summary>
        /// Appends a batch of lines to the bound collection.
        /// Trims the collection to keep the UI responsive.
        /// Triggers <see cref="ImportantLineArrived"/> for any important lines in the batch.
        /// </summary>
        /// <param name="batch">The batch of lines to add.</param>
        private void AddLinesToUI(UiBatch batch)
        {
            var lines = batch.Lines;
            if (lines.Count == 0) return;

            // Trim first so we only trim once per batch.
            var overflow = (_lines.Count + lines.Count) - MaxLines;
            if (overflow > 0)
                TrimExact(overflow);

            _lines.AddRange(lines);

            //track the lengths for future trims (UI-thread only).
            for (int i = 0; i < lines.Count; i++)
                _lineTextLengths.Enqueue(lines[i].Text.Length);

            //build chunk with a pre-sized StringBuilder.
            var totalLen = 0;
            for (int i = 0; i < lines.Count; i++)
                totalLen += lines[i].Text.Length;

            var sb = new StringBuilder(totalLen);
            for (int i = 0; i < lines.Count; i++)
                sb.Append(lines[i].Text);

            var chunkString = sb.ToString();

            lock (_bufferLock) _buffer.Append(chunkString);
            OutputChunk?.Invoke(this, chunkString);

            for (int i = 0; i < batch.Important.Count; i++)
                ImportantLineArrived?.Invoke(this, batch.Important[i]);
        }
        /// <summary>
        /// Trims the collection of lines and the associated buffer if the number of lines exceeds the maximum allowed.
        /// </summary>
        private void TrimExact(int linesToRemove)
        {
            if (linesToRemove <= 0) return;
            if (linesToRemove > _lines.Count) linesToRemove = _lines.Count;

            var charsToRemove = 0;
            for (int i = 0; i < linesToRemove; i++)
            {
                if (_lineTextLengths.Count == 0) break;
                charsToRemove += _lineTextLengths.Dequeue();
            }

            _lines.RemoveRangeAt(0, linesToRemove);

            lock (_bufferLock)
            {
                if (charsToRemove > 0 && _buffer.Length >= charsToRemove)
                    _buffer.Remove(0, charsToRemove);
                else if (_buffer.Length > 0 && charsToRemove > 0)
                    _buffer.Clear();
            }

            OutputTrimmed?.Invoke(this, charsToRemove);
        }
        /// <summary>
        /// Enqueues a line to be added to the console output on the UI thread.
        /// </summary>
        /// <param name="line">The console line to enqueue.</param>
        public void EnqueueLine(ConsoleLine line)
        {
            if (_queueCount >= MaxQueueSize) return;
            _outputQueue.Enqueue(line);
            Interlocked.Increment(ref _queueCount);
        }
        /// <summary>
        /// Creates and enqueues a console line with the provided text and flags.
        /// Cleans ANSI escape sequences from the text before enqueuing.
        /// </summary>
        /// <param name="text">Text to append.</param>
        /// <param name="isError">Whether the line is an error.</param>
        /// <param name="isImportant">Whether the line is important.</param>
        public void EnqueueLine(string text, bool isError, bool isImportant = false)
        {
            var cleanText = CleanAnsi(text);
            EnqueueLine(new ConsoleLine(DateTime.Now, cleanText, isError, isImportant));
        }
        /// <summary>
        /// Discards any pending lines that haven't yet been flushed to the UI. Use when switching data sources.
        /// </summary>
        public void DiscardPending()
        {
            Interlocked.Exchange(ref _outputQueue, new ConcurrentQueue<ConsoleLine>());
            Interlocked.Exchange(ref _queueCount, 0);
        }

        public void Clear()
        {
            void DoClear()
            {
                _lines.Clear();
                _lineTextLengths.Clear();

                DiscardPending();

                lock (_bufferLock) _buffer.Clear();
                OutputCleared?.Invoke(this, EventArgs.Empty);
            }

            if (_uiContext is null || SynchronizationContext.Current == _uiContext)
                DoClear();
            else
                _uiContext.Post(_ => DoClear(), null);
        }

        public async Task CopyAsync(IClipboardService clipboard)
        {
            if (clipboard is null) throw new ArgumentNullException(nameof(clipboard));

            List<ConsoleLine> snapshot;
            if (_uiContext is null || SynchronizationContext.Current == _uiContext)
            {
                snapshot = _lines.ToList();
            }
            else
            {
                var tcs = new TaskCompletionSource<List<ConsoleLine>>(TaskCreationOptions.RunContinuationsAsynchronously);
                _uiContext.Post(_ =>
                {
                    try { tcs.SetResult(_lines.ToList()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }, null);
                snapshot = await tcs.Task.ConfigureAwait(false);
            }

            var text = string.Join(Environment.NewLine, snapshot.Select(l => $"{l.Timestamp:HH:mm:ss} {l.Text}"));
            await clipboard.SetTextAsync(text).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
        /// <summary>
        /// Removes ANSI escape sequences from the specified string.
        /// </summary>
        /// <param name="s">The input string from which ANSI escape sequences will be removed. Cannot be <see langword="null"/>.</param>
        /// <returns>A string with all ANSI escape sequences removed. If the input string is empty, an empty string is returned.</returns>
        private static string CleanAnsi(string s)
            => AnsiRegex.Replace(s, "").Replace("\r\n", "\n").Replace("\r", "");
    }
}
