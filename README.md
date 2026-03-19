# Lite.AspNetCore.RequestBinder

SG-first request binder for ASP.NET Core, focused on generated binders with a tight hot path.

## Pitch

- **No DTO pollution**: the generator emits separate `*Binder` types (no `partial` DTO modifications).
- **No DI lookups in the hot path**: dependencies are constructor-injected into the generated binder.
- **Body is buffered and rewound**: `IBodyParser` rewinds buffered request body.

## System.Text.Json source-gen (optional)

If you want STJ **source generation** (less reflection, better AOT/trim), declare a `JsonSerializerContext` in your app:

```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(UpdateSomeEntityCommand))]
public partial class AppJsonContext : JsonSerializerContext { }
```

Then register the JSON body parser with either:

- **source-gen only** (throws if a type is missing from the context):

```csharp
services.AddSingleton<Lite.AspNetCore.RequestBinder.Body.IBodyParser>(
    _ => Lite.AspNetCore.RequestBinder.Body.SystemTextJsonBodyParser.CreateSourceGenOnly(AppJsonContext.Default));
```

- **source-gen + fallback** (uses reflection for missing types):

```csharp
services.AddSingleton<Lite.AspNetCore.RequestBinder.Body.IBodyParser>(
    _ => Lite.AspNetCore.RequestBinder.Body.SystemTextJsonBodyParser.CreateSourceGenWithFallback(AppJsonContext.Default));
```

## Documentation

User docs live in `docs/wiki/` (English). Start here:

- `docs/wiki/Home.md`

Developer docs (how to work on this repo) live in `docs/`.

