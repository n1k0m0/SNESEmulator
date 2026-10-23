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
