using SNESEmulator.Core;
namespace SNESEmulator.Testing;

public static class NativeFrameOracle
{
    public static string? FirstDifference(PpuFrame actual,PpuFrame expected,IEnumerable<string>? trace=null)
    {
        if(actual.Geometry!=expected.Geometry)return $"raw field geometry: expected {expected.Geometry}, actual {actual.Geometry}";
        string? native=FirstDifference(actual.Native.Span,expected.Native.Span,actual.Geometry.Stride,trace);if(native!=null)return native;
        var a=actual.Brightness.Span;var e=expected.Brightness.Span;
        if(a.Length!=e.Length)return $"brightness count: expected {e.Length}, actual {a.Length}";
        for(int i=0;i<a.Length;i++)if(a[i]!=e[i])return $"brightness pixel ({i%actual.Geometry.Stride},{i/actual.Geometry.Stride}) line {i/actual.Geometry.Stride+1}: expected {e[i]}, actual {a[i]}";
        return null;
    }
    public static string? FirstDifference(ReadOnlySpan<ushort> actual,ReadOnlySpan<ushort> expected,int width,IEnumerable<string>? trace=null)
    {
        if(width<1)throw new ArgumentOutOfRangeException(nameof(width));
        if(actual.Length!=expected.Length)return $"native pixel count: expected {expected.Length}, actual {actual.Length}";
        for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])return $"native pixel ({i%width},{i/width}) line {i/width+1}: expected ${expected[i]:X4}, actual ${actual[i]:X4}"+(trace==null?"":"\n"+string.Join("\n",trace.TakeLast(24)));
        return null;
    }
}
