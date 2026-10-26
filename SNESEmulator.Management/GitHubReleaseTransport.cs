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
// Restricts official release redirects to approved HTTPS GitHub asset hosts.
// Custom update origins do not inherit this special CDN redirect permission.
//

using System.Net;
namespace SNESEmulator.Management;

/// <summary>Shared setup/ZIP transport. Only an explicit official release-asset origin admits GitHub CDN redirects.</summary>
public static class GitHubReleaseTransport
{
    static Uri Https(string text){
        if(!Uri.TryCreate(text,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0)throw new InvalidDataException("Release download requires uncredentialed HTTPS");return uri;
    }
    public static bool IsOfficialAsset(Uri uri,string repository){
        OfficialReleaseSettings.ValidateRepository(repository);
        if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.Host!="github.com"||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.Query.Length!=0)return false;
        string[] pieces=Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Split('/');var repo=repository.Split('/');
        return pieces.Length==6&&pieces[0]==repo[0]&&pieces[1]==repo[1]&&pieces[2]=="releases"&&pieces[3]=="download"&&SemanticVersion.TryParse(pieces[4],out _)&&pieces[5].Length>0&&pieces[5] is not ("." or "..");
    }
    public static async Task<HttpResponseMessage> OpenAsync(HttpClient client,Uri initial,string repository,CancellationToken token)
    {
        if(!IsOfficialAsset(initial,repository))throw new InvalidDataException("Not an official release asset URL");
        Uri uri=initial;
        for(int hop=0;hop<5;hop++){
            var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
            bool redirect=response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
            if(!redirect){try{response.EnsureSuccessStatusCode();return response;}catch{response.Dispose();throw;}}
            var next=response.Headers.Location;response.Dispose();if(next==null)throw new InvalidDataException("Missing release redirect");
            uri=Https(new Uri(uri,next).AbsoluteUri);
            if(uri.Host=="github.com"){
                if(!string.Equals(uri.AbsoluteUri,initial.AbsoluteUri,StringComparison.Ordinal))throw new InvalidDataException("Release redirect changed the selected asset");
            }
            else if(uri.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))throw new InvalidDataException("Untrusted release redirect");
        }
        throw new InvalidDataException("Too many release redirects");
    }
}
