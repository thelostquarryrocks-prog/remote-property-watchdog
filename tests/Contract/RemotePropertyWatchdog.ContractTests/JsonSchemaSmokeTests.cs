using System.Text.Json;
using RemotePropertyWatchdog.Shared.Contracts;

namespace RemotePropertyWatchdog.ContractTests;

public sealed class JsonSchemaSmokeTests
{
    [Fact]
    public void RequiredSchemasAreValidJsonSchemaDocuments()
    {
        string repositoryRoot = FindRepositoryRoot();

        foreach (string schemaFile in ContractSchema.RequiredSchemaFiles)
        {
            string path = Path.Combine(repositoryRoot, "schemas", schemaFile);
            using FileStream stream = File.OpenRead(path);
            using JsonDocument document = JsonDocument.Parse(stream);

            JsonElement root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("$schema", out JsonElement schema));
            Assert.Contains("2020-12", schema.GetString(), StringComparison.Ordinal);
            Assert.True(root.TryGetProperty("$id", out _));
            Assert.True(root.TryGetProperty("type", out JsonElement type));
            Assert.Equal("object", type.GetString());
        }
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
