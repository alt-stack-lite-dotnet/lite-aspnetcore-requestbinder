namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromBodyAttribute : FromSourceAttributeBase
{
    public FromBodyAttribute()
        : base(null) { }

    public FromBodyAttribute(string key)
        : base(key) { }
}
