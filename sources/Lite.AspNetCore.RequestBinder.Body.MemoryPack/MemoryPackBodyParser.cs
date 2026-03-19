using MemoryPack;
using Microsoft.AspNetCore.Http;

namespace Lite.AspNetCore.RequestBinder.Body;

public sealed class MemoryPackBodyParser : IBodyParser
{
    public async ValueTask<(bool Success, T? Value)> TryParseAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken
    )
        where T : notnull
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        if (request.Body.Length == 0)
            return (true, (T?)(object?)null);
        try
        {
            var result = await MemoryPackSerializer
                .DeserializeAsync<T>(request.Body)
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
        if (request.Body.Length == 0)
            return (true, null);
        try
        {
            var result = await MemoryPackSerializer
                .DeserializeAsync(type, request.Body)
                .ConfigureAwait(false);
            return (true, result);
        }
        catch
        {
            return (false, null);
        }
    }
}
