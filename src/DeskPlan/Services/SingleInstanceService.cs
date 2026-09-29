using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace DeskPlan.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    public SingleInstanceService(string scope)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user + "|" + scope)));
        var name = @"Local\DeskPlan." + key;
        // Create the event first so an activation during startup is retained until the UI is ready.
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
        _mutex = new Mutex(false, name + ".Instance");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    public bool IsPrimary { get; }

    public void ActivateExisting() => _activation.Set();

    public void Listen(Action activate)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen.");
        _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(),
            null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activation.Dispose();
    }
}
