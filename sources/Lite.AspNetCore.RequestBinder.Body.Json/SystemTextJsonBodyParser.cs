using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace Lite.AspNetCore.RequestBinder.Body;

public sealed class SystemTextJsonBodyParser : IBodyParser
{
    private readonly JsonSerializerOptions _options;

    /// <summary>Uses <see cref="JsonSerializerDefaults.Web"/> (reflection-based).</summary>
    public SystemTextJsonBodyParser()
    {
        _options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>Use your own options, e.g. with <see cref="JsonSerializerContext"/> for source-gen (zero reflection).</summary>
    /// <example>new SystemTextJsonBodyParser(MyAppJsonContext.Default.Options)</example>
    public SystemTextJsonBodyParser(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public static SystemTextJsonBodyParser CreateReflection() => new();

    /// <summary>
    /// Source-gen only: will throw at runtime for types missing in <paramref name="context"/>.
    /// </summary>
    public static SystemTextJsonBodyParser CreateSourceGenOnly(JsonSerializerContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = context,
        };
        return new SystemTextJsonBodyParser(options);
    }

    /// <summary>
    /// Source-gen when available, otherwise falls back to reflection metadata.
    /// </summary>
    public static SystemTextJsonBodyParser CreateSourceGenWithFallback(
        JsonSerializerContext context
    )
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                context,
                new DefaultJsonTypeInfoResolver()
            ),
        };
        return new SystemTextJsonBodyParser(options);
    }

    public ValueTask<(bool Success, T? Value)> TryParseAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken
    )
        where T : notnull
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        if (request.Body.Length == 0)
            return ValueTask.FromResult((true, (T?)(object?)null));
        try
        {
            var result = JsonSerializer.Deserialize<T>(request.Body, _options);
            return ValueTask.FromResult((true, (T?)(object?)result!));
        }
        catch
        {
            return ValueTask.FromResult((false, (T?)(object?)null));
        }
    }

    public ValueTask<(bool Success, object? Value)> TryParseAsync(
        HttpRequest request,
        Type type,
        CancellationToken cancellationToken
    )
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        if (request.Body.Length == 0)
            return ValueTask.FromResult((true, (object?)null));
        try
        {
            var result = JsonSerializer.Deserialize(request.Body, type, _options);
            return ValueTask.FromResult((true, result));
        }
        catch
        {
            return ValueTask.FromResult((false, (object?)null));
        }
    }
}
