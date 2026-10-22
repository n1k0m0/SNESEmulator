using System.Buffers.Binary;
using SNESEmulator.Core;

namespace SNESEmulator.Testing;

public static class StereoOracle
{
    /// <summary>Digital oracle comparator; never derives expected samples from the emulator.</summary>
    public static string? Difference(ReadOnlySpan<StereoSample> actual,ReadOnlySpan<byte> expected,long firstCrystal=672)
    {
        if(expected.Length%4!=0)return "Malformed independent stereo oracle";
        if(actual.Length!=expected.Length/4)return $"sample count expected{expected.Length/4}, actual{actual.Length}";
        for(int i=0;i<actual.Length;i++){
            short l=BinaryPrimitives.ReadInt16LittleEndian(expected[(i*4)..]),r=BinaryPrimitives.ReadInt16LittleEndian(expected[(i*4+2)..]);
            if(actual[i].Left!=l||actual[i].Right!=r)return $"first sample{i} phase27 crystal{actual[i].Crystal}: expected({l},{r}) actual({actual[i].Left},{actual[i].Right})";
            if(actual[i].Index!=i||actual[i].Crystal!=firstCrystal+i*768L)return $"sample{i} timestamp/index mismatch";
        }
        return null;
    }
}
