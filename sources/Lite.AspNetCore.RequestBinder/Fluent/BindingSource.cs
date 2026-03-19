namespace Lite.AspNetCore.RequestBinder.Fluent;

public enum BindingSource
{
    Query = 1,
    Route = 2,
    Header = 3,
    Cookie = 4,
    Form = 5,
    Body = 6,
    Services = 7,
}
