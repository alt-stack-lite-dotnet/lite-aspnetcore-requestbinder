namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromQueryAttribute : FromSourceAttributeBase
{
    public FromQueryAttribute()
        : base(null) { }

    public FromQueryAttribute(string key)
        : base(key) { }
}
