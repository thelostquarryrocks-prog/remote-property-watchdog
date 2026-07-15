using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.ContractTests;

public sealed class ContractPayloadSchemaTests
{
    public static TheoryData<string, object> Payloads => new()
    {
        { "heartbeat.schema.json", ContractExamples.Heartbeat() },
        { "material-event.schema.json", ContractExamples.MaterialEvent() },
        { "event-batch.schema.json", ContractExamples.EventBatch() },
        { "incident-summary.schema.json", ContractExamples.IncidentSummary() }
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void ExamplePayloadsValidateAgainstSchemas(string schemaFile, object payload)
    {
        RegisterAllSchemas();
        JsonSchema schema = LoadSchema(schemaFile);
        JsonNode node = JsonNode.Parse(ContractJson.Serialize(payload))!;

        EvaluationResults results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid, results.ToString());
    }

    [Fact]
    public void JsonOutputIsDeterministicForSnapshots()
    {
        string first = ContractJson.Serialize(ContractExamples.Heartbeat());
        string second = ContractJson.Serialize(ContractExamples.Heartbeat());

        Assert.Equal(first, second);
        Assert.StartsWith("{\"schema_version\":\"0.1\",\"heartbeat_id\":", first, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditiveOptionalFieldsCanBeIgnoredByCurrentDtoReaders()
    {
        string json = ContractJson.Serialize(ContractExamples.MaterialEvent());
        using JsonDocument original = JsonDocument.Parse(json);
        Dictionary<string, object?> fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, ContractJson.Options)!;
        fields["future_optional"] = "safe-to-ignore";
        string additiveJson = JsonSerializer.Serialize(fields, ContractJson.Options);

        MaterialEventDto? payload = ContractJson.Deserialize<MaterialEventDto>(additiveJson);

        Assert.NotNull(payload);
        Assert.Equal(original.RootElement.GetProperty("message_id").GetString(), payload.MessageId);
        Assert.Empty(payload.Validate());
    }

    private static void RegisterAllSchemas()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string schemaFile in ContractSchema.RequiredSchemaFiles)
        {
            JsonSchema schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(repositoryRoot, "schemas", schemaFile)));
            SchemaRegistry.Global.Register(schema);
        }
    }

    private static JsonSchema LoadSchema(string schemaFile)
    {
        string repositoryRoot = FindRepositoryRoot();
        string path = Path.Combine(repositoryRoot, "schemas", schemaFile);
        string schemaText = File.ReadAllText(path);
        if (schemaFile == "event-batch.schema.json")
        {
            JsonNode root = JsonNode.Parse(schemaText)!;
            JsonNode materialEvent = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "schemas", "material-event.schema.json")))!;
            root["properties"]!["events"]!["items"] = materialEvent;
            schemaText = root.ToJsonString();
        }

        return JsonSchema.FromText(schemaText);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
