using Lite.AspNetCore.RequestBinder.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lite.AspNetCore.RequestBinder.Tests;

public sealed class GetUserByIdRequest
{
    [FromRoute("id")]
    public int Id { get; set; }
}

public sealed class GetUserByIdScenarioTests
{
    [Fact]
    public void GET_users_id_BindsRouteValue()
    {
        var ctx = TestHttp.CreateContext(
            routeValues: new Dictionary<string, string> { ["id"] = "42" }
        );
        var binder = new GetUserByIdRequestBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal(42, request.Id);
    }
}

public sealed class SearchProductsRequest
{
    [FromQuery("q")]
    public string Query { get; set; } = "";

    [FromQuery("page")]
    public int Page { get; set; }

    [FromQuery("limit")]
    public int Limit { get; set; }
}

public sealed class SearchProductsScenarioTests
{
    [Fact]
    public void GET_search_QueryString_BindsAllParameters()
    {
        var ctx = TestHttp.CreateContext(queryString: "?q=coffee&page=2&limit=20");
        var binder = new SearchProductsRequestBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal("coffee", request.Query);
        Assert.Equal(2, request.Page);
        Assert.Equal(20, request.Limit);
    }
}

[FromBody]
public sealed class CreateOrderRequest
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class CreateOrderScenarioTests
{
    [Fact]
    public async Task POST_orders_JsonBody_DeserializesToCommand()
    {
        var ctx = TestHttp.CreateContext(jsonBody: """{"productId":"prod-1","quantity":3}""");
        var binder = new CreateOrderRequestBinder(
            ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );
        var request = await binder.BindAsync(ctx.Request, default);
        Assert.Equal("prod-1", request.ProductId);
        Assert.Equal(3, request.Quantity);
    }

    [Fact]
    public async Task POST_orders_XmlBody_DeserializesToCommand()
    {
        var xml =
            """<?xml version="1.0"?><CreateOrderRequest><ProductId>prod-1</ProductId><Quantity>3</Quantity></CreateOrderRequest>""";
        var ctx = TestHttp.CreateContext(
            bodyBytes: System.Text.Encoding.UTF8.GetBytes(xml),
            contentType: "application/xml",
            addServices: s => s.AddSingleton<Body.IBodyParser, Body.XmlBodyParser>()
        );
        var binder = new CreateOrderRequestBinder(
            ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );
        var request = await binder.BindAsync(ctx.Request, default);
        Assert.Equal("prod-1", request.ProductId);
        Assert.Equal(3, request.Quantity);
    }

    [Fact]
    public async Task POST_orders_MessagePackBody_DeserializesToCommand()
    {
        var opts = MessagePack.MessagePackSerializerOptions.Standard.WithResolver(
            MessagePack.Resolvers.ContractlessStandardResolver.Instance
        );
        var payload = new CreateOrderRequest { ProductId = "prod-1", Quantity = 3 };
        var bytes = MessagePack.MessagePackSerializer.Serialize(payload, opts);
        var ctx = TestHttp.CreateContext(
            bodyBytes: bytes,
            contentType: "application/msgpack",
            addServices: s => s.AddSingleton<Body.IBodyParser>(new Body.MessagePackBodyParser(opts))
        );
        var binder = new CreateOrderRequestBinder(
            ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );
        var request = await binder.BindAsync(ctx.Request, default);
        Assert.Equal("prod-1", request.ProductId);
        Assert.Equal(3, request.Quantity);
    }
}

[MemoryPack.MemoryPackable]
[FromBody]
public sealed partial class CreateOrderMemoryPackRequest
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class CreateOrderMemoryPackScenarioTests
{
    [Fact]
    public async Task POST_orders_MemoryPackBody_DeserializesToCommand()
    {
        var payload = new CreateOrderMemoryPackRequest { ProductId = "prod-1", Quantity = 3 };
        var bytes = MemoryPack.MemoryPackSerializer.Serialize(payload);
        var ctx = TestHttp.CreateContext(
            bodyBytes: bytes,
            contentType: "application/x-memorypack",
            addServices: s => s.AddSingleton<Body.IBodyParser, Body.MemoryPackBodyParser>()
        );
        var binder = new CreateOrderMemoryPackRequestBinder(
            ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );
        var request = await binder.BindAsync(ctx.Request, default);
        Assert.Equal("prod-1", request.ProductId);
        Assert.Equal(3, request.Quantity);
    }
}

public sealed class ContactFormRequest
{
    [FromForm("name")]
    public string Name { get; set; } = "";

    [FromForm("email")]
    public string Email { get; set; } = "";

    [FromForm("message")]
    public string Message { get; set; } = "";
}

public sealed class ContactFormScenarioTests
{
    [Fact]
    public async Task POST_contact_FormData_BindsAllFields()
    {
        var ctx = TestHttp.CreateContext(
            form: new Dictionary<string, string>
            {
                ["name"] = "Jane",
                ["email"] = "jane@example.com",
                ["message"] = "Hello",
            }
        );
        var binder = new ContactFormRequestBinder();
        var request = await binder.BindAsync(ctx.Request, default);
        Assert.Equal("Jane", request.Name);
        Assert.Equal("jane@example.com", request.Email);
        Assert.Equal("Hello", request.Message);
    }
}

public sealed class RequestWithCorrelationId
{
    [FromHeader("X-Correlation-Id")]
    public string CorrelationId { get; set; } = "";
}

public sealed class CorrelationIdScenarioTests
{
    [Fact]
    public void AnyRequest_Header_XCorrelationId_Binds()
    {
        var ctx = TestHttp.CreateContext(
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "req-abc-123" }
        );
        var binder = new RequestWithCorrelationIdBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal("req-abc-123", request.CorrelationId);
    }
}

public sealed class RequestWithSession
{
    [FromCookie("session_id")]
    public string SessionId { get; set; } = "";
}

public sealed class SessionCookieScenarioTests
{
    [Fact]
    public void Request_WithSessionCookie_Binds()
    {
        var ctx = TestHttp.CreateContext(
            cookies: new Dictionary<string, string> { ["session_id"] = "sess-xyz-789" }
        );
        var binder = new RequestWithSessionBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal("sess-xyz-789", request.SessionId);
    }
}

public interface ICurrentTenant
{
    string TenantId { get; }
}

internal sealed class FakeTenant : ICurrentTenant
{
    public string TenantId { get; init; } = "";
}

public sealed class RequestWithInjectedService
{
    [FromServices]
    public ICurrentTenant Tenant { get; set; } = null!;
}

public sealed class FromServicesScenarioTests
{
    [Fact]
    public void Request_FromServices_InjectsRegisteredService()
    {
        var tenant = new FakeTenant { TenantId = "tenant-1" };
        var ctx = TestHttp.CreateContext(addServices: sc =>
            sc.AddSingleton<ICurrentTenant>(tenant)
        );
        var binder = new RequestWithInjectedServiceBinder(tenant);
        var request = binder.Bind(ctx.Request);
        Assert.Same(tenant, request.Tenant);
        Assert.Equal("tenant-1", request.Tenant.TenantId);
    }
}

public sealed class SyncOnlyRequest
{
    [FromQuery("filter")]
    public string Filter { get; set; } = "";
}

public sealed class SyncOnlyScenarioTests
{
    [Fact]
    public void SyncBinder_NoBody_OnlyBindUsed()
    {
        var ctx = TestHttp.CreateContext(queryString: "?filter=active");
        var binder = new SyncOnlyRequestBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal("active", request.Filter);
    }

    [Fact]
    public void QueryParam_NotPresent_PropertyStaysDefault()
    {
        var ctx = TestHttp.CreateContext();
        var binder = new SyncOnlyRequestBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal("", request.Filter);
    }
}

public sealed class ListUserOrdersRequest
{
    [FromRoute("userId")]
    public int UserId { get; set; }

    [FromQuery("status")]
    public string Status { get; set; } = "";
}

public sealed class ListUserOrdersScenarioTests
{
    [Fact]
    public void GET_users_userId_orders_RouteAndQuery_BothBound()
    {
        var ctx = TestHttp.CreateContext(
            routeValues: new Dictionary<string, string> { ["userId"] = "7" },
            queryString: "?status=shipped"
        );
        var binder = new ListUserOrdersRequestBinder();
        var request = binder.Bind(ctx.Request);
        Assert.Equal(7, request.UserId);
        Assert.Equal("shipped", request.Status);
    }
}

public readonly record struct UpdateProductCommand(
    [property: FromRoute("id")] int Id,
    [property: FromBody] ProductUpdateDto Body
);

public sealed class ProductUpdateDto
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class UpdateProductScenarioTests
{
    [Fact]
    public async Task PUT_products_id_JsonBody_RouteOverridesBodyId()
    {
        var ctx = TestHttp.CreateContext(
            jsonBody: """{"Id":0,"Body":{"Name":"Widget","Price":9.99}}"""
        );
        ctx.Request.RouteValues["id"] = "100";
        var binder = new UpdateProductCommandBinder(
            ctx.RequestServices.GetRequiredService<Body.IBodyParser>()
        );
        var cmd = await binder.BindAsync(ctx.Request, default);
        Assert.Equal(100, cmd.Id);
        Assert.NotNull(cmd.Body);
        Assert.Equal("Widget", cmd.Body.Name);
        Assert.Equal(9.99m, cmd.Body.Price);
    }
}
