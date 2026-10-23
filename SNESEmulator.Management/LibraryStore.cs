using Microsoft.Data.Sqlite;
using SNESEmulator.Core;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SNESEmulator.Manager.Tests")]
namespace SNESEmulator.Management;
public sealed record GameItem(string Hash, string Title, string Path, bool Favorite, string Notes, string? LastPlayed, bool Exists);
public sealed class LibraryStore : IDisposable
{
    internal Action<int>? AfterScanInsert { get; set; }

    private readonly SemaphoreSlim gate = new(1);
    private readonly string path;
    public string? Notice { get; private set; }
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Pooling = false
    }.ToString();

    public LibraryStore(string path)
    {
        this.path = Path.GetFullPath(path);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(ConnectionString);
        c.Open();
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        if ((string? )cmd.ExecuteScalar() != "ok")
            throw new SqliteException("Database integrity failure", 11);
        cmd.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS Game(Hash TEXT PRIMARY KEY,Title TEXT NOT NULL,Favorite INTEGER NOT NULL DEFAULT 0,Notes TEXT NOT NULL DEFAULT '',LastPlayed TEXT); CREATE TABLE IF NOT EXISTS Rom(Path TEXT PRIMARY KEY COLLATE NOCASE,Hash TEXT NOT NULL REFERENCES Game(Hash)); CREATE TABLE IF NOT EXISTS StateMetadata(Path TEXT PRIMARY KEY COLLATE NOCASE,Name TEXT NOT NULL,GameHash TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    private void Ready()
    {
        if (Notice != null)
            throw new InvalidDataException(Notice);
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                try
                {
                    Initialize();
                }
                catch (SqliteException)
                {
                    Notice = "DATABASE DAMAGED. Original retained. Choose explicit recovery before editing.";
                }
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<GameItem>> ListAsync(string search = "", CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run<IReadOnlyList<GameItem>>(() =>
            {
                Ready();
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT g.Hash,g.Title,r.Path,g.Favorite,g.Notes,g.LastPlayed FROM Game g JOIN Rom r ON r.Hash=g.Hash WHERE g.Title LIKE $q OR g.Notes LIKE $q ORDER BY g.LastPlayed DESC,g.Title,r.Path LIMIT 10000";
                cmd.Parameters.AddWithValue("$q", "%" + search[..Math.Min(search.Length, 256)] + "%");
                using var r = cmd.ExecuteReader();
                var result = new List<GameItem>();
                while (r.Read())
                {
                    token.ThrowIfCancellationRequested();
                    string file = r.GetString(2);
                    result.Add(new(r.GetString(0), r.GetString(1), file, r.GetInt32(3) != 0, r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), File.Exists(file)));
                }

                return result;
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> ScanAsync(string root, CancellationToken token = default)
    {
        // One scanner, <=10000 files, <=64 levels; skip reparse points and inaccessible folders.
        await gate.WaitAsync(token);
        try
        {
            Ready();
            return await Task.Run(() =>
            {
                using var c = Open();
                using var tx = c.BeginTransaction();
                int count = 0, visited = 0;
                foreach (string file in BoundedFiles.Walk(root, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (++visited > 10000)
                        throw new InvalidDataException("Scan budget10000 files exceeded; transaction rolled back. Select a narrower folder.");
                    if (!new[]
                    {
                        ".sfc",
                        ".smc"
                    }.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        continue;
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length is < 32768 or > 8389120)
                            continue;
                        var cart = new Cartridge(AtomicFile.ReadBytes(file, 8 * 1024 * 1024 + 512), file);
                        using var cmd = c.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT OR IGNORE INTO Game(Hash,Title)VALUES($h,$t); INSERT INTO Rom(Path,Hash)VALUES($p,$h) ON CONFLICT(Path) DO UPDATE SET Hash=excluded.Hash";
                        cmd.Parameters.AddWithValue("$h", cart.Hash);
                        cmd.Parameters.AddWithValue("$t", cart.Title);
                        cmd.Parameters.AddWithValue("$p", Path.GetFullPath(file));
                        cmd.ExecuteNonQuery();
                        count++;
                        AfterScanInsert?.Invoke(count);
                    }
                    catch (Exception e)when (e is IOException or NotSupportedException or InvalidDataException)
                    {
                    }
                }

                token.ThrowIfCancellationRequested();
                tx.Commit();
                return count;
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task UpdateAsync(string hash, bool favorite, string notes, bool played = false, CancellationToken token = default)
    {
        if (notes.Length > 8192)
            throw new ArgumentException("Notes maximum8192 characters");
        await gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                Ready();
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Game SET Favorite=$f,Notes=$n,LastPlayed=CASE WHEN $played=1 THEN $now ELSE LastPlayed END WHERE Hash=$h";
                cmd.Parameters.AddWithValue("$f", favorite);
                cmd.Parameters.AddWithValue("$n", notes);
                cmd.Parameters.AddWithValue("$h", hash);
                cmd.Parameters.AddWithValue("$played", played);
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RelinkAsync(GameItem item, string file, CancellationToken token = default)
    {
        var cart = await Task.Run(() => new Cartridge(AtomicFile.ReadBytes(file, 8 * 1024 * 1024 + 512)), token);
        if (cart.Hash != item.Hash)
            throw new InvalidDataException("Replacement cartridge differs from canonical content");
        await gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                Ready();
                using var c = Open();
                using var tx = c.BeginTransaction();
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Rom WHERE Path=$old; INSERT INTO Rom(Path,Hash)VALUES($p,$h) ON CONFLICT(Path) DO UPDATE SET Hash=excluded.Hash";
                cmd.Parameters.AddWithValue("$old", item.Path);
                cmd.Parameters.AddWithValue("$p", Path.GetFullPath(file));
                cmd.Parameters.AddWithValue("$h", item.Hash);
                cmd.ExecuteNonQuery();
                tx.Commit();
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecoverAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                if (Notice == null)
                    return;
                string retained = path + ".corrupt-" + Guid.NewGuid().ToString("N");
                File.Move(path, retained);
                try
                {
                    Initialize();
                    Notice = null;
                }
                catch
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(retained, path);
                    throw;
                }
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetStateMetadataAsync(string file, string name, string hash, CancellationToken token = default)
    {
        if (name.Length > 256 || hash.Length > 64)
            throw new ArgumentException("Metadata budget");
        await gate.WaitAsync(token);
        try
        {
            await Task.Run(() =>
            {
                Ready();
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO StateMetadata(Path,Name,GameHash)VALUES($p,$n,$h) ON CONFLICT(Path) DO UPDATE SET Name=excluded.Name,GameHash=excluded.GameHash";
                cmd.Parameters.AddWithValue("$p", Path.GetFullPath(file));
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$h", hash);
                cmd.ExecuteNonQuery();
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Dictionary<string, (string Name, string Hash)>> StateMetadataAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                Ready();
                using var c = Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Path,Name,GameHash FROM StateMetadata LIMIT 1000";
                using var reader = cmd.ExecuteReader();
                var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                    result[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
                return result;
            }, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}
