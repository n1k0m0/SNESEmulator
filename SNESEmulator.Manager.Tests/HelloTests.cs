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
// Checks Manager/emulator handshake framing, version agreement and stale-session rejection.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using System.Diagnostics;
using System.IO.Pipes;
using SNESEmulator.Contracts;

internal static class HelloTests
{
    internal static async Task Run(Func<string, Func<Task>, Task> test, Action<bool, string> check)
    {
        await test("M9e Hello and response correlation rejects real wrong-peer greetings", async () =>
        {
            var request = Request.Create("Hello");
            var good = Response.Ok(request, new HelloInfo(IpcProtocol.Version, ["Echo"], Framing.MaximumBytes));
            check(Framing.ValidateHello(good, request).Capabilities.Single() == "Echo", "Valid capabilities");
            foreach (var invalid in new[]
            {
                good with
                {
                    Version = 1
                },
                good with
                {
                    RequestId = Guid.NewGuid().ToString("N")
                },
                Response.Ok(request, new HelloInfo(1, ["Echo"], Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(IpcProtocol.Version, null!, Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(IpcProtocol.Version, ["Echo", "Echo"], Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(IpcProtocol.Version, ["Echo"], Framing.MaximumBytes + 1))
            }

            )
            {
                try
                {
                    _ = Framing.ValidateHello(invalid, request);
                    throw new Exception("Invalid Hello accepted");
                }
                catch (InvalidDataException)
                {
                }
            }

            foreach (bool wrongVersion in new[]
            {
                false,
                true
            }

            )
            {
                using var current = Process.GetCurrentProcess();
                string name = "SNESEmulator." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
                var instance = new InstanceInfo(Environment.ProcessId, Discovery.StartIdentity(current), name, IpcProtocol.Version, Environment.ProcessPath ?? "", Discovery.StartIdentityKind);
                using var stop = new CancellationTokenSource(5000);
                using var fake = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var server = Task.Run(async () =>
                {
                    await fake.WaitForConnectionAsync(stop.Token);
                    var hello = await Framing.ReadAsync<Request>(fake, stop.Token);
                    var response = Response.Ok(hello, new HelloInfo(wrongVersion?1:IpcProtocol.Version, ["Echo"], Framing.MaximumBytes));
                    response = wrongVersion ? response with
                    {
                        Version = 1
                    }

                    : response with
                    {
                        RequestId = Guid.NewGuid().ToString("N")
                    };
                    await Framing.WriteAsync(fake, response, stop.Token);
                    byte[] one = new byte[1];
                    check(await fake.ReadAsync(one, stop.Token) == 0, "Client forwarded a command after wrong Hello");
                });
                try
                {
                    await new IpcClient(instance).SendAsync(Request.Create("Echo"), stop.Token);
                    throw new Exception("Wrong Hello accepted");
                }
                catch (InvalidDataException)
                {
                }

                await server;
            }
        });
    }
}
