namespace SNESEmulator.Core;

/// <summary>Original CPU→IPL→SPC→DSP stereo diagnostic. Sound comes exclusively from BRR voices.</summary>
public static class AudioDiagnosticRom
{
    public static byte[] SpcProgram()
    {
        List<byte> code=[];
        void Ram(int a,byte n)=>code.AddRange([0xe8,n,0xc5,(byte)a,(byte)(a>>8)]);
        void Dsp(byte r,byte n)=>code.AddRange([0x8f,r,0xf2,0x8f,n,0xf3]);
        Dsp(0x6c,0x20); // DSP active, echo write disabled.
        for(int i=0;i<4;i++)Ram(0x1000+i,(byte)(i%2==0?0:0x11));
        byte[] brr=[0x83,0x00,0x44,0x77,0x44,0x00,0xcc,0x88,0xcc];
        for(int i=0;i<brr.Length;i++)Ram(0x1100+i,brr[i]);
        Dsp(0x5d,0x10);Dsp(0x0c,0x7f);Dsp(0x1c,0x7f);Dsp(0x5c,0);
        for(int v=0;v<2;v++){
            int r=v*16;Dsp((byte)r,(byte)(v==0?100:0));Dsp((byte)(r+1),(byte)(v==1?100:0));
            Dsp((byte)(r+2),0);Dsp((byte)(r+3),(byte)(v==0?0x10:0x18));
            Dsp((byte)(r+4),0);Dsp((byte)(r+5),0);Dsp((byte)(r+7),0x7f);
        }
        Dsp(0x4c,3);
        // Preserve the existing SNES upload diagnostic's real port pingpong signal.
        code.AddRange(SpcUploadRom.Program);return code.ToArray();
    }
    public static byte[] Create()=>SpcUploadRom.Create(payload:SpcProgram());
}
