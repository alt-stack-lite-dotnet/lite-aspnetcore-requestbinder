namespace Lite.AspNetCore.RequestBinder.Attributes;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false
)]
public sealed class BinderNameAttribute : Attribute
{
    public BinderNameAttribute(string name) => Name = name;

    public string Name { get; }
}
