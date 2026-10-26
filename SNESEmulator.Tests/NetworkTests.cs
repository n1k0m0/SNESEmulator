// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Checks real socket/TLS/relay sessions, peer permissions and bounded transport shutdown.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using SNESEmulator.Network;
using SNESEmulator.Core;
using SNESEmulator.Runtime;

internal static class NetworkTests
{
    private static void Check(bool value,string reason="network assertion"){if(!value)throw new Exception(reason);}
    private static void Wait(Func<bool> predicate,string reason="network wait"){Check(SpinWait.SpinUntil(predicate,4000),reason);}
    private static string ReadLiveLog(string path)
    {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    private static void Processed(SnesNetServer host,int id,Action send)
    {long before=host.GetProcessedInputPackets(id);send();Wait(()=>host.GetProcessedInputPackets(id)>before,"actual server input parsing barrier");}
    private sealed class Peer : IDisposable
    {
        public readonly TcpClient Socket=new();
        public readonly System.IO.Stream Stream;
        public int Id;
        public Peer(int port,string password="p",SnesNetClientRole role=SnesNetClientRole.Player,bool fragmented=false)
        {
            Socket.NoDelay=true;Socket.ReceiveBufferSize=1024;Socket.Connect(IPAddress.Loopback,port);Stream=SnesNetTls.AuthenticateClient(Socket,"localhost",port,out _);Stream.ReadTimeout=3000;Stream.WriteTimeout=3000;
            var message=new SnesNetMessage{Type=SnesNetMessageType.ClientHello,Payload=SnesNetProtocol.CreateClientHelloPayload("raw",password,role)};
            if(fragmented){using var b=new MemoryStream();SnesNetProtocol.WriteMessage(b,message);foreach(byte part in b.ToArray())Stream.WriteByte(part);}else SnesNetProtocol.WriteMessage(Stream,message);
            var welcome=Read(SnesNetMessageType.ServerWelcome);SnesNetProtocol.ReadServerWelcomePayload(welcome.Payload,out Id,out _,out _,out _,out _,out _,out _,out _);
        }
        public SnesNetMessage Read(SnesNetMessageType type)
        {
            for(int i=0;i<100;i++)
            {
                var message=SnesNetProtocol.ReadMessage(Stream)??throw new EndOfStreamException();
                if(message.Type==type)return message;
                if(message.Type==SnesNetMessageType.Ping)SnesNetProtocol.WriteMessage(Stream,new(){Type=SnesNetMessageType.Pong,Timestamp=message.Timestamp,Payload=[]});
            }
            throw new IOException("missing protocol message "+type);
        }
        public void Barrier(){SnesNetProtocol.WriteMessage(Stream,new(){Type=SnesNetMessageType.Ping,Payload=[],Timestamp=0});Read(SnesNetMessageType.Pong);}
        public void Input(ushort state,uint generation)=>SnesNetProtocol.WriteMessage(Stream,new(){Type=SnesNetMessageType.InputState,Sequence=generation,Payload=SnesNetProtocol.CreateInputStatePayload(state)});
        public void Dispose(){Stream.Dispose();Socket.Dispose();}
    }
    private static X509Certificate2 Certificate()
    {
        using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var raw=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
        return X509CertificateLoader.LoadPkcs12(raw.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.Exportable);
    }
    private static string lastProbeStatus="";
    private static SnesNetTlsException? ProbeCertificate(X509Certificate2 cert,int port)
    {
        var listener=new TcpListener(IPAddress.Loopback,port);listener.Start();
        Task server=Task.Run(()=>{try{using var tcp=listener.AcceptTcpClient();using var ssl=new SslStream(tcp.GetStream());ssl.AuthenticateAsServer(cert,false,SslProtocols.Tls12|SslProtocols.Tls13,false);}catch(AuthenticationException){}catch(IOException){}});
        try{using var tcp=new TcpClient();tcp.Connect(IPAddress.Loopback,port);try{using var ssl=SnesNetTls.AuthenticateClient(tcp,"localhost",port,out string result);lastProbeStatus=result;return null;}catch(SnesNetTlsException e){return e;}}
        finally{listener.Stop();Check(server.Wait(4000),"custom TLS peer drained");}
    }
    private static int Port(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    public static void Run(Action<string,Action> test,string root)
    {
        test("M9d actual TLS fragmented input revoke regrant and stale generation",()=>{
            using var host=new SnesNetServer(256,224);host.Start(0,"p");using var p=new Peer(host.Port,fragmented:true);Wait(()=>host.GetClientSnapshots().Count==1);p.Read(SnesNetMessageType.Timeline);
            ushort held=0x7fff;host.SetClientPermission(p.Id,SnesNetJoystickPermission.Port1);uint first=p.Read(SnesNetMessageType.PermissionUpdate).Sequence;
            p.Input(65535,first);p.Input(held,first);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==held);
            host.SetClientPermission(p.Id,SnesNetJoystickPermission.Observer);host.SetClientPermission(p.Id,SnesNetJoystickPermission.Port2);
            Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"immediate revoke neutral");
            uint revoke=p.Read(SnesNetMessageType.PermissionUpdate).Sequence,grant=p.Read(SnesNetMessageType.PermissionUpdate).Sequence;Check(grant>first&&grant>revoke,"monotone rights epoch");
            p.Input(held,first);p.Input(65535,revoke);p.Input(held,grant);p.Barrier();Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"stale and unreleased input rejected");
            p.Input(65535,grant);p.Input(held,grant);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==held);
            p.Dispose();Wait(()=>host.GetClientSnapshots().Count==0);Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"disconnect neutral");host.Stop();Check(host.PendingTasks==0,"all tasks drained");
        });
        test("M9d actual two TLS clients observer held-state portchange focusneutral kick",()=>{
            using var host=new SnesNetServer(256,224);using var a=new SnesNetClient{PlayAudio=false};using var b=new SnesNetClient{PlayAudio=false};host.Start(0,"p");
            Check(a.Connect("localhost",host.Port,"p",SnesNetClientRole.Player,"a",out _));Check(b.Connect("localhost",host.Port,"p",SnesNetClientRole.Observer,"b",out _));Wait(()=>host.GetClientSnapshots().Count==2);
            using(var failed=new SnesNetClient{PlayAudio=false})using(var mailbox=new UiMailbox()){
                failed.StatusChanged+=mailbox.SetStatus;Check(!failed.Connect("localhost",host.Port,"wrong",SnesNetClientRole.Player,"wrong",out string result),"wrong LAN password rejected");string shown="CONNECTING";mailbox.PostOutcomeAsync(()=>shown=result,()=>shown).GetAwaiter().GetResult();mailbox.Drain();shown=mailbox.TakeStatus()??shown;Check(shown.Contains("PASSWORD"),"actual rejected-password outcome shown after disconnect status");}
            Processed(host,b.ClientId,()=>b.SendJoystickState(0x7fff,true));Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==65535,"observer no input");
            host.SetClientPermission(a.ClientId,SnesNetJoystickPermission.Port1);Wait(()=>a.Permission==SnesNetJoystickPermission.Port1);a.SendJoystickState(65535,true);a.SendJoystickState(0x7fff,true);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==0x7fff);
            host.SetClientPermission(a.ClientId,SnesNetJoystickPermission.Port2);Wait(()=>a.Permission==SnesNetJoystickPermission.Port2);Processed(host,a.ClientId,()=>a.SendJoystickState(0x7fff,true));Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"held state not repackaged after port change");
            a.SendJoystickState(65535,true);a.SendJoystickState(0x7fff,true);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==0x7fff);a.RequireNeutral();Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535);
            Processed(host,a.ClientId,()=>a.SendJoystickState(0x7fff,true));Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"focusrestore requires release");
            host.KickClient(a.ClientId);Wait(()=>!a.IsConnected);Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"kickneutral");a.Disconnect();b.Disconnect();host.Stop();Check(a.TasksDrained&&b.TasksDrained&&host.PendingTasks==0,"drained after kick");
        });
        test("M9d TLS certificate replacement recovery unknown endpoint and corrupt trust fail closed",()=>{
            using var one=Certificate();using var two=Certificate();int a=Port(),b=Port();
            Check(ProbeCertificate(one,a)==null,"first TOFU");var changed=ProbeCertificate(two,a);Check(changed?.IsCertificateChanged==true,"changed certificate rejected");
            using(var mailbox=new UiMailbox()){mailbox.SetStatus("DISCONNECTED");string shown="CONNECTING";mailbox.PostOutcomeAsync(()=>shown="TLS CERTIFICATE CHANGED - REVIEW CERTIFICATE ROW",()=>shown).GetAwaiter().GetResult();mailbox.Drain();shown=mailbox.TakeStatus()??shown;Check(shown.Contains("REVIEW CERTIFICATE"),"actual changed-cert result remains reviewable in displayed outcome");}
            SnesNetTls.ReplaceTrustedServerCertificate("localhost",a,Convert.ToHexString(SHA256.HashData(two.RawData)));Check(ProbeCertificate(two,a)==null,"explicit confirmed replacement");
            SnesNetTls.ReplaceTrustedServerCertificate("localhost",b,Convert.ToHexString(SHA256.HashData(one.RawData)));
            string trust=Path.Combine(root,"network-trust.json");File.WriteAllText(trust,"broken");
            Check(ProbeCertificate(two,a)==null&&lastProbeStatus.Contains("RECOVERED"),"known recovered pin remains visible and verifies same certificate");
            var lost=ProbeCertificate(two,b);Check(lost?.PinChange?.OldFingerprint=="UNKNOWN AFTER TRUST RECOVERY","backup pinloss cannot trigger silent TOFU");
            File.WriteAllText(trust+".bak","broken");bool rejected=false;try{ProbeCertificate(one,a);}catch(InvalidDataException){rejected=true;}catch(AuthenticationException){rejected=true;}Check(rejected,"corrupt generations fail closed");
            // Explicit isolated cleanup, preserving corrupt inputs for the evidence fixture.
            File.Copy(trust,trust+".test-corrupt");File.Copy(trust+".bak",trust+".bak.test-corrupt");File.Delete(trust);File.Delete(trust+".bak");
        });
        test("M9d crossprocess trust readmodifywrite and certificate create identity",()=>{
            string data=Path.Combine(root,"two-process"),barrier=Path.Combine(root,"start");Directory.CreateDirectory(data);
            var processes=new List<Process>();try{
                for(int i=0;i<2;i++){
                    var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true};
                    foreach(string arg in new[]{typeof(NetworkTests).Assembly.Location,"--network-persist-worker",data,"peer"+i,new string((char)('A'+i),64),barrier,Path.Combine(root,"cert"+i),"reserved"})start.ArgumentList.Add(arg);
                    processes.Add(Process.Start(start)!);
                }
                Wait(()=>Enumerable.Range(0,2).All(i=>File.Exists(Path.Combine(root,"cert"+i+".ready"))),"both processes ready");File.WriteAllText(barrier,"start");Wait(()=>Enumerable.Range(0,2).All(i=>File.Exists(Path.Combine(root,"cert"+i+".pin-done"))),"both pin transactions complete");File.WriteAllText(barrier+".cert","start");foreach(var p in processes)Check(p.WaitForExit(10000)&&p.ExitCode==0,"persist worker completion");
                var pins=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"network-trust.json"))).RootElement.GetProperty("PinnedCertificates");Check(pins.EnumerateObject().Count()==2,"no pin overwritten");Check(File.ReadAllText(Path.Combine(root,"cert0"))==File.ReadAllText(Path.Combine(root,"cert1")),"one crossprocess PFX identity");
            }finally{foreach(var p in processes){if(!p.HasExited){p.Kill(true);p.WaitForExit();}p.Dispose();}}
        });
        test("M9d strict malformed strings enums booleans counts and real TLS oversize frames",()=>{
            void Reject(Action parse){try{parse();throw new Exception("malformed accepted");}catch(Exception e)when(e is InvalidDataException or EndOfStreamException or System.Text.DecoderFallbackException){}}
            byte[] hello=SnesNetProtocol.CreateClientHelloPayload("a","p",SnesNetClientRole.Player);
            Reject(()=>SnesNetProtocol.ReadClientHelloPayload(hello[..^1],out _,out _,out _,out _));
            Reject(()=>SnesNetProtocol.ReadClientHelloPayload([..hello,0],out _,out _,out _,out _));
            var invalid=hello.ToArray();invalid[8]=0xff;Reject(()=>SnesNetProtocol.ReadClientHelloPayload(invalid,out _,out _,out _,out _));
            invalid=hello.ToArray();invalid[^1]=255;Reject(()=>SnesNetProtocol.ReadClientHelloPayload(invalid,out _,out _,out _,out _));
            Reject(()=>SnesNetProtocol.ReadClientListPayload(BitConverter.GetBytes(int.MaxValue)));Reject(()=>SnesNetProtocol.ReadClientListPayload([255,255,255,255]));
            Reject(()=>SnesNetProtocol.ReadPermissionPayload([1,2],out _,out _));Reject(()=>SnesNetProtocol.ReadTextPayload([4,0,0,0,65]));
            using var host=new SnesNetServer(256,224);host.Start(0,"p");
            foreach(int length in new[]{-1,SnesNetProtocol.MaxPayloadLength+1}){
                using var peer=new Peer(host.Port);Wait(()=>host.GetClientSnapshots().Count==1);byte[] header=new byte[20];BitConverter.GetBytes(length).CopyTo(header,0);header[4]=4;peer.Stream.Write(header);Wait(()=>host.GetClientSnapshots().Count==0,"oversize rejected before allocation");
            }
            using(var peer=new Peer(host.Port)){
                Wait(()=>host.GetClientSnapshots().Count==1);SnesNetProtocol.WriteMessage(peer.Stream,new(){Type=SnesNetMessageType.ClientHello,Payload=hello});Wait(()=>host.GetClientSnapshots().Count==0,"unexpected runtime hello rejected");
            }
            host.Stop();Check(host.PendingTasks==0,"malformed peers drained");
        });
        test("M9d session timeline rejects delayed output and preserves input across media-only epochs",()=>{
            using var host=new SnesNetServer(256,224);using var client=new SnesNetClient{PlayAudio=false};host.Start(0,"p");Check(client.Connect("localhost",host.Port,"p",SnesNetClientRole.Player,"a",out _));Wait(()=>host.GetClientSnapshots().Count==1);
            host.SetClientPermission(client.ClientId,SnesNetJoystickPermission.Port1);Wait(()=>client.Permission==SnesNetJoystickPermission.Port1);client.SendJoystickState(65535,true);client.SendJoystickState(0x7fff,true);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==0x7fff);
            uint input=client.InputGeneration;host.UpdateTimeline(0,945000000,44,24576000,1,false,false,true);Wait(()=>client.TimelineIdentity?.Muted==true);Check(client.InputGeneration==input&&host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==0x7fff,"audio mute does not revoke held input");
            host.UpdateTimeline(1,945000000,44,24576000,1,false,true,true);Wait(()=>client.TimelineIdentity?.ConsoleEpoch==1);Check(client.InputGeneration==input,"turbo mode epoch preserves current input");
            host.UpdateTimeline(2,945000000,44,24576000,1,false,false,false);Wait(()=>client.TimelineIdentity?.ConsoleEpoch==2);Check(client.InputGeneration==input,"turbo resume preserves current input");
            int video=0,audio=0;client.FrameReceived+=_=>Interlocked.Increment(ref video);client.AudioReceived+=_=>Interlocked.Increment(ref audio);
            var g=new PpuGeometry(256,224,256,false,FieldIndex:10,StartClock:3573680,EndClock:3931048);var field=new PpuFrame(g,new ushort[256*224],new byte[256*224],new uint[256*224]);
            var old=new FieldOutput(2,field,ReadOnlyMemory<StereoSample>.Empty,0,0,new(0,0),false);
            var status=new WorkerStatus(3,11,3931048,"test","hash",false,false,null,0,0,0,0,24576000,1,HardwareGeneration:1,InputEpoch:1);
            host.UpdateHostStatus(status,g);Wait(()=>client.InputGeneration>input);Check(!host.BroadcastOutput(old,status),"old dequeued sink callback rejected after epoch publication");Thread.Sleep(80);Check(video==0&&audio==0,"old media never scheduled");
            Processed(host,client.ClientId,()=>client.SendJoystickState(0x7fff,true));Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==65535,"reset/load epoch requires fresh neutral");
            host.UpdateTimeline(4,945000000,44,24606720,1,false,false,false);host.BroadcastAudio([1,0,2,0],4,0,672);Thread.Sleep(80);Check(audio==0,"32040 lab rate never mislabeled as32000");
            client.Disconnect();host.Stop();Check(client.TasksDrained&&host.PendingTasks==0,"timeline shutdown drained");
        });
        test("M9d registration versus stop atomic ownership and callback disconnect completion",()=>{
            using var host=new SnesNetServer(256,224);using var reached=new ManualResetEventSlim();host.BeforeClientRegistration=token=>{reached.Set();token.WaitHandle.WaitOne();};host.Start(0,"p");using(var peer=new Peer(host.Port)){Check(reached.Wait(3000),"registration barrier");host.Stop();Check(host.PendingTasks==0&&host.GetClientSnapshots().Count==0,"no client registered after stop snapshot");}
            host.BeforeClientRegistration=null;host.Start(0,"p");using var client=new SnesNetClient{PlayAudio=false};Check(client.Connect("localhost",host.Port,"p",SnesNetClientRole.Observer,"callback",out _));using var called=new ManualResetEventSlim();client.FrameReceived+=_=>{client.Disconnect();called.Set();};host.BroadcastVideoFrame(new uint[256*224],256,224);Check(called.Wait(3000),"playout callback");Check(client.CleanupCompletion.Wait(3000),"owned deferred cleanup completed");client.Dispose();host.Stop();Check(client.TasksDrained&&host.PendingTasks==0,"callback resources drained");
        });
        test("M9d actual WinMM provider source map initial prefill underrun refill and flush",()=>{
            using var output=new NetworkAudioOutput();byte[] pcm=new byte[6400*4];for(int i=0;i<6400;i++){System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4),4000);System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i*4+2),-2000);}
            output.Write(pcm,672);Wait(()=>output.PlayedCrystal is >=672 and <(672+6400*768L),"first actual real-sample driver playhead");Wait(()=>output.PaddingFrames>0,"actual WinMM requested padding");Wait(()=>!output.PlayedCrystal.HasValue,"underflow cursor is not a real sample");
            output.Write(pcm,672+6400*768L);Check(!output.PlayedCrystal.HasValue,"new block is not instantly marked played through prior silence");Wait(()=>output.PlayedCrystal is >=(672+6400*768L) and <(672+12800*768L),"refilled block becomes genuinely played");output.Clear();Check(output.PlayedCrystal==null&&output.BufferedNativeFrames==0,"wave reset invalidates device and provider maps");
        });
        test("M9d actual LAN source-timed stereo and nonzero driver-synchronized video presentation",()=>{
            using var host=new SnesNetServer(256,224);using var client=new SnesNetClient();using var worker=new EmulationWorker();host.Start(0,"p");Check(client.Connect("localhost",host.Port,"p",SnesNetClientRole.Observer,"av",out _));worker.InsertAsync(DiagnosticRom.Create()).GetAwaiter().GetResult();worker.SetModeAsync(false,false).GetAwaiter().GetResult();
            bool observe=Environment.GetEnvironmentVariable("SNESEMULATOR_AV_OBSERVE")=="1";var observedPlayout=client.Playout;observedPlayout.CaptureDiagnostics=observe;
            double? measurementStart=null;
            try{
            Check(client.AudioDeviceAvailable,"actual remote WinMM device is available");int receivedVideo=0;client.FrameReceived+=_=>Interlocked.Increment(ref receivedVideo);
            var timer=Stopwatch.StartNew();int fields=0;double stableSince=-1,lastRow=-1;
            void Pump(){host.UpdateHostStatus(worker.Status);if(worker.TryDequeue(out var output)){host.BroadcastOutput(output!,()=>worker.Status);fields++;}else Thread.Sleep(1);}
            while(timer.Elapsed.TotalSeconds<30)
            {
                Pump();double now=timer.Elapsed.TotalSeconds;
                if(now-lastRow>=.5){lastRow=now;Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Kind="actual-driver-av-cold",Seconds=now,fields,worker.Status.PacingLateSeconds,client.TimelineIdentity,client.Playout.PresentedAudio,client.Playout.PresentedVideo,client.Playout.DriverComparedVideo,client.Playout.Dropped,client.Playout.Fault}));}
                if(worker.Status.PacingLateSeconds<.05&&client.TimelineIdentity is {Muted:false}&&client.Playout.DriverComparedVideo>=5&&client.Playout.PresentedAudio>=5)
                {if(stableSince<0)stableSince=now;if(now-stableSince>=.5)break;}else stableSince=-1;
            }
            Check(stableSince>=0&&timer.Elapsed.TotalSeconds<30,"bounded stable unmuted driver readiness");
            long beginVideo=client.Playout.PresentedVideo,beginAudio=client.Playout.PresentedAudio,beginDriver=client.Playout.DriverComparedVideo,beginDropped=client.Playout.Dropped;double begin=timer.Elapsed.TotalSeconds,beginMaximum=client.Playout.MaximumSoftwareAvErrorSeconds;
            measurementStart=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
            while(timer.Elapsed.TotalSeconds-begin<5)Pump();
            var playout=client.Playout;Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Kind="actual-driver-av",ColdSeconds=begin,MeasurementSeconds=timer.Elapsed.TotalSeconds-begin,fields,Video=playout.PresentedVideo-beginVideo,Audio=playout.PresentedAudio-beginAudio,DriverVideo=playout.DriverComparedVideo-beginDriver,ColdDropped=beginDropped,MeasurementDropped=playout.Dropped-beginDropped,ColdMaximumSoftwareAvErrorSeconds=beginMaximum,playout.Dropped,playout.MaximumSoftwareAvErrorSeconds,playout.MaximumSchedulingErrorSeconds,playout.MaximumQueueLatencySeconds,playout.Fault}));
            Check(playout.PresentedVideo-beginVideo>=30&&playout.PresentedAudio-beginAudio>=30&&playout.DriverComparedVideo-beginDriver>=30,"nonzero measured audio and driver-synchronized video");
            Check(playout.Fault==null&&playout.MaximumSoftwareAvErrorSeconds<.04,"measured final-provider/driver A/V distance");client.Disconnect();host.Stop();Check(client.TasksDrained&&host.PendingTasks==0,"AV tasks drained");
            }finally{if(observe){var rows=observedPlayout.DiagnosticSnapshot();string path=Path.Combine(root,"actual-driver-av-observations.json");File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{MeasurementStart=measurementStart,PhaseReached=measurementStart.HasValue?"measurement":"cold-readiness",CumulativeMaximum=observedPlayout.MaximumSoftwareAvErrorSeconds,Rows=rows,Limit=4096,Meaning="Opt-in software samples including rejected commits; fresh commit and callback probes feed cumulative metric including Cold; not physical DAC or original failing-run reconstruction"}));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Kind="actual-driver-av-observations",Path=path,Count=rows.Length}));}}
        });
        test("M9d actual local TLS relay two clients v4 epochs stereo malformed budgets and kick",()=>{
            using var cert=Certificate();string certPath=Path.Combine(root,"m9d-relay.pem"),keyPath=Path.Combine(root,"m9d-relay.key");File.WriteAllText(certPath,cert.ExportCertificatePem());using(var rsa=cert.GetRSAPrivateKey())File.WriteAllText(keyPath,rsa!.ExportPkcs8PrivateKeyPem());
            int port=Port();string script=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../relay_server/snes_relay_server.py"));var start=new ProcessStartInfo("python"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};foreach(string arg in new[]{script,"--host","127.0.0.1","--port",port.ToString(),"--cert",certPath,"--key",keyPath,"--log-file",Path.Combine(root,"relay.log")})start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            try
            {
                Wait(()=>{try{using var tcp=new TcpClient();tcp.Connect(IPAddress.Loopback,port);return true;}catch{return false;}},"local relay readiness");
                using var host=new SnesNetServer(256,224);using var a=new SnesNetClient{PlayAudio=false};using var b=new SnesNetClient{PlayAudio=false};host.StartRelay("localhost",port,"m9d","","p");
                Check(a.ConnectRelay("localhost",port,"m9d","","p",SnesNetClientRole.Player,"a",out string status),status);Check(b.ConnectRelay("localhost",port,"m9d","","p",SnesNetClientRole.Observer,"b",out status),status);Wait(()=>host.GetClientSnapshots().Count==2);
                using(var wrong=new SnesNetClient{PlayAudio=false})Check(!wrong.ConnectRelay("localhost",port,"m9d","","wrong",SnesNetClientRole.Player,"wrong",out status)&&status.Contains("PASSWORD"),"relay session wrong password");
                host.SetClientPermission(a.ClientId,SnesNetJoystickPermission.Port1);Wait(()=>a.Permission==SnesNetJoystickPermission.Port1);a.SendJoystickState(65535,true);a.SendJoystickState(0x7fff,true);Wait(()=>host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port1)==0x7fff);
                host.SetClientPermission(a.ClientId,SnesNetJoystickPermission.Observer);host.SetClientPermission(a.ClientId,SnesNetJoystickPermission.Port2);Wait(()=>a.Permission==SnesNetJoystickPermission.Port2);Processed(host,a.ClientId,()=>a.SendJoystickState(0x7fff,true));Check(host.GetAggregatedJoystickState(SnesNetJoystickPermission.Port2)==65535,"relay regrant held state suppressed");
                using var video=new CountdownEvent(2);a.FrameReceived+=_=>video.Signal();b.FrameReceived+=_=>video.Signal();var g=new PpuGeometry(512,239,512,true,true,true,0,0,426568,21281370,1,TimingInterlace:true);host.UpdateTimeline(5,g.MasterHzNumerator,g.MasterHzDenominator,g.ApuHzNumerator,g.ApuHzDenominator,false,false,false,1,1);host.BroadcastVideoFrame(new uint[512*239],512,239,g);Check(video.Wait(3000),"both relay clients retain full field geometry/parity");
                using var audio=new CountdownEvent(2);byte[] pcm=[0x34,0x12,0xcc,0xed,0,0x80,0xff,0x7f];a.AudioReceived+=block=>{if(block.Pcm.SequenceEqual(pcm)&&block.FirstSample==987&&block.Crystal==672+987*768L)audio.Signal();};b.AudioReceived+=block=>{if(block.Pcm.SequenceEqual(pcm))audio.Signal();};host.BroadcastAudio(pcm,pcm.Length,987,672+987*768L);Check(audio.Wait(3000),"relay exact native sample identity");
                host.KickClient(a.ClientId);Wait(()=>!a.IsConnected,"relay channel close reaches client");a.Disconnect();using(var later=new SnesNetClient{PlayAudio=false})Check(later.ConnectRelay("localhost",port,"m9d","","p",SnesNetClientRole.Observer,"later",out _),"kick must not ban the entire anonymous relay address");b.Disconnect();host.Stop();Check(a.TasksDrained&&b.TasksDrained&&host.PendingTasks==0,"all virtual clients and handshakes drained");
                using var raw=new TcpClient();raw.Connect(IPAddress.Loopback,port);using var tls=SnesNetTls.AuthenticateClient(raw,"localhost",port,out _);tls.ReadTimeout=3000;byte[] header=new byte[16];BitConverter.GetBytes(0x52534e53).CopyTo(header,0);header[4]=1;header[5]=1;BitConverter.GetBytes(2*1024*1024).CopyTo(header,12);foreach(byte part in header)tls.WriteByte(part);Check(tls.ReadByte()==-1,"fragmented oversized relay header rejected before body allocation");
                var pending=new List<TcpClient>();string relayLog=Path.Combine(root,"relay.log");
                try
                {
                    for(int i=0;i<70;i++){var pendingTcp=new TcpClient();pendingTcp.Connect(IPAddress.Loopback,port);pending.Add(pendingTcp);}
                    Wait(()=>ReadLiveLog(relayLog).Contains("preTLS connection budget exceeded"),"actual preTLS resource admission barrier");
                    bool refused=false;using var tcp=new TcpClient();tcp.Connect(IPAddress.Loopback,port);try{using var denied=SnesNetTls.AuthenticateClient(tcp,"localhost",port,out _);}catch(Exception error)when(error is SnesNetTlsException or IOException){refused=true;}
                    Check(refused,"relay cap applies before TLS, not only after authentication");
                }
                finally{foreach(var tcp in pending)tcp.Dispose();}
                Wait(()=>{var lines=ReadLiveLog(relayLog).Split('\n');return lines.Count(line=>line.Contains("TLS admitted"))==lines.Count(line=>line.Contains("TLS closed"));},"all admitted relay TLS tasks drained after socket close");
                using var connection=new SnesRelayConnection(true);using var channel=new SnesRelayRawStream(connection,1);for(int i=0;i<2;i++)channel.EnqueueData(new byte[1024*1024]);Check(channel.BufferedBytes==2*1024*1024,"raw channel byte budget");try{channel.EnqueueData([1]);throw new Exception("unbounded raw channel");}catch(InvalidDataException){}
            }
            finally{if(!process.HasExited){process.Kill(true);process.WaitForExit();}File.WriteAllText(Path.Combine(root,"relay.stdout.log"),stdout.GetAwaiter().GetResult());File.WriteAllText(Path.Combine(root,"relay.stderr.log"),stderr.GetAwaiter().GetResult());}
        });
        test("M9d actual slow TLS server bounded nonblocking client input and concurrent shutdown",()=>{
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;using var release=new ManualResetEventSlim();
            Task server=Task.Run(()=>{using var tcp=listener.AcceptTcpClient();tcp.ReceiveBufferSize=1024;using var stream=SnesNetTls.AuthenticateServer(tcp);Check(SnesNetProtocol.ReadMessage(stream)?.Type==SnesNetMessageType.ClientHello,"real slowserver hello");SnesNetProtocol.WriteMessage(stream,new(){Type=SnesNetMessageType.ServerWelcome,Payload=SnesNetProtocol.CreateServerWelcomePayload(1,256,224,32000,SnesNetClientRole.Player,SnesNetJoystickPermission.Port1,false,"TLS CONNECTED")});SnesNetProtocol.WriteMessage(stream,new(){Type=SnesNetMessageType.Timeline,Payload=new NetworkTimeline(0,1,945000000,44,24576000,1,false,false,false).Encode()});release.Wait(TimeSpan.FromSeconds(10));});
            using var client=new SnesNetClient{PlayAudio=false};try{
                Check(client.Connect("localhost",port,"p",SnesNetClientRole.Player,"input",out string status),status);Wait(()=>client.TimelineIdentity!=null);var timer=Stopwatch.StartNew();client.SendJoystickState(65535,true);
                for(int i=0;i<100000;i++)client.SendJoystickState((ushort)(i%2==0?0x7fff:0xbfff),true);
                Check(timer.Elapsed.TotalSeconds<2&&client.PendingSend<=33,"UI producer bounded and independent from socket writes");
                timer.Restart();Task.WaitAll(Task.Run(client.Disconnect),Task.Run(client.Disconnect));Check(timer.Elapsed.TotalSeconds<3&&client.TasksDrained,"concurrent cancel closes slow socket and fully joins sendreceive");
            }finally{release.Set();listener.Stop();Check(server.Wait(4000),"owned slow server drained");}
        });
        test("M9d reconnect clears old owned picture for a paused new host with reused epochs",()=>{
            using var oldHost=new SnesNetServer(256,224);using var client=new SnesNetClient{PlayAudio=false};var picture=new EpochPublication<SnesNetVideoFrame>();client.SessionStarted+=picture.Clear;client.TimelineChanged+=picture.Update;client.FrameReceived+=f=>picture.Publish(f.Timeline,f);
            oldHost.Start(0,"p");oldHost.UpdateTimeline(0,945000000,44,24576000,1,false,false,true);Check(client.Connect("localhost",oldHost.Port,"p",SnesNetClientRole.Observer,"before",out _));oldHost.BroadcastVideoFrame(Enumerable.Repeat(0xff123456u,256*224).ToArray(),256,224);Wait(()=>picture.Read(client.TimelineIdentity)!=null,"old image committed");oldHost.Stop();Wait(()=>!client.IsConnected,"unexpected host stop");
            using var nextHost=new SnesNetServer(256,224);nextHost.Start(0,"p");nextHost.UpdateTimeline(0,945000000,44,24576000,1,true,false,false);Check(client.Connect("localhost",nextHost.Port,"p",SnesNetClientRole.Observer,"after",out _));Wait(()=>client.TimelineIdentity is {Paused:true},"new paused timeline parsed");Check(picture.Read(client.TimelineIdentity)==null,"new TCP session cannot inherit old pixels with reused numeric epochs");client.Disconnect();nextHost.Stop();Check(client.TasksDrained&&nextHost.PendingTasks==0,"both sessions drained");
        });
        test("M9d actual TLS handshake and slowcontrol resource budgets and shutdown",()=>{
            using var host=new SnesNetServer(256,224);host.Start(0,"p");var sockets=new List<TcpClient>();try{
                for(int i=0;i<12;i++){var tcp=new TcpClient();tcp.Connect(IPAddress.Loopback,host.Port);sockets.Add(tcp);}Thread.Sleep(50);Check(host.PendingTasks<=SnesNetServer.MaxHandshakes,"handshake cap");var timer=Stopwatch.StartNew();host.Stop();Check(timer.Elapsed.TotalSeconds<3&&host.PendingTasks==0,"cancel closes pending TLS before drain");
            }finally{foreach(var tcp in sockets)tcp.Dispose();}
            host.Start(0,"p");using var slow=new Peer(host.Port);Wait(()=>host.GetClientSnapshots().Count==1);var statuses=new List<string>();host.StatusChanged+=s=>{lock(statuses)statuses.Add(s);};
            for(int i=0;i<5000&&host.GetClientSnapshots().Count>0;i++)host.SetHostOverlayStatus(i+new string('x',200));
            Wait(()=>host.GetClientSnapshots().Count==0,"slow client disconnected at control capacity");host.Stop();Check(host.PendingTasks==0,"slow sender drained");Check(statuses.Any(s=>s.Contains("OVERFLOW")||s.Contains("FAILED")),"visible pressure failure");
        });
    }
}
