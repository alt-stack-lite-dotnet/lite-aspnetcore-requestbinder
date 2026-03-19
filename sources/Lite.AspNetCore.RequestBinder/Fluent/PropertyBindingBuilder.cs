namespace Lite.AspNetCore.RequestBinder.Fluent;

internal sealed class PropertyBindingBuilder<TRequest, TProperty>
    : IPropertyBindingBuilder<TRequest, TProperty>
{
    private readonly PropertyBindingDefinition<TRequest> _def;

    public PropertyBindingBuilder(PropertyBindingDefinition<TRequest> def) => _def = def;

    public IPropertyBindingBuilder<TRequest, TProperty> FromQuery(string key)
    {
        _def.Source = BindingSource.Query;
        _def.Key = key;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromRoute(string key)
    {
        _def.Source = BindingSource.Route;
        _def.Key = key;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromHeader(string key)
    {
        _def.Source = BindingSource.Header;
        _def.Key = key;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromCookie(string key)
    {
        _def.Source = BindingSource.Cookie;
        _def.Key = key;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromForm(string key)
    {
        _def.Source = BindingSource.Form;
        _def.Key = key;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromBody()
    {
        _def.Source = BindingSource.Body;
        _def.Key = null;
        return this;
    }

    public IPropertyBindingBuilder<TRequest, TProperty> FromServices()
    {
        _def.Source = BindingSource.Services;
        _def.Key = null;
        return this;
    }
}
