namespace Lite.AspNetCore.RequestBinder;

public interface IAsyncRequestBinder<T> : IRequestBinderCore
{
    ValueTask<T> BindAsync(HttpRequest request, CancellationToken cancellationToken);
}
