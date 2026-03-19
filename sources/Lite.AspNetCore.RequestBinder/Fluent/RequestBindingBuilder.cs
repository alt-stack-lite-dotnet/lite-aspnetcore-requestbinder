using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Lite.AspNetCore.RequestBinder.Fluent;

internal sealed class RequestBindingBuilder<TRequest> : IRequestBindingBuilder<TRequest>
{
    private readonly List<PropertyBindingDefinition<TRequest>> _bindings = new();

    public IReadOnlyList<PropertyBindingDefinition<TRequest>> Bindings => _bindings;

    public IPropertyBindingBuilder<TRequest, TProperty> Bind<TProperty>(
        Expression<Func<TRequest, TProperty>> property
    )
    {
        var propName = ExpressionHelpers.GetPropertyName(property);
        var def = new PropertyBindingDefinition<TRequest>(propName, typeof(TProperty));
        _bindings.Add(def);
        return new PropertyBindingBuilder<TRequest, TProperty>(def);
    }
}

internal static class ExpressionHelpers
{
    public static string GetPropertyName<T, TProperty>(Expression<Func<T, TProperty>> expression)
    {
        if (expression.Body is MemberExpression member)
            return member.Member.Name;

        if (expression.Body is UnaryExpression { Operand: MemberExpression um })
            return um.Member.Name;

        throw new ArgumentException("Expression must be a simple property access expression.");
    }
}
