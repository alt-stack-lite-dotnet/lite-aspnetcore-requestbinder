namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromHeaderAttribute : FromSourceAttributeBase
{
    public FromHeaderAttribute()
        : base(null) { }

    public FromHeaderAttribute(string key)
        : base(key) { }
}
