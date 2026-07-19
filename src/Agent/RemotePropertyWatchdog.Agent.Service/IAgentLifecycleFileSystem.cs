namespace RemotePropertyWatchdog.Agent.Service;

public interface IAgentLifecycleFileSystem
{
    bool FileExists(string path);

    Stream OpenRead(string path, FileShare fileShare);

    Stream CreateNew(string path);

    void CreateDirectory(string path);

    void ReplaceFile(string sourceFileName, string destinationFileName);

    void DeleteFileIfExists(string path);

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
