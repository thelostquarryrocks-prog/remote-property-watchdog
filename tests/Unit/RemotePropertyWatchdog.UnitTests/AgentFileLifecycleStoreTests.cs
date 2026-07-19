using System.Text;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentFileLifecycleStoreTests
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
    };
    [Fact]
    public async Task ReadLastMarkerAsyncUsesDeleteCompatibleSharing()
    {
        RecordingFileSystem fileSystem = new();
        AgentLifecycleMarker marker = CreateMarker(Guid.NewGuid());
        fileSystem.Files[fileSystem.MarkerPath] = Serialize(marker);
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        AgentLifecycleMarker? read = await store.ReadLastMarkerAsync(CancellationToken.None);

        Assert.NotNull(read);
        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, fileSystem.LastReadShare);
    }

    [Fact]
    public async Task TransientMarkerReplacementFailureIsRetriedAndEventuallySucceeds()
    {
        RecordingFileSystem fileSystem = new() { TransientReplaceFailuresRemaining = 2 };
        AgentLifecycleMarker marker = CreateMarker(Guid.NewGuid());
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        await store.WriteMarkerAsync(marker, CancellationToken.None);

        Assert.Equal(3, fileSystem.ReplaceAttempts);
        Assert.Contains(fileSystem.Files.Keys, path => path == fileSystem.MarkerPath);
        Assert.Equal(2, fileSystem.DeletedTempPaths.Count);
        Assert.DoesNotContain(fileSystem.Files.Keys, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PersistentMarkerReplacementFailureExpiresAfterBoundedRetry()
    {
        RecordingFileSystem fileSystem = new() { TransientReplaceFailuresRemaining = 99 };
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        await Assert.ThrowsAsync<SharingViolationIOException>(() => store.WriteMarkerAsync(CreateMarker(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(5, fileSystem.ReplaceAttempts);
        Assert.DoesNotContain(fileSystem.Files.Keys, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadersDoNotObservePartiallyWrittenJsonDuringSuccessfulWrite()
    {
        RecordingFileSystem fileSystem = new();
        AgentLifecycleMarker oldMarker = CreateMarker(Guid.NewGuid());
        fileSystem.Files[fileSystem.MarkerPath] = Serialize(oldMarker);
        fileSystem.BeforeReplace = () => Assert.Equal(oldMarker.SessionId, Deserialize(fileSystem.Files[fileSystem.MarkerPath]).SessionId);
        AgentLifecycleMarker newMarker = CreateMarker(Guid.NewGuid());
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        await store.WriteMarkerAsync(newMarker, CancellationToken.None);

        Assert.Equal(newMarker.SessionId, Deserialize(fileSystem.Files[fileSystem.MarkerPath]).SessionId);
    }

    [Fact]
    public async Task UniqueTemporaryFilenamesPreventSharedTmpPathForConcurrentWrites()
    {
        RecordingFileSystem fileSystem = new();
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        await Task.WhenAll(
            store.WriteMarkerAsync(CreateMarker(Guid.NewGuid()), CancellationToken.None),
            store.WriteMarkerAsync(CreateMarker(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(2, fileSystem.CreatedTempPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(string.Concat(fileSystem.MarkerPath, ".tmp"), fileSystem.CreatedTempPaths);
    }

    [Fact]
    public async Task TemporaryFilesAreRemovedAfterFailedWritesWhereSafe()
    {
        RecordingFileSystem fileSystem = new() { PermanentReplaceFailure = true };
        FileAgentLifecycleStore store = CreateStore(fileSystem);

        await Assert.ThrowsAsync<IOException>(() => store.WriteMarkerAsync(CreateMarker(Guid.NewGuid()), CancellationToken.None));

        Assert.Single(fileSystem.DeletedTempPaths);
        Assert.DoesNotContain(fileSystem.Files.Keys, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    private static FileAgentLifecycleStore CreateStore(RecordingFileSystem fileSystem) =>
        new(Options.Create(new AgentLifecycleOptions { MarkerPath = fileSystem.MarkerPath }), fileSystem);

    private static AgentLifecycleMarker CreateMarker(Guid sessionId) =>
        new(sessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, null, PlannedShutdown: false, null, PreviousSessionEndedCleanly: true);

    private static byte[] Serialize(AgentLifecycleMarker marker) => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(marker, SerializerOptions));

    private static AgentLifecycleMarker Deserialize(byte[] bytes) =>
        System.Text.Json.JsonSerializer.Deserialize<AgentLifecycleMarker>(bytes, SerializerOptions)!;

    private sealed class RecordingFileSystem : IAgentLifecycleFileSystem
    {
        private readonly object gate = new();

        public string MarkerPath { get; } = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");

        public Dictionary<string, byte[]> Files { get; } = [];

        public List<string> CreatedTempPaths { get; } = [];

        public List<string> DeletedTempPaths { get; } = [];

        public FileShare? LastReadShare { get; private set; }

        public int TransientReplaceFailuresRemaining { get; set; }

        public bool PermanentReplaceFailure { get; init; }

        public int ReplaceAttempts { get; private set; }

        public Action? BeforeReplace { get; set; }

        public bool FileExists(string path) => Files.ContainsKey(path);

        public Stream OpenRead(string path, FileShare fileShare)
        {
            LastReadShare = fileShare;
            return new MemoryStream(Files[path], writable: false);
        }

        public Stream CreateNew(string path)
        {
            lock (gate)
            {
                CreatedTempPaths.Add(path);
            }

            return new CommitOnDisposeStream(bytes =>
            {
                lock (gate)
                {
                    Files[path] = bytes;
                }
            });
        }

        public void CreateDirectory(string path)
        {
        }

        public void ReplaceFile(string sourceFileName, string destinationFileName)
        {
            ReplaceAttempts++;
            BeforeReplace?.Invoke();
            if (PermanentReplaceFailure)
            {
                throw new IOException("Permanent replace failure.");
            }

            if (TransientReplaceFailuresRemaining > 0)
            {
                TransientReplaceFailuresRemaining--;
                throw new SharingViolationIOException();
            }

            lock (gate)
            {
                Files[destinationFileName] = Files[sourceFileName];
                Files.Remove(sourceFileName);
            }
        }

        public void DeleteFileIfExists(string path)
        {
            lock (gate)
            {
                DeletedTempPaths.Add(path);
                Files.Remove(path);
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CommitOnDisposeStream(Action<byte[]> commit) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                commit(ToArray());
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            commit(ToArray());
            await base.DisposeAsync();
        }
    }

    private sealed class SharingViolationIOException : IOException
    {
        public SharingViolationIOException()
            : base("Sharing violation.")
        {
            HResult = unchecked((int)0x80070020);
        }
    }
}
