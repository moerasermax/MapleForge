using Maple.Core.Data;
using System.Collections.Concurrent;

namespace Maple.Content.Wz;

/// <summary>
/// IDataProvider 的 WZ 二進位實作。
/// 快取所有權：process 級唯讀單例。
/// 每個 WZ 檔案在第一次存取時開啟並永久快取（WZ 資料在運行時不變動）。
/// 多執行緒安全：ConcurrentDictionary + WzFile 內部 lock。
/// 備注：WzFile 持有 FileStream（FileShare.Read），整個 process 生命週期不關閉。
/// </summary>
public sealed class WzDataProvider : IDataProvider, IDisposable
{
    private readonly string _wzDirectory;
    private readonly ConcurrentDictionary<string, (WzFile File, IDataNode Root)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public WzDataProvider(string wzDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wzDirectory);
        _wzDirectory = wzDirectory;
    }

    /// <inheritdoc/>
    public IDataNode GetRoot(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetOrLoad(fileName).Root;
    }

    /// <inheritdoc/>
    public IDataNode? GetAt(string fileName, string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(path))
        {
            return GetRoot(fileName);
        }

        var root = GetRoot(fileName);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IDataNode? current = root;
        foreach (var seg in segments)
        {
            current = current[seg];
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _cache.Values)
        {
            entry.File.Dispose();
        }

        _cache.Clear();
    }

    private (WzFile File, IDataNode Root) GetOrLoad(string fileName)
    {
        return _cache.GetOrAdd(fileName, name =>
        {
            var path = Path.Combine(_wzDirectory, name + ".wz");
            var file = WzFile.Open(path);
            IDataNode root = new WzDataNode(file.Root);
            return (file, root);
        });
    }
}
