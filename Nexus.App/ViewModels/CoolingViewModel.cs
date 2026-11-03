using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Cooling;
using Nexus.Core.DeviceManager;
using Nexus.Engines.FanControl;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.LianLi;

namespace Nexus.App.ViewModels;

public class FanChannelItem : ViewModelBase
{
    private double _targetPercent = 50.0;
    private int _rpm = 1200;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string HubName { get; init; } = string.Empty;
    public string IconKey { get; init; } = "IconFanBlade";
    public bool IsPump { get; init; }

    public int Rpm
    {
        get => _rpm;
        set => SetProperty(ref _rpm, value);
    }

    public Action<double>? OnTargetChanged { get; set; }

    public double TargetPercent
    {
        get => _targetPercent;
        set
        {
            if (SetProperty(ref _targetPercent, Math.Round(value, 0)))
            {
                Rpm = IsPump ? (int)(1800 + (_targetPercent * 10)) : (int)(600 + (_targetPercent * 13));
                OnTargetChanged?.Invoke(_targetPercent);
            }
        }
    }
}

public class CurvePointItem : ViewModelBase
{
    private double _fanSpeedPercent;
    public double TemperatureCelsius { get; init; }
    public string TempLabel => $"{TemperatureCelsius:0}°C";

    public double FanSpeedPercent
    {
        get => _fanSpeedPercent;
        set => SetProperty(ref _fanSpeedPercent, Math.Clamp(Math.Round(value, 0), 20, 100));
    }
}

public class CoolingViewModel : ViewModelBase
{
    private readonly FanControlEngine _fanEngine;
    private readonly DeviceManager? _deviceManager;
    private FanCurve _selectedCurve;
    private double _manualSpeed = 50.0;
    private bool _isManualOverrideActive;
    private double _simulatedTemperature = 45.0;
    private double _simulatedCalculatedSpeed;
    private string _thermalZoneStatus = "Acoustics Optimized (Silent)";

    public ObservableCollection<FanCurve> SavedCurves { get; } = new();
    public ObservableCollection<FanChannelItem> FanChannels { get; } = new();
    public ObservableCollection<CurvePointItem> EditableCurvePoints { get; } = new();

    public FanCurve SelectedCurve
    {
        get => _selectedCurve;
        set
        {
            if (SetProperty(ref _selectedCurve, value))
            {
                UpdateSimulation();
            }
        }
    }

    public double ManualSpeed
    {
        get => _manualSpeed;
        set => SetProperty(ref _manualSpeed, value);
    }

    public bool IsManualOverrideActive
    {
        get => _isManualOverrideActive;
        set => SetProperty(ref _isManualOverrideActive, value);
    }

    public double SimulatedTemperature
    {
        get => _simulatedTemperature;
        set
        {
            if (SetProperty(ref _simulatedTemperature, value))
            {
                UpdateSimulation();
            }
        }
    }

    public double SimulatedCalculatedSpeed
    {
        get => _simulatedCalculatedSpeed;
        set => SetProperty(ref _simulatedCalculatedSpeed, value);
    }

    public string ThermalZoneStatus
    {
        get => _thermalZoneStatus;
        set => SetProperty(ref _thermalZoneStatus, value);
    }

    private string _selectedSource = "CPU";
    public string SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    public int Fan1Rpm => FanChannels.Count > 2 ? FanChannels[2].Rpm : 1420;
    public int Fan2Rpm => FanChannels.Count > 0 ? FanChannels[0].Rpm : 1150;
    public int FanPumpRpm => FanChannels.Count > 4 ? FanChannels[4].Rpm : 2350;

    public bool IsSafetyWatchdogActive => _fanEngine.IsSafetyWatchdogActive;

    public ICommand ApplyManualSpeedCommand { get; }
    public ICommand ResetSafetyDefaultsCommand { get; }
    public ICommand SelectPresetCommand { get; }
    public ICommand SelectSourceCommand { get; }

    public CoolingViewModel(FanControlEngine fanEngine, DeviceManager? deviceManager = null)
    {
        _fanEngine = fanEngine;
        _deviceManager = deviceManager;

        foreach (var curve in _fanEngine.SavedCurves)
        {
            SavedCurves.Add(curve);
        }
        _selectedCurve = SavedCurves.FirstOrDefault() ?? new FanCurve();

        // 5 Real Ecosystem Fan Channels (4 Lian Li Clusters + Corsair AIO Pump)
        var ch1 = new FanChannelItem { Id = "ll-cluster-1", Name = "Side Intake (3x AL120)", HubName = "Lian Li UNI FAN Hub", IconKey = "IconFanBlade", Rpm = 1185, TargetPercent = 45 };
        ch1.OnTargetChanged = val => SendLianLiSpeed(0, (int)val);
        FanChannels.Add(ch1);

        var ch2 = new FanChannelItem { Id = "ll-cluster-2", Name = "Bottom Intake (3x AL120)", HubName = "Lian Li UNI FAN Hub", IconKey = "IconFanBlade", Rpm = 1185, TargetPercent = 45 };
        ch2.OnTargetChanged = val => SendLianLiSpeed(1, (int)val);
        FanChannels.Add(ch2);

        var ch3 = new FanChannelItem { Id = "ll-cluster-3", Name = "Top Exhaust (3x AL120)", HubName = "Lian Li UNI FAN Hub", IconKey = "IconFanBlade", Rpm = 1250, TargetPercent = 50 };
        ch3.OnTargetChanged = val => SendLianLiSpeed(2, (int)val);
        FanChannels.Add(ch3);

        var ch4 = new FanChannelItem { Id = "ll-cluster-4", Name = "Rear Exhaust (1x AL120)", HubName = "Lian Li UNI FAN Hub", IconKey = "IconFanBlade", Rpm = 1224, TargetPercent = 48 };
        ch4.OnTargetChanged = val => SendLianLiSpeed(3, (int)val);
        FanChannels.Add(ch4);

        var pump = new FanChannelItem { Id = "corsair-pump", Name = "Capellix Elite Pump", HubName = "Corsair Commander CORE", IconKey = "IconAioPump", Rpm = 2600, TargetPercent = 80, IsPump = true };
        pump.OnTargetChanged = val => SendCorsairSpeed(0, (int)val);
        FanChannels.Add(pump);

        // Editable Fan Curve Control Points
        EditableCurvePoints.Add(new CurvePointItem { TemperatureCelsius = 20, FanSpeedPercent = 25 });
        EditableCurvePoints.Add(new CurvePointItem { TemperatureCelsius = 40, FanSpeedPercent = 35 });
        EditableCurvePoints.Add(new CurvePointItem { TemperatureCelsius = 60, FanSpeedPercent = 55 });
        EditableCurvePoints.Add(new CurvePointItem { TemperatureCelsius = 75, FanSpeedPercent = 80 });
        EditableCurvePoints.Add(new CurvePointItem { TemperatureCelsius = 85, FanSpeedPercent = 100 });

        foreach (var pt in EditableCurvePoints)
        {
            pt.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(EditableCurvePoints));
                UpdateSimulation();
            };
        }

        UpdateSimulation();

        ApplyManualSpeedCommand = new RelayCommand(async () =>
        {
            IsManualOverrideActive = true;
            foreach (var fan in _fanEngine.FanControllers)
            {
                await _fanEngine.SetManualFanSpeedAsync(fan.FanId, ManualSpeed);
            }
            foreach (var ch in FanChannels)
            {
                ch.TargetPercent = ManualSpeed;
            }
        });

        ResetSafetyDefaultsCommand = new RelayCommand(async () =>
        {
            IsManualOverrideActive = false;
            await _fanEngine.ResetAllToSafeDefaultsAsync();
            foreach (var ch in FanChannels)
            {
                ch.TargetPercent = ch.IsPump ? 80 : 50;
            }
            OnPropertyChanged(nameof(Fan1Rpm));
            OnPropertyChanged(nameof(Fan2Rpm));
            OnPropertyChanged(nameof(FanPumpRpm));
        });

        SelectSourceCommand = new RelayCommand(param =>
        {
            if (param is string src) SelectedSource = src;
        });

        SelectPresetCommand = new RelayCommand(param =>
        {
            if (param is string preset)
            {
                if (preset.Equals("Silent", StringComparison.OrdinalIgnoreCase))
                {
                    EditableCurvePoints[0].FanSpeedPercent = 20;
                    EditableCurvePoints[1].FanSpeedPercent = 25;
                    EditableCurvePoints[2].FanSpeedPercent = 40;
                    EditableCurvePoints[3].FanSpeedPercent = 60;
                    EditableCurvePoints[4].FanSpeedPercent = 100;
                    ThermalZoneStatus = "Acoustics Optimized (Silent Baseline)";
                }
                else if (preset.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
                {
                    EditableCurvePoints[0].FanSpeedPercent = 25;
                    EditableCurvePoints[1].FanSpeedPercent = 35;
                    EditableCurvePoints[2].FanSpeedPercent = 55;
                    EditableCurvePoints[3].FanSpeedPercent = 80;
                    EditableCurvePoints[4].FanSpeedPercent = 100;
                    ThermalZoneStatus = "Active Dynamic Cooling Zone";
                }
                else if (preset.Equals("Performance", StringComparison.OrdinalIgnoreCase))
                {
                    EditableCurvePoints[0].FanSpeedPercent = 40;
                    EditableCurvePoints[1].FanSpeedPercent = 60;
                    EditableCurvePoints[2].FanSpeedPercent = 80;
                    EditableCurvePoints[3].FanSpeedPercent = 100;
                    EditableCurvePoints[4].FanSpeedPercent = 100;
                    ThermalZoneStatus = "Aggressive Thermal Dissipation Zone";
                }
                UpdateSimulation();
            }
        });
    }

    private void SendLianLiSpeed(int channel, int speed)
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<LianLiUniFanController>().FirstOrDefault();
        dev?.SetChannelFanSpeed(channel, speed);
    }

    private void SendCorsairSpeed(int port, int speed)
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<CorsairCommanderCoreController>().FirstOrDefault();
        dev?.SetFanSpeed(port, speed);
    }

    private void UpdateSimulation()
    {
        if (SelectedCurve == null) return;
        SimulatedCalculatedSpeed = Math.Round(SelectedCurve.Evaluate(SimulatedTemperature), 1);

        if (SimulatedTemperature >= 85.0)
        {
            ThermalZoneStatus = "EMERGENCY SAFETY OVERRIDE (Forced 100% Fan RPM)";
        }
        else if (SimulatedTemperature >= 75.0)
        {
            ThermalZoneStatus = "Aggressive Thermal Dissipation Zone";
        }
        else if (SimulatedTemperature >= 55.0)
        {
            ThermalZoneStatus = "Active Dynamic Cooling Zone";
        }
        else
        {
            ThermalZoneStatus = "Acoustics Optimized (Silent Baseline)";
        }
    }
}
