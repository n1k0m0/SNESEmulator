using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace SNESEmulator.Contracts;

public static class IpcProtocol
{
    // M9g settings include structured keyboard/frontend bindings and host-owned
    // turbo/reset semantics. Version1 Managers cannot preserve this schema safely.
    public const int Version=2;
}

public sealed record Request(int Version, string RequestId, string Command, JsonElement Payload)
{
    public static Request Create(string command, object? payload = null) => new(IpcProtocol.Version, Guid.NewGuid().ToString("N"), command, JsonSerializer.SerializeToElement(payload ?? new { }));
}

public sealed record Response(int Version, string RequestId, bool Success, string Code, string Message, JsonElement Data)
{
    public static Response Ok(Request r, object? value = null) => new(IpcProtocol.Version, r.RequestId, true, "OK", "OK", JsonSerializer.SerializeToElement(value ?? new { }));
    public static Response Fail(Request r, string code, string message) => new(IpcProtocol.Version, r.RequestId, false, code, message, JsonSerializer.SerializeToElement(new { }));
    public T Value<T>() => Data.Deserialize<T>() ?? throw new InvalidDataException("Missing response value");
}

public sealed record InstanceInfo(int ProcessId, long StartTicks, string PipeName, int Version, string Executable, string StartIdentityKind = "utc-ticks");
public sealed record HelloInfo(int Version, string[] Capabilities, int MaximumBytes);
public sealed record StatusInfo(int ProcessId, long Epoch, long Field, string Title, string CartridgeHash, string RomPath, bool Paused, bool Turbo, bool MenuOpen, string Network, long SettingsRevision, string? Fault);
public sealed record SettingsInfo(long Revision, JsonElement Settings, Dictionary<string, string> Effects, string? PersistenceNotice = null);
public sealed record ApplySettings(long ExpectedRevision, JsonElement Settings);
public sealed record PathCommand(string Path, long ExpectedEpoch = 0, bool Recover = false);
public sealed record StateInfo(string Path, string Name, string Hash, int Format, int Width, int Height, uint[]? Pixels, string? Error, bool RecoveryAvailable);
public static class Framing
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    public static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 16,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 2 or > MaximumBytes)
            throw new InvalidDataException("Message length outside budget");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token);
        _ = new UTF8Encoding(false, true).GetString(bytes);
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        int nodes = 0;
        void Inspect(JsonElement e)
        {
            if (++nodes > 150000)
                throw new InvalidDataException("Node budget");
            if (e.ValueKind == JsonValueKind.String && e.GetString()!.Length > 65536)
                throw new InvalidDataException("String budget");
            if (e.ValueKind == JsonValueKind.Array)
            {
                if (e.GetArrayLength() > 122368)
                    throw new InvalidDataException("Array budget");
                foreach (var x in e.EnumerateArray())
                    Inspect(x);
            }

            if (e.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>();
                foreach (var p in e.EnumerateObject())
                {
                    if (!names.Add(p.Name) || names.Count > 64 || p.Name.Length > 128)
                        throw new InvalidDataException("Object budget or duplicate member");
                    Inspect(p.Value);
                }
            }
        }

        Inspect(doc.RootElement);
        return doc.RootElement.Deserialize<T>(Options) ?? throw new InvalidDataException("Empty message");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("Message too large");
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    public static void ValidateResponse(Response response, Request request)
    {
        if (response == null || response.Version != IpcProtocol.Version || response.RequestId != request.RequestId)
            throw new InvalidDataException("Response version/requestId mismatch");
    }

    public static HelloInfo ValidateHello(Response response, Request hello)
    {
        ValidateResponse(response, hello);
        if (!response.Success)
            throw new IOException(response.Code + ": " + response.Message);
        HelloInfo info;
        try
        {
            info = response.Data.Deserialize<HelloInfo>(Options) ?? throw new InvalidDataException("Missing Hello capabilities");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Invalid Hello capabilities", error);
        }

        if (info.Version != IpcProtocol.Version || info.MaximumBytes != MaximumBytes || info.Capabilities == null || info.Capabilities.Length > 32 || info.Capabilities.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 64 || c.Any(char.IsControl)) || info.Capabilities.Distinct(StringComparer.Ordinal).Count() != info.Capabilities.Length)
            throw new InvalidDataException("Unsupported or malformed Hello capabilities");
        return info;
    }

    public static void Validate(Request r)
    {
        if (r.RequestId == null || r.Command == null || r.Payload.ValueKind != JsonValueKind.Object && r.Payload.ValueKind != JsonValueKind.Array || r.RequestId.Length != 32 || !Guid.TryParseExact(r.RequestId, "N", out _) || r.Command.Length is < 1 or > 64)
            throw new InvalidDataException("Invalid request identity");
    }
}
