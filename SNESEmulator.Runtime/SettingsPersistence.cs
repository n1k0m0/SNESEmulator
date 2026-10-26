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
// Persists validated settings with recovery and explicit failure reporting.
// An unsuccessful write does not silently advance the saved revision.
//

using SNESEmulator.Core;

namespace SNESEmulator.Runtime;
/// <summary>At most one active settings write and one latest owned JSON snapshot.</summary>
public sealed class SettingsPersistence(Action<string> error, Action<string>? save = null)
{
    private readonly object gate = new();
    private readonly Action<string> write = save ?? (json => WriteOrdered(json));
    public static void WriteOrdered(string json, string? path = null)
    {
        string target = Path.GetFullPath(path ?? UserDataPaths.GetSettingsPath());
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(target.ToUpperInvariant())));
        // Serialize independently launched menu/Manager writers before opening
        // AtomicFile's transaction lock. A slow holder is an explicit bounded
        // failure; callers must retain/report it, never silently drop settings.
        // Named-mutex construction itself can fail in the OS/runtime backend;
        // that exception deliberately propagates with its original details.
        using var mutex = new Mutex(false, "SNESEmulator.Settings." + identity);
        bool owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
                throw new IOException("Another settings write exceeded the 2 second serialization budget");
            EmulatorSettings.SaveSerialized(json, target);
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    private string? next;
    private bool running;
    private Task pending = Task.CompletedTask;
    public Task Pending
    {
        get
        {
            lock (gate)
                return pending;
        }
    }

    public void Queue(string json)
    {
        lock (gate)
        {
            next = json;
            if (running)
                return;
            running = true;
            pending = Task.Run(() =>
            {
                while (true)
                {
                    string snapshot;
                    lock (gate)
                    {
                        if (next == null)
                        {
                            running = false;
                            return;
                        }

                        snapshot = next;
                        next = null;
                    }

                    try
                    {
                        write(snapshot);
                    }
                    catch (Exception e)
                    {
                        error("SETTINGS SAVE FAILED: " + e.Message);
                    }
                }
            });
        }
    }
}
