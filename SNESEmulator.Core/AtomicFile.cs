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
// Publishes flushed file generations while retaining a recoverable valid backup.
// Validation failure must not replace the previous usable state.
//


namespace SNESEmulator.Core;

/// <summary>Same-directory atomic replacement, with a flushed, validated previous generation.</summary>
public static class AtomicFile
{
    private static readonly object[] Gates=Enumerable.Range(0,64).Select(_=>new object()).ToArray();
    public static string Normalize(string path) => Path.GetFullPath(path);
    public static T Locked<T>(string path, Func<string,T> action)
    {
        path=Normalize(path);
        lock(Gates[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(path)%Gates.Length])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Keep the empty lock file. Deleting it could race another process opening
            // a different lock inode. Contention is explicit and immediately retryable.
            using var ownership=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            return action(path);
        }
    }
    public static void Write(string path, Action<Stream> write, Func<string,bool> valid, CancellationToken cancellation=default, Action<string>? checkpoint=null)
    {
        Locked(path,p=>{
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            string temp=p+"."+Guid.NewGuid().ToString("N")+".tmp",backupTemp=temp+".bak";
            void Point(string name){checkpoint?.Invoke(name);cancellation.ThrowIfCancellationRequested();}
            bool IsValid(string candidate)
            {
                try{return valid(candidate);}catch(Exception e) when(e is InvalidDataException or EndOfStreamException or System.Text.Json.JsonException){return false;}
            }
            try
            {
                Point("before-write");
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {write(file);Point("after-write");Point("before-flush");file.Flush(true);Point("after-flush");}
                if(!IsValid(temp))throw new InvalidDataException("New persistence data failed validation; previous data retained.");
                // Only a valid old primary may replace the last known-good
                // backup. Preserve an invalid primary separately for diagnosis;
                // failure before replacement must leave a recoverable generation.
                if(File.Exists(p))
                {
                    if(IsValid(p))
                    {
                        using(var input=File.OpenRead(p))using(var output=new FileStream(backupTemp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
                        Point("before-backup");File.Move(backupTemp,p+".bak",true);Point("after-backup");
                    }
                    else File.Copy(p,p+".corrupt-"+Guid.NewGuid().ToString("N"),false);
                }
                Point("before-replace");File.Move(temp,p,true);
                // Once replaced, cancellation cannot undo a completed transaction.
                checkpoint?.Invoke("after-replace");return true;
            }
            finally {if(File.Exists(temp))File.Delete(temp);if(File.Exists(backupTemp))File.Delete(backupTemp);}
        });
    }
    public static void DeleteLogical(string path)=>Locked(path,p=>{
        File.Delete(p);File.Delete(p+".bak");return true;
    });
    public static byte[] ReadBytes(string path,int maximum)
    {
        using var file=File.OpenRead(path);
        if(file.Length>maximum)throw new InvalidDataException("Persistence file exceeds the supported size limit.");
        byte[] bytes=new byte[(int)file.Length];file.ReadExactly(bytes);
        if(file.ReadByte()!=-1)throw new InvalidDataException("Persistence file changed while reading.");return bytes;
    }
    public static byte[] Read(string path,int maximum,Func<byte[],bool> valid,out bool recovered)
    {
        bool recovery=false;
        byte[] result=Locked(path,p=>{
            byte[]? TryRead(string file)
            {
                if(!File.Exists(file))return null;
                try{var bytes=ReadBytes(file,maximum);return valid(bytes)?bytes:null;}
                catch(Exception e) when(e is InvalidDataException or EndOfStreamException or System.Text.Json.JsonException){return null;}
            }
            var primary=TryRead(p);if(primary!=null)return primary;
            var backup=TryRead(p+".bak");if(backup!=null){recovery=true;return backup;}
            if(!File.Exists(p)&&!File.Exists(p+".bak"))throw new FileNotFoundException("Persistence file and backup are missing.",p);
            throw new InvalidDataException("Persistence file and backup are invalid or incompatible; original files retained.");
        });recovered=recovery;return result;
    }
}
