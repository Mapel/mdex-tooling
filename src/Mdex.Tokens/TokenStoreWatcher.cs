namespace Mdex.Tokens;

/// <summary>
/// Watches the token file and calls <see cref="TokenStore.Reload"/> shortly after any change, so
/// <c>mdex-token</c> edits (e.g. revoke) take effect without a gateway restart. Debounced because a
/// single logical save can fire several FS events.
/// </summary>
public sealed class TokenStoreWatcher : IDisposable
{
    readonly TokenStore _store;
    readonly FileSystemWatcher _fsw;
    readonly Lock _gate = new();
    Timer? _debounce;

    public TokenStoreWatcher(TokenStore store, string path)
    {
        _store = store;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        _fsw = new FileSystemWatcher(dir, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _fsw.Changed += OnChanged;
        _fsw.Created += OnChanged;
        _fsw.Deleted += OnChanged;
        _fsw.Renamed += OnChanged;
    }

    void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => _store.Reload(), null, 300, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        _fsw.Dispose();
        lock (_gate) _debounce?.Dispose();
    }
}
