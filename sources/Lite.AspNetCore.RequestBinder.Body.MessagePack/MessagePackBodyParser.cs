using System.Buffers;
using MessagePack;
using Microsoft.AspNetCore.Http;

namespace Lite.AspNetCore.RequestBinder.Body;

public sealed class MessagePackBodyParser : IBodyParser
{
    private readonly MessagePackSerializerOptions? _options;

    public MessagePackBodyParser(MessagePackSerializerOptions? options = null)
    {
        _options = options;
    }

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
            await using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            request.Body.Position = 0;
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return (true, (T?)(object?)null);
            var seq = new ReadOnlySequence<byte>(bytes);
            var result = _options is not null
                ? MessagePackSerializer.Deserialize<T>(in seq, _options)
                : MessagePackSerializer.Deserialize<T>(in seq);
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
            await using var ms = new MemoryStream();
            await request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            request.Body.Position = 0;
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return (true, null);
            var seq = new ReadOnlySequence<byte>(bytes);

            var method =
                typeof(MessagePackSerializer).GetMethod(
                    nameof(MessagePackSerializer.Deserialize),
                    1,
                    new[] { typeof(ReadOnlySequence<byte>), typeof(MessagePackSerializerOptions) }
                )
                ?? typeof(MessagePackSerializer).GetMethod(
                    nameof(MessagePackSerializer.Deserialize),
                    1,
                    new[] { typeof(ReadOnlySequence<byte>) }
                )!;

            var generic = method.MakeGenericMethod(type);
            var args = _options is not null ? new object[] { seq, _options } : new object[] { seq };
            return (true, generic.Invoke(null, args));
        }
        catch
        {
            return (false, null);
        }
    }
}
