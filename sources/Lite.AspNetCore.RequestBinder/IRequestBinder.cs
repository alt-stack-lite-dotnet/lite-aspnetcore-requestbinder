namespace Lite.AspNetCore.RequestBinder;

public interface IRequestBinder<out T> : IRequestBinderCore
{
    T Bind(HttpRequest request);
}
