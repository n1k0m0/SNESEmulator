using System.Security.Cryptography;
using System.Text.Json;
using SNESEmulator.Testing;



/// <summary>Explicit software-reference adjudication of a documented upstream corpus discrepancy.
/// Original vectors and raw mismatch counts remain visible; this is not hardware evidence.</summary>
internal sealed class CpuDpAdjudication
{
    public const string FixtureSha256 = "a5edc1ed2fb06356b3e14cd63651d116bee51a8fc91351f2780136ef1eb7f199";
    private readonly Dictionary<string, JsonElement> cases;

    public CpuDpAdjudication()
    {
        byte[] bytes = File.ReadAllBytes("testdata/65816-dp-indexed-quirk.json");
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != FixtureSha256)
            throw new InvalidDataException("The reviewed independent (dp,X) fixture changed.");
        using var document = JsonDocument.Parse(bytes);
        cases = document.RootElement.GetProperty("cases").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.Clone());
        if (cases.Count != 264) throw new InvalidDataException("Unexpected (dp,X) adjudication coverage.");
    }

    public JsonElement? Expected(JsonElement vector, int opcode, string mode)
    {
        if (mode != "e" || (opcode & 31) != 1 ||
            !cases.TryGetValue(vector.GetProperty("name").GetString()!, out var expected)) return null;
        if (expected.GetProperty("opcode").GetString() != opcode.ToString("x2")) return null;
        string digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(vector)));
        if (digest != expected.GetProperty("rawVectorSha256").GetString())
            throw new InvalidDataException("Adjudicated vector bytes differ from the pinned upstream input.");
        var initial = vector.GetProperty("initial");
        if (initial.GetProperty("e").GetInt32() != 1 || (initial.GetProperty("d").GetInt32() & 255) == 0 ||
            (expected.GetProperty("pointerLow").GetInt32() & 255) != 255 ||
            expected.GetProperty("pointerHigh").GetInt32() != (expected.GetProperty("pointerLow").GetInt32() & 0xff00))
            throw new InvalidDataException("Adjudication escaped the reviewed emulation page-boundary condition.");
        return expected;
    }
}

