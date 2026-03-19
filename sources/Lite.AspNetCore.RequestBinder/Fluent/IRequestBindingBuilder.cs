using System.Linq.Expressions;

namespace Lite.AspNetCore.RequestBinder.Fluent;

public interface IRequestBindingBuilder<TRequest>
{
    IPropertyBindingBuilder<TRequest, TProperty> Bind<TProperty>(
        Expression<Func<TRequest, TProperty>> property
    );
}
