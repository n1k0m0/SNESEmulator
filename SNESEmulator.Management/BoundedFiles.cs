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
// Reads bounded metadata and previews from potentially malformed local files.
// Length/depth limits are checked before allocating or exposing their contents.
//

namespace SNESEmulator.Management;
public static class BoundedFiles
{
    public static IEnumerable<string> Walk(string root, CancellationToken token = default)
    {
        if (!Directory.Exists(root))
            yield break;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((Path.GetFullPath(root), 0));
        int entries = 0, directories = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (++directories > 1024 || folder.Depth > 32)
                throw new InvalidDataException("Directory scan budget exceeded (1024 directories / depth32)");
            using var iterator = Directory.EnumerateFileSystemEntries(folder.Path).GetEnumerator();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                bool next;
                try
                {
                    next = iterator.MoveNext();
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
                catch (DirectoryNotFoundException)
                {
                    break;
                }

                if (!next)
                    break;
                if (++entries > 10000)
                    throw new InvalidDataException("Directory entry budget10000 exceeded");
                string path = iterator.Current;
                FileAttributes attr;
                try
                {
                    attr = File.GetAttributes(path);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attr & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attr & FileAttributes.Directory) != 0)
                    pending.Push((path, folder.Depth + 1));
                else
                    yield return path;
            }
        }
    }
}
