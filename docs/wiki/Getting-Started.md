# Getting Started

## Install / reference

Reference `Lite.AspNetCore.RequestBinder` from your application.

## Register services

At minimum, you need a body parser if you use `[FromBody]`:

```csharp
services.AddSingleton<Lite.AspNetCore.RequestBinder.Body.IBodyParser,
    Lite.AspNetCore.RequestBinder.Body.SystemTextJsonBodyParser>();
```

## Annotate request models

### Whole model from body

```csharp
using Lite.AspNetCore.RequestBinder.Attributes;

[FromBody]
public sealed record CreateUserRequest(string Name, int Age);
```

### Mixed sources (route + body), immutable struct

```csharp
using Lite.AspNetCore.RequestBinder.Attributes;

public readonly record struct UpdateSomeEntityCommand(
    [property: FromRoute("entityId")] int EntityId,
    [property: FromBody] Payload Payload);

public sealed record Payload(string Name);
```

## Use the generated binder

The generator emits `*Binder` classes implementing `IRequestBinder<T>` or `IAsyncRequestBinder<T>`.

Binders have constructor-injected dependencies (register as singletons in your app).

