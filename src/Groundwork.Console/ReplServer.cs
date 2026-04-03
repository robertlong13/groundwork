// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Groundwork.Console.Commands;
using Groundwork.Core.Channels;
using Groundwork.Core.Vehicles;
using Microsoft.Extensions.Logging;

namespace Groundwork.Console;

/// <summary>
/// Provides a TCP server that accepts remote REPL connections.
/// </summary>
/// <remarks>
/// Each connection gets a line-oriented command session backed by the same
/// <see cref="CommandRegistry"/> as the interactive console.
/// </remarks>
public sealed class ReplServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CommandRegistry _commands;
    private readonly VehicleRegistry _vehicleRegistry;
    private readonly MavChannelRegistry _channelRegistry;
    private readonly LinkManager _links;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ReplServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Task, byte> _clientTasks = new();
    private readonly Task _acceptLoop;

    public ReplServer(
        int port,
        CommandRegistry commands,
        VehicleRegistry vehicleRegistry,
        MavChannelRegistry channelRegistry,
        LinkManager links,
        ILoggerFactory loggerFactory
    )
    {
        _commands = commands;
        _vehicleRegistry = vehicleRegistry;
        _channelRegistry = channelRegistry;
        _links = links;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ReplServer>();

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _logger.LogInformation("Remote REPL listening on port {Port}", port);

        _acceptLoop = RunAcceptLoopAsync(_cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            await _acceptLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        await Task.WhenAll(_clientTasks.Keys).ConfigureAwait(false);
        _cts.Dispose();
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                var task = HandleClientAsync(client, ct);
                _clientTasks.TryAdd(task, 0);
                _ = task.ContinueWith(t => _clientTasks.TryRemove(t, out _), TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown.
        }
        catch (ObjectDisposedException)
        {
            // Listener stopped.
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
            // Listener stopped during accept.
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) { AutoFlush = true };

            var ctx = new CommandContext(
                _vehicleRegistry,
                _channelRegistry,
                _links,
                _loggerFactory,
                writer,
                ct
            );

            _logger.LogDebug(
                "Remote client connected from {Endpoint}",
                client.Client.RemoteEndPoint
            );

            try
            {
                await writer.WriteAsync("> ").ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                    if (line is null)
                        break; // Client disconnected.

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
                    var match = _commands.Resolve(tokens);

                    if (match is null)
                    {
                        var subs = _commands.FindSubcommands(tokens);
                        if (subs.Count > 0)
                        {
                            foreach (var kv in subs)
                                await writer
                                    .WriteLineAsync(
                                        $"  {kv.Value.Usage, -25} {kv.Value.Description}"
                                    )
                                    .ConfigureAwait(false);
                        }
                        else
                        {
                            await writer
                                .WriteLineAsync($"Unknown command: {tokens[0]}")
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        try
                        {
                            await match
                                .Value.Command.ExecuteAsync(match.Value.Args, ctx)
                                .ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            await writer.WriteLineAsync("Command timed out").ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            await writer
                                .WriteLineAsync($"Error: {ex.Message}")
                                .ConfigureAwait(false);
                        }
                    }

                    await writer.WriteAsync("> ").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown or client disconnect.
            }
            catch (IOException)
            {
                // Client disconnected mid-read/write.
            }
        }

        _logger.LogDebug("Remote client disconnected");
    }
}
