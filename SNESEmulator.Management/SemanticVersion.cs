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
// Compares semantic versions without integer overflow or lexicographic preview errors.
// Build metadata does not affect precedence; numeric preview identifiers do.
//

namespace SNESEmulator.Management;

/// <summary>SemVer 2.0 precedence, including numeric prerelease identifiers; build metadata is ignored.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>
{
    public string Text { get; }
    public bool IsPreview => preview.Length != 0;
    readonly string[] core,preview;
    SemanticVersion(string text,string[] core,string[] preview){Text=text;this.core=core;this.preview=preview;}
    static bool Numeric(string s)=>s.Length>0&&s.All(c=>c is >= '0' and <= '9');
    static bool Identifier(string s)=>s.Length>0&&s.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-');
    public static bool TryParse(string? value,out SemanticVersion version)
    {
        version=null!;if(string.IsNullOrEmpty(value)||value.Length>128)return false;
        string text=value.StartsWith('v')?value[1..]:value;
        var metadata=text.Split('+');if(metadata.Length>2||(metadata.Length==2&&!metadata[1].Split('.').All(Identifier)))return false;
        var parts=metadata[0].Split('-',2);var core=parts[0].Split('.');
        if(core.Length!=3||core.Any(s=>!Numeric(s)||(s.Length>1&&s[0]=='0')))return false;
        string[] preview=parts.Length==2?parts[1].Split('.'):[];
        if(preview.Any(s=>!Identifier(s)||(Numeric(s)&&s.Length>1&&s[0]=='0')))return false;
        version=new(text,core,preview);return true;
    }
    public static SemanticVersion Parse(string value)=>TryParse(value,out var version)?version:throw new InvalidDataException("Invalid semantic version");
    static int Number(string a,string b)=>a.Length!=b.Length?a.Length.CompareTo(b.Length):string.CompareOrdinal(a,b);
    public int CompareTo(SemanticVersion? other)
    {
        if(other==null)return 1;
        for(int i=0;i<3;i++){int c=Number(core[i],other.core[i]);if(c!=0)return c;}
        if(!IsPreview||!other.IsPreview)return IsPreview==other.IsPreview?0:IsPreview?-1:1;
        for(int i=0;i<Math.Min(preview.Length,other.preview.Length);i++){
            bool a=Numeric(preview[i]),b=Numeric(other.preview[i]);int c=a&&b?Number(preview[i],other.preview[i]):a!=b?(a?-1:1):string.CompareOrdinal(preview[i],other.preview[i]);if(c!=0)return c;
        }
        return preview.Length.CompareTo(other.preview.Length);
    }
}
