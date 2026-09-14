using DisplayControl.Application.Storage;

namespace DisplayControl.Infrastructure.Storage;

public sealed class LocalPrivateObjectStore : IPrivateObjectStore
{
    private const int CopyBufferSize = 128 * 1024;
    private readonly string _rootDirectory;
    private readonly string _rootDirectoryPrefix;
    private readonly long _maximumObjectBytes;

    public LocalPrivateObjectStore(string rootDirectory, long maximumObjectBytes)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
        {
            throw new ArgumentException("Private storage root must be a fully qualified path.", nameof(rootDirectory));
        }

        if (maximumObjectBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumObjectBytes), "Maximum object size must be positive.");
        }

        _rootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _rootDirectoryPrefix = _rootDirectory + Path.DirectorySeparatorChar;
        _maximumObjectBytes = maximumObjectBytes;

        Directory.CreateDirectory(_rootDirectory);
        EnsureNotReparsePoint(_rootDirectory);
    }

    public async Task PutAsync(
        PrivateObjectKey key,
        Stream content,
        long expectedLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("Content stream must be readable.", nameof(content));
        }

        if (expectedLength < 0 || expectedLength > _maximumObjectBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedLength),
                $"Expected length must be between zero and {_maximumObjectBytes} bytes.");
        }

        var targetPath = ResolvePath(key);
        var parentDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Object key did not resolve to a parent directory.");
        Directory.CreateDirectory(parentDirectory);
        EnsureSafeExistingPath(parentDirectory);
        EnsureTargetIsNotReparsePoint(targetPath);

        var temporaryPath = Path.Combine(parentDirectory, $".{key.ObjectId:N}.{Guid.NewGuid():N}.upload");
        var published = false;
        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[CopyBufferSize];
                long totalBytes = 0;
                while (true)
                {
                    var bytesRead = await content.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalBytes = checked(totalBytes + bytesRead);
                    if (totalBytes > expectedLength || totalBytes > _maximumObjectBytes)
                    {
                        throw new InvalidDataException("Content length exceeds the declared or configured limit.");
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                if (totalBytes != expectedLength)
                {
                    throw new InvalidDataException("Content length does not match the declared length.");
                }

                await output.FlushAsync(cancellationToken);
            }

            EnsureSafeExistingPath(parentDirectory);
            EnsureTargetIsNotReparsePoint(targetPath);
            File.Move(temporaryPath, targetPath, overwrite: false);
            published = true;
        }
        finally
        {
            if (!published && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(
        PrivateObjectKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetPath = ResolvePath(key);
        EnsureSafeExistingPath(Path.GetDirectoryName(targetPath)!);
        EnsureTargetIsNotReparsePoint(targetPath);
        Stream stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(
        PrivateObjectKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetPath = ResolvePath(key);
        EnsureSafeExistingPath(Path.GetDirectoryName(targetPath)!);
        EnsureTargetIsNotReparsePoint(targetPath);
        return Task.FromResult(File.Exists(targetPath));
    }

    public Task DeleteAsync(
        PrivateObjectKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetPath = ResolvePath(key);
        EnsureSafeExistingPath(Path.GetDirectoryName(targetPath)!);
        EnsureTargetIsNotReparsePoint(targetPath);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(PrivateObjectKey key)
    {
        var relativeSegments = key.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        var targetPath = Path.GetFullPath(Path.Combine([_rootDirectory, .. relativeSegments]));
        if (!targetPath.StartsWith(_rootDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Object key resolved outside private storage root.");
        }

        return targetPath;
    }

    private void EnsureSafeExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(_rootDirectory, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(_rootDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage path resolved outside private storage root.");
        }

        EnsureNotReparsePoint(_rootDirectory);
        var relativePath = Path.GetRelativePath(_rootDirectory, fullPath);
        if (relativePath == ".")
        {
            return;
        }

        var currentPath = _rootDirectory;
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (Directory.Exists(currentPath) || File.Exists(currentPath))
            {
                EnsureNotReparsePoint(currentPath);
            }
        }
    }

    private static void EnsureTargetIsNotReparsePoint(string targetPath)
    {
        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            EnsureNotReparsePoint(targetPath);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Private storage paths cannot contain symbolic links or reparse points.");
        }
    }
}
