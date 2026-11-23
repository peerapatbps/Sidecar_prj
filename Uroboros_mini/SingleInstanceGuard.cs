namespace Uroboros.MiniUploader;

internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, $@"Global\{name}", out var createdNew);
        HasHandle = createdNew;
    }

    public bool HasHandle { get; }

    public void Dispose()
    {
        if (HasHandle)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }

        _mutex.Dispose();
    }
}
