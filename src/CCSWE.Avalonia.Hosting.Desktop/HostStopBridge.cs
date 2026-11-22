namespace CCSWE.Avalonia.Hosting;

/// <summary>
/// Shuts the Avalonia UI down when the host stops (a signal, <c>IHostApplicationLifetime.StopApplication()</c>, a
/// faulted <c>BackgroundService</c>). A stop can arrive on any thread, before or after the UI is set up; the shutdown
/// is posted to the UI dispatcher once one is attached, never invoked synchronously.
/// </summary>
internal sealed class HostStopBridge
{
    private readonly Lock _lock = new();
    private Action<Action>? _post;
    private readonly Action _shutdown;
    private bool _stopRequested;

    /// <summary>Creates a bridge that posts <paramref name="shutdown"/> to the UI dispatcher.</summary>
    /// <param name="shutdown">Shuts the UI down; runs on the UI thread.</param>
    public HostStopBridge(Action shutdown)
    {
        _shutdown = shutdown;
    }

    /// <summary>
    /// Attaches the UI dispatcher (call on the UI thread once it is set up). Posts the shutdown at once if the host
    /// already started stopping.
    /// </summary>
    /// <param name="post">Posts an action to the UI dispatcher without blocking.</param>
    public void Attach(Action<Action> post)
    {
        lock (_lock)
        {
            _post = post;
            if (_stopRequested)
            {
                _post(_shutdown);
            }
        }
    }

    /// <summary>Detaches the UI dispatcher (once the UI has exited), so later stops post nothing.</summary>
    public void Detach()
    {
        lock (_lock)
        {
            _post = null;
        }
    }

    /// <summary>
    /// Records that the host is stopping and, if attached, posts the shutdown. Runs as an
    /// <c>ApplicationStopping</c> callback on any thread: it must not block, because <c>StopApplication</c> holds a
    /// lock while its callbacks run.
    /// </summary>
    public void RequestStop()
    {
        lock (_lock)
        {
            _stopRequested = true;
            _post?.Invoke(_shutdown);
        }
    }
}
