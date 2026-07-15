using System.Text.Json;

namespace RemotePropertyWatchdog.Contracts;

public readonly record struct ContractValidationError(string Field, string Code, string Message);

public sealed class ContractValidationException(IReadOnlyList<ContractValidationError> errors)
    : ArgumentException("Contract payload failed validation.")
{
    public IReadOnlyList<ContractValidationError> Errors { get; } = errors;
}

public interface IContractValidatable
{
    IReadOnlyList<ContractValidationError> Validate();
}

public static class ContractValidation
{
    public const int MaximumHeartbeatUtf8Bytes = 10 * 1024;

    public static void ThrowIfInvalid(this IContractValidatable value)
    {
        IReadOnlyList<ContractValidationError> errors = value.Validate();
        if (errors.Count > 0)
        {
            throw new ContractValidationException(errors);
        }
    }

    internal static void RequireSchemaVersion(List<ContractValidationError> errors, string value) =>
        RequireExact(errors, "schema_version", value, ContractSchema.CurrentVersion);

    internal static void RequireIdentifier(List<ContractValidationError> errors, string field, string? value, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) errors.Add(new(field, "required", "A stable opaque identifier is required."));
            return;
        }

        if (value.Length is < 8 or > 128)
        {
            errors.Add(new(field, "invalid_length", "Identifier length must be between 8 and 128 characters."));
        }
    }

    internal static void RequirePositive(List<ContractValidationError> errors, string field, long value)
    {
        if (value < 1) errors.Add(new(field, "required_positive", "Value must be greater than or equal to 1."));
    }

    internal static void RequireNonNegative(List<ContractValidationError> errors, string field, long value)
    {
        if (value < 0) errors.Add(new(field, "required_non_negative", "Value must be greater than or equal to 0."));
    }

    internal static void RequireUtc(List<ContractValidationError> errors, string field, DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            errors.Add(new(field, "utc_required", "Timestamp must use UTC offset zero."));
        }
    }

    internal static void RequireExact(List<ContractValidationError> errors, string field, string? value, string expected)
    {
        if (!StringComparer.Ordinal.Equals(value, expected))
        {
            errors.Add(new(field, "unsupported_value", $"Value must be '{expected}'."));
        }
    }

    internal static void RejectUndefinedEnum<T>(List<ContractValidationError> errors, string field, T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) errors.Add(new(field, "unsupported_value", "Enum value is not defined by the current contract."));
    }

    internal static void RequireMaxSerializedSize<T>(List<ContractValidationError> errors, string field, T value, int maxBytes)
    {
        int bytes = JsonSerializer.SerializeToUtf8Bytes(value, ContractJson.Options).Length;
        if (bytes > maxBytes) errors.Add(new(field, "payload_too_large", $"Serialized payload must be no more than {maxBytes} bytes."));
    }
}
