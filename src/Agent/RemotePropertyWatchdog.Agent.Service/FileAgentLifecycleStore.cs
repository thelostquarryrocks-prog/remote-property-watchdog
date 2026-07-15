using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RemotePropertyWatchdog.Agent.Service;

public sealed class FileAgentLifecycleStore(IOptions<AgentLifecycleOptions> options) : IAgentLifecycleStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private readonly string markerPath = ResolveMarkerPath(options.Value.MarkerPath);

    public async Task<AgentLifecycleMarker?> ReadLastMarkerAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        await using FileStream stream = new(markerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<AgentLifecycleMarker>(stream, SerializerOptions, cancellationToken);
    }

    public async Task WriteMarkerAsync(AgentLifecycleMarker marker, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = string.Concat(markerPath, ".tmp");
        await using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, marker, SerializerOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, markerPath, overwrite: true);
    }

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
