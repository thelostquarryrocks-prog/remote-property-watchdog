using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using RemotePropertyWatchdog.Contracts;

namespace RemotePropertyWatchdog.ContractTests;

public sealed class ContractPayloadSchemaTests
{
    public static TheoryData<string, string, Type> ExampleFiles => new()
    {
        { "heartbeat.example.json", "heartbeat.schema.json", typeof(HeartbeatDto) },
        { "material-event.example.json", "material-event.schema.json", typeof(MaterialEventDto) },
        { "event-batch.example.json", "event-batch.schema.json", typeof(EventBatchDto) },
        { "incident-summary.example.json", "incident-summary.schema.json", typeof(IncidentSummaryDto) }
    };

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void CheckedExampleFilesDeserializeValidateAgainstSchemaAndMatchCanonicalSerialization(string exampleFile, string schemaFile, Type dtoType)
    {
        RegisterAllSchemas();
        string repositoryRoot = FindRepositoryRoot();
        string exampleJson = File.ReadAllText(Path.Combine(repositoryRoot, "examples", "contracts", exampleFile)).TrimEnd();
        object payload = JsonSerializer.Deserialize(exampleJson, dtoType, ContractJson.Options) ?? throw new JsonException($"{exampleFile} did not deserialize.");
        IContractValidatable validatable = Assert.IsAssignableFrom<IContractValidatable>(payload);

        Assert.Empty(validatable.Validate());
        AssertValidAgainstSchema(schemaFile, JsonNode.Parse(exampleJson)!);
        Assert.Equal(ContractJson.Serialize(payload), exampleJson);
    }

    [Fact]
    public void EveryCheckedExampleFileHasContractCoverage()
    {
        string examplesPath = Path.Combine(FindRepositoryRoot(), "examples", "contracts");
        string[] checkedExamples = Directory.GetFiles(examplesPath, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
        string[] coveredExamples = ExampleFiles.Select(row => (string)row[0]).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(coveredExamples, checkedExamples);
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

    private static void AssertValidAgainstSchema(string schemaFile, JsonNode node)
    {
        JsonSchema schema = LoadSchema(schemaFile);
        EvaluationResults results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(results.IsValid, results.ToString());
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
        return JsonSchema.FromText(File.ReadAllText(path));
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
