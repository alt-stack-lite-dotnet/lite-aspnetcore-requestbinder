using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lite.AspNetCore.RequestBinder.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class RequestBinderSourceGenerator : IIncrementalGenerator
{
    private const string FromSourceBase =
        "Lite.AspNetCore.RequestBinder.Attributes.FromSourceAttributeBase";
    private const string FromBody = "Lite.AspNetCore.RequestBinder.Attributes.FromBodyAttribute";
    private const string FromForm = "Lite.AspNetCore.RequestBinder.Attributes.FromFormAttribute";
    private const string BinderNameAttribute =
        "Lite.AspNetCore.RequestBinder.Attributes.BinderNameAttribute";
    private const string RequestBindingConfigurationOpen =
        "Lite.AspNetCore.RequestBinder.Fluent.IRequestBindingConfiguration<TRequest>";
    private const string IParsableOpen = "System.IParsable<TSelf>";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => TryGetModel(ctx, ct)
            )
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var configs = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, ct) => TryGetConfig(ctx, ct)
            )
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        var modelsFromConfigs = configs.Select(static (c, _) => c.Model);

        var attrsList = candidates.Collect();
        var configList = modelsFromConfigs.Collect();

        var allModels = attrsList
            .Combine(configList)
            .Select(static (pair, _) => DedupModels(pair.Left, pair.Right));

        var combined = allModels.Combine(configs.Collect());

        context.RegisterSourceOutput(
            combined,
            static (spc, pair) =>
            {
                var models = pair.Left;
                var allConfigs = pair.Right;
                for (var i = 0; i < models.Count; i++)
                {
                    var model = models[i];
                    Emit(spc, model, FindConfigFor(model, allConfigs));
                }
            }
        );
    }

    private static IReadOnlyList<RequestModel> DedupModels(
        System.Collections.Immutable.ImmutableArray<RequestModel> fromAttrs,
        System.Collections.Immutable.ImmutableArray<RequestModel> fromConfigs
    )
    {
        var dict = new Dictionary<string, RequestModel>(StringComparer.Ordinal);
        for (var i = 0; i < fromConfigs.Length; i++)
        {
            var m = fromConfigs[i];
            dict[m.FullName] = m;
        }

        for (var i = 0; i < fromAttrs.Length; i++)
        {
            var m = fromAttrs[i];
            if (!dict.TryGetValue(m.FullName, out var existing))
            {
                dict[m.FullName] = m;
                continue;
            }

            var existingScore = (existing.BindFromBody ? 10 : 0) + existing.Properties.Count;
            var score = (m.BindFromBody ? 10 : 0) + m.Properties.Count;
            if (score > existingScore)
                dict[m.FullName] = m;
        }
        return dict.Values.ToList();
    }

    private static RequestModel? TryGetModel(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(typeDecl, ct) is not INamedTypeSymbol typeSymbol)
            return null;

        if (typeSymbol.Arity > 0)
            return null;

        var hasTypeFromBody = typeSymbol
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == FromBody);
        var props = new List<PropertyBinding>();

        foreach (var propSym in GetAllInstanceProperties(typeSymbol))
        {
            var attr = FindFromAttribute(propSym);
            if (attr is null)
                continue;

            var (attrMetaName, key) = attr.Value;
            var isParsable = IsParsable(propSym.Type);
            var isInitOnly = propSym.SetMethod?.IsInitOnly == true;
            var isSettable = propSym.SetMethod is not null && !isInitOnly;
            props.Add(
                new PropertyBinding(
                    propName: propSym.Name,
                    propType: propSym.Type,
                    fromAttributeMetadataName: attrMetaName,
                    key: key,
                    isParsable: isParsable,
                    isSettable: isSettable,
                    isInitOnly: isInitOnly
                )
            );
        }

        if (!hasTypeFromBody && props.Count == 0)
            return null;

        var needsAsync =
            hasTypeFromBody
            || props.Any(p =>
                p.FromAttributeMetadataName == FromBody || p.FromAttributeMetadataName == FromForm
            );

        return new RequestModel(
            ns: typeSymbol.ContainingNamespace is { IsGlobalNamespace: true }
                ? ""
                : (typeSymbol.ContainingNamespace?.ToDisplayString() ?? ""),
            typeName: typeSymbol.Name,
            binderName: GetBinderName(typeSymbol, overrideName: null),
            typeKind: typeSymbol.TypeKind,
            isRecord: typeDecl is RecordDeclarationSyntax,
            fullName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            needsAsync: needsAsync,
            properties: props,
            bindFromBody: hasTypeFromBody,
            ctorParams: GetBestCtorParams(typeSymbol),
            isValueType: typeSymbol.IsValueType
        );
    }

    private static string GetBinderName(INamedTypeSymbol type, string? overrideName)
    {
        if (!string.IsNullOrWhiteSpace(overrideName))
            return overrideName!;

        foreach (var a in type.GetAttributes())
        {
            if (
                a.AttributeClass?.ToDisplayString() == BinderNameAttribute
                && a.ConstructorArguments.Length > 0
                && a.ConstructorArguments[0].Value is string s
                && !string.IsNullOrWhiteSpace(s)
            )
            {
                return s;
            }
        }

        var parts = new Stack<string>();
        INamedTypeSymbol? cur = type;
        while (cur is not null)
        {
            parts.Push(cur.Name);
            cur = cur.ContainingType;
        }
        return string.Join("_", parts) + "Binder";
    }

    private static IReadOnlyList<CtorParam> GetBestCtorParams(INamedTypeSymbol type)
    {
        var ctors = type
            .InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .ToList();
        var best = ctors.FirstOrDefault();
        if (best is null || best.Parameters.Length == 0)
            return Array.Empty<CtorParam>();

        return best
            .Parameters.Select(p => new CtorParam(
                p.Name,
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ))
            .ToList();
    }

    private static bool IsParsable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nt)
        {
            foreach (var i in nt.AllInterfaces)
            {
                if (i.IsGenericType && i.OriginalDefinition.ToDisplayString() == IParsableOpen)
                {
                    return i.TypeArguments.Length == 1
                        && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type);
                }
            }
        }
        return false;
    }

    private static (string AttributeMetadataName, string Key)? FindFromAttribute(
        IPropertySymbol prop
    )
    {
        foreach (var a in prop.GetAttributes())
        {
            var at = a.AttributeClass;
            if (at is null)
                continue;

            if (!DerivesFrom(at, FromSourceBase))
                continue;

            var key =
                a.ConstructorArguments.Length > 0 && a.ConstructorArguments[0].Value is string s
                    ? s
                    : (InferExternalName(prop) ?? prop.Name);
            return (at.ToDisplayString(), key);
        }
        return null;
    }

    private static string? InferExternalName(IPropertySymbol prop)
    {
        foreach (var a in prop.GetAttributes())
        {
            var name = a.AttributeClass?.ToDisplayString();
            if (name == "System.Text.Json.Serialization.JsonPropertyNameAttribute")
            {
                if (
                    a.ConstructorArguments.Length > 0
                    && a.ConstructorArguments[0].Value is string s
                    && !string.IsNullOrWhiteSpace(s)
                )
                    return s;
            }
        }

        foreach (var a in prop.GetAttributes())
        {
            var name = a.AttributeClass?.ToDisplayString();
            if (name == "System.Runtime.Serialization.DataMemberAttribute")
            {
                foreach (var kv in a.NamedArguments)
                {
                    if (
                        kv.Key == "Name"
                        && kv.Value.Value is string s
                        && !string.IsNullOrWhiteSpace(s)
                    )
                        return s;
                }
            }
        }

        foreach (var a in prop.GetAttributes())
        {
            var name = a.AttributeClass?.ToDisplayString();
            if (name == "Newtonsoft.Json.JsonPropertyAttribute")
            {
                if (
                    a.ConstructorArguments.Length > 0
                    && a.ConstructorArguments[0].Value is string s
                    && !string.IsNullOrWhiteSpace(s)
                )
                    return s;
                foreach (var kv in a.NamedArguments)
                {
                    if (
                        kv.Key == "PropertyName"
                        && kv.Value.Value is string s2
                        && !string.IsNullOrWhiteSpace(s2)
                    )
                        return s2;
                }
            }
        }

        return null;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string baseMetadataName)
    {
        var cur = type;
        while (cur is not null)
        {
            if (cur.ToDisplayString() == baseMetadataName)
                return true;
            cur = cur.BaseType;
        }
        return false;
    }

    private static IEnumerable<IPropertySymbol> GetAllInstanceProperties(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        INamedTypeSymbol? cur = type;
        while (cur is not null)
        {
            foreach (var m in cur.GetMembers().OfType<IPropertySymbol>())
            {
                if (m.IsStatic)
                    continue;
                if (m.IsIndexer)
                    continue;
                if (m.DeclaredAccessibility is Accessibility.Private)
                    continue;
                if (!seen.Add(m.Name))
                    continue;
                yield return m;
            }
            cur = cur.BaseType;
        }
    }

    private static RequestConfigModel? FindConfigFor(
        RequestModel model,
        IReadOnlyList<RequestConfigModel> configs
    )
    {
        for (var i = 0; i < configs.Count; i++)
            if (configs[i].TargetTypeFqn == model.FullName)
                return configs[i];
        return null;
    }

    private static RequestConfigModel? TryGetConfig(
        GeneratorSyntaxContext ctx,
        CancellationToken ct
    )
    {
        if (ctx.Node is not ClassDeclarationSyntax cls)
            return null;
        if (ctx.SemanticModel.GetDeclaredSymbol(cls, ct) is not INamedTypeSymbol symbol)
            return null;

        INamedTypeSymbol? iface = null;
        foreach (var i in symbol.AllInterfaces)
        {
            if (
                i.IsGenericType
                && i.OriginalDefinition.ToDisplayString() == RequestBindingConfigurationOpen
            )
            {
                iface = i;
                break;
            }
        }
        if (iface is null)
            return null;

        var targetType = iface.TypeArguments[0] as INamedTypeSymbol;
        if (targetType is null)
            return null;
        var configure = cls
            .Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m =>
                m.Identifier.Text == "Configure" && m.ParameterList.Parameters.Count == 1
            );
        if (configure?.Body is null)
            return null;

        var builderParam = configure.ParameterList.Parameters[0].Identifier.Text;
        var bindings = ParseConfigureBody(configure.Body, builderParam);
        if (bindings.Count == 0)
            return null;

        var needsAsync = bindings.Any(b =>
            b.Source is ConfigBindingSource.Body or ConfigBindingSource.Form
        );
        var binderOverride = TryGetBinderNameOverride(symbol);
        var model = BuildModelFromTypeSymbol(targetType, needsAsync, binderOverride);
        if (model is null)
            return null;
        return new RequestConfigModel(
            targetTypeFqn: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            needsAsync: needsAsync,
            bindings: bindings,
            model: model
        );
    }

    private static string? TryGetBinderNameOverride(INamedTypeSymbol configType)
    {
        foreach (var a in configType.GetAttributes())
        {
            if (
                a.AttributeClass?.ToDisplayString() == BinderNameAttribute
                && a.ConstructorArguments.Length > 0
                && a.ConstructorArguments[0].Value is string s
                && !string.IsNullOrWhiteSpace(s)
            )
            {
                return s;
            }
        }
        return null;
    }

    private static RequestModel? BuildModelFromTypeSymbol(
        INamedTypeSymbol typeSymbol,
        bool needsAsyncFromConfig,
        string? binderOverride
    )
    {
        if (typeSymbol.Arity > 0)
            return null;

        var hasTypeFromBody = typeSymbol
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == FromBody);
        var props = new List<PropertyBinding>();
        foreach (var propSym in GetAllInstanceProperties(typeSymbol))
        {
            var isParsable = IsParsable(propSym.Type);
            var isInitOnly = propSym.SetMethod?.IsInitOnly == true;
            var isSettable = propSym.SetMethod is not null && !isInitOnly;
            props.Add(
                new PropertyBinding(
                    propName: propSym.Name,
                    propType: propSym.Type,
                    fromAttributeMetadataName: "Lite.AspNetCore.RequestBinder.Attributes.FromQueryAttribute",
                    key: InferExternalName(propSym) ?? propSym.Name,
                    isParsable: isParsable,
                    isSettable: isSettable,
                    isInitOnly: isInitOnly
                )
            );
        }

        var needsAsync = needsAsyncFromConfig || hasTypeFromBody;
        return new RequestModel(
            ns: typeSymbol.ContainingNamespace is { IsGlobalNamespace: true }
                ? ""
                : (typeSymbol.ContainingNamespace?.ToDisplayString() ?? ""),
            typeName: typeSymbol.Name,
            binderName: GetBinderName(typeSymbol, binderOverride),
            typeKind: typeSymbol.TypeKind,
            isRecord: typeSymbol.IsRecord,
            fullName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            needsAsync: needsAsync,
            properties: props,
            bindFromBody: hasTypeFromBody,
            ctorParams: GetBestCtorParams(typeSymbol),
            isValueType: typeSymbol.IsValueType
        );
    }

    private static List<ConfigBinding> ParseConfigureBody(BlockSyntax body, string builderParam)
    {
        var list = new List<ConfigBinding>();

        foreach (var stmt in body.Statements)
        {
            if (
                stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
            )
                continue;

            var binding = ParseBindingChain(inv, builderParam);
            if (binding is not null)
                list.Add(binding.Value);
        }

        return list;
    }

    private static ConfigBinding? ParseBindingChain(
        InvocationExpressionSyntax tail,
        string builderParam
    )
    {
        string? propName = null;
        ConfigBindingSource? source = null;
        string? key = null;

        var cur = tail;
        while (cur is not null)
        {
            if (cur.Expression is not MemberAccessExpressionSyntax ma)
                break;

            var method = ma.Name.Identifier.Text;

            if (method == "Bind")
            {
                if (ma.Expression is IdentifierNameSyntax id && id.Identifier.Text == builderParam)
                {
                    if (
                        cur.ArgumentList.Arguments.Count > 0
                        && cur.ArgumentList.Arguments[0].Expression
                            is SimpleLambdaExpressionSyntax sl
                        && sl.ExpressionBody is MemberAccessExpressionSyntax spa
                    )
                    {
                        propName = spa.Name.Identifier.Text;
                    }
                }
                break;
            }

            if (
                method
                is "FromQuery"
                    or "FromRoute"
                    or "FromHeader"
                    or "FromCookie"
                    or "FromForm"
                    or "FromBody"
                    or "FromServices"
            )
            {
                if (source is null)
                {
                    source = method switch
                    {
                        "FromQuery" => ConfigBindingSource.Query,
                        "FromRoute" => ConfigBindingSource.Route,
                        "FromHeader" => ConfigBindingSource.Header,
                        "FromCookie" => ConfigBindingSource.Cookie,
                        "FromForm" => ConfigBindingSource.Form,
                        "FromBody" => ConfigBindingSource.Body,
                        "FromServices" => ConfigBindingSource.Services,
                        _ => null,
                    };

                    if (method is "FromBody" or "FromServices")
                        key = null;
                    else if (cur.ArgumentList.Arguments.Count > 0)
                        key = TryGetStringLiteral(cur.ArgumentList.Arguments[0].Expression);
                }
            }

            cur = ma.Expression as InvocationExpressionSyntax;
        }

        if (propName is null || source is null)
            return null;

        return new ConfigBinding(propName, source.Value, key);
    }

    private static string? TryGetStringLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax { Token.ValueText: var t } ? t : null;

    private static IReadOnlyList<PropertyBinding> ApplyConfig(
        IReadOnlyList<PropertyBinding> attrs,
        RequestConfigModel? config
    )
    {
        if (config is null)
            return attrs;

        var dict = new Dictionary<string, ConfigBinding>(StringComparer.Ordinal);
        foreach (var b in config.Bindings)
            dict[b.PropertyName] = b;

        var list = new List<PropertyBinding>(attrs.Count);
        foreach (var a in attrs)
        {
            if (dict.TryGetValue(a.PropName, out var b))
            {
                var meta = b.Source switch
                {
                    ConfigBindingSource.Query =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromQueryAttribute",
                    ConfigBindingSource.Route =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromRouteAttribute",
                    ConfigBindingSource.Header =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromHeaderAttribute",
                    ConfigBindingSource.Cookie =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromCookieAttribute",
                    ConfigBindingSource.Form =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromFormAttribute",
                    ConfigBindingSource.Body =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromBodyAttribute",
                    ConfigBindingSource.Services =>
                        "Lite.AspNetCore.RequestBinder.Attributes.FromServicesAttribute",
                    _ => a.FromAttributeMetadataName,
                };
                list.Add(
                    new PropertyBinding(
                        a.PropName,
                        a.PropType,
                        meta,
                        b.Key ?? a.Key,
                        a.IsParsable,
                        a.IsSettable,
                        a.IsInitOnly
                    )
                );
            }
            else
            {
                list.Add(a);
            }
        }
        return list;
    }

    private static void Emit(
        SourceProductionContext ctx,
        RequestModel model,
        RequestConfigModel? config
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Globalization;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;");
        sb.AppendLine("using Microsoft.Extensions.Primitives;");
        sb.AppendLine("using Lite.AspNetCore.RequestBinder.Parsing;");
        sb.AppendLine("using Lite.AspNetCore.RequestBinder;");
        sb.AppendLine("using Lite.AspNetCore.RequestBinder.Body;");
        sb.AppendLine();

        var hasNs = !string.IsNullOrWhiteSpace(model.Namespace);
        if (hasNs)
        {
            sb.AppendLine($"namespace {model.Namespace}");
            sb.AppendLine("{");
        }

        var i = hasNs ? "    " : "";
        var binderName = model.BinderName;

        sb.AppendLine(
            $"{i}public sealed class {binderName} : global::Lite.AspNetCore.RequestBinder.{(model.NeedsAsync ? $"IAsyncRequestBinder<{model.FullName}>" : $"IRequestBinder<{model.FullName}>")}"
        );
        sb.AppendLine($"{i}{{");

        var bindings = ApplyConfig(model.Properties, config);
        var needsBodyParser =
            model.BindFromBody || bindings.Any(p => p.FromAttributeMetadataName == FromBody);
        if (needsBodyParser)
            sb.AppendLine(
                $"{i}    private readonly global::Lite.AspNetCore.RequestBinder.Body.IBodyParser _bodyParser;"
            );

        var valueParserTypes = bindings
            .Where(p =>
                p.FromAttributeMetadataName != FromBody
                && !p.FromAttributeMetadataName.EndsWith(
                    ".FromServicesAttribute",
                    StringComparison.Ordinal
                )
                && !p.IsParsable
                && p.PropTypeFqn != "global::System.String"
                && p.PropTypeFqn != "string"
            )
            .Select(p => p.PropTypeFqn)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var vpFieldByType = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var idx = 0; idx < valueParserTypes.Count; idx++)
        {
            var t = valueParserTypes[idx];
            var field = $"_vp{idx}";
            vpFieldByType[t] = field;
            sb.AppendLine(
                $"{i}    private readonly global::Lite.AspNetCore.RequestBinder.Parsing.IValueParser<{t}> {field};"
            );
        }

        var serviceTypes = bindings
            .Where(p =>
                p.FromAttributeMetadataName.EndsWith(
                    ".FromServicesAttribute",
                    StringComparison.Ordinal
                )
            )
            .Select(p => p.PropTypeFqn)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var svcFieldByType = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var idx = 0; idx < serviceTypes.Count; idx++)
        {
            var t = serviceTypes[idx];
            var field = $"_svc{idx}";
            svcFieldByType[t] = field;
            sb.AppendLine($"{i}    private readonly {t} {field};");
        }

        var ctorParams = new List<string>();
        if (needsBodyParser)
            ctorParams.Add("global::Lite.AspNetCore.RequestBinder.Body.IBodyParser bodyParser");
        for (var idx = 0; idx < valueParserTypes.Count; idx++)
            ctorParams.Add(
                $"global::Lite.AspNetCore.RequestBinder.Parsing.IValueParser<{valueParserTypes[idx]}> vp{idx}"
            );
        for (var idx = 0; idx < serviceTypes.Count; idx++)
            ctorParams.Add($"{serviceTypes[idx]} svc{idx}");

        sb.AppendLine();
        sb.AppendLine($"{i}    public {binderName}({string.Join(", ", ctorParams)})");
        sb.AppendLine($"{i}    {{");
        if (needsBodyParser)
            sb.AppendLine($"{i}        _bodyParser = bodyParser;");
        for (var idx = 0; idx < valueParserTypes.Count; idx++)
            sb.AppendLine($"{i}        _vp{idx} = vp{idx};");
        for (var idx = 0; idx < serviceTypes.Count; idx++)
            sb.AppendLine($"{i}        _svc{idx} = svc{idx};");
        sb.AppendLine($"{i}    }}");
        sb.AppendLine();

        if (model.NeedsAsync)
        {
            sb.AppendLine(
                $"{i}    public async global::System.Threading.Tasks.ValueTask<{model.FullName}> BindAsync("
            );
            sb.AppendLine($"{i}        global::Microsoft.AspNetCore.Http.HttpRequest request,");
            sb.AppendLine(
                $"{i}        global::System.Threading.CancellationToken cancellationToken)"
            );
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        {model.FullName} result = default;");
            var needsCtorCreation =
                model.CtorParams.Count > 0
                && (
                    model.IsRecord
                    || model.TypeKind == TypeKind.Struct
                    || bindings.Any(b => b.IsInitOnly || !b.IsSettable)
                );
            var needsForm = bindings.Any(p => p.FromAttributeMetadataName == FromForm);
            if (needsForm && !needsCtorCreation)
            {
                sb.AppendLine(
                    $"{i}        global::Microsoft.AspNetCore.Http.IFormCollection? form = null;"
                );
                sb.AppendLine($"{i}        bool formLoaded = false;");
            }
            if (needsCtorCreation)
            {
                if (model.IsValueType)
                {
                    sb.AppendLine(
                        $"{i}        (bool bodyOk, {model.FullName}? bodyNullable) = await _bodyParser.TryParseAsync<{model.FullName}>(request, cancellationToken).ConfigureAwait(false);"
                    );
                    sb.AppendLine(
                        $"{i}        var body = bodyOk ? (bodyNullable ?? default({model.FullName})) : default({model.FullName});"
                    );
                }
                else
                {
                    sb.AppendLine(
                        $"{i}        var (bodyOk, body) = await _bodyParser.TryParseAsync<{model.FullName}>(request, cancellationToken).ConfigureAwait(false);"
                    );
                    sb.AppendLine($"{i}        if (!bodyOk) body = null;");
                }

                foreach (var cp in model.CtorParams)
                {
                    var pn = San(cp.Name);
                    sb.AppendLine($"{i}        {cp.TypeFqn} p_{pn} = default!;");

                    var match = bindings.FirstOrDefault(b =>
                        string.Equals(b.PropName, cp.Name, StringComparison.OrdinalIgnoreCase)
                    );
                    if (match is not null && match.FromAttributeMetadataName != FromBody)
                    {
                        EmitBindToLocal(sb, $"{i}        ", match, $"p_{pn}");
                    }
                    else
                    {
                        if (model.IsValueType)
                            sb.AppendLine($"{i}        p_{pn} = body.{cp.Name};");
                        else
                            sb.AppendLine(
                                $"{i}        if (body is not null) p_{pn} = body.{cp.Name};"
                            );
                    }
                }

                var argList = string.Join(", ", model.CtorParams.Select(cp => $"p_{San(cp.Name)}"));
                sb.AppendLine($"{i}        result = new {model.FullName}({argList});");
            }
            else
            {
                sb.AppendLine($"{i}        result = new {model.FullName}();");
                if (model.BindFromBody)
                {
                    if (model.IsValueType)
                    {
                        sb.AppendLine(
                            $"{i}        (bool bodyOk, {model.FullName}? bodyNullable) = await _bodyParser.TryParseAsync<{model.FullName}>(request, cancellationToken).ConfigureAwait(false);"
                        );
                        sb.AppendLine(
                            $"{i}        result = bodyOk ? (bodyNullable ?? default({model.FullName})) : default({model.FullName});"
                        );
                    }
                    else
                    {
                        sb.AppendLine(
                            $"{i}        var (parsedOk, parsed) = await _bodyParser.TryParseAsync<{model.FullName}>(request, cancellationToken).ConfigureAwait(false);"
                        );
                        sb.AppendLine(
                            $"{i}        if (parsedOk && parsed is not null) result = parsed;"
                        );
                    }
                }
                foreach (var p in bindings)
                    EmitAssign(
                        sb,
                        $"{i}        ",
                        p,
                        async: true,
                        vpFieldByType: vpFieldByType,
                        svcFieldByType: svcFieldByType
                    );
            }

            sb.AppendLine($"{i}        return result;");
            sb.AppendLine($"{i}    }}");
        }
        else
        {
            sb.AppendLine(
                $"{i}    public {model.FullName} Bind(global::Microsoft.AspNetCore.Http.HttpRequest request)"
            );
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        var result = new {model.FullName}();");

            foreach (var p in bindings)
                EmitAssign(
                    sb,
                    $"{i}        ",
                    p,
                    async: false,
                    vpFieldByType: vpFieldByType,
                    svcFieldByType: svcFieldByType
                );

            sb.AppendLine($"{i}        return result;");
            sb.AppendLine($"{i}    }}");
        }

        sb.AppendLine();
        foreach (var p in bindings)
        {
            if (p.FromAttributeMetadataName == FromBody)
                continue;
            if (
                p.FromAttributeMetadataName.EndsWith(
                    ".FromServicesAttribute",
                    StringComparison.Ordinal
                )
            )
                continue;

            var mn = San(p.PropName);
            sb.AppendLine(
                $"{i}    private {p.PropTypeFqn} Parse_{mn}(global::Microsoft.Extensions.Primitives.StringValues values)"
            );
            sb.AppendLine($"{i}        => Parse_{mn}((string?)values);");
            sb.AppendLine();
            sb.AppendLine($"{i}    private {p.PropTypeFqn} Parse_{mn}(string? value)");
            sb.AppendLine($"{i}    {{");
            sb.AppendLine($"{i}        if (value is null) return default!;");

            if (p.PropTypeFqn is "string" or "global::System.String")
            {
                sb.AppendLine($"{i}        return value;");
            }
            else if (p.IsParsable)
            {
                sb.AppendLine(
                    $"{i}        return {p.PropTypeFqn}.TryParse(value, global::System.Globalization.CultureInfo.InvariantCulture, out var tmp) ? tmp : default!;"
                );
            }
            else if (vpFieldByType.TryGetValue(p.PropTypeFqn, out var vpField))
            {
                sb.AppendLine(
                    $"{i}        return {vpField}.TryParse(value, global::System.Globalization.CultureInfo.InvariantCulture, out var tmp) ? tmp : default!;"
                );
            }
            else
            {
                sb.AppendLine($"{i}        return default!;");
            }

            sb.AppendLine($"{i}    }}");
            sb.AppendLine();
        }

        sb.AppendLine($"{i}}}");

        if (hasNs)
            sb.AppendLine("}");

        ctx.AddSource($"{binderName}.g.cs", sb.ToString());
    }

    private static void EmitAssign(
        StringBuilder sb,
        string ind,
        PropertyBinding p,
        bool async,
        IReadOnlyDictionary<string, string> vpFieldByType,
        IReadOnlyDictionary<string, string> svcFieldByType
    )
    {
        var keyLiteral = EscapeString(p.Key);
        var propAccess = $"result.{p.PropName}";

        if (p.FromAttributeMetadataName == FromBody)
        {
            if (!async)
                return;
            if (p.PropType.IsValueType)
            {
                sb.AppendLine(
                    $"{ind}(bool bodyOk_{p.PropName}, {p.PropTypeFqn}? bodyValue_{p.PropName}) = await _bodyParser.TryParseAsync<{p.PropTypeFqn}>(request, cancellationToken).ConfigureAwait(false);"
                );
                sb.AppendLine(
                    $"{ind}{propAccess} = bodyOk_{p.PropName} ? (bodyValue_{p.PropName} ?? default({p.PropTypeFqn})) : default({p.PropTypeFqn});"
                );
            }
            else
            {
                sb.AppendLine(
                    $"{ind}var (bodyOk_{p.PropName}, bodyValue_{p.PropName}) = await _bodyParser.TryParseAsync<{p.PropTypeFqn}>(request, cancellationToken).ConfigureAwait(false);"
                );
                sb.AppendLine(
                    $"{ind}if (bodyOk_{p.PropName} && bodyValue_{p.PropName} is not null) {propAccess} = bodyValue_{p.PropName};"
                );
            }
            return;
        }

        if (p.FromAttributeMetadataName == FromForm)
        {
            if (!async)
                return;
            sb.AppendLine($"{ind}if (!formLoaded)");
            sb.AppendLine($"{ind}{{");
            sb.AppendLine($"{ind}    formLoaded = true;");
            sb.AppendLine($"{ind}    form = request.HasFormContentType");
            sb.AppendLine(
                $"{ind}        ? await request.ReadFormAsync(cancellationToken).ConfigureAwait(false)"
            );
            sb.AppendLine($"{ind}        : null;");
            sb.AppendLine($"{ind}}}");
            sb.AppendLine($"{ind}StringValues sv_{p.PropName} = default;");
            sb.AppendLine(
                $"{ind}if (form is not null && form.TryGetValue(\"{keyLiteral}\", out sv_{p.PropName}))"
            );
            sb.AppendLine($"{ind}    {propAccess} = Parse_{San(p.PropName)}(sv_{p.PropName});");
            return;
        }

        if (
            p.FromAttributeMetadataName.EndsWith(".FromServicesAttribute", StringComparison.Ordinal)
        )
        {
            var f = svcFieldByType[p.PropTypeFqn];
            sb.AppendLine($"{ind}{propAccess} = {f};");
            return;
        }

        if (p.FromAttributeMetadataName.EndsWith(".FromCookieAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.Cookies.TryGetValue(\"{keyLiteral}\", out var s_{p.PropName}))"
            );
            sb.AppendLine($"{ind}    {propAccess} = Parse_{San(p.PropName)}(s_{p.PropName});");
            return;
        }

        if (p.FromAttributeMetadataName.EndsWith(".FromHeaderAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.Headers.TryGetValue(\"{keyLiteral}\", out var hv_{p.PropName}))"
            );
            sb.AppendLine($"{ind}    {propAccess} = Parse_{San(p.PropName)}(hv_{p.PropName});");
            return;
        }

        if (p.FromAttributeMetadataName.EndsWith(".FromRouteAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.RouteValues.TryGetValue(\"{keyLiteral}\", out var rv_{p.PropName}))"
            );
            sb.AppendLine(
                $"{ind}    {propAccess} = Parse_{San(p.PropName)}(rv_{p.PropName} is null ? null : Convert.ToString(rv_{p.PropName}, CultureInfo.InvariantCulture));"
            );
            return;
        }

        sb.AppendLine(
            $"{ind}if (request.Query.TryGetValue(\"{keyLiteral}\", out var qv_{p.PropName}))"
        );
        sb.AppendLine($"{ind}    {propAccess} = Parse_{San(p.PropName)}(qv_{p.PropName});");
    }

    private static void EmitBindToLocal(
        StringBuilder sb,
        string ind,
        PropertyBinding p,
        string localName
    )
    {
        var keyLiteral = EscapeString(p.Key);
        var parseCall = $"Parse_{San(p.PropName)}";

        if (
            p.FromAttributeMetadataName.EndsWith(".FromServicesAttribute", StringComparison.Ordinal)
        )
        {
            sb.AppendLine($"{ind}{localName} = {localName};");
            return;
        }

        if (p.FromAttributeMetadataName.EndsWith(".FromCookieAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.Cookies.TryGetValue(\"{keyLiteral}\", out var s_{p.PropName}))"
            );
            sb.AppendLine($"{ind}    {localName} = {parseCall}(s_{p.PropName});");
            return;
        }
        if (p.FromAttributeMetadataName.EndsWith(".FromHeaderAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.Headers.TryGetValue(\"{keyLiteral}\", out var hv_{p.PropName}))"
            );
            sb.AppendLine($"{ind}    {localName} = {parseCall}(hv_{p.PropName});");
            return;
        }
        if (p.FromAttributeMetadataName.EndsWith(".FromRouteAttribute", StringComparison.Ordinal))
        {
            sb.AppendLine(
                $"{ind}if (request.RouteValues.TryGetValue(\"{keyLiteral}\", out var rv_{p.PropName}))"
            );
            sb.AppendLine(
                $"{ind}    {localName} = {parseCall}(rv_{p.PropName} is null ? null : Convert.ToString(rv_{p.PropName}, CultureInfo.InvariantCulture));"
            );
            return;
        }
        sb.AppendLine(
            $"{ind}if (request.Query.TryGetValue(\"{keyLiteral}\", out var qv_{p.PropName}))"
        );
        sb.AppendLine($"{ind}    {localName} = {parseCall}(qv_{p.PropName});");
    }

    private static string San(string name) =>
        new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

    private static string EscapeString(string s) =>
        s.Replace("\\\\", "\\\\\\\\").Replace("\"", "\\\\\"");
}

internal sealed class RequestModel
{
    public string Namespace { get; }
    public string TypeName { get; }
    public string BinderName { get; }
    public TypeKind TypeKind { get; }
    public bool IsRecord { get; }
    public string FullName { get; }
    public bool NeedsAsync { get; }
    public bool BindFromBody { get; }
    public bool IsValueType { get; }
    public IReadOnlyList<PropertyBinding> Properties { get; }
    public IReadOnlyList<CtorParam> CtorParams { get; }

    public RequestModel(
        string ns,
        string typeName,
        string binderName,
        TypeKind typeKind,
        bool isRecord,
        string fullName,
        bool needsAsync,
        IReadOnlyList<PropertyBinding> properties,
        bool bindFromBody,
        IReadOnlyList<CtorParam> ctorParams,
        bool isValueType
    )
    {
        Namespace = ns;
        TypeName = typeName;
        BinderName = binderName;
        TypeKind = typeKind;
        IsRecord = isRecord;
        FullName = fullName;
        NeedsAsync = needsAsync;
        Properties = properties;
        BindFromBody = bindFromBody;
        CtorParams = ctorParams;
        IsValueType = isValueType;
    }
}

internal sealed class PropertyBinding
{
    public string PropName { get; }
    public ITypeSymbol PropType { get; }
    public string FromAttributeMetadataName { get; }
    public string Key { get; }
    public bool IsParsable { get; }
    public bool IsSettable { get; }
    public bool IsInitOnly { get; }

    public PropertyBinding(
        string propName,
        ITypeSymbol propType,
        string fromAttributeMetadataName,
        string key,
        bool isParsable,
        bool isSettable,
        bool isInitOnly
    )
    {
        PropName = propName;
        PropType = propType;
        FromAttributeMetadataName = fromAttributeMetadataName;
        Key = key;
        IsParsable = isParsable;
        IsSettable = isSettable;
        IsInitOnly = isInitOnly;
    }

    public string PropTypeFqn => PropType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}

internal sealed class CtorParam
{
    public string Name { get; }
    public string TypeFqn { get; }

    public CtorParam(string name, string typeFqn)
    {
        Name = name;
        TypeFqn = typeFqn;
    }
}

internal sealed class RequestConfigModel
{
    public string TargetTypeFqn { get; }
    public bool NeedsAsync { get; }
    public IReadOnlyList<ConfigBinding> Bindings { get; }
    public RequestModel Model { get; }

    public RequestConfigModel(
        string targetTypeFqn,
        bool needsAsync,
        IReadOnlyList<ConfigBinding> bindings,
        RequestModel model
    )
    {
        TargetTypeFqn = targetTypeFqn;
        NeedsAsync = needsAsync;
        Bindings = bindings;
        Model = model;
    }
}

internal enum ConfigBindingSource
{
    Query = 1,
    Route = 2,
    Header = 3,
    Cookie = 4,
    Form = 5,
    Body = 6,
    Services = 7,
}

internal readonly struct ConfigBinding
{
    public string PropertyName { get; }
    public ConfigBindingSource Source { get; }
    public string? Key { get; }

    public ConfigBinding(string propertyName, ConfigBindingSource source, string? key)
    {
        PropertyName = propertyName;
        Source = source;
        Key = key;
    }
}
