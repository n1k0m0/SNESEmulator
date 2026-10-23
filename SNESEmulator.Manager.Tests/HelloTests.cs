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
            var good = Response.Ok(request, new HelloInfo(1, ["Echo"], Framing.MaximumBytes));
            check(Framing.ValidateHello(good, request).Capabilities.Single() == "Echo", "Valid capabilities");
            foreach (var invalid in new[]
            {
                good with
                {
                    Version = 2
                },
                good with
                {
                    RequestId = Guid.NewGuid().ToString("N")
                },
                Response.Ok(request, new HelloInfo(2, ["Echo"], Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(1, null!, Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(1, ["Echo", "Echo"], Framing.MaximumBytes)),
                Response.Ok(request, new HelloInfo(1, ["Echo"], Framing.MaximumBytes + 1))
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
                var instance = new InstanceInfo(Environment.ProcessId, current.StartTime.ToUniversalTime().Ticks, name, 1, Environment.ProcessPath ?? "");
                using var stop = new CancellationTokenSource(5000);
                using var fake = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var server = Task.Run(async () =>
                {
                    await fake.WaitForConnectionAsync(stop.Token);
                    var hello = await Framing.ReadAsync<Request>(fake, stop.Token);
                    var response = Response.Ok(hello, new HelloInfo(1, ["Echo"], Framing.MaximumBytes));
                    response = wrongVersion ? response with
                    {
                        Version = 2
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
