namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public abstract class FromSourceAttributeBase : Attribute
{
    protected FromSourceAttributeBase(string? key) => Key = key;

    public string? Key { get; }
}
