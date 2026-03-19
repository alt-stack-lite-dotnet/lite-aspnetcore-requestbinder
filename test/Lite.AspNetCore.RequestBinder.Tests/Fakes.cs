using System.Text;
using System.Text.Json;
using Lite.AspNetCore.RequestBinder.Body;
using Lite.AspNetCore.RequestBinder.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lite.AspNetCore.RequestBinder.Tests;

internal sealed class TestBodyParser : IBodyParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<(bool Success, T? Value)> TryParseAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken
    )
        where T : notnull
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        try
        {
            var result = await JsonSerializer
                .DeserializeAsync<T>(request.Body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return (true, (T?)(object?)result!);
        }
        catch
        {
            return (false, (T?)(object?)null);
        }
    }

    public async ValueTask<(bool Success, object? Value)> TryParseAsync(
        HttpRequest request,
        Type type,
        CancellationToken cancellationToken
    )
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        try
        {
            var result = await JsonSerializer
                .DeserializeAsync(request.Body, type, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return (true, result);
        }
        catch
        {
            return (false, null);
        }
    }
}

internal sealed class IntParser : IValueParser<int>
{
    public bool TryParse(string? value, IFormatProvider? provider, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, provider, out result);
}

internal static class TestHttp
{
    public static DefaultHttpContext CreateContext(
        string? jsonBody = null,
        string? queryString = null,
        IReadOnlyDictionary<string, string>? routeValues = null,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? cookies = null,
        IReadOnlyDictionary<string, string>? form = null,
        Action<IServiceCollection>? addServices = null,
        byte[]? bodyBytes = null,
        string? contentType = null
    )
    {
        var ctx = new DefaultHttpContext();
        var services = new ServiceCollection()
            .AddSingleton<IBodyParser, TestBodyParser>()
            .AddSingleton<IValueParser<int>, IntParser>();
        addServices?.Invoke(services);
        ctx.RequestServices = services.BuildServiceProvider();

        if (queryString is not null)
            ctx.Request.QueryString = new QueryString(queryString);

        if (routeValues is not null)
            foreach (var (k, v) in routeValues)
                ctx.Request.RouteValues[k] = v;

        if (headers is not null)
            foreach (var (k, v) in headers)
                ctx.Request.Headers[k] = v;

        if (cookies is not null)
        {
            var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
            ctx.Request.Headers.Cookie = cookieHeader;
        }

        if (bodyBytes is not null && contentType is not null)
        {
            ctx.Request.Body = new MemoryStream(bodyBytes);
            ctx.Request.ContentType = contentType;
        }
        else if (form is not null)
        {
            var formContent = string.Join(
                "&",
                form.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}")
            );
            var bytes = Encoding.UTF8.GetBytes(formContent);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentType = "application/x-www-form-urlencoded";
        }
        else if (jsonBody is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentType = "application/json";
        }

        return ctx;
    }
}
