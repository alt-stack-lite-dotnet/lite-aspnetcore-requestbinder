using Lite.AspNetCore.RequestBinder.Attributes;
using Lite.AspNetCore.RequestBinder.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lite.AspNetCore.RequestBinder.Tests;

public sealed class BinderGenerationTests
{
    public readonly record struct UpdateSomeEntityCommand(
        [property: FromRoute("entityId")] int EntityId,
        [property: FromBody] Payload Payload
    );

    public sealed record Payload(string Name);

    [Fact]
    public async Task RecordStruct_CtorCreation_IdFromRoute_RestFromBody()
    {
        var ctx = TestHttp.CreateContext(
            jsonBody: "{\"entityId\":999,\"payload\":{\"name\":\"abc\"}}"
        );
        ctx.Request.RouteValues["entityId"] = "123";

        var binder = new BinderGenerationTests_UpdateSomeEntityCommandBinder(
            bodyParser: ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );

        var cmd = await binder.BindAsync(ctx.Request, default);

        Assert.Equal(123, cmd.EntityId);
        Assert.Equal("abc", cmd.Payload.Name);
    }
}

public sealed class GetByIdQuery
{
    public int Id { get; set; }
}

public sealed class GetByIdQueryConfig : IRequestBindingConfiguration<GetByIdQuery>
{
    public void Configure(IRequestBindingBuilder<GetByIdQuery> builder)
    {
        builder.Bind(x => x.Id).FromRoute("id");
    }
}

public sealed class ConfigOnlyTests
{
    [Fact]
    public void ConfigOnly_BindsFromRoute()
    {
        var ctx = TestHttp.CreateContext();
        ctx.Request.RouteValues["id"] = "42";

        var binder = new GetByIdQueryBinder();
        var q = binder.Bind(ctx.Request);

        Assert.Equal(42, q.Id);
    }
}

[BinderName("MyCustomBinder")]
public sealed class NamedRequest
{
    [FromQuery("name")]
    public string Name { get; set; } = "";
}

public sealed class BinderNameTests
{
    [Fact]
    public void BinderName_Override_UsedAsGeneratedClassName()
    {
        var ctx = TestHttp.CreateContext();
        ctx.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?name=hello");

        var binder = new MyCustomBinder();
        var r = binder.Bind(ctx.Request);

        Assert.Equal("hello", r.Name);
    }
}
