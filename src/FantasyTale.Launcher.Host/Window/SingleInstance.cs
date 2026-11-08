using System.Security.Cryptography;
using System.Text;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Keeps one launcher per user and data directory; subsequent invocations signal the existing window.</summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly ManualResetEvent _stop = new(false);
    private Task? _listener;
    internal bool IsPrimary { get; }

    internal SingleInstance(string dataRoot)
    {
        var identity = Environment.UserDomainName + "\\" + Environment.UserName + "\n" + Path.GetFullPath(dataRoot).TrimEnd('\\', '/').ToUpperInvariant();
        var name = "Local\\FantasyTale.Launcher." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        _mutex = new Mutex(false, name);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        if (!IsPrimary) _activate.Set();
    }

    internal void Listen(Action activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Only the primary launcher can listen for activation.");
        _listener = Task.Run(() =>
        {
            while (WaitHandle.WaitAny([_stop, _activate]) == 1) activate();
        });
    }

    public void Dispose()
    {
        _stop.Set();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // A posted UI activation may be draining while the native window closes.
        _ = _listener?.ContinueWith(_ => { _activate.Dispose(); _stop.Dispose(); });
        if (_listener is null) { _activate.Dispose(); _stop.Dispose(); }
    }
}
