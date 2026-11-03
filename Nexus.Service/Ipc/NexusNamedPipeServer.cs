using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Profiles;
using Nexus.Abstractions.Rgb;
using Nexus.Abstractions.Sensors;
using Nexus.Core.ProfileManager;
using Nexus.Engines.FanControl;
using Nexus.Engines.Hardware;
using Nexus.Engines.RGB;

namespace Nexus.Service.Ipc;

public class NexusNamedPipeServer : IDisposable
{
    public const string PipeName = "NexusControlCenter";
    private readonly HardwareEngine _hardwareEngine;
    private readonly ProfileManager _profileManager;
    private readonly RgbEngine _rgbEngine;
    private readonly FanControlEngine _fanEngine;
    private readonly ILogger<NexusNamedPipeServer>? _logger;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _isDisposed;

    public NexusNamedPipeServer(
        HardwareEngine hardwareEngine,
        ProfileManager profileManager,
        RgbEngine rgbEngine,
        FanControlEngine fanEngine,
        ILogger<NexusNamedPipeServer>? logger = null)
    {
        _hardwareEngine = hardwareEngine;
        _profileManager = profileManager;
        _rgbEngine = rgbEngine;
        _fanEngine = fanEngine;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_cts.Token));
        _logger?.LogInformation("Named Pipe IPC Server started on pipe '{PipeName}'.", PipeName);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _serverTask?.Wait(1000); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(token);
                await HandleClientAsync(pipeServer, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in IPC Named Pipe server loop.");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(line)) return;

            var request = JsonSerializer.Deserialize<IpcMessage>(line);
            if (request == null) return;

            var response = await ProcessRequestAsync(request);
            var responseJson = JsonSerializer.Serialize(response);
            await writer.WriteLineAsync(responseJson.AsMemory(), token);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling IPC client request.");
        }
    }

    private async Task<IpcResponse> ProcessRequestAsync(IpcMessage request)
    {
        var response = new IpcResponse
        {
            RequestId = request.MessageId,
            Success = true
        };

        try
        {
            switch (request.Command)
            {
                case IpcCommands.Ping:
                    response.DataJson = JsonSerializer.Serialize(new { Status = "Online", ServiceUptime = Environment.TickCount64 });
                    break;

                case IpcCommands.GetTelemetry:
                    var snapshot = _hardwareEngine.CurrentSnapshot;
                    response.DataJson = JsonSerializer.Serialize(snapshot);
                    break;

                case IpcCommands.ActivateProfile:
                    if (!string.IsNullOrEmpty(request.PayloadJson))
                    {
                        await _profileManager.ActivateProfileAsync(request.PayloadJson);
                    }
                    break;

                case IpcCommands.SetRgbColor:
                    if (!string.IsNullOrEmpty(request.PayloadJson))
                    {
                        var color = JsonSerializer.Deserialize<RgbColor>(request.PayloadJson);
                        await _rgbEngine.ApplyStaticColorToAllAsync(color);
                    }
                    break;

                default:
                    response.Success = false;
                    response.ErrorMessage = $"Unknown command '{request.Command}'.";
                    break;
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.ErrorMessage = ex.Message;
        }

        return response;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}

public class NexusNamedPipeClient
{
    private readonly string _pipeName;

    public NexusNamedPipeClient(string pipeName = NexusNamedPipeServer.PipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<IpcResponse?> SendCommandAsync(string command, string? payload = null, int timeoutMs = 2000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await client.ConnectAsync(cts.Token);
            using var reader = new StreamReader(client, Encoding.UTF8);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };

            var req = new IpcMessage
            {
                Command = command,
                PayloadJson = payload
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(req).AsMemory(), cts.Token);
            var responseLine = await reader.ReadLineAsync(cts.Token);
            if (!string.IsNullOrEmpty(responseLine))
            {
                return JsonSerializer.Deserialize<IpcResponse>(responseLine);
            }
        }
        catch
        {
            // IPC service not running or unreachable
        }

        return null;
    }
}
