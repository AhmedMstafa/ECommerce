using System.Text.Json;
using System.Text.Json.Serialization;

namespace ECommerce.UseCases.Common.Models;

/// <summary>
/// Distinguishes between an omitted JSON property and an explicit null/value.
/// Unspecified => preserve existing value. Specified with null => clear.
/// Specified with value => update.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        HasValue = true;
        Value = value;
    }

    public bool HasValue { get; private init; }
    public T? Value { get; private init; }

    public static Optional<T> Unspecified() => default;
    public static Optional<T> Of(T? value) => new(value);

    public TResult Match<TResult>(Func<T?, TResult> specified, Func<TResult> unspecified) =>
        HasValue ? specified(Value) : unspecified();
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(innerType);
        return (JsonConverter)Activator.CreateInstance(converterType, options)!;
    }
}

public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    private readonly JsonConverter<T>? _inner;

    public OptionalJsonConverter(JsonSerializerOptions options)
    {
        _inner = options.GetConverter(typeof(T)) as JsonConverter<T>;
    }

    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Optional<T>.Of(default);
        }

        var value = _inner is not null
            ? _inner.Read(ref reader, typeof(T), options)
            : JsonSerializer.Deserialize<T>(ref reader, options);

        return Optional<T>.Of(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (_inner is not null)
        {
            _inner.Write(writer, value.Value, options);
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
