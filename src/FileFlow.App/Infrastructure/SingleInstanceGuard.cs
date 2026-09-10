using System.Security.Principal;

namespace FileFlow.App.Infrastructure;

/// <summary>Acquire and dispose on the startup thread. No IPC or secondary-instance forwarding.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private bool disposed;
    private SingleInstanceGuard(Mutex mutex) => this.mutex = mutex;

    public static SingleInstanceGuard? TryAcquire(string? mutexName = null)
    {
        if (mutexName is null)
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("The current Windows user could not be identified.");
            mutexName = @"Global\FileFlow." + sid;
        }
        var mutex = new Mutex(false, mutexName);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new(mutex);
            mutex.Dispose();
            return null;
        }
        catch { mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        mutex.ReleaseMutex();
        mutex.Dispose();
        disposed = true;
    }
}
