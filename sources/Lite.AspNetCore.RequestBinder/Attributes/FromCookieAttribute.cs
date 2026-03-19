namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromCookieAttribute : FromSourceAttributeBase
{
    public FromCookieAttribute()
        : base(null) { }

    public FromCookieAttribute(string key)
        : base(key) { }
}
