using SNESEmulator.Core;
namespace SNESEmulator.Testing;

public static class NativeFrameOracle
{
    public static string? FirstDifference(ReadOnlySpan<ushort> actual,ReadOnlySpan<ushort> expected,int width,IEnumerable<string>? trace=null)
    {
        if(width<1)throw new ArgumentOutOfRangeException(nameof(width));
        if(actual.Length!=expected.Length)return $"native pixel count: expected {expected.Length}, actual {actual.Length}";
        for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])return $"native pixel ({i%width},{i/width}) line {i/width+1}: expected ${expected[i]:X4}, actual ${actual[i]:X4}"+(trace==null?"":"\n"+string.Join("\n",trace.TakeLast(24)));
        return null;
    }
}
