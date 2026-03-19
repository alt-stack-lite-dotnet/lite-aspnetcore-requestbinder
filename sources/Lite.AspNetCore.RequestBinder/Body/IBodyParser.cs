using Microsoft.AspNetCore.Http;

namespace Lite.AspNetCore.RequestBinder.Body;

public interface IBodyParser
{
    ValueTask<(bool Success, T? Value)> TryParseAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken
    )
        where T : notnull;
    ValueTask<(bool Success, object? Value)> TryParseAsync(
        HttpRequest request,
        Type type,
        CancellationToken cancellationToken
    );
}
