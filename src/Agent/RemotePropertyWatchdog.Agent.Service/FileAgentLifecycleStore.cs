using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed class FileAgentLifecycleStore : IAgentLifecycleStore
{
    private const int MaxWriteAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly IAgentLifecycleFileSystem fileSystem;
    private readonly string markerPath;

    public FileAgentLifecycleStore(IOptions<AgentLifecycleOptions> options)
        : this(options, new DefaultAgentLifecycleFileSystem())
    {
    }

    public FileAgentLifecycleStore(IOptions<AgentLifecycleOptions> options, IAgentLifecycleFileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
        markerPath = ResolveMarkerPath(options.Value.MarkerPath);
    }

    public async Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken)
    {
        if (!fileSystem.FileExists(markerPath))
        {
            return null;
        }

        await using Stream stream = fileSystem.OpenRead(markerPath, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<AgentLifecycleMarker>(stream, SerializerOptions, cancellationToken);
    }

    public async Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        Exception? lastTransientException = null;
        for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string tempPath = CreateTempPath(markerPath);
            try
            {
                await using (Stream stream = fileSystem.CreateNew(tempPath))
                {
                    await JsonSerializer.SerializeAsync(stream, marker, SerializerOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                fileSystem.ReplaceFile(tempPath, markerPath);
                return;
            }
            catch (Exception ex) when (IsTransientFileAccessException(ex))
            {
                lastTransientException = ex;
                CleanupTempFile(tempPath);
                if (attempt == MaxWriteAttempts)
                {
                    throw;
                }

                await fileSystem.DelayAsync(RetryDelay, cancellationToken);
            }
            catch
            {
                CleanupTempFile(tempPath);
                throw;
            }
        }

        throw new IOException("Lifecycle marker write failed after bounded retries.", lastTransientException);
    }

    private static string CreateTempPath(string destinationPath) =>
        string.Concat(destinationPath, ".", Guid.NewGuid().ToString("N"), ".tmp");

    private void CleanupTempFile(string tempPath)
    {
        try
        {
            fileSystem.DeleteFileIfExists(tempPath);
        }
        catch (Exception ex) when (IsTransientFileAccessException(ex))
        {
        }
    }

    private static bool IsTransientFileAccessException(Exception ex) =>
        ex is IOException ioException && IsSharingOrLockViolation(ioException.HResult) ||
        ex is UnauthorizedAccessException unauthorizedAccessException && IsAccessDenied(unauthorizedAccessException.HResult);

    private static bool IsSharingOrLockViolation(int hresult)
    {
        int errorCode = hresult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private static bool IsAccessDenied(int hresult) => (hresult & 0xFFFF) == 5;

    private static string ResolveMarkerPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "RemotePropertyWatchdog", "Agent", "lifecycle-marker.json");
    }
}
