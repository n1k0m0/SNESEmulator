namespace SNESEmulator.Core;

public interface ISpcBus { byte Read(ushort address); void Write(ushort address,byte value); }
public sealed record SpcCycle(ushort? Address,byte? Value,string Direction);
public sealed record SpcRegisters(ushort PC,byte A,byte X,byte Y,byte SP,byte PSW,bool Sleeping,bool Stopped,long Instructions);
public sealed record SpcContinuation(SpcRegisters Entry,SpcCycle[] Completed);

/// <summary>Own SPC700 decoder. Flat electrical cycles are separate from S-SMP MMIO and crystal timing.</summary>
public sealed partial class Spc700(ISpcBus bus)
{
    public ushort PC {get;set;}
    public byte A {get;set;} public byte X {get;set;} public byte Y {get;set;}
    public byte SP {get;set;} public byte PSW {get;set;}
    public bool Sleeping {get;set;} public bool Stopped {get;set;}
    public long Instructions {get;set;}
    public Action<SpcCycle>? BeforeCycle {get;set;}
    public Action<SpcCycle>? CycleObserved {get;set;}
    internal Func<SpcCycle,bool>? PrepareCycle {get;set;}
    private SpcRegisters? suspended;
    private bool cooperative;
    internal Func<SpcCycle,bool>? EffectCompleted {get;set;}
    private SpcRegisters? entry;
    // The private journal stores values. Public continuation snapshots still own
    // immutable SpcCycle records and retain the existing State9 representation.
    private readonly record struct JournalCycle(ushort? Address,byte? Value,string Direction) {
        public static implicit operator JournalCycle(SpcCycle c)=>new(c.Address,c.Value,c.Direction);
        public SpcCycle Public()=>new(Address,Value,Direction);
    }
    internal Func<ushort?,string,bool>? PrepareBusCycle {get;set;}
    internal Func<ushort?,byte?,string,bool>? CompleteBusCycle {get;set;}
    private readonly List<JournalCycle> completed=[];
    private int cursor;
    private bool single;
    private int limit;
    private sealed class Yield : Exception;
    private readonly Yield yielded=new();
    public bool AtInstructionBoundary=>entry==null;
    public SpcRegisters Capture()=>new(PC,A,X,Y,SP,PSW,Sleeping,Stopped,Instructions);
    public void Restore(SpcRegisters r) {PC=r.PC;A=r.A;X=r.X;Y=r.Y;SP=r.SP;PSW=r.PSW;Sleeping=r.Sleeping;Stopped=r.Stopped;Instructions=r.Instructions;}
    public SpcContinuation? CaptureContinuation() => entry == null ? null :
        new(entry, completed.Select(c => c.Public()).ToArray());

    public void RestoreContinuation(SpcContinuation? continuation)
    {
        InvalidateHotContinuation();
        entry = continuation?.Entry;
        completed.Clear();
        if (continuation == null) return;
        if (continuation.Completed.Length > 16 || continuation.Entry.Instructions < 0 ||
            continuation.Completed.Any(row => row.Direction == "wait"
                ? row.Address != null || row.Value != null
                : row.Direction is not ("read" or "write") || row.Address == null || row.Value == null))
            throw new InvalidDataException("Invalid SPC continuation");
        completed.AddRange(continuation.Completed.Select(row => (JournalCycle)row));
    }

    public void Reset()
    {
        InvalidateHotContinuation();
        Restore(new(0xffc0, 0, 0, 0, 0xef, 2, false, false, 0));
        entry = null;
        completed.Clear();
    }

    // A completed cycle is appended before observation, so snapshots never repeat an MMIO effect.
    private byte Cycle(ushort? address,byte? value,string direction)
    {
        if(suspended!=null)return 0;
        if(entry!=null&&cursor<completed.Count){var c=completed[cursor++];if(c.Address!=address||c.Direction!=direction||(direction=="write"&&c.Value!=value))throw new InvalidDataException("SPC replay diverged");return c.Value??0;}
        if(single&&cursor>=limit)throw yielded;
        var next=new SpcCycle(address,value,direction);
        // A legacy veto must precede electrical admission: otherwise an APU
        // endpoint could advance without a committed bus cycle in the journal.
        if((PrepareCycle!=null&&!PrepareCycle(next))||(PrepareBusCycle!=null&&!PrepareBusCycle(address,direction))){
            // Keep exactly the register point formerly exposed by budget throw.
            // Remaining decoder operations are private speculation: all bus and
            // observer calls are suppressed, then every architectural field is
            // restored. The finite decoder has no host effects outside Cycle.
            return Suspend();
        }
        BeforeCycle?.Invoke(next);
        if(direction=="read")value=bus.Read(address!.Value);
        if(direction=="write")bus.Write(address!.Value,value!.Value);
        next=next with {Value=value};if(entry!=null)completed.Add(next);cursor++;
        // Both callbacks observe this committed effect, including when either
        // requests suspension. Short-circuiting here would lose a notification.
        bool fastCompleted = CompleteBusCycle?.Invoke(address, value, direction) ?? true;
        bool legacyCompleted = EffectCompleted?.Invoke(next) ?? true;
        if(!fastCompleted||!legacyCompleted){return Suspend();}
        CycleObserved?.Invoke(next);return value??0;
    }
    private byte Suspend(){if(!cooperative)throw yielded;suspended=Capture();return 0;}
    private byte R(int a)=>Cycle((ushort)a,0,"read");
    private void W(int a,int v)=>Cycle((ushort)a,(byte)v,"write");
    private void Idle()=>Cycle(null,null,"wait");
    private void Dummy()=>Cycle(PC,null,"read");
    private byte Fetch(){byte v=R(PC);PC++;return v;}
    private int Word(){int lo=Fetch();return lo|Fetch()<<8;}
    private int D(int a)=>((PSW&32)<<3)|(byte)a;
    private int Pointer(int a)=>R(D(a))|R(D(a+1))<<8;
    private void Push(int v){W(0x100|SP,v);SP--;}
    private byte Pop(){SP++;return R(0x100|SP);}
    private bool F(int mask)=>(PSW&mask)!=0;
    private void Flag(int m,bool b)=>PSW=(byte)(b?PSW|m:PSW&~m);
    private byte NZ(int v){byte n=(byte)v;Flag(128,(n&128)!=0);Flag(2,n==0);return n;}
    private int NZ16(int v){v&=65535;Flag(128,(v&32768)!=0);Flag(2,v==0);return v;}
    private byte Compare(int l,int r){int d=l-r;Flag(1,d>=0);NZ(d);return (byte)l;}
    private byte Add(int l,int r,bool sub=false)
    {
        int b=sub?r^255:r,c=F(1)?1:0,s=l+b+c;
        Flag(8,((l&15)+(b&15)+c)>15);Flag(64,((~(l^b)&(l^s))&128)!=0);Flag(1,s>255);return NZ(s);
    }
    private byte Alu(int kind,int l,int r)=>kind switch {0=>NZ(l|r),1=>NZ(l&r),2=>NZ(l^r),3=>Compare(l,r),4=>Add(l,r),5=>Add(l,r,true),_=>throw new InvalidOperationException()};
    private byte Modify(int kind,int n)
    {
        int c=F(1)?1:0;
        switch(kind){case 0:Flag(1,(n&128)!=0);n<<=1;break;case 1:Flag(1,(n&128)!=0);n=(n<<1)|c;break;case 2:Flag(1,(n&1)!=0);n>>=1;break;case 3:Flag(1,(n&1)!=0);n=(n>>1)|(c<<7);break;case 4:n--;break;case 5:n++;break;}
        return NZ(n);
    }
    private void Branch(bool take){int rel=(sbyte)Fetch();if(take){Idle();Idle();PC=(ushort)(PC+rel);}}
    public int Step(){if(entry!=null)throw new InvalidOperationException("SPC instruction in progress");cursor=0;Execute();return cursor;}
    public bool AdvanceCycle()
    {
        InvalidateHotContinuation();single=true;entry??=Capture();limit=completed.Count+1;Restore(entry);cursor=0;
        try{Execute();entry=null;completed.Clear();return false;}catch(Yield){return true;}finally{single=false;}
    }
    // Returns false at a cooperative timing boundary. Completed reads/writes are replayed only from data.
    private bool RunInstructionLegacy()
    {
        entry??=Capture();Restore(entry);cursor=0;suspended=null;cooperative=true;
        try {
            Execute();
            if(suspended!=null){Restore(suspended);return false;}
            entry=null;completed.Clear();return true;
        } finally {suspended=null;cooperative=false;}
    }
    private void Execute()
    {
        if(Sleeping||Stopped){Dummy();Idle();return;}
        int op=Fetch(),lo=op&15,hi=op>>4;
        if(lo==1){R(PC);Idle();Push(PC>>8);Push(PC);Idle();int v=0xffde-(hi<<1);PC=(ushort)(R(v)|R(v+1)<<8);}
        else if(lo==2){int a=D(Fetch()),n=R(a),mask=1<<(hi>>1);W(a,(hi&1)==0?n|mask:n&~mask);}
        else if(lo==3){int n=R(D(Fetch()));Idle();Branch(((n>>(hi>>1))&1)==((hi&1)==0?1:0));}
        else if(hi<12&&lo is >=4 and <=9)
        {
            int kind=hi>>1;bool indexed=(hi&1)!=0;int address=0,right=0,left=A;
            if(!indexed){switch(lo){case 4:right=R(D(Fetch()));break;case 5:right=R(Word());break;case 6:Dummy();right=R(D(X));break;case 7:int d=Fetch();Idle();right=R(Pointer(d+X));break;case 8:right=Fetch();break;case 9:right=R(D(Fetch()));address=D(Fetch());left=R(address);break;}}
            else {switch(lo){case 4:int d=Fetch();Idle();right=R(D(d+X));break;case 5:address=Word();Idle();right=R((ushort)(address+X));break;case 6:address=Word();Idle();right=R((ushort)(address+Y));break;case 7:int p=Pointer(Fetch());Idle();right=R((ushort)(p+Y));break;case 8:right=Fetch();address=D(Fetch());left=R(address);break;case 9:Dummy();right=R(D(Y));address=D(X);left=R(address);break;}}
            byte result=Alu(kind,left,right);
            if(lo==9||indexed&&lo==8){if(kind==3)Idle();else W(address,result);}else A=result;
        }
        else if(lo is 0xb or 0xc&&hi<12)
        {
            int a;if(lo==0xc&&(hi&1)!=0){Dummy();A=Modify(hi>>1,A);}
            else {if(lo==0xc)a=Word();else {int d=Fetch();if((hi&1)!=0){Idle();d+=X;}a=D(d);}byte n=R(a);W(a,Modify(hi>>1,n));}
        }
        else switch(op)
        {
            case 0x00:Dummy();break;
            case 0x10:Branch(!F(128));break;case 0x30:Branch(F(128));break;case 0x50:Branch(!F(64));break;case 0x70:Branch(F(64));break;
            case 0x90:Branch(!F(1));break;case 0xb0:Branch(F(1));break;case 0xd0:Branch(!F(2));break;case 0xf0:Branch(F(2));break;case 0x2f:Branch(true);break;
            case 0x0d:Dummy();Push(PSW);Idle();break;case 0x2d:Dummy();Push(A);Idle();break;case 0x4d:Dummy();Push(X);Idle();break;case 0x6d:Dummy();Push(Y);Idle();break;
            case 0x8e:Dummy();Idle();PSW=Pop();break;case 0xae:Dummy();Idle();A=Pop();break;case 0xce:Dummy();Idle();X=Pop();break;case 0xee:Dummy();Idle();Y=Pop();break;
            case 0x20:Dummy();Flag(32,false);break;case 0x40:Dummy();Flag(32,true);break;case 0x60:Dummy();Flag(1,false);break;case 0x80:Dummy();Flag(1,true);break;
            case 0xa0:Dummy();Idle();Flag(4,true);break;case 0xc0:Dummy();Idle();Flag(4,false);break;case 0xe0:Dummy();PSW&=0xb7;break;case 0xed:Dummy();Idle();Flag(1,!F(1));break;
            case 0x5d:Dummy();X=NZ(A);break;case 0x7d:Dummy();A=NZ(X);break;case 0x9d:Dummy();X=NZ(SP);break;case 0xbd:Dummy();SP=X;break;case 0xdd:Dummy();A=NZ(Y);break;case 0xfd:Dummy();Y=NZ(A);break;
            case 0x1d:Dummy();X=NZ(X-1);break;case 0x3d:Dummy();X=NZ(X+1);break;case 0xdc:Dummy();Y=NZ(Y-1);break;case 0xfc:Dummy();Y=NZ(Y+1);break;
            case 0xc8:Compare(X,Fetch());break;case 0xad:Compare(Y,Fetch());break;case 0x1e:Compare(X,R(Word()));break;case 0x3e:Compare(X,R(D(Fetch())));break;case 0x5e:Compare(Y,R(Word()));break;case 0x7e:Compare(Y,R(D(Fetch())));break;
            case 0xe8:A=NZ(Fetch());break;case 0xcd:X=NZ(Fetch());break;case 0x8d:Y=NZ(Fetch());break;
            case 0xe4:A=NZ(R(D(Fetch())));break;case 0xe5:A=NZ(R(Word()));break;case 0xe6:Dummy();A=NZ(R(D(X)));break;
            case 0xe7:{int d=Fetch();Idle();A=NZ(R(Pointer(d+X)));break;}
            case 0xf4:{int d=Fetch();Idle();A=NZ(R(D(d+X)));break;}
            case 0xf5:{int a=Word();Idle();A=NZ(R(a+X));break;}case 0xf6:{int a=Word();Idle();A=NZ(R(a+Y));break;}
            case 0xf7:{int a=Pointer(Fetch());Idle();A=NZ(R(a+Y));break;}
            case 0xf8:X=NZ(R(D(Fetch())));break;case 0xf9:{int d=Fetch();Idle();X=NZ(R(D(d+Y)));break;}case 0xe9:X=NZ(R(Word()));break;
            case 0xeb:Y=NZ(R(D(Fetch())));break;case 0xfb:{int d=Fetch();Idle();Y=NZ(R(D(d+X)));break;}case 0xec:Y=NZ(R(Word()));break;
            case 0xc4:Store(D(Fetch()),A);break;case 0xc5:Store(Word(),A);break;case 0xc6:Dummy();Store(D(X),A);break;
            case 0xc7:{int d=Fetch();Idle();Store(Pointer(d+X),A);break;}
            case 0xd4:{int d=Fetch();Idle();Store(D(d+X),A);break;}
            case 0xd5:{int a=Word();Idle();Store(a+X,A);break;}case 0xd6:{int a=Word();Idle();Store(a+Y,A);break;}
            case 0xd7:{int a=Pointer(Fetch());Idle();Store(a+Y,A);break;}
            case 0xd8:Store(D(Fetch()),X);break;case 0xd9:{int d=Fetch();Idle();Store(D(d+Y),X);break;}case 0xc9:Store(Word(),X);break;
            case 0xcb:Store(D(Fetch()),Y);break;case 0xdb:{int d=Fetch();Idle();Store(D(d+X),Y);break;}case 0xcc:Store(Word(),Y);break;
            case 0x8f:{int n=Fetch(),a=D(Fetch());R(a);W(a,n);break;}
            case 0xfa:{int n=R(D(Fetch())),a=D(Fetch());W(a,n);break;}
            case 0xaf:Dummy();Idle();W(D(X),A);X++;break;
            case 0xbf:Dummy();A=NZ(R(D(X)));X++;Idle();break;
            case 0xba:{int d=Fetch(),l=R(D(d));Idle();A=(byte)l;Y=R(D(d+1));NZ16(A|Y<<8);break;}
            case 0xda:{int d=Fetch();R(D(d));W(D(d),A);W(D(d+1),Y);break;}
            case 0x1a:case 0x3a:{int d=Fetch(),n=R(D(d))+(op==0x1a?-1:1);W(D(d),n);n+=R(D(d+1))<<8;W(D(d+1),n>>8);NZ16(n);break;}
            case 0x5a:case 0x7a:case 0x9a:{int d=Fetch(),l=R(D(d));if(op!=0x5a)Idle();int n=l|R(D(d+1))<<8,left=A|Y<<8,s=op==0x7a?left+n:left-n;
                Flag(1,op==0x7a?s>65535:s>=0);NZ16(s);
                if(op!=0x5a){Flag(8,op==0x7a?(left&4095)+(n&4095)>4095:(left&4095)>=(n&4095));Flag(64,((op==0x7a?~(left^n):left^n)&(left^s)&32768)!=0);A=(byte)s;Y=(byte)(s>>8);}break;}
            case 0x0a:case 0x2a:case 0x4a:case 0x6a:case 0x8a:case 0xaa:case 0xca:case 0xea:{int encoded=Word(),a=encoded&8191,bit=encoded>>13,n=R(a);bool b=(n&(1<<bit))!=0;
                switch(op){case 0x0a:Idle();Flag(1,F(1)||b);break;case 0x2a:Idle();Flag(1,F(1)||!b);break;case 0x4a:Flag(1,F(1)&&b);break;case 0x6a:Flag(1,F(1)&&!b);break;case 0x8a:Idle();Flag(1,F(1)^b);break;case 0xaa:Flag(1,b);break;case 0xca:Idle();W(a,F(1)?n|(1<<bit):n&~(1<<bit));break;case 0xea:W(a,n^(1<<bit));break;}break;}
            case 0x0e:case 0x4e:{int a=Word(),n=R(a);NZ(A-n);R(a);W(a,op==0x0e?n|A:n&~A);break;}
            case 0x2e:case 0xde:{int d=Fetch();if(op==0xde){Idle();d+=X;}int n=R(D(d));Idle();Branch(A!=n);break;}
            case 0x6e:{int a=D(Fetch()),n=(byte)(R(a)-1);W(a,n);Branch(n!=0);break;}
            case 0xfe:Dummy();Idle();Y--;Branch(Y!=0);break;
            case 0x5f:PC=(ushort)Word();break;
            case 0x1f:{int a=Word();Idle();a=(ushort)(a+X);PC=(ushort)(R(a)|R(a+1)<<8);break;}
            case 0x3f:{int a=Word();Idle();Push(PC>>8);Push(PC);Idle();Idle();PC=(ushort)a;break;}
            case 0x4f:{int a=Fetch();Idle();Push(PC>>8);Push(PC);Idle();PC=(ushort)(0xff00|a);break;}
            case 0x6f:Dummy();Idle();PC=(ushort)(Pop()|Pop()<<8);break;
            case 0x7f:Dummy();Idle();PSW=Pop();PC=(ushort)(Pop()|Pop()<<8);break;
            case 0x0f:Dummy();Push(PC>>8);Push(PC);Push(PSW);Idle();PC=(ushort)(R(0xffde)|R(0xffdf)<<8);Flag(16,true);Flag(4,false);break;
            case 0x9f:Dummy();Idle();Idle();A=NZ((A<<4)|(A>>4));Idle();break;
            case 0xcf:{Dummy();for(int i=0;i<7;i++)Idle();int n=A*Y;A=(byte)n;Y=NZ(n>>8);break;}
            case 0x9e:{Dummy();for(int i=0;i<10;i++)Idle();int n=A|Y<<8;Flag(64,Y>=X);Flag(8,(Y&15)>=(X&15));if(Y<2*X){A=NZ(n/X);Y=(byte)(n%X);}else{int t=n-(X<<9);A=NZ(255-t/(256-X));Y=(byte)(X+t%(256-X));}break;}
            case 0xdf:Dummy();Idle();if(F(1)||A>0x99){A+=0x60;Flag(1,true);}if(F(8)||(A&15)>9)A+=6;NZ(A);break;
            case 0xbe:Dummy();Idle();if(!F(1)||A>0x99){A-=0x60;Flag(1,false);}if(!F(8)||(A&15)>9)A-=6;NZ(A);break;
            case 0xef:case 0xff:for(int i=0;i<3;i++){Dummy();Idle();}Sleeping=op==0xef;Stopped=op==0xff;break;
            default:throw new InvalidOperationException($"Unimplemented SPC opcode {op:X2}");
        }
        Instructions++;
    }
    private void Store(int address,byte value){R(address);W(address,value);}
}
