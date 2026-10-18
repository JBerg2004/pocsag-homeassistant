using System.Runtime.InteropServices;
using PocsagDecoder.Service;

// Aufruf: pocsag-decoder [Pfad zu options.json]
var options = AddonOptions.Load(args.Length > 0 ? args[0] : AddonOptions.DefaultPath);
using var cts = new CancellationTokenSource();
using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; cts.Cancel(); });
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await new DecoderService(options, new HomeAssistantClient()).RunAsync(cts.Token);
