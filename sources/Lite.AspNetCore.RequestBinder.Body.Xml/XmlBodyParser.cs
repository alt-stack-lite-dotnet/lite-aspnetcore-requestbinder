using System.Xml.Serialization;
using Microsoft.AspNetCore.Http;

namespace Lite.AspNetCore.RequestBinder.Body;

public sealed class XmlBodyParser : IBodyParser
{
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
            var serializer = new XmlSerializer(typeof(T));
            var result = serializer.Deserialize(request.Body);
            return ValueTask.FromResult((true, (T?)(object?)(result is T t ? t : default)));
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
            var serializer = new XmlSerializer(type);
            var result = serializer.Deserialize(request.Body);
            return ValueTask.FromResult((true, result));
        }
        catch
        {
            return ValueTask.FromResult((false, (object?)null));
        }
    }
}
