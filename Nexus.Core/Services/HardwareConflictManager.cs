using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nexus.Core.Services;

public class HardwareConflictManager
{
    private readonly ILogger<HardwareConflictManager>? _logger;
    private readonly List<int> _pausedProcessIds = new();

    public bool IsLianLiConflictDetected { get; private set; }
    public bool IsCorsairConflictDetected { get; private set; }
    public bool IsExclusiveLockAcquired { get; private set; }
    public string ConflictStatusMessage { get; private set; } = "No hardware conflicts detected.";

    public event EventHandler? ConflictStatusChanged;

    public HardwareConflictManager(ILogger<HardwareConflictManager>? logger = null)
    {
        _logger = logger;
        CheckConflicts();
    }

    public void CheckConflicts()
    {
        try
        {
            var lianLiProcs = Process.GetProcessesByName("L-Connect-Service")
                .Concat(Process.GetProcessesByName("L-Connect"))
                .Concat(Process.GetProcessesByName("L-Connect 3"))
                .ToList();

            var corsairProcs = Process.GetProcessesByName("iCUE")
                .Concat(Process.GetProcessesByName("iCUEDevicePluginHost"))
                .ToList();

            IsLianLiConflictDetected = lianLiProcs.Count > 0;
            IsCorsairConflictDetected = corsairProcs.Count > 0;

            if (IsLianLiConflictDetected && IsCorsairConflictDetected)
            {
                ConflictStatusMessage = "iCUE & L-Connect are running in background. Click 'Take Exclusive Lock' to prevent LED overwrite.";
            }
            else if (IsLianLiConflictDetected)
            {
                ConflictStatusMessage = "L-Connect service is active and may overwrite Lian Li fan colors.";
            }
            else if (IsCorsairConflictDetected)
            {
                ConflictStatusMessage = "Corsair iCUE is active and maintaining direct hardware lock.";
            }
            else
            {
                ConflictStatusMessage = "Direct hardware access clear. No vendor bloatware conflicts.";
            }

            ConflictStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error checking hardware conflicts.");
        }
    }

    /// <summary>
    /// Temporarily pauses or stops conflicting vendor background processes to allow NEXUS exclusive physical hardware control.
    /// </summary>
    public async Task<bool> AcquireExclusiveLockAsync()
    {
        _logger?.LogInformation("Attempting to acquire exclusive hardware control from vendor services...");

        try
        {
            // Kill/suspend L-Connect-Service so it stops overriding Lian Li USB packets
            var lianLiProcs = Process.GetProcessesByName("L-Connect-Service")
                .Concat(Process.GetProcessesByName("L-Connect"))
                .Concat(Process.GetProcessesByName("L-Connect 3"));

            foreach (var p in lianLiProcs)
            {
                try
                {
                    p.Kill();
                    _logger?.LogInformation("Terminated conflicting Lian Li background process (PID {Pid})", p.Id);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not terminate Lian Li process {Pid}", p.Id);
                }
            }

            // Kill/suspend conflicting MSI Mystic Light processes
            var msiProcs = Process.GetProcessesByName("LEDKeeper2")
                .Concat(Process.GetProcessesByName("LEDKeeper"))
                .Concat(Process.GetProcessesByName("LightKeeperService"))
                .Concat(Process.GetProcessesByName("MSI.CentralServer"));

            foreach (var p in msiProcs)
            {
                try
                {
                    p.Kill();
                    _logger?.LogInformation("Terminated conflicting MSI background process (PID {Pid})", p.Id);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not terminate MSI process {Pid}", p.Id);
                }
            }

            // Kill/suspend conflicting Corsair iCUE processes
            var corsairProcs = Process.GetProcessesByName("iCUE")
                .Concat(Process.GetProcessesByName("iCUEDevicePluginHost"))
                .Concat(Process.GetProcessesByName("iCUEUpdateService"));

            foreach (var p in corsairProcs)
            {
                try
                {
                    p.Kill();
                    _logger?.LogInformation("Terminated conflicting Corsair background process (PID {Pid})", p.Id);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not terminate Corsair process {Pid}", p.Id);
                }
            }

            // If L-Connect-Service is running as a SYSTEM Windows service, elevate via UAC to stop it cleanly
            var remainingLianLi = Process.GetProcessesByName("L-Connect-Service");
            if (remainingLianLi.Length > 0)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c net stop LConnectService & taskkill /F /IM L-Connect-Service.exe /IM iCUE.exe /IM iCUEDevicePluginHost.exe",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    var elevatedProc = Process.Start(psi);
                    if (elevatedProc != null) await elevatedProc.WaitForExitAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "UAC elevation cancelled or failed to stop LConnectService.");
                }
            }

            await Task.Delay(300);
            CheckConflicts();
            IsExclusiveLockAcquired = true;
            ConflictStatusMessage = "Exclusive hardware lock acquired. NEXUS now directly drives all LED packets.";
            ConflictStatusChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to acquire exclusive hardware lock.");
            return false;
        }
    }
}
