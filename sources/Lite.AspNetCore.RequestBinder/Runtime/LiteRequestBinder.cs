using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lite.AspNetCore.RequestBinder.Body;
using Lite.AspNetCore.RequestBinder.Fluent;
using Lite.AspNetCore.RequestBinder.Parsing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Lite.AspNetCore.RequestBinder;

public sealed class LiteRequestBinder<TRequest>
    : IRequestBinder<TRequest>,
        IAsyncRequestBinder<TRequest>
    where TRequest : new()
{
    private readonly IRequestBindingConfiguration<TRequest> _config;
    private readonly Lazy<CompiledRequestBinder<TRequest>> _compiled;

    public LiteRequestBinder(IRequestBindingConfiguration<TRequest> config)
    {
        _config = config;
        _compiled = new Lazy<CompiledRequestBinder<TRequest>>(Compile);
    }

    private CompiledRequestBinder<TRequest> Compiled => _compiled.Value;

    public TRequest Bind(HttpRequest request) => Compiled.Bind(request);

    public ValueTask<TRequest> BindAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    ) => Compiled.BindAsync(request, cancellationToken);

    private CompiledRequestBinder<TRequest> Compile()
    {
        var b = new RequestBindingBuilder<TRequest>();
        _config.Configure(b);
        return CompiledRequestBinder<TRequest>.FromDefinitions(b.Bindings);
    }
}

internal sealed class CompiledRequestBinder<TRequest>
    where TRequest : new()
{
    private readonly CompiledPropertyBinder<TRequest>[] _props;
    private readonly bool _needsAsync;
    private readonly bool _hasBody;

    private CompiledRequestBinder(
        CompiledPropertyBinder<TRequest>[] props,
        bool needsAsync,
        bool hasBody
    )
    {
        _props = props;
        _needsAsync = needsAsync;
        _hasBody = hasBody;
    }

    public static CompiledRequestBinder<TRequest> FromDefinitions(
        System.Collections.Generic.IReadOnlyList<PropertyBindingDefinition<TRequest>> defs
    )
    {
        var list = new CompiledPropertyBinder<TRequest>[defs.Count];
        var needsAsync = false;
        var hasBody = false;
        for (var i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            var src = d.Source ?? BindingSource.Query;
            var key = d.Key ?? d.PropertyName;
            if (src is BindingSource.Body or BindingSource.Form)
                needsAsync = true;
            if (src is BindingSource.Body)
                hasBody = true;

            var prop = typeof(TRequest).GetProperty(
                d.PropertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (prop is null || prop.SetMethod is null)
                continue;
            list[i] = new CompiledPropertyBinder<TRequest>(prop, src, key);
        }
        return new CompiledRequestBinder<TRequest>(list, needsAsync, hasBody);
    }

    public TRequest Bind(HttpRequest request)
    {
        if (_needsAsync)
            throw new InvalidOperationException(
                "This binder requires async binding (body/form). Use BindAsync()."
            );

        var result = new TRequest();
        for (var i = 0; i < _props.Length; i++)
            _props[i]?.Bind(request, result);
        return result;
    }

    public async ValueTask<TRequest> BindAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = new TRequest();
        var formState = new FormState();
        var bodyState = new BodyState<TRequest>();
        if (_hasBody)
        {
            bodyState.Parser = request.HttpContext.RequestServices.GetService<IBodyParser>();
            if (bodyState.Parser is null)
                throw new InvalidOperationException(
                    "Body binding requires an IBodyParser registered in DI."
                );
        }
        for (var i = 0; i < _props.Length; i++)
            if (_props[i] is not null)
                await _props[i]
                    .BindAsync(request, result, cancellationToken, formState, bodyState)
                    .ConfigureAwait(false);
        return result;
    }
}

internal sealed class CompiledPropertyBinder<TRequest>
{
    private readonly PropertyInfo _prop;
    private readonly BindingSource _source;
    private readonly string _key;

    public CompiledPropertyBinder(PropertyInfo prop, BindingSource source, string key)
    {
        _prop = prop;
        _source = source;
        _key = key;
    }

    public void Bind(HttpRequest request, TRequest target)
    {
        switch (_source)
        {
            case BindingSource.Query:
                if (request.Query.TryGetValue(_key, out var qv))
                    Set(request, target, qv);
                break;
            case BindingSource.Route:
                if (request.RouteValues.TryGetValue(_key, out var rv))
                    Set(
                        request,
                        target,
                        rv is null ? null : Convert.ToString(rv, CultureInfo.InvariantCulture)
                    );
                break;
            case BindingSource.Header:
                if (request.Headers.TryGetValue(_key, out var hv))
                    Set(request, target, hv);
                break;
            case BindingSource.Cookie:
                if (request.Cookies.TryGetValue(_key, out var cv))
                    Set(request, target, cv);
                break;
            case BindingSource.Services:
                var svc = request.HttpContext.RequestServices.GetService(_prop.PropertyType);
                if (svc is not null)
                    _prop.SetValue(target, svc);
                break;
            default:
                throw new InvalidOperationException("This binding source requires async binding.");
        }
    }

    public async ValueTask BindAsync(
        HttpRequest request,
        TRequest target,
        CancellationToken cancellationToken,
        FormState formState,
        BodyState<TRequest> bodyState
    )
    {
        switch (_source)
        {
            case BindingSource.Body:
            {
                var parser =
                    bodyState.Parser
                    ?? throw new InvalidOperationException(
                        "Body binding requires an IBodyParser registered in DI."
                    );
                var (success, v) = await parser
                    .TryParseAsync(request, _prop.PropertyType, cancellationToken)
                    .ConfigureAwait(false);
                if (success && v is not null)
                    _prop.SetValue(target, v);
                return;
            }
            case BindingSource.Form:
            {
                if (!formState.Loaded)
                {
                    formState.Loaded = true;
                    formState.Form = request.HasFormContentType
                        ? await request.ReadFormAsync(cancellationToken).ConfigureAwait(false)
                        : null;
                }
                if (formState.Form is not null && formState.Form.TryGetValue(_key, out var fv))
                    Set(request, target, fv);
                return;
            }
            default:
                Bind(request, target);
                return;
        }
    }

    private void Set(HttpRequest request, TRequest target, StringValues values) =>
        Set(request, target, (string?)values);

    private void Set(HttpRequest request, TRequest target, string? value)
    {
        if (value is null)
            return;
        var t = _prop.PropertyType;
        object? parsed = null;

        var svcType = typeof(IValueParser<>).MakeGenericType(t);
        var parserObj = request.HttpContext.RequestServices.GetService(svcType);
        if (parserObj is not null)
        {
            var tryParse = svcType.GetMethod(
                "TryParse",
                new[] { typeof(string), typeof(IFormatProvider), t.MakeByRefType() }
            );
            if (tryParse is not null)
            {
                var args = new object?[] { value, ValueParser.Invariant, null };
                var ok = tryParse.Invoke(parserObj, args);
                if (ok is bool b0 && b0 && args[2] is not null)
                {
                    _prop.SetValue(target, args[2]);
                    return;
                }
            }
        }

        if (t == typeof(string))
            parsed = value;
        else if (
            t == typeof(int)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32)
        )
            parsed = i32;
        else if (
            t == typeof(long)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64)
        )
            parsed = i64;
        else if (t == typeof(bool) && bool.TryParse(value, out var b))
            parsed = b;
        else if (t == typeof(Guid) && Guid.TryParse(value, out var g))
            parsed = g;
        else if (
            t == typeof(double)
            && double.TryParse(
                value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var d
            )
        )
            parsed = d;
        else if (
            t == typeof(decimal)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)
        )
            parsed = m;
        else if (
            t == typeof(DateTime)
            && DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dt
            )
        )
            parsed = dt;
        else if (t.IsEnum && Enum.TryParse(t, value, ignoreCase: true, out var e))
            parsed = e;
        else
        {
            var tp = t.GetMethod(
                "TryParse",
                new[] { typeof(string), typeof(IFormatProvider), t.MakeByRefType() }
            );
            if (tp is not null)
            {
                var args = new object?[] { value, CultureInfo.InvariantCulture, null };
                var ok = tp.Invoke(null, args);
                if (ok is bool b2 && b2 && args[2] is not null)
                    parsed = args[2];
            }
        }

        if (parsed is not null)
            _prop.SetValue(target, parsed);
    }
}

internal sealed class FormState
{
    public IFormCollection? Form;
    public bool Loaded;
}

internal sealed class BodyState<TRequest>
{
    public IBodyParser? Parser;
}
