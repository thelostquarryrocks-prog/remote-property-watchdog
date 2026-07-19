using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RemotePropertyWatchdog.Agent.Service;

namespace RemotePropertyWatchdog.UnitTests;

public sealed class AgentHostSmokeTests
{
    [Fact]
    public void AgentHostBuilderRegistersHostedServiceAndLifecycleServices()
    {
        using IHost host = Program.CreateHostBuilder([]).Build();

        IEnumerable<IHostedService> hostedServices = host.Services.GetRequiredService<IEnumerable<IHostedService>>();

        Assert.Contains(hostedServices, service => service.GetType() == typeof(AgentWorker));
        Assert.IsType<AgentLifecycleCoordinator>(host.Services.GetRequiredService<IAgentLifecycleCoordinator>());
        Assert.IsType<FileAgentLifecycleStore>(host.Services.GetRequiredService<IAgentLifecycleStore>());
    }

    [Fact]
    public async Task AgentHostStartsAndStopsWithCleanLifecycleMarker()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:00"
        ]).Build();

        await host.StartAsync(CancellationToken.None);
        IAgentLifecycleCoordinator coordinator = host.Services.GetRequiredService<IAgentLifecycleCoordinator>();
        AgentLifecycleState? started = null;
        for (int i = 0; i < 20; i++)
        {
            started = coordinator.CurrentState;
            if (started?.IsReady == true)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        await host.StopAsync(CancellationToken.None);

        AgentLifecycleState? stopped = coordinator.CurrentState;

        Assert.NotNull(started);
        Assert.True(started.IsReady);
        Assert.NotNull(stopped);
        Assert.True(stopped.PlannedShutdown);
        Assert.True(File.Exists(markerPath));
    }

    [Fact]
    public async Task AgentHostRejectsReadinessTimeoutOverSixtySecondsOnStartup()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:ReadinessTimeout", "00:01:01"
        ]).Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AgentHostRejectsCleanShutdownWriteTimeoutOverThirtySecondsOnStartup()
    {
        string markerPath = Path.Combine(Path.GetTempPath(), "rpw-agent-tests", $"{Guid.NewGuid():N}.json");
        using IHost host = Program.CreateHostBuilder([
            "--AgentLifecycle:MarkerPath", markerPath,
            "--AgentLifecycle:CleanShutdownWriteTimeout", "00:00:31"
        ]).Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(CancellationToken.None));
    }

    [Fact]
    public void CanonicalServiceNameDoesNotDriftFromWindowsLifecycleScript()
    {
        string script = File.ReadAllText(FindRepositoryFile("scripts", "windows-service-lifecycle.ps1"));

        Assert.Contains(AgentServiceIdentity.ServiceName, script, StringComparison.Ordinal);
        Assert.Contains(AgentServiceIdentity.DisplayName, script, StringComparison.Ordinal);
    }


    [Fact]
    public void PowerShellLiveMarkerReaderUsesDeleteCompatibleFileStreamWithoutGetContent()
    {
        string script = File.ReadAllText(FindRepositoryFile("scripts", "windows-service-lifecycle.ps1"));
        int start = script.IndexOf("function Read-LifecycleMarker", StringComparison.Ordinal);
        int end = script.IndexOf("function Test-LifecycleMarker", StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        string readerFunction = script[start..end];

        Assert.DoesNotContain("Get-Content", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileStream]::new", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileShare]::Delete", readerFunction, StringComparison.Ordinal);
        Assert.Contains("[System.IO.FileAccess]::Read", readerFunction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWorkerRecordsCleanShutdownOnlyAfterBaseStopCompletes()
    {
        RecordingLifecycleCoordinator coordinator = new();
        AgentWorker worker = CreateWorker(coordinator);
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilStartedAsync(coordinator);

        Assert.False(coordinator.StopCalled);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(coordinator.StopCalled);
    }


    [Fact]
    public async Task AgentWorkerMarkReadyFailureMarksFaultedAndRemainsObservable()
    {
        InvalidOperationException expected = new("readiness failed");
        RecordingLifecycleCoordinator coordinator = new() { MarkReadyException = expected };
        ListLogger<AgentWorker> logger = new();
        AgentWorker worker = CreateWorker(coordinator, logger: logger);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilLoggedAsync(logger, eventId: 5);
        await worker.StopAsync(CancellationToken.None);
        Assert.False(coordinator.StopCalled);
        Assert.Contains(logger.Entries, entry => entry.EventId == 5 && entry.LogLevel == LogLevel.Error && ReferenceEquals(entry.Exception, expected));
        Assert.Contains(logger.Entries, entry => entry.EventId == 6 && entry.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public async Task AgentWorkerFaultDrivenStopDoesNotRecordPlannedShutdown()
    {
        RecordingLifecycleCoordinator coordinator = new() { MarkReadyException = new InvalidOperationException("readiness failed") };
        AgentWorker worker = CreateWorker(coordinator);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilFaultedAsync(coordinator);
        await worker.StopAsync(CancellationToken.None);

        Assert.False(coordinator.StopCalled);
        Assert.False(coordinator.CleanShutdownRecorded);
    }

    [Fact]
    public async Task AgentWorkerLeavesMarkerUncleanAndContinuesShutdownWhenFinalLifecycleWriteTimesOut()
    {
        RecordingLifecycleCoordinator coordinator = new() { DelayStopUntilCancelled = true };
        AgentWorker worker = CreateWorker(coordinator, TimeSpan.FromMilliseconds(1));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntilStartedAsync(coordinator);

        await worker.StopAsync(CancellationToken.None);

        Assert.True(coordinator.StopCalled);
        Assert.False(coordinator.CleanShutdownRecorded);
    }

    private static async Task WaitUntilLoggedAsync(ListLogger<AgentWorker> logger, int eventId)
    {
        for (int i = 0; i < 20; i++)
        {
            if (logger.Entries.Any(entry => entry.EventId == eventId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Expected log entry was not written within the test timeout.");
    }

    private static async Task WaitUntilFaultedAsync(RecordingLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 20; i++)
        {
            if (coordinator.MarkReadyCalled)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Worker did not reach MarkReadyAsync within the test timeout.");
    }

    private static async Task WaitUntilStartedAsync(RecordingLifecycleCoordinator coordinator)
    {
        for (int i = 0; i < 20; i++)
        {
            if (coordinator.CurrentState is not null)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }

        throw new InvalidOperationException("Worker did not start within the test timeout.");
    }

    private static AgentWorker CreateWorker(
        RecordingLifecycleCoordinator coordinator,
        TimeSpan? cleanShutdownWriteTimeout = null,
        ILogger<AgentWorker>? logger = null) =>
        new(
            coordinator,
            Options.Create(new AgentLifecycleOptions { CleanShutdownWriteTimeout = cleanShutdownWriteTimeout ?? TimeSpan.FromSeconds(1) }),
            logger ?? NullLogger<AgentWorker>.Instance);

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(relativeParts));
    }

    private sealed class RecordingLifecycleCoordinator : IAgentLifecycleCoordinator
    {
        private readonly AgentLifecycleState state = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            StoppedAtUtc: null,
            PlannedShutdown: false,
            PreviousSessionId: null,
            PreviousSessionEndedCleanly: true);

        public bool StopCalled { get; private set; }

        public bool CleanShutdownRecorded { get; private set; }

        public bool DelayStopUntilCancelled { get; init; }

        public Exception? MarkReadyException { get; init; }

        public bool MarkReadyCalled { get; private set; }

        public AgentLifecycleState? CurrentState { get; private set; }

        public Task<AgentLifecycleState> StartAsync(CancellationToken cancellationToken)
        {
            CurrentState = state;
            return Task.FromResult(state);
        }

        public Task<AgentLifecycleState> MarkReadyAsync(CancellationToken cancellationToken)
        {
            MarkReadyCalled = true;
            if (MarkReadyException is not null)
            {
                return Task.FromException<AgentLifecycleState>(MarkReadyException);
            }

            CurrentState = state;
            return Task.FromResult(state);
        }

        public async Task<AgentLifecycleState> StopAsync(CancellationToken cancellationToken)
        {
            StopCalled = true;
            if (DelayStopUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            CleanShutdownRecorded = true;
            CurrentState = state with { PlannedShutdown = true, StoppedAtUtc = DateTimeOffset.UtcNow };
            return CurrentState;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId.Id, exception, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, int EventId, Exception? Exception, string Message);
}
