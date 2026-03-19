namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromFormAttribute : FromSourceAttributeBase
{
    public FromFormAttribute()
        : base(null) { }

    public FromFormAttribute(string key)
        : base(key) { }
}
