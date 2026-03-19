using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Lite.AspNetCore.RequestBinder.Attributes;
using Lite.AspNetCore.RequestBinder.Body;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

BenchmarkRunner.Run<BindBench>();

// --- Shared DTOs (route + body) ---
public readonly record struct UpdateSomeEntityCommand(
    [property: FromRoute("entityId")] int EntityId,
    [property: FromBody] Payload Payload
);

public sealed record Payload(string Name);

// --- Body-only DTO for format benchmarks ---
[FromBody]
public sealed class CreateOrderRequest
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
}

[JsonSerializable(typeof(CreateOrderRequest))]
public sealed partial class BenchJsonContext : JsonSerializerContext { }

[MemoryPack.MemoryPackable]
[FromBody]
public sealed partial class CreateOrderMemoryPackRequest
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
}

[MemoryDiagnoser]
public class BindBench
{
    private DefaultHttpContext _ctxJson = default!;
    private DefaultHttpContext _ctxXml = default!;
    private DefaultHttpContext _ctxMessagePack = default!;
    private DefaultHttpContext _ctxMemoryPack = default!;
    private DefaultHttpContext _ctxMixed = default!; // route + body (existing scenario)

    private UpdateSomeEntityCommandBinder _mixedBinder = default!;
    private CreateOrderRequestBinder _jsonBinder = default!;
    private CreateOrderRequestBinder _jsonSourceGenBinder = default!;
    private CreateOrderRequestBinder _xmlBinder = default!;
    private CreateOrderRequestBinder _messagePackBinder = default!;
    private CreateOrderMemoryPackRequestBinder _memoryPackBinder = default!;
    private RequestDelegate _minimalApiDelegate = default!;

    private static readonly byte[] JsonBytes = Encoding.UTF8.GetBytes(
        """{"productId":"prod-1","quantity":3}"""
    );
    private static readonly byte[] XmlBytes = Encoding.UTF8.GetBytes(
        """<?xml version="1.0"?><CreateOrderRequest><ProductId>prod-1</ProductId><Quantity>3</Quantity></CreateOrderRequest>"""
    );
    private static readonly byte[] MessagePackBytes = MakeMessagePackBytes();
    private static readonly byte[] MemoryPackBytes = MemoryPack.MemoryPackSerializer.Serialize(
        new CreateOrderMemoryPackRequest { ProductId = "prod-1", Quantity = 3 }
    );

    private static byte[] MakeMessagePackBytes()
    {
        var opts = MessagePack.MessagePackSerializerOptions.Standard.WithResolver(
            MessagePack.Resolvers.ContractlessStandardResolver.Instance
        );
        return MessagePack.MessagePackSerializer.Serialize(
            new CreateOrderRequest { ProductId = "prod-1", Quantity = 3 },
            opts
        );
    }

    [GlobalSetup]
    public void Setup()
    {
        var jsonSp = new ServiceCollection()
            .AddSingleton<IBodyParser>(_ => SystemTextJsonBodyParser.CreateReflection())
            .BuildServiceProvider();

        var jsonSourceGenSp = new ServiceCollection()
            .AddSingleton<IBodyParser>(_ =>
                SystemTextJsonBodyParser.CreateSourceGenOnly(BenchJsonContext.Default)
            )
            .BuildServiceProvider();
        var xmlSp = new ServiceCollection()
            .AddSingleton<IBodyParser, XmlBodyParser>()
            .BuildServiceProvider();
        var msgPackOpts = MessagePack.MessagePackSerializerOptions.Standard.WithResolver(
            MessagePack.Resolvers.ContractlessStandardResolver.Instance
        );
        var msgPackSp = new ServiceCollection()
            .AddSingleton<IBodyParser>(new MessagePackBodyParser(msgPackOpts))
            .BuildServiceProvider();
        var memPackSp = new ServiceCollection()
            .AddSingleton<IBodyParser, MemoryPackBodyParser>()
            .BuildServiceProvider();

        _ctxJson = NewContext(JsonBytes, "application/json", jsonSp);
        var _ctxJsonSourceGen = NewContext(JsonBytes, "application/json", jsonSourceGenSp);
        _ctxXml = NewContext(XmlBytes, "application/xml", xmlSp);
        _ctxMessagePack = NewContext(MessagePackBytes, "application/msgpack", msgPackSp);
        _ctxMemoryPack = NewContext(MemoryPackBytes, "application/x-memorypack", memPackSp);

        var mixedJson = "{\"entityId\":999,\"payload\":{\"name\":\"abc\"}}";
        _ctxMixed = new DefaultHttpContext();
        _ctxMixed.RequestServices = jsonSp;
        _ctxMixed.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(mixedJson));
        _ctxMixed.Request.ContentType = "application/json";
        _ctxMixed.Request.RouteValues["entityId"] = "123";

        _mixedBinder = new UpdateSomeEntityCommandBinder(jsonSp.GetRequiredService<IBodyParser>());
        _jsonBinder = new CreateOrderRequestBinder(jsonSp.GetRequiredService<IBodyParser>());
        _jsonSourceGenBinder = new CreateOrderRequestBinder(
            jsonSourceGenSp.GetRequiredService<IBodyParser>()
        );
        _xmlBinder = new CreateOrderRequestBinder(xmlSp.GetRequiredService<IBodyParser>());
        _messagePackBinder = new CreateOrderRequestBinder(
            msgPackSp.GetRequiredService<IBodyParser>()
        );
        _memoryPackBinder = new CreateOrderMemoryPackRequestBinder(
            memPackSp.GetRequiredService<IBodyParser>()
        );

        var handler = (int entityId, Payload payload) =>
        {
            _ = new UpdateSomeEntityCommand(entityId, payload);
            return Results.Ok();
        };
        _minimalApiDelegate = RequestDelegateFactory
            .Create(handler, new RequestDelegateFactoryOptions { ServiceProvider = jsonSp })
            .RequestDelegate;
    }

    private static DefaultHttpContext NewContext(
        byte[] body,
        string contentType,
        IServiceProvider sp
    )
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = sp;
        ctx.Request.Body = new MemoryStream(body);
        ctx.Request.ContentType = contentType;
        return ctx;
    }

    [IterationSetup]
    public void RewindStreams()
    {
        _ctxJson.Request.Body.Position = 0;
        _ctxXml.Request.Body.Position = 0;
        _ctxMessagePack.Request.Body.Position = 0;
        _ctxMemoryPack.Request.Body.Position = 0;
        _ctxMixed.Request.Body.Position = 0;
    }

    // --- Route + Body (mixed) ---
    [Benchmark]
    public ValueTask<UpdateSomeEntityCommand> LiteBinder_Mixed_RouteAndBody() =>
        _mixedBinder.BindAsync(_ctxMixed.Request, default);

    [Benchmark]
    public async Task MinimalApi_Mixed_RouteAndBody()
    {
        await _minimalApiDelegate(_ctxMixed);
    }

    [Benchmark]
    public async Task Manual_Mixed_ParseAndCtor()
    {
        var entityIdStr = _ctxMixed.Request.RouteValues.TryGetValue("entityId", out var rv)
            ? Convert.ToString(rv)
            : null;
        var entityId = int.TryParse(entityIdStr, out var tmp) ? tmp : default;
        var payload = await _ctxMixed.Request.ReadFromJsonAsync<Payload>(
            cancellationToken: default
        );
        _ = new UpdateSomeEntityCommand(entityId, payload ?? new Payload(""));
    }

    // --- JSON ---
    [Benchmark]
    public ValueTask<CreateOrderRequest> LiteBinder_Json() =>
        _jsonBinder.BindAsync(_ctxJson.Request, default);

    [Benchmark]
    public ValueTask<CreateOrderRequest> LiteBinder_Json_SourceGenOnly() =>
        _jsonSourceGenBinder.BindAsync(_ctxJson.Request, default);

    [Benchmark]
    public async Task MinimalApi_Json()
    {
        _ = await _ctxJson.Request.ReadFromJsonAsync<CreateOrderRequest>(
            cancellationToken: default
        );
    }

    [Benchmark]
    public async Task Manual_Json()
    {
        _ = await _ctxJson.Request.ReadFromJsonAsync<CreateOrderRequest>(
            cancellationToken: default
        );
    }

    // --- XML ---
    [Benchmark]
    public ValueTask<CreateOrderRequest> LiteBinder_Xml() =>
        _xmlBinder.BindAsync(_ctxXml.Request, default);

    [Benchmark]
    public async Task Manual_Xml()
    {
        _ctxXml.Request.EnableBuffering();
        _ctxXml.Request.Body.Position = 0;
        var serializer = new XmlSerializer(typeof(CreateOrderRequest));
        _ = serializer.Deserialize(_ctxXml.Request.Body);
    }

    // --- MessagePack ---
    [Benchmark]
    public ValueTask<CreateOrderRequest> LiteBinder_MessagePack() =>
        _messagePackBinder.BindAsync(_ctxMessagePack.Request, default);

    [Benchmark]
    public async Task Manual_MessagePack()
    {
        _ctxMessagePack.Request.EnableBuffering();
        _ctxMessagePack.Request.Body.Position = 0;
        await using var ms = new MemoryStream();
        await _ctxMessagePack.Request.Body.CopyToAsync(ms);
        var seq = new System.Buffers.ReadOnlySequence<byte>(ms.ToArray());
        _ = MessagePack.MessagePackSerializer.Deserialize<CreateOrderRequest>(
            in seq,
            MessagePack.MessagePackSerializerOptions.Standard.WithResolver(
                MessagePack.Resolvers.ContractlessStandardResolver.Instance
            )
        );
    }

    // --- MemoryPack ---
    [Benchmark]
    public ValueTask<CreateOrderMemoryPackRequest> LiteBinder_MemoryPack() =>
        _memoryPackBinder.BindAsync(_ctxMemoryPack.Request, default);

    [Benchmark]
    public async Task Manual_MemoryPack()
    {
        _ctxMemoryPack.Request.EnableBuffering();
        _ctxMemoryPack.Request.Body.Position = 0;
        _ = await MemoryPack.MemoryPackSerializer.DeserializeAsync<CreateOrderMemoryPackRequest>(
            _ctxMemoryPack.Request.Body
        );
    }
}
