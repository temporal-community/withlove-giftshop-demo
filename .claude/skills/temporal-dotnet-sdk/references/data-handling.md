# .NET SDK Data Handling

## Overview

The .NET SDK provides flexible data conversion through payload converters, codecs, and failure converters.

## Data Converter Architecture

```
Application Objects
       ↓ (PayloadConverter)
     Payloads
       ↓ (PayloadCodec - optional)
  Encoded Payloads
       ↓
    Temporal Server
```

## Default Data Conversion

The default `PayloadConverter` supports:
- `null`
- `byte[]`
- `Google.Protobuf.IMessage` instances
- Anything `System.Text.Json` supports
- `IRawValue` for unconverted raw payloads

## Custom JSON Serialization

```csharp
using System.Text.Json;
using Temporalio.Client;
using Temporalio.Converters;

// Custom converter with camelCase naming
public class CamelCasePayloadConverter : DefaultPayloadConverter
{
    public CamelCasePayloadConverter()
        : base(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })
    {
    }
}

// Use with client
var client = await TemporalClient.ConnectAsync(new()
{
    TargetHost = "localhost:7233",
    Namespace = "default",
    DataConverter = DataConverter.Default with
    {
        PayloadConverter = new CamelCasePayloadConverter()
    },
});
```

## Payload Codecs (Encryption/Compression)

Payload codecs transform bytes to bytes for encryption or compression:

```csharp
using System.Security.Cryptography;
using Google.Protobuf;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

public sealed class EncryptionCodec : IPayloadCodec
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;
    private readonly string _keyId;

    public EncryptionCodec(string keyId, byte[] key)
    {
        _keyId = keyId;
        _key = key;
    }

    public Task<IReadOnlyCollection<Payload>> EncodeAsync(
        IReadOnlyCollection<Payload> payloads) =>
        Task.FromResult<IReadOnlyCollection<Payload>>(
            payloads.Select(p => new Payload
            {
                Metadata =
                {
                    ["encoding"] = ByteString.CopyFromUtf8("binary/encrypted"),
                    ["encryption-key-id"] = ByteString.CopyFromUtf8(_keyId),
                },
                Data = ByteString.CopyFrom(Encrypt(p.ToByteArray())),
            }).ToList());

    public Task<IReadOnlyCollection<Payload>> DecodeAsync(
        IReadOnlyCollection<Payload> payloads) =>
        Task.FromResult<IReadOnlyCollection<Payload>>(
            payloads.Select(p =>
            {
                if (p.Metadata.GetValueOrDefault("encoding")?.ToStringUtf8() != "binary/encrypted")
                    return p;

                return Payload.Parser.ParseFrom(Decrypt(p.Data.ToByteArray()));
            }).ToList());

    private byte[] Encrypt(byte[] data)
    {
        var bytes = new byte[NonceSize + TagSize + data.Length];
        var nonce = bytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data,
            bytes.AsSpan(NonceSize, data.Length),
            bytes.AsSpan(NonceSize + data.Length, TagSize));
        return bytes;
    }

    private byte[] Decrypt(byte[] data)
    {
        var bytes = new byte[data.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            data.AsSpan(0, NonceSize),
            data.AsSpan(NonceSize, bytes.Length),
            data.AsSpan(NonceSize + bytes.Length, TagSize),
            bytes);
        return bytes;
    }
}

// Usage
var client = await TemporalClient.ConnectAsync(new()
{
    TargetHost = "localhost:7233",
    DataConverter = DataConverter.Default with
    {
        PayloadCodec = new EncryptionCodec("my-key-id", myKey)
    }
});
```

## Raw Values

Use `IRawValue` to defer conversion or handle dynamic types:

```csharp
using Temporalio.Converters;

[Workflow]
public class DynamicWorkflow
{
    [WorkflowRun]
    public async Task<object?> RunAsync(IRawValue[] args)
    {
        // Convert raw value to specific type when needed
        var firstArg = Workflow.PayloadConverter.ToValue<MyType>(args[0].Payload);

        // Or pass through to activity
        return await Workflow.ExecuteActivityAsync(
            (MyActivities act) => act.ProcessRaw(args[0]),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5) });
    }
}
```

## Failure Converter

Customize how exceptions are serialized:

```csharp
using Temporalio.Converters;
using Temporalio.Exceptions;

public class CustomFailureConverter : IFailureConverter
{
    public Failure ToFailure(Exception exception, IPayloadConverter payloadConverter)
    {
        // Customize failure serialization
        // Default implementation encodes exception details
    }

    public Exception ToException(Failure failure, IPayloadConverter payloadConverter)
    {
        // Customize failure deserialization
    }
}
```

## Codec Server for UI

Run a codec server to decrypt payloads in the Temporal Web UI:

```csharp
using Microsoft.AspNetCore.Builder;
using Temporalio.Converters;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// The codec
var codec = new EncryptionCodec("my-key-id", myKey);

app.MapPost("/encode", async (HttpContext ctx) =>
{
    var payloads = await ReadPayloadsAsync(ctx.Request.Body);
    var encoded = await codec.EncodeAsync(payloads);
    await WritePayloadsAsync(ctx.Response.Body, encoded);
});

app.MapPost("/decode", async (HttpContext ctx) =>
{
    var payloads = await ReadPayloadsAsync(ctx.Request.Body);
    var decoded = await codec.DecodeAsync(payloads);
    await WritePayloadsAsync(ctx.Response.Body, decoded);
});

app.Run("http://localhost:8081");
```

## Best Practices

1. **Use records or POCOs** for workflow/activity parameters - they serialize cleanly
2. **Avoid complex object graphs** that may have circular references
3. **Use `IRawValue`** for dynamic or multi-type scenarios
4. **Encrypt sensitive data** using payload codecs
5. **Test serialization** - ensure types round-trip correctly
6. **Use consistent converters** across all clients and workers
7. **Run a codec server** to enable UI visibility of encrypted data
