namespace RemotePropertyWatchdog.Agent.Service;

public sealed class DefaultAgentLifecycleFileSystem : IAgentLifecycleFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public Stream OpenRead(string path, FileShare fileShare) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, fileShare);

    public Stream CreateNew(string path) =>
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void ReplaceFile(string sourceFileName, string destinationFileName) =>
        File.Move(sourceFileName, destinationFileName, overwrite: true);

    public void DeleteFileIfExists(string path) => File.Delete(path);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
