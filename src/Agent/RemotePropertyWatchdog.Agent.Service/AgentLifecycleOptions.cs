namespace RemotePropertyWatchdog.Agent.Service;

public sealed class AgentLifecycleOptions
{
    public const string SectionName = "AgentLifecycle";

    public string? MarkerPath { get; set; }

    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
