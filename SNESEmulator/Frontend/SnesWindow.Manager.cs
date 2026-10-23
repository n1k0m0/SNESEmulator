using System.Diagnostics;
using System.Text.Json;
using SNESEmulator.Contracts;
using SNESEmulator.Core;
using SNESEmulator.Management;
using SNESEmulator.Runtime;
using SNESEmulator.SaveStates;

namespace SNESEmulator.Frontend;
public sealed partial class SnesWindow
{
    private long managerCloseAfter;
    internal void ProbeLaunchManager()
    {
        Open(Menu.Main);
        selection = 4;
        Activate(1);
    }

    internal void ProbeNetwork(bool host, int port)
    {
        settings.Host = "localhost";
        settings.Port = port;
        settings.Relay = false;
        BeginConnection(host);
    }

    internal void ProbeMenuVolume()
    {
        Open(Menu.Settings);
        selection = 7;
        Activate(1);
        Open(Menu.None);
    }

    internal string? ManagerPipe => managerIpc?.PipeName;

    private IpcServer? managerIpc;
    private bool managerPaused;
    private long settingsRevision = 1;
    private void StartManagerIpc()
    {
        try
        {
            managerIpc = new(HandleManagerRequest);
        }
        catch (Exception e)
        {
            status = "MANAGER IPC UNAVAILABLE: " + e.Message;
        }
    }

    private void LaunchManager()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Manager", "SNESEmulatorManager.exe");
        if (!File.Exists(exe))
        {
            status = "MANAGER NOT INSTALLED";
            return;
        }

        try
        {
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            };
            start.ArgumentList.Add("--connect");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start)?.Dispose();
        }
        catch (Exception e)
        {
            status = "MANAGER START FAILED: " + e.Message;
        }
    }

    private async Task<T> OnUi<T>(Func<T> action, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        await pending.PostAsync(() =>
        {
            if (token.IsCancellationRequested)
                return;
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        }, token);
        return await completion.Task.ConfigureAwait(false);
    }

    private StatusInfo ManagerStatus()
    {
        var s = worker.Status;
        return new(Environment.ProcessId, s.Epoch, s.Field, s.Title, s.CartridgeHash, previewCartridge?.SourcePath ?? "", s.Paused, s.Turbo, menu != Menu.None, client.IsConnected ? "CLIENT" : server.IsRunning ? "HOST" : "LOCAL", settingsRevision, s.Fault);
    }

    private SettingsInfo ManagerSettings() => new(settingsRevision, JsonSerializer.SerializeToElement(settings), typeof(EmulatorSettings).GetProperties().Where(p => p.CanWrite).ToDictionary(p => p.Name, p => new[] { "Host", "Port", "RelayHost", "RelayPort", "ConnectionId", "PlayerName", "Relay", "Observer" }.Contains(p.Name) ? (client.IsConnected || server.IsRunning ? "LOCKED: active network session" : "NEXT CONNECTION") : "IMMEDIATE"), settings.PersistenceNotice);
    private async Task<Response> HandleManagerRequest(Request r, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        try
        {
            return await (await OnUi(() => BeginManagerRequest(r, token), token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Response.Fail(r, "CANCELLED", "Operation cancelled; committed changes may have completed");
        }
        catch (Exception e)
        {
            return Response.Fail(r, e is InvalidDataException ? "INVALID_DATA" : "FAILED", e.Message[..Math.Min(512, e.Message.Length)]);
        }
    }

    private Task<Response> BeginManagerRequest(Request r, CancellationToken token)
    {
        Task<Response> Result(object? data = null) => Task.FromResult(Response.Ok(r, data));
        if (closing)
            return Task.FromResult(Response.Fail(r, "CLOSING", "Emulator is closing"));
        switch (r.Command)
        {
            case "Close":
                managerCloseAfter = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                return Result();
            case "Status":
                return Result(ManagerStatus());
            case "Settings":
                return Result(ManagerSettings());
            case "ApplySettings":
                var apply = r.Payload.Deserialize<ApplySettings>(Framing.Options) ?? throw new InvalidDataException("Settings required");
                if (apply.ExpectedRevision != settingsRevision)
                    return Task.FromResult(Response.Fail(r, "REVISION_CONFLICT", "Settings changed in emulator; reload before applying"));
                var candidate = apply.Settings.Deserialize<EmulatorSettings>(Framing.Options) ?? throw new InvalidDataException("Settings required");
                string before = JsonSerializer.Serialize(candidate);
                candidate.Validate();
                if (before != JsonSerializer.Serialize(candidate))
                    throw new InvalidDataException("Settings are outside supported ranges or contain invalid bindings");
                foreach (var effect in ManagerSettings().Effects.Where(e => e.Value.StartsWith("LOCKED")))
                    if (apply.Settings.GetProperty(effect.Key).GetRawText() != JsonSerializer.SerializeToElement(settings).GetProperty(effect.Key).GetRawText())
                        return Task.FromResult(Response.Fail(r, "NETWORK_CLIENT_LOCKED", "Leave active network session before editing network settings"));
                bool fullscreen = candidate.Fullscreen != settings.Fullscreen;
                settings = candidate;
                settings.GamepadControls ??= GamepadMapping.Defaults();
                foreach (var b in GamepadMapping.Defaults())
                    settings.GamepadControls.TryAdd(b.Key, b.Value);
                ResetLocalInput();
                if (fullscreen)
                    ToggleFullscreen();
                SaveSettings();
                return Result(ManagerSettings());
            case "Mode":
                if (client.IsConnected)
                    return Task.FromResult(Response.Fail(r, "NETWORK_CLIENT_LOCKED", "Host owns hardware mode"));
                managerPaused = r.Payload.GetProperty("Paused").GetBoolean();
                turbo = r.Payload.GetProperty("Turbo").GetBoolean();
                sentPause = null;
                sentTurbo = null;
                return ApplyManagerMode(r, token);
            case "LoadRom":
            case "LoadState":
            case "SaveState":
            case "Reset":
            case "StatePreview":
            case "States":
            case "DeleteState":
            case "RecoverState":
            case "ImportState":
            case "ExportState":
                if (client.IsConnected)
                    return Task.FromResult(Response.Fail(r, "NETWORK_CLIENT_LOCKED", "Host owns cartridges and savestates"));
                if (mediaBusy)
                    return Task.FromResult(Response.Fail(r, "BUSY", "Media operation already pending"));
                if (r.Command == "States")
                    return Task.Run(() => Response.Ok(r, StateCatalog.List(UserDataPaths.GetSaveDirectory())), token);
                PathCommand? path = r.Command == "Reset" ? null : r.Payload.Deserialize<PathCommand>(Framing.Options);
                if (path != null && (path.Path.Length is < 1 or > 1024 || path.Path.Any(char.IsControl)))
                    throw new InvalidDataException("Invalid path");
                if (path?.ExpectedEpoch > 0 && path.ExpectedEpoch != worker.Status.Epoch)
                    return Task.FromResult(Response.Fail(r, "STALE_EPOCH", "Machine changed; refresh before retrying"));
                var cart = previewCartridge;
                mediaBusy = true;
                ResetLocalInput();
                var operation = RunManagerMedia(r, path, cart, token);
                mediaTask = operation;
                return operation;
            default:
                return Task.FromResult(Response.Fail(r, "UNSUPPORTED", "Unknown command: " + r.Command));
        }
    }

    private async Task<Response> ApplyManagerMode(Request r, CancellationToken token)
    {
        await worker.SetModeAsync(menu != Menu.None || mediaBusy || managerPaused, turbo, token);
        return await OnUi(() => Response.Ok(r, ManagerStatus()), token);
    }

    private async Task<Response> RunManagerMedia(Request r, PathCommand? path, Cartridge? cart, CancellationToken token)
    {
        Response result;
        Cartridge? installed = null;
        bool installationCommitted = false;
        try
        {
            await worker.SetModeAsync(true, turbo, token);
            token.ThrowIfCancellationRequested();
            object? value = null;
            await Task.Run(async () =>
            {
                switch (r.Command)
                {
                    case "LoadRom":
                        var image = AtomicFile.ReadBytes(path!.Path, 8 * 1024 * 1024 + 512);
                        installed = new Cartridge(image, path.Path);
                        await battery.SwitchAsync(installed.Hash, installed.Sram.Length, bytes => worker.InsertAsync(image, bytes, path.Path, token));
                        installationCommitted = true;
                        break;
                    case "Reset":
                        await worker.ResetAsync(token);
                        break;
                    case "LoadState":
                        if (cart == null)
                            throw new InvalidOperationException("Load matching cartridge first");
                        byte[] compressed;
                        if (path!.Recover)
                        {
                            compressed = StateSerializer.ReadForLoad(cart, path.Path, out bool recovered);
                            if (recovered)
                                pending.SetStatus("STATE RECOVERED FROM VALIDATED BACKUP; DAMAGED FILE RETAINED");
                        }
                        else
                            compressed = AtomicFile.ReadBytes(path.Path, StateSerializer.MaximumCompressedBytes);
                        await worker.LoadAsync(compressed, token);
                        break;
                    case "SaveState":
                        if (cart == null)
                            throw new InvalidOperationException("Load a cartridge first");
                        byte[] raw = await worker.CaptureAsync(token);
                        token.ThrowIfCancellationRequested();
                        StateSerializer.SaveCaptured(raw, path!.Path, cart);
                        value = path.Path;
                        break;
                    case "StatePreview":
                        if (cart == null)
                            throw new InvalidOperationException("Load matching cartridge first");
                        value = StateCatalog.Preview(path!.Path, cart, path.Recover);
                        break;
                    case "DeleteState":
                        AtomicFile.DeleteLogical(path!.Path);
                        break;
                    case "RecoverState":
                        if (cart == null)
                            throw new InvalidOperationException("Load matching cartridge first");
                        StateCatalog.Recover(path!.Path, cart);
                        break;
                    case "ImportState":
                    case "ExportState":
                        throw new NotSupportedException("Use validated manager import/export with matching local cartridge");
                }
            }, token);
            result = Response.Ok(r, value);
        }
        catch (OperationCanceledException)
        {
            result = Response.Fail(r, "CANCELLED", "Cancelled; committed transaction may have completed");
        }
        catch (Exception e)
        {
            result = Response.Fail(r, e is InvalidDataException ? "INVALID_DATA" : "FAILED", e.Message[..Math.Min(512, e.Message.Length)]);
        }

        try
        {
            return await OnUi(() =>
            {
                if (installationCommitted && installed != null && worker.Status.CartridgeHash == installed.Hash)
                    previewCartridge = installed;
                mediaBusy = false;
                sentPause = null;
                sentTurbo = null;
                if (result.Success && r.Command is "LoadRom" or "LoadState" or "Reset")
                    Open(Menu.None);
                status = result.Success ? "MANAGER " + r.Command.ToUpperInvariant() + " COMPLETED" : result.Code + ": " + result.Message;
                return result;
            }, lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return result;
        }
    }
}
