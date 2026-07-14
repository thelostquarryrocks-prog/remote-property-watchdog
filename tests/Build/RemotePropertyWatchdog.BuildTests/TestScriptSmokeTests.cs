namespace RemotePropertyWatchdog.BuildTests;

public sealed class TestScriptSmokeTests
{
    [Fact]
    public void TestScriptRunsKickoffValidationAndDotnetSolutionCommands()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "test.ps1"));

        Assert.Contains("validate-kickoff.py", script, StringComparison.Ordinal);
        Assert.Contains("dotnet restore", script, StringComparison.Ordinal);
        Assert.Contains("dotnet build", script, StringComparison.Ordinal);
        Assert.Contains("dotnet test", script, StringComparison.Ordinal);
        Assert.Contains("scan-secrets.py", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
