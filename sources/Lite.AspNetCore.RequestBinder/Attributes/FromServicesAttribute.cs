namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class FromServicesAttribute : FromSourceAttributeBase
{
    public FromServicesAttribute()
        : base(null) { }

    public FromServicesAttribute(string key)
        : base(key) { }
}
