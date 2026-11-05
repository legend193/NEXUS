using System.Threading.Tasks;

namespace Nexus.Abstractions.Audio;

public interface IAudioRoutingService
{
    bool IsSupported { get; }
    Task<bool> SetAppOutputDeviceAsync(int processId, string endpointDeviceId);
    Task<string?> GetAppOutputDeviceAsync(int processId);
    Task<bool> ResetAppRoutingAsync(int processId);
}
