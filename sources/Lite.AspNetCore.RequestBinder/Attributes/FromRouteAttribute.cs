namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromRouteAttribute : FromSourceAttributeBase
{
    public FromRouteAttribute()
        : base(null) { }

    public FromRouteAttribute(string key)
        : base(key) { }
}
