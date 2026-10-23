using System.Text;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SNESEmulator.Contracts;
public static class Discovery
{
    public static string DirectoryPath => Path.Combine(Environment.GetEnvironmentVariable("SNESEMULATOR_IPC_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SNESEmulator", "ipc"), "instances");

    public static bool IsAlive(InstanceInfo info)
    {
        try
        {
            using var p = Process.GetProcessById(info.ProcessId);
            return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == info.StartTicks && info.Version == 1 && info.PipeName != null && info.PipeName.StartsWith("SNESEmulator." + info.ProcessId + ".", StringComparison.Ordinal) && info.PipeName.Length < 128;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadBounded(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 8192)
            throw new InvalidDataException("Discovery budget");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("Discovery changed");
        return bytes;
    }

    public static IReadOnlyList<InstanceInfo> List()
    {
        if (!Directory.Exists(DirectoryPath))
            return[];
        var list = new List<InstanceInfo>();
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.json").Take(256))
        {
            try
            {
                var f = new FileInfo(path);
                if (f.Length > 8192)
                    continue;
                var info = JsonSerializer.Deserialize<InstanceInfo>(ReadBounded(path), Framing.Options);
                if (info != null && IsAlive(info))
                    list.Add(info);
            }
            catch (Exception e)when (e is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }

        return list.OrderBy(x => x.ProcessId).ToArray();
    }
}

public sealed class IpcClient(InstanceInfo instance)
{
    public async Task<Response> SendAsync(Request request, CancellationToken token = default)
    {
        if (!Discovery.IsAlive(instance))
            throw new IOException("Selected emulator instance ended or PID was reused");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        token = deadline.Token;
        using var pipe = new NamedPipeClientStream(".", instance.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(token);
        var hello = Request.Create("Hello");
        await Framing.WriteAsync(pipe, hello, token);
        var answer = await Framing.ReadAsync<Response>(pipe, token);
        if (answer.Version != 1 || answer.RequestId != hello.RequestId)
            throw new InvalidDataException("Hello correlation mismatch");
        if (!answer.Success)
            throw new IOException(answer.Code + ": " + answer.Message);
        await Framing.WriteAsync(pipe, request, token);
        var result = await Framing.ReadAsync<Response>(pipe, token);
        if (result.RequestId != request.RequestId || result.Version != 1)
            throw new InvalidDataException("Response correlation mismatch");
        return result;
    }
}

public sealed class IpcServer : IAsyncDisposable
{
    private readonly Func<Request, CancellationToken, Task<Response>> handler;
    private readonly CancellationTokenSource stop = new();
    private readonly Task[] loops;
    private readonly string? discovery;
    public string PipeName { get; }

    public IpcServer(Func<Request, CancellationToken, Task<Response>> handler, string? pipeName = null, bool publish = true)
    {
        this.handler = handler;
        PipeName = pipeName ?? "SNESEmulator." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        if (publish)
        {
            Directory.CreateDirectory(Discovery.DirectoryPath);
            discovery = Path.Combine(Discovery.DirectoryPath, Environment.ProcessId + ".json");
            using var p = Process.GetCurrentProcess();
            var info = new InstanceInfo(Environment.ProcessId, p.StartTime.ToUniversalTime().Ticks, PipeName, 1, Environment.ProcessPath ?? "");
            string tmp = discovery + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(info));
            File.Move(tmp, discovery, true);
        }

        loops = Enumerable.Range(0, 4).Select(_ => Task.Run(Loop)).ToArray();
    }

    private async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var life = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                life.CancelAfter(TimeSpan.FromSeconds(30));
                bool hello = false;
                for (int i = 0; i < 32 && pipe.IsConnected; i++)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    var token = deadline.Token;
                    var r = await Framing.ReadAsync<Request>(pipe, token);
                    Framing.Validate(r);
                    Response response;
                    if (r.Version != 1)
                        response = Response.Fail(r, "PROTOCOL_MISMATCH", "IPC version1 required");
                    else if (!hello && r.Command != "Hello")
                        response = Response.Fail(r, "HELLO_REQUIRED", "Hello must be first");
                    else if (r.Command == "Hello")
                    {
                        hello = true;
                        response = Response.Ok(r, new { Version = 1, Capabilities = new[] { "Status", "Settings", "ApplySettings", "LoadRom", "LoadState", "SaveState", "Reset", "Mode", "States", "StatePreview", "DeleteState", "RecoverState" }, MaximumBytes = Framing.MaximumBytes });
                    }
                    else
                    {
                        // A sequential client sends no further bytes until the response. EOF cancels
                        // its pending operation promptly; pipelining is an explicit protocol violation.
                        using var probeStop = CancellationTokenSource.CreateLinkedTokenSource(token);
                        Task<Response> command = handler(r, token);
                        byte[] probeByte = new byte[1];
                        var probe = pipe.ReadAsync(probeByte, probeStop.Token).AsTask();
                        try
                        {
                            if (await Task.WhenAny(command, probe) == probe)
                            {
                                deadline.Cancel();
                                try
                                {
                                    await command;
                                }
                                catch (Exception)
                                {
                                }

                                break;
                            }

                            response = await command;
                        }
                        catch (OperationCanceledException)
                        {
                            response = Response.Fail(r, "CANCELLED", "Request cancelled or timed out; a committed transaction may already have completed");
                        }
                        catch (Exception e)
                        {
                            response = Response.Fail(r, "FAILED", e.Message[..Math.Min(e.Message.Length, 512)]);
                        }
                        finally
                        {
                            probeStop.Cancel();
                            try
                            {
                                await probe;
                            }
                            catch (Exception e)when (e is OperationCanceledException or IOException)
                            {
                            }
                        }
                    }

                    await Framing.WriteAsync(pipe, response, token);
                    if (!hello || r.Version != 1)
                        break;
                }
            }
            catch (Exception e)when (e is IOException or OperationCanceledException or InvalidDataException or JsonException or DecoderFallbackException or UnauthorizedAccessException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try
        {
            await Task.WhenAll(loops);
        }
        finally
        {
            if (discovery != null)
            {
                try
                {
                    var info = JsonSerializer.Deserialize<InstanceInfo>(File.ReadAllBytes(discovery));
                    if (info?.PipeName == PipeName)
                        File.Delete(discovery);
                }
                catch (IOException)
                {
                }
            }

            stop.Dispose();
        }
    }
}
