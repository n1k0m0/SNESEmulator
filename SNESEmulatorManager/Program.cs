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
// Starts the WPF library Manager and its UI dispatcher.
// The Manager controls a separate emulator process through bounded local IPC.
//

using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using SNESEmulator.Contracts;

namespace SNESEmulatorManager;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];
        string pipe = "SNESEmulator.Manager." + user;
        // Test instances use a separate explicit data-root identity, never the user's live manager.
        if (Environment.GetEnvironmentVariable("SNESEMULATOR_MANAGER_TEST_ID")is { } id)
            pipe += "." + id;
        using var mutex = new Mutex(false, "Local\\" + pipe);
        bool primary;
        try
        {
            primary = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            primary = true;
        }

        if (!primary)
        {
            try
            {
                using var token = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                client.ConnectAsync(token.Token).GetAwaiter().GetResult();
                var hello = Request.Create("Hello");
                Framing.WriteAsync(client, hello, token.Token).GetAwaiter().GetResult();
                var greeting = Framing.ReadAsync<Response>(client, token.Token).GetAwaiter().GetResult();
                if (!Framing.ValidateHello(greeting, hello).Capabilities.Contains("Activate", StringComparer.Ordinal))
                    throw new InvalidDataException("Selected endpoint does not support Manager activation");
                var activation = Request.Create("Activate", args);
                Framing.WriteAsync(client, activation, token.Token).GetAwaiter().GetResult();
                var response = Framing.ReadAsync<Response>(client, token.Token).GetAwaiter().GetResult();
                Framing.ValidateResponse(response, activation);
                return response.Success ? 0 : 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }

        try
        {
            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            var window = new MainWindow(args);
            app.MainWindow = window;
            var server = new IpcServer(async (r, token) =>
            {
                if (r.Command != "Activate")
                    return Response.Fail(r, "UNSUPPORTED", "Activation only");
                var forwarded = r.Payload.Deserialize<string[]>() ?? [];
                if (forwarded.Length > 16 || forwarded.Any(x => x == null || x.Length > 1024))
                    return Response.Fail(r, "INVALID_ARGUMENTS", "Argument budget");
                await app.Dispatcher.InvokeAsync(() =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    window.DeliverArguments(forwarded);
                }, System.Windows.Threading.DispatcherPriority.Normal, token);
                return Response.Ok(r);
            }, pipe, false, ["Activate"]);
            try
            {
                app.Run(window);
            }
            finally
            {
                server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return window.ExitCode;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            try
            {
                string directory = Path.Combine(SNESEmulator.Core.UserDataPaths.GetBaseDirectory(), "manager");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "last-error.log"), e.ToString());
            }
            catch (Exception log)
            {
                Console.Error.WriteLine(log.Message);
            }

            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
