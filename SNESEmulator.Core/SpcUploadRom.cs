namespace SNESEmulator.Core;

/// <summary>Own SNES ROM: actual65816 code uploads and starts actual SPC instructions through the IPL protocol.</summary>
public static class SpcUploadRom
{
    // Writes $5A to RAM$0300, replies$A5, then echoes inverted CPU commands to port1.
    public static readonly byte[] Program=[0xe8,0x5a,0xc5,0x00,0x03,0x8f,0xa5,0xf4,0xe4,0xf5,0x48,0xff,0xc4,0xf5,0x2f,0xf8];
    public static readonly byte[] StressData=Enumerable.Range(0,520).Select(i=>(byte)((i*73)^0xa6)).ToArray();
    public static readonly byte[] WrapData=Enumerable.Range(0,257).Select(i=>(byte)(i^0x39)).ToArray();
    public static readonly byte[] ZeroCommandData=Enumerable.Range(0,255).Select(i=>(byte)(0xee-i)).ToArray();
    public static byte[] Create(bool stress=false,byte[]? payload=null)
    {
        payload??=Program;byte[] rom=DiagnosticRom.Create();List<byte> code=[];
        void Emit(params byte[] b)=>code.AddRange(b);
        void Store(int v,int address)=>Emit(0xa9,(byte)v,0x8d,(byte)address,(byte)(address>>8));
        void Wait(int v,int address){int begin=code.Count;Emit(0xad,(byte)address,(byte)(address>>8),0xc9,(byte)v,0xd0,0);code[^1]=(byte)(begin-code.Count);}
        Emit(0x78,0x18,0xfb,0xc2,0x10);Store(0,0x4200);Store(0,0x420c);Wait(0xaa,0x2140);Wait(0xbb,0x2141);
        void Block(byte[] data,int source,int destination,int command){
            data.CopyTo(rom,source);Store(destination,0x2142);Store(destination>>8,0x2143);Store(1,0x2141);Store(command,0x2140);Wait(command,0x2140);
            Emit(0xa2,0,0);int begin=code.Count;Emit(0xbd,(byte)source,(byte)(0x80+(source>>8)),0x8d,0x41,0x21,0x8a,0x8d,0x40,0x21);
            int poll=code.Count;Emit(0xcd,0x40,0x21,0xd0,0);code[^1]=(byte)(poll-code.Count);
            Emit(0xe8,0xe0,(byte)data.Length,(byte)(data.Length>>8),0xd0,0);code[^1]=(byte)(begin-code.Count);
        }
        Block(payload,0x1000,0x0200,0xcc);int endCommand=(payload.Length+1)&255;if(endCommand==0)endCommand=1;
        if(stress){Block(StressData,0x1100,0x04f0,endCommand);endCommand=(StressData.Length+1)&255;if(endCommand==0)endCommand=1;Block(WrapData,0x1400,0x08ff,endCommand);endCommand=(WrapData.Length+1)&255;if(endCommand==0)endCommand=1;Block(ZeroCommandData,0x1600,0x0b80,endCommand);endCommand=(ZeroCommandData.Length+1)&255;if(endCommand==0)endCommand=1;}
        Store(0,0x2142);Store(2,0x2143);Store(0,0x2141);Store(endCommand,0x2140);
        Wait(0xa5,0x2140);Store(0x3c,0x2141);Wait(0xc3,0x2141);Store(0xa5,0x1ff0);Emit(0xdb);
        if(code.Count>=0x1000)throw new InvalidOperationException("Upload fixture too large");code.CopyTo(rom,0);return rom;
    }
}
