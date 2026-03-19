namespace Lite.AspNetCore.RequestBinder.Fluent;

public sealed class PropertyBindingDefinition<TRequest>
{
    public string PropertyName { get; }
    public System.Type PropertyType { get; }

    public BindingSource? Source { get; internal set; }
    public string? Key { get; internal set; }

    public PropertyBindingDefinition(string propertyName, System.Type propertyType)
    {
        PropertyName = propertyName;
        PropertyType = propertyType;
    }
}
