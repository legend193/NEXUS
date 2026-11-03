using Nexus.Abstractions.Sensors;

namespace Nexus.Service.Ipc;

public class IpcMessage
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string Command { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
}

public class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DataJson { get; set; }
}

public static class IpcCommands
{
    public const string Ping = "PING";
    public const string GetTelemetry = "GET_TELEMETRY";
    public const string ActivateProfile = "ACTIVATE_PROFILE";
    public const string SetRgbColor = "SET_RGB_COLOR";
    public const string SetFanSpeed = "SET_FAN_SPEED";
    public const string GetStatus = "GET_STATUS";
}
