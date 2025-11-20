using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Collections.Specialized;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    #region UI/Presentation DTOs
    /// <summary>
    /// Represents a console line, containing metadata
    /// </summary>
    /// <param name="Timestamp">The time at which the line was produced.</param>
    /// <param name="Text">The line text.</param>
    /// <param name="IsError">True if the line represents an error output.</param>
    /// <param name="IsImportant">True if the line is considered important (e.g., should trigger auto-scroll).</param>
    public sealed record ConsoleLine(DateTime Timestamp, string Text, bool IsError, bool IsImportant = false);
    #endregion
    public sealed class UILineBuffer : IAsyncDisposable
    {

        /// <summary>
        /// Repreents a batch for the UI, comprised of two subsets: general lines that are posted to the UI, and important lines
        /// that might need some special handling (e.g., auto-scroll in the view). Only defined internally 
        /// </summary>
        public sealed record UiBatch(IReadOnlyList<ConsoleLine> Lines, IReadOnlyList<ConsoleLine> Important);

        public event EventHandler<string>? OutputChunk;
        public event EventHandler? OutputCleared;

        private readonly StringBuilder _buffer = new();
        private readonly object _bufferLock = new();
        public string Output
        {
            get { lock (_bufferLock) return _buffer.ToString(); }
        }
        /// <summary>
        /// Lines currently displayed in the console UI.
        /// </summary>
        private readonly ObservableCollection<ConsoleLine> _lines;

        private readonly SynchronizationContext? _uiContext;

        //global UI update
        private readonly ConcurrentQueue<ConsoleLine> _outputQueue = new();
        private CancellationTokenSource? _uiUpdateCts;
        private Task? _uiUpdateTask;

        // ANSI escape code, RegexOptions.Compiled for better performance compiling regex 
        private static readonly Regex AnsiRegex =
            new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])|\a|\r", RegexOptions.Compiled);

        public int MaxLinesPerTick { get; set; } = 50;
        public TimeSpan Interval { get; }

        /// <summary>
        /// Raised whenever an important line is added to the UI, ex a error line that needs attention.
        /// Used for view-specific behaviors (e.g., auto-scroll).
        /// </summary>
        public event EventHandler<ConsoleLine>? ImportantLineArrived;

        public UILineBuffer(ObservableCollection<ConsoleLine> lines, TimeSpan? interval = null, SynchronizationContext? uiContext = null)
        {
            _lines = lines;
            Interval = interval ?? TimeSpan.FromMilliseconds(75);
            _uiContext = uiContext ?? SynchronizationContext.Current;
        }

        #region UI Update Mechanism

        /// <summary>
        /// Starts the global UI update task that processes the output queue.
        /// Separated to avoid multiple concurrent UI updates.
        /// </summary>
        public void Start()
        {
            if (_uiUpdateTask != null)
                return; //if it is already running

            _uiUpdateCts = new CancellationTokenSource();
            var ct = _uiUpdateCts.Token;

            var uiTimer = new PeriodicTimer(Interval);

            _uiUpdateTask = Task.Run(async () =>
            {
                try
                {
                    while (await uiTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                    {
                        //Restrict the number of lines per tick
                        //If we dequeue everything at once it might overwhelm the UI
                        var batch = new List<ConsoleLine>(MaxLinesPerTick);
                        var important = new List<ConsoleLine>();

                        while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                        {
                            batch.Add(line);
                            if (line.IsImportant)
                                important.Add(line);
                        }

                        if (batch.Count > 0)
                        {
                            PostBatchToUI(new UiBatch(batch, important));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    //On cancellation, flush any remaining items
                    FlushOutputQueue();
                }
                catch (Exception ex)
                {
                    //Log and swallow
                    System.Diagnostics.Debug.WriteLine($"UILineBuffer UI update task error: {ex}");
                }
                finally
                {
                    uiTimer.Dispose();
                }
            }, ct);
        }

        /// <summary>
        /// Stops the UI update task.
        /// </summary>
        public async Task StopAsync()
        {
            _uiUpdateCts?.Cancel();

            if (_uiUpdateTask != null)
            {
                try
                {
                    await _uiUpdateTask.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (OperationCanceledException) { }
                catch (TimeoutException)
                {
                    //On cancellation, flush any remaining items
                    FlushOutputQueue();
                }
                finally
                {
                    _uiUpdateTask = null;
                    _uiUpdateCts?.Dispose();
                    _uiUpdateCts = null;
                }
            }
        }

        private void FlushOutputQueue()
        {

            //If operation is canceled, flush any remaining items
            while (!_outputQueue.IsEmpty)
            {
                var batch = new List<ConsoleLine>(MaxLinesPerTick);
                var important = new List<ConsoleLine>();
                while (batch.Count < MaxLinesPerTick && _outputQueue.TryDequeue(out var line))
                {
                    batch.Add(line);
                    if (line.IsImportant)
                        important.Add(line);
                }
                if (batch.Count == 0) break;
                PostBatchToUI(new UiBatch(batch, important));
            }
        }

        /// <summary>
        /// Posts a batch to the UI thread (OR directly if no synchronization context is available).
        /// </summary>
        /// <param name="batch">The batch of console lines to append to the UI.</param>
        private void PostBatchToUI(UiBatch batch)
        {
            //constructed without the UI context, assume we are already on UI thread.
            if (_uiContext is null || SynchronizationContext.Current == _uiContext)
                AddLinesToUI(batch);
            else
                //already on UI thread
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

            //append new lines first
            foreach (var l in lines) _lines.Add(l);

            //output events
            var chunk = new StringBuilder();
            foreach (var line in lines)
            {
                chunk.Append(line.Text);
            }
            OutputChunk?.Invoke(this, chunk.ToString());
            lock (_bufferLock) _buffer.Append(chunk);

            // Notify only for important lines captured during batching
            foreach (var l in batch.Important) ImportantLineArrived?.Invoke(this, l);
        }

        /// <summary>
        /// Enqueues a line to be added to the console output on the UI thread.
        /// </summary>
        /// <param name="line">The console line to enqueue.</param>
        public void EnqueueLine(ConsoleLine line)
        {
            _outputQueue.Enqueue(line);
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
            while (_outputQueue.TryDequeue(out _)) { }
        }

        public void ClearAsync()
        {
            void DoClear()
            {
                _lines.Clear();
                //clear pending queue so old lines don't repopulate.
                while (_outputQueue.TryDequeue(out _)) { }
                lock (_bufferLock) _buffer.Clear();
            }

            if (_uiContext is null) DoClear();
            else _uiContext.Post(_ => DoClear(), null);

            OutputCleared?.Invoke(this, EventArgs.Empty);
        }

        public async Task CopyAsync(IClipboardService clipboard)
        {
            if (clipboard is null) throw new ArgumentNullException(nameof(clipboard));

            List<ConsoleLine> snapshot;
            if (_uiContext is null)
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

        #endregion
        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        #region ANSI Cleaning
        /// <summary>
        /// Removes ANSI escape sequences from the specified string.
        /// </summary>
        /// <param name="s">The input string from which ANSI escape sequences will be removed. Cannot be <see langword="null"/>.</param>
        /// <returns>A string with all ANSI escape sequences removed. If the input string is empty, an empty string is returned.</returns>
        private static string CleanAnsi(string s)
            => AnsiRegex.Replace(s, "");
        #endregion


    }
}
