namespace Lite.AspNetCore.RequestBinder.Fluent;

public interface IPropertyBindingBuilder<TRequest, TProperty>
{
    IPropertyBindingBuilder<TRequest, TProperty> FromQuery(string key);
    IPropertyBindingBuilder<TRequest, TProperty> FromRoute(string key);
    IPropertyBindingBuilder<TRequest, TProperty> FromHeader(string key);
    IPropertyBindingBuilder<TRequest, TProperty> FromCookie(string key);
    IPropertyBindingBuilder<TRequest, TProperty> FromForm(string key);
    IPropertyBindingBuilder<TRequest, TProperty> FromBody();
    IPropertyBindingBuilder<TRequest, TProperty> FromServices();
}
