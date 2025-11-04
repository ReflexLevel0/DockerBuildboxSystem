using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DockerBuildBoxSystem.TestUtils;

public static class ChannelTestUtil
{
    //Creates a channel reader that writes all lines AND THEN completes, to simulate completed output.
    public static ChannelReader<(bool IsStdErr, string Line)> CreateCompletedReader(
        IEnumerable<(bool IsStdErr, string Line)> lines,
        TimeSpan? delayBetween = null,
        CancellationToken ct = default)
    {
        var ch = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var item in lines)
                {
                    ct.ThrowIfCancellationRequested();
                    await ch.Writer.WriteAsync(item, ct);
                    if (delayBetween is { } d && d > TimeSpan.Zero)
                        await Task.Delay(d, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                ch.Writer.TryComplete();
            }
        }, ct);

        return ch.Reader;
    }

    //Creates a channel reader that posts lines then waits until cancellation to complete, to simulate streaming output.
    //Almost same code as above, but with different behavior after lines are sent... (abstract common behavior to its own method?)
    public static ChannelReader<(bool IsStdErr, string Line)> CreateCancellableReader(
        IEnumerable<(bool IsStdErr, string Line)>? lines = null,
        TimeSpan? delayBetween = null,
        CancellationToken ct = default)
    {
        var ch = Channel.CreateUnbounded<(bool, string)>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                if (lines != null)
                {
                    foreach (var item in lines)
                    {
                        ct.ThrowIfCancellationRequested();
                        await ch.Writer.WriteAsync(item, ct);
                        if (delayBetween is { } d && d > TimeSpan.Zero)
                            await Task.Delay(d, ct);
                    }
                }

                //then block until cancellation...
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                ch.Writer.TryComplete();
            }
        }, ct);

        return ch.Reader;
    }

    //Waits until the predicate returns true, polling at the specified interval, or until the timeout is reached.
    public static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, TimeSpan? poll = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var timer = new PeriodicTimer(poll ?? TimeSpan.FromMilliseconds(25));
        do
        {
            if (predicate()) return true;
            try { await timer.WaitForNextTickAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
        } while (true);

        return predicate();
    }
}
