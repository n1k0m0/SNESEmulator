using SNESEmulator.Network;
using SNESEmulator.Core;
using SNESEmulator.SaveStates;

string testRoot=Path.Combine(Path.GetTempPath(),"SNESEmulator-tests-"+Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("SNESEMULATOR_DATA_DIR",testRoot);
Directory.CreateDirectory(testRoot);
var report = new SNESEmulator.Testing.TestReport("windows-integration");
int passed=0,failed=0;
void Check(bool value,string message="Assertion failed") { if(!value) throw new Exception(message); }
void Test(string name,Action test)
{
    try { test(); passed++; report.Add(name,"pass"); Console.WriteLine("PASS "+name); }
    catch(Exception e) { failed++; report.Add(name,"fail",e.ToString()); Console.WriteLine("FAIL "+name+": "+e); }
}
Test("Network video retains all 32768 SNES colors",()=>
{
    uint[] pixels=Enumerable.Range(0,32768).Select(i=>0xFF000000u|(uint)(((i&31)*255/31)<<16|(((i>>5)&31)*255/31)<<8|((i>>10)&31)*255/31)).ToArray();
    byte[] data=SnesNetProtocol.CreateVideoFramePayload(pixels,256,128,99);
    var decoded=SnesNetProtocol.ReadVideoFramePayload(data);
    Check(decoded!=null && decoded.FrameId==99 && decoded.Pixels.SequenceEqual(pixels));
    Check(SnesNetProtocol.ReadVideoFramePayload([0,0,0])==null);
});
Test("Network controller includes every SNES button",()=>
{
    for(int i=4;i<16;i++)
    {
        ushort value=(ushort)~(1<<i);
        Check(SnesNetProtocol.ReadInputStatePayload(SnesNetProtocol.CreateInputStatePayload(value))==value);
    }
});
Test("TLS host/client, permission, input, video, disconnect",()=>
{
    using var server=new SnesNetServer(256,224); using var client=new SnesNetClient();
    server.Start(0,"test-password");
    Check(client.Connect("localhost",server.Port,"test-password",SnesNetClientRole.Player,"test",out string status),status);
    Check(SpinWait.SpinUntil(()=>server.GetClientSnapshots().Count==1,3000));
    int id=server.GetClientSnapshots()[0].ClientId;
    Check(server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535);
    server.SetClientPermission(id,SnesNetJoystickPermission.Port2);
    Check(SpinWait.SpinUntil(()=>client.Permission==SnesNetJoystickPermission.Port2,3000));
    client.SendJoystickState((ushort)(65535 ^ (ushort)(SnesButtons.A|SnesButtons.L)),true);
    Check(SpinWait.SpinUntil(()=>server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==(ushort)(65535 ^ (ushort)(SnesButtons.A|SnesButtons.L)),3000));
    Check(server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==65535);
    using var received=new ManualResetEventSlim();
    client.FrameReceived+=f=>{if(f.Pixels[0]==0xFF123456) received.Set();};
    uint[] frame=Enumerable.Repeat(0xFF123456u,256*224).ToArray(); server.BroadcastVideoFrame(frame,256,224);
    Check(received.Wait(3000),"No video received");
    server.SetClientPermission(id,SnesNetJoystickPermission.Observer);
    Check(server.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535);
    client.Disconnect(); Check(SpinWait.SpinUntil(()=>server.GetClientSnapshots().Count==0,3000));
});
Test("TLS rejects wrong session password",()=>
{
    using var s=new SnesNetServer(256,224);using var c=new SnesNetClient();s.Start(0,"correct");
    Check(!c.Connect("localhost",s.Port,"wrong",SnesNetClientRole.Player,"test",out _));
});
Test("Python relay TLS + end-to-end session + full controller input",()=>
{
    using var rsa=System.Security.Cryptography.RSA.Create(2048);
    var request=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=localhost",rsa,
        System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
    string certPath=Path.Combine(testRoot,"relay-cert.pem"), keyPath=Path.Combine(testRoot,"relay-key.pem");
    File.WriteAllText(certPath,cert.ExportCertificatePem());File.WriteAllText(keyPath,rsa.ExportPkcs8PrivateKeyPem());
    var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
    int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
    string relay=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../relay_server/snes_relay_server.py"));
    // The executable is four levels below the repository root.
    if(!File.Exists(relay)) relay=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../relay_server/snes_relay_server.py"));
    var start=new System.Diagnostics.ProcessStartInfo("python") { UseShellExecute=false,CreateNoWindow=true };
    foreach(string arg in new[]{relay,"--host","127.0.0.1","--port",port.ToString(),"--cert",certPath,"--key",keyPath}) start.ArgumentList.Add(arg);
    using var process=System.Diagnostics.Process.Start(start)!;
    try
    {
        Check(SpinWait.SpinUntil(()=>
        {
            try {using var probe=new System.Net.Sockets.TcpClient();probe.Connect("127.0.0.1",port);return true;} catch{return false;}
        },5000),"Relay failed to start");
        using var host=new SnesNetServer(256,224);using var client=new SnesNetClient();
        host.StartRelay("localhost",port,"test-session","","secret");
        Check(client.ConnectRelay("localhost",port,"test-session","","secret",SnesNetClientRole.Player,"relay-player",out string status),status);
        Check(SpinWait.SpinUntil(()=>host.GetClientSnapshots().Count==1,3000));
        host.SetClientPermission(host.GetClientSnapshots()[0].ClientId,SnesNetJoystickPermission.Port1);
        Check(SpinWait.SpinUntil(()=>client.Permission==SnesNetJoystickPermission.Port1,3000));
        ushort buttons=(ushort)(65535 ^ (ushort)(SnesButtons.X|SnesButtons.R));client.SendJoystickState(buttons,true);
        Check(SpinWait.SpinUntil(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==buttons,3000));
        using var received=new ManualResetEventSlim();client.FrameReceived+=f=>{if(f.Pixels[0]==0xFFABCDEF) received.Set();};
        host.BroadcastVideoFrame(Enumerable.Repeat(0xFFABCDEFu,256*224).ToArray(),256,224);
        Check(received.Wait(3000),"Relay video missing");
    }
    finally { if(!process.HasExited) {process.Kill(true);process.WaitForExit();} }
});
Console.WriteLine($"{passed} passed, {failed} failed. Temporary test data: {testRoot}");
report.Save(args.Length>0?args[0]:Environment.GetEnvironmentVariable("SNES_LAB_CHILD_REPORT")??"artifacts/windows-integration");
return failed==0?0:1;
