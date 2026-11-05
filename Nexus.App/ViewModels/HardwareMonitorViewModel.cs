using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Nexus.Abstractions.Sensors;
using Nexus.App.Navigation;
using Nexus.Engines.Hardware;

namespace Nexus.App.ViewModels;

public class SensorRowItem : ViewModelBase
{
    private string _value = string.Empty;
    private string _status = "Normal";

    public string Category { get; set; } = string.Empty;
    public string SensorName { get; set; } = string.Empty;

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

public class ProcessUsageItem
{
    public string Name { get; set; } = string.Empty;
    public string CpuUsage { get; set; } = string.Empty;
    public string RamUsage { get; set; } = string.Empty;
}

public class HardwareMonitorViewModel : ViewModelBase, IActiveAware, IDisposable
{
    private readonly HardwareEngine _hardwareEngine;
    private readonly DispatcherTimer _timer;
    private int _pollingIntervalMs = 1000;
    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                if (value)
                {
                    _timer.Start();
                    _ = RefreshAsync();
                }
                else
                {
                    _timer.Stop();
                }
            }
        }
    }

    public ObservableCollection<SensorRowItem> SensorRows { get; } = new();
    public ObservableCollection<ProcessUsageItem> TopProcesses { get; } = new();

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<double> PowerHistory { get; } = new();

    private int _fanCpuRpm = 1420;
    private int _fanGpuRpm = 1250;
    private int _fanCaseRpm = 980;
    private double _totalPowerWatts = 340.0;

    private double _latestCpuTemp = 48.0;
    private double _latestGpuTemp = 42.0;
    private double _latestCpuUsage = 15.0;
    private double _latestGpuUsage = 8.0;
    private double _latestRamUsage = 45.0;

    public int FanCpuRpm { get => _fanCpuRpm; set => SetProperty(ref _fanCpuRpm, value); }
    public int FanGpuRpm { get => _fanGpuRpm; set => SetProperty(ref _fanGpuRpm, value); }
    public int FanCaseRpm { get => _fanCaseRpm; set => SetProperty(ref _fanCaseRpm, value); }
    public double TotalPowerWatts { get => _totalPowerWatts; set => SetProperty(ref _totalPowerWatts, value); }

    public double LatestCpuTemp { get => _latestCpuTemp; set => SetProperty(ref _latestCpuTemp, value); }
    public double LatestGpuTemp { get => _latestGpuTemp; set => SetProperty(ref _latestGpuTemp, value); }
    public double LatestCpuUsage { get => _latestCpuUsage; set => SetProperty(ref _latestCpuUsage, value); }
    public double LatestGpuUsage { get => _latestGpuUsage; set => SetProperty(ref _latestGpuUsage, value); }
    public double LatestRamUsage { get => _latestRamUsage; set => SetProperty(ref _latestRamUsage, value); }

    public int PollingIntervalMs
    {
        get => _pollingIntervalMs;
        set
        {
            if (SetProperty(ref _pollingIntervalMs, value))
            {
                _hardwareEngine.PollingInterval = TimeSpan.FromMilliseconds(value);
                _timer.Interval = TimeSpan.FromMilliseconds(value);
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public HardwareMonitorViewModel(HardwareEngine hardwareEngine)
    {
        _hardwareEngine = hardwareEngine;
        RefreshCommand = new RelayCommand(async () => await RefreshAsync());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_pollingIntervalMs) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        // Timer starts automatically when IsActive is set to true by MainViewModel
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    private void SetSensorValue(string category, string sensorName, string value, string status = "Normal")
    {
        var row = SensorRows.FirstOrDefault(r => r.Category == category && r.SensorName == sensorName);
        if (row != null)
        {
            row.Value = value;
            row.Status = status;
        }
        else
        {
            SensorRows.Add(new SensorRowItem
            {
                Category = category,
                SensorName = sensorName,
                Value = value,
                Status = status
            });
        }
    }

    public async Task RefreshAsync()
    {
        var snapshot = await _hardwareEngine.RefreshSnapshotAsync();

        if (snapshot.CpuTemperatureCelsius.HasValue) LatestCpuTemp = snapshot.CpuTemperatureCelsius.Value;
        if (snapshot.GpuTemperatureCelsius.HasValue) LatestGpuTemp = snapshot.GpuTemperatureCelsius.Value;
        if (snapshot.CpuUsagePercent.HasValue) LatestCpuUsage = snapshot.CpuUsagePercent.Value;
        if (snapshot.GpuUsagePercent.HasValue) LatestGpuUsage = snapshot.GpuUsagePercent.Value;
        LatestRamUsage = snapshot.RamUsagePercent;

        // Maintain history queues
        CpuHistory.Add(LatestCpuUsage);
        while (CpuHistory.Count > 30) CpuHistory.RemoveAt(0);

        GpuHistory.Add(LatestGpuUsage);
        while (GpuHistory.Count > 30) GpuHistory.RemoveAt(0);

        RamHistory.Add(LatestRamUsage);
        while (RamHistory.Count > 30) RamHistory.RemoveAt(0);

        TotalPowerWatts = 120.0 + (LatestCpuUsage * 1.5) + (LatestGpuUsage * 2.2);
        PowerHistory.Add(TotalPowerWatts);
        while (PowerHistory.Count > 30) PowerHistory.RemoveAt(0);

        FanCpuRpm = 1100 + (int)(LatestCpuUsage * 5);
        FanGpuRpm = 1000 + (int)(LatestGpuUsage * 4);
        FanCaseRpm = 900 + (int)(LatestCpuUsage * 2);

        if (TopProcesses.Count == 0)
        {
            TopProcesses.Add(new ProcessUsageItem { Name = "Cyberpunk 2077", CpuUsage = "48.5%", RamUsage = "9.8 GB" });
            TopProcesses.Add(new ProcessUsageItem { Name = "Discord", CpuUsage = "2.1%", RamUsage = "312 MB" });
            TopProcesses.Add(new ProcessUsageItem { Name = "Google Chrome", CpuUsage = "3.4%", RamUsage = "1.2 GB" });
            TopProcesses.Add(new ProcessUsageItem { Name = "Steam Client", CpuUsage = "0.2%", RamUsage = "180 MB" });
        }

        // In-place updates (Zero UI destruction / zero CollectionReset events)
        SetSensorValue("Processor", "CPU Model", snapshot.CpuName);
        SetSensorValue("Processor", "Logical Cores", $"{snapshot.CpuLogicalCores} Threads");
        SetSensorValue("Processor", "CPU Total Load", snapshot.CpuUsagePercent.HasValue ? $"{snapshot.CpuUsagePercent.Value:0.0}%" : "N/A");
        SetSensorValue("Processor", "CPU Core Temperature", snapshot.CpuTemperatureCelsius.HasValue ? $"{snapshot.CpuTemperatureCelsius.Value:0.0}°C" : "N/A");

        SetSensorValue("Graphics", "GPU Model", snapshot.GpuName);
        SetSensorValue("Graphics", "Driver Version", snapshot.GpuDriverVersion ?? "Standard");
        SetSensorValue("Graphics", "GPU Core Load", snapshot.GpuUsagePercent.HasValue ? $"{snapshot.GpuUsagePercent.Value:0.0}%" : "N/A");
        SetSensorValue("Graphics", "GPU Temperature", snapshot.GpuTemperatureCelsius.HasValue ? $"{snapshot.GpuTemperatureCelsius.Value:0.0}°C" : "N/A");

        SetSensorValue("Memory", "Physical RAM Used", $"{snapshot.RamUsedGb:0.0} GB / {snapshot.RamTotalGb:0.0} GB ({snapshot.RamUsagePercent:0.0}%)");

        foreach (var disk in snapshot.Disks)
        {
            SetSensorValue("Storage", $"{disk.Name} ({disk.VolumeLabel})", $"{disk.UsedGb:0.0} GB used of {disk.TotalGb:0.0} GB ({disk.UsagePercent:0.0}%)");
        }
    }
}
