// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Console;
using Groundwork.Console.Commands;
using Groundwork.Core.Connections;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;
using ArduPilot = Groundwork.Core.ArduPilot;

// Parse args before creating the logger so --log-level can take effect.
var linkDescriptors = new List<string>();
int? remotePort = null;
var logLevel = LogLevel.Information;
for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--link", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        linkDescriptors.Add(args[++i]);
    else if (
        args[i].Equals("--repl-remote", StringComparison.OrdinalIgnoreCase)
        && i + 1 < args.Length
    )
    {
        if (!int.TryParse(args[++i], out var port))
        {
            Console.Error.WriteLine("--repl-remote requires a port number");
            return 1;
        }

        remotePort = port;
    }
    else if (
        args[i].Equals("--log-level", StringComparison.OrdinalIgnoreCase)
        && i + 1 < args.Length
    )
    {
        if (!Enum.TryParse<LogLevel>(args[++i], ignoreCase: true, out logLevel))
        {
            Console.Error.WriteLine(
                "--log-level must be one of: Trace, Debug, Information, Warning, Error, Critical"
            );
            return 1;
        }
    }
}

if (linkDescriptors.Count == 0)
    linkDescriptors.Add("udpin:14550");

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(logLevel);
});

var logger = loggerFactory.CreateLogger("Groundwork");
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// -- Parameter metadata resolution --

var metadataCacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Groundwork",
    "param-metadata"
);
var httpClient = new HttpClient();
var metadataMemoryCache = new ParamMetadataCache();
var metadataDiskCache = new ArduPilot.ParamMetadataDiskCache(metadataCacheDir);
var metadataFetcher = new ArduPilot.ParamMetadataFetcher(
    httpClient,
    metadataMemoryCache,
    loggerFactory.CreateLogger<ArduPilot.ParamMetadataFetcher>(),
    metadataDiskCache
);

var registry = new VehicleRegistry(
    loggerFactory,
    new Dictionary<MAVLink.MAV_DATA_STREAM, int>
    {
        [MAVLink.MAV_DATA_STREAM.EXTRA1] = 10,
        [MAVLink.MAV_DATA_STREAM.POSITION] = 5,
        [MAVLink.MAV_DATA_STREAM.EXTENDED_STATUS] = 2,
        [MAVLink.MAV_DATA_STREAM.RC_CHANNELS] = 2,
        [MAVLink.MAV_DATA_STREAM.EXTRA2] = 2,
        [MAVLink.MAV_DATA_STREAM.EXTRA3] = 2,
    },
    metadataFetcher
);

var channelRegistry = new Groundwork.Core.Channels.MavChannelRegistry();
var tlogDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Groundwork",
    "tlogs"
);
Directory.CreateDirectory(tlogDir);
var tlogPath = Path.Combine(tlogDir, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.tlog");
var tlogWriter = new Groundwork.Core.Channels.TlogWriter(tlogPath);
await using var links = new LinkManager(channelRegistry, registry, loggerFactory, tlogWriter);

foreach (var descriptor in linkDescriptors)
{
    try
    {
        await links.AddAsync(descriptor, cts.Token);
    }
    catch (FormatException ex)
    {
        logger.LogError("{Message}", ex.Message);
        return 1;
    }
}

var discoverySub = registry.Discovered.Subscribe(v =>
    Console.WriteLine(
        $"Vehicle discovered: sysid {v.SysId} ({v.CanonicalState.Heartbeat.Value.Type})"
    )
);

// -- REPL setup --

var commands = new CommandRegistry();
var commandCtx = new CommandContext(
    registry,
    channelRegistry,
    links,
    loggerFactory,
    Console.Out,
    cts.Token
);

new ArmModule().Register(commands);
new ModeModule().Register(commands);
new FlightModule().Register(commands);
new ParamModule().Register(commands);
new RcModule(cts.Token).Register(commands);
new LinkModule().Register(commands);
new VehicleModule().Register(commands);
new WpModule().Register(commands);
new FtpModule().Register(commands);
new DiagModule().Register(commands);
new OverviewModule().Register(commands);
#if LOSSY_LINK
new LossyModule().Register(commands);
#endif
commands.Register("help", new HelpCommand(commands));

// -- Remote REPL socket (opt-in via --repl-remote <port>) --

ReplServer? remoteServer = remotePort.HasValue
    ? new ReplServer(remotePort.Value, commands, commandCtx, loggerFactory)
    : null;

Console.WriteLine("Type 'help' for commands, 'exit' to quit.");

// -- REPL loop --

while (!cts.Token.IsCancellationRequested)
{
    Console.Write("> ");

    // Console.ReadLine() blocks and can't be cancelled. Run it on the
    // thread pool so we can bail when Ctrl+C fires the token.
    string? line;
    try
    {
        line = await Task.Run(Console.ReadLine).WaitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    if (line is null)
    {
        if (remoteServer is not null)
        {
            // stdin gone but remote REPL is active -- wait for Ctrl+C.
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException) { }
            break;
        }

        break; // EOF
    }

    line = line.Trim();
    if (line.Length == 0)
        continue;

    if (
        line.Equals("exit", StringComparison.OrdinalIgnoreCase)
        || line.Equals("quit", StringComparison.OrdinalIgnoreCase)
    )
    {
        break;
    }

    var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var match = commands.Resolve(tokens);

    if (match is null)
    {
        var subs = commands.FindSubcommands(tokens);
        if (subs.Count > 0)
        {
            foreach (var kv in subs)
                Console.WriteLine($"  {kv.Value.Usage, -25} {kv.Value.Description}");
        }
        else
        {
            Console.WriteLine($"Unknown command: {tokens[0]}. Type 'help' for commands.");
        }

        continue;
    }

    try
    {
        await match.Value.Command.ExecuteAsync(match.Value.Args, commandCtx);
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Command timed out");
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

discoverySub.Dispose();

if (remoteServer is not null)
    await remoteServer.DisposeAsync();

logger.LogInformation("Shutting down");
return 0;
