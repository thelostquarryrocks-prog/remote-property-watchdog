using RemotePropertyWatchdog.Shared.Contracts;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "remote-property-watchdog-cloud",
    schema_version = ContractSchema.CurrentVersion
}));
app.MapHealthChecks("/health/live");

app.Run();

public partial class Program
{
}
