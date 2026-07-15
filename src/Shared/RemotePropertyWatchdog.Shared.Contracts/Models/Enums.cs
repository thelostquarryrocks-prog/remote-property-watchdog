namespace RemotePropertyWatchdog.Contracts;

public enum AgentMode { Normal, Away, Maintenance }
public enum ProbeState { Unknown, Up, Degraded, Down }
public enum MaterialEventType { TargetStateChanged, GatewayStateChanged, InternetStateChanged, AgentLifecycleChanged, ModeChanged, ClockAnomalyDetected, ConfigurationApplyResult }
public enum IncidentClass { PropertyUnreachable, InternetUnavailable, GatewayOrLocalNetworkUnavailable, CriticalDeviceOffline, ServiceUnavailable, SharedDependencyFailure, UnusualSequentialFailures, MonitoringStopped }
public enum IncidentSeverity { Info, Medium, High, Critical }
public enum IncidentState { Open, Acknowledged, Recovering, Resolved }
public enum Confidence { Low, Medium, High }
