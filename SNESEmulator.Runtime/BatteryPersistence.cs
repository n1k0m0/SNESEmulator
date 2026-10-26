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
// Coordinates SRAM generations and reports write/recovery failures.
// A failed save must remain visible and must not be treated as a clean shutdown.
//

using System.Security.Cryptography;
using System.Text;
using SNESEmulator.Core;

namespace SNESEmulator.Runtime;

/// <summary>Serializes capture itself, not just writes: an old autosave cannot overtake reload/close.</summary>
public sealed class BatteryPersistence(Func<Task<BatterySnapshot?>> capture,Func<string,string> legacyPath,Action<string>? notice=null):IDisposable
{
    private readonly SemaphoreSlim order=new(1,1);
    private string? persistedHash;
    private byte[]? persistedBytes;
    private bool needsRepair;
    private string? leasedHash;
    private FileStream? lease;
    private FileStream? AcquireLease(string hash)
    {
        if(leasedHash==hash)return null;
        string path=ProtectedPath(legacyPath(hash));Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try{return new FileStream(path+".session.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException e){throw new IOException("SRAM is already open in another emulator instance; close that session and retry.",e);}
    }
    private void CommitLease(string hash,FileStream? next)
    {
        if(next==null)return;lease?.Dispose();lease=next;leasedHash=hash;
    }
    private void EnsureLease(string hash)=>CommitLease(hash,AcquireLease(hash));
    public void Dispose(){order.Wait();try{lease?.Dispose();lease=null;leasedHash=null;}finally{order.Release();}}
    public long Writes {get;private set;}
    public const int MaximumBytes=512*1024;
    public static string ProtectedPath(string legacy)=>legacy+".v1";
    public static byte[] Encode(BatterySnapshot snapshot)
    {
        if(snapshot.Bytes.Length>MaximumBytes||snapshot.Hash.Length is <1 or >127||snapshot.Hash.Any(c=>!char.IsAsciiLetterOrDigit(c)))throw new InvalidDataException("Invalid battery identity or size.");
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
        writer.Write("SNESSRAM1");writer.Write(snapshot.Hash);writer.Write(snapshot.Bytes.Length);writer.Write(SHA256.HashData(snapshot.Bytes));writer.Write(snapshot.Bytes);return stream.ToArray();
    }
    public static byte[] Decode(byte[] encoded,string hash,int size)
    {
        // Fixed prefix + bounded identity checked without trusting BinaryReader string allocation.
        if(encoded.Length>MaximumBytes+256)throw new InvalidDataException("Battery file exceeds size limit.");
        using var reader=new BinaryReader(new MemoryStream(encoded,false));
        if(reader.ReadByte()!=9||Encoding.ASCII.GetString(reader.ReadBytes(9))!="SNESSRAM1")throw new InvalidDataException("Unsupported battery format.");
        int length=reader.ReadByte();if(length>128||length!=hash.Length||Encoding.ASCII.GetString(reader.ReadBytes(length))!=hash||reader.ReadInt32()!=size||size<0||size>MaximumBytes)throw new InvalidDataException("Battery ROM identity or SRAM size mismatch.");
        byte[] digest=reader.ReadBytes(32),bytes=reader.ReadBytes(size);
        if(bytes.Length!=size||reader.BaseStream.Position!=reader.BaseStream.Length||!SHA256.HashData(bytes).AsSpan().SequenceEqual(digest))throw new InvalidDataException("Battery checksum mismatch or truncated data.");
        return bytes;
    }
    private static bool Valid(byte[] bytes,string hash,int size)
    {try{_ = Decode(bytes,hash,size);return true;}catch(Exception e) when(e is IOException or InvalidDataException){return false;}}
    private async Task FlushOrderedAsync()
    {
        // The ordering semaphore covers capture as well as the write. Otherwise
        // an earlier autosave could finish after a same-ROM reload or final close
        // and durably replace newer SRAM with an older owner-thread snapshot.
        var snapshot=await capture().ConfigureAwait(false);
        if(snapshot==null||snapshot.Bytes.Length==0)return;
        EnsureLease(snapshot.Hash);
        string path=ProtectedPath(legacyPath(snapshot.Hash));
        if(!needsRepair&&persistedHash==snapshot.Hash&&persistedBytes!=null&&persistedBytes.AsSpan().SequenceEqual(snapshot.Bytes))
        {
            bool intact=AtomicFile.Locked(path,p=>{
                try{return File.Exists(p)&&Valid(AtomicFile.ReadBytes(p,MaximumBytes+256),snapshot.Hash,snapshot.Bytes.Length);}
                catch(InvalidDataException){return false;}
            });
            if(intact)return;
        }
        var encoded=Encode(snapshot);
        AtomicFile.Write(path,file=>file.Write(encoded),candidate=>Valid(AtomicFile.ReadBytes(candidate,MaximumBytes+256),snapshot.Hash,snapshot.Bytes.Length));
        persistedHash=snapshot.Hash;persistedBytes=snapshot.Bytes;needsRepair=false;Writes++;
    }
    /// <summary>Serializes pending battery writes and propagates failure to the shutdown/session caller.</summary>
    public async Task FlushAsync()
    {
        await order.WaitAsync().ConfigureAwait(false);try{await FlushOrderedAsync().ConfigureAwait(false);}finally{order.Release();}
    }
    // Hold both old and tentative target leases until validated installation succeeds.
    // A rejected import/owner command leaves the old live session exclusively owned.
    /// <summary>Flushes the old session and obtains the new SRAM lease before installing battery data.</summary>
    public async Task SwitchAsync(string hash,int size,Func<byte[],Task> install)
    {
        await order.WaitAsync().ConfigureAwait(false);FileStream? tentative=null;
        try
        {
            await FlushOrderedAsync().ConfigureAwait(false);tentative=AcquireLease(hash);
            byte[] bytes=ReadBattery(hash,size,out bool repair);
            await install(bytes).ConfigureAwait(false);
            CommitLease(hash,tentative);tentative=null;
            persistedHash=hash;persistedBytes=bytes.Length==0?null:bytes.ToArray();needsRepair=repair||bytes.Length==0;
        }
        finally{tentative?.Dispose();order.Release();}
    }
    public async Task<byte[]> FlushAndReadAsync(string hash,int size)
    {
        byte[] result=[];await SwitchAsync(hash,size,bytes=>{result=bytes;return Task.CompletedTask;}).ConfigureAwait(false);return result;
    }
    private byte[] ReadBattery(string hash,int size,out bool repair)
    {
        string legacy=legacyPath(hash),path=ProtectedPath(legacy);repair=false;
        if(File.Exists(path)||File.Exists(path+".bak"))
        {
            var encoded=AtomicFile.Read(path,MaximumBytes+256,data=>Valid(data,hash,size),out bool recovered);
            repair=recovered;if(recovered)notice?.Invoke("SRAM RECOVERED FROM VALIDATED BACKUP; DAMAGED FILE RETAINED");
            return Decode(encoded,hash,size);
        }
        if(File.Exists(legacy))
        {
            if(new FileInfo(legacy).Length!=size)throw new InvalidDataException("Legacy SRAM size mismatch; file retained.");
            byte[] bytes=AtomicFile.ReadBytes(legacy,MaximumBytes);notice?.Invoke("LEGACY SRAM IMPORTED BY ROM HASH / SIZE; NO HISTORICAL CHECKSUM");
            var encoded=Encode(new(hash,bytes));AtomicFile.Write(path,file=>file.Write(encoded),candidate=>Valid(AtomicFile.ReadBytes(candidate,MaximumBytes+256),hash,size));Writes++;return bytes;
        }
        return [];
    }
}
