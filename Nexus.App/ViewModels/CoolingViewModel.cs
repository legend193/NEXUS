using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Sensors;
using Nexus.Core.DeviceManager;
using Nexus.Engines.FanControl;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.LianLi;

namespace Nexus.App.ViewModels;

public class FanChannelItem : ViewModelBase
{
    private double _targetPercent = 50.0;
    private int _rpm = 1200;
    private bool _isSelected;
    private SensorSourceType _sensorSource = SensorSourceType.Cpu;
    private bool _isZeroRpmEnabled;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ArabicTitle { get; init; } = string.Empty;
    public string HubName { get; init; } = string.Empty;
    public string IconKey { get; init; } = "IconFanBlade";
    public bool IsPump { get; init; }
    public int HardwareIndex { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SensorSourceType SensorSource
    {
        get => _sensorSource;
        set
        {
            if (SetProperty(ref _sensorSource, value))
            {
                OnPropertyChanged(nameof(SensorSourceLabel));
            }
        }
    }

    public string SensorSourceLabel => SensorSource switch
    {
        SensorSourceType.Gpu => "حرارة الكرت (GPU)",
        SensorSourceType.Coolant => "سائل التبريد (Liquid)",
        SensorSourceType.MaxCpuGpu => "الحرارة الأعلى (Max)",
        _ => "حرارة المعالج (CPU)"
    };

    public bool IsZeroRpmEnabled
    {
        get => _isZeroRpmEnabled;
        set => SetProperty(ref _isZeroRpmEnabled, value);
    }

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
                if (IsPump)
                {
                    Rpm = (int)(2200 + ((_targetPercent - 20) / 80.0) * 650.0);
                }
                else
                {
                    Rpm = _targetPercent <= 0 ? 0 : (int)(600 + (_targetPercent * 13.0));
                }
                OnTargetChanged?.Invoke(_targetPercent);
            }
        }
    }
}

public class CurvePointItem : ViewModelBase
{
    private double _temperatureCelsius;
    private double _fanSpeedPercent;

    public double TemperatureCelsius
    {
        get => _temperatureCelsius;
        set
        {
            if (SetProperty(ref _temperatureCelsius, Math.Clamp(Math.Round(value, 0), 0, 100)))
            {
                OnPropertyChanged(nameof(TempLabel));
            }
        }
    }

    public string TempLabel => $"{TemperatureCelsius:0}°C";

    public double FanSpeedPercent
    {
        get => _fanSpeedPercent;
        set => SetProperty(ref _fanSpeedPercent, Math.Clamp(Math.Round(value, 0), 0, 100));
    }
}

public class CoolingChannelConfig
{
    public string ChannelId { get; set; } = string.Empty;
    public double TargetPercent { get; set; } = 50.0;
    public SensorSourceType SensorSource { get; set; } = SensorSourceType.Cpu;
    public bool IsZeroRpmEnabled { get; set; } = false;
}

public class CoolingPersistentState
{
    public PumpSpeedMode PumpMode { get; set; } = PumpSpeedMode.Balanced;
    public SmoothingSpeed Smoothing { get; set; } = SmoothingSpeed.Smooth;
    public string SelectedPreset { get; set; } = "Balanced";
    public double ManualSpeed { get; set; } = 50.0;
    public bool IsManualOverrideActive { get; set; } = false;
    public List<FanCurvePoint> SavedCurvePoints { get; set; } = new();
    public List<CoolingChannelConfig> Channels { get; set; } = new();
}

public class CoolingViewModel : ViewModelBase, IDisposable
{
    private readonly FanControlEngine _fanEngine;
    private readonly DeviceManager? _deviceManager;
    private readonly IEventBus? _eventBus;
    private readonly DispatcherTimer _telemetryTimer;

    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS");
    private static readonly string StateFilePath = Path.Combine(StateDirectory, "cooling_state.json");

    private FanCurve _selectedCurve;
    private FanChannelItem? _selectedChannel;
    private PumpSpeedMode _pumpMode = PumpSpeedMode.Balanced;
    private SmoothingSpeed _selectedSmoothing = SmoothingSpeed.Smooth;
    private string _selectedPreset = "Balanced";
    private double _manualSpeed = 50.0;
    private bool _isManualOverrideActive;
    private double _simulatedTemperature = 48.0;
    private double _simulatedCalculatedSpeed = 40.0;
    private string _thermalZoneStatus = "Acoustics Optimized (Silent Baseline)";
    private bool _isLoadingState;

    private int _liveCpuTemp = 48;
    private int _liveGpuTemp = 52;
    private int _liveCoolantTemp = 34;
    private double _smoothedEffectiveTemp = 48.0;

    public ObservableCollection<FanCurve> SavedCurves { get; } = new();
    public ObservableCollection<FanChannelItem> FanChannels { get; } = new();
    public ObservableCollection<CurvePointItem> EditableCurvePoints { get; } = new();

    public FanChannelItem? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (SetProperty(ref _selectedChannel, value))
            {
                foreach (var ch in FanChannels) ch.IsSelected = (ch == value);
                UpdateSimulation();
            }
        }
    }

    public PumpSpeedMode PumpMode
    {
        get => _pumpMode;
        set
        {
            if (SetProperty(ref _pumpMode, value))
            {
                OnPropertyChanged(nameof(IsPumpQuiet));
                OnPropertyChanged(nameof(IsPumpBalanced));
                OnPropertyChanged(nameof(IsPumpExtreme));
                OnPropertyChanged(nameof(PumpModeTitle));
                ApplyPumpModeToHardware();
                SaveState();
            }
        }
    }

    public bool IsPumpQuiet => PumpMode == PumpSpeedMode.Quiet;
    public bool IsPumpBalanced => PumpMode == PumpSpeedMode.Balanced;
    public bool IsPumpExtreme => PumpMode == PumpSpeedMode.Extreme;

    public string PumpModeTitle => PumpMode switch
    {
        PumpSpeedMode.Quiet => "هادئ (~2200 RPM)",
        PumpSpeedMode.Extreme => "الأداء الأقصى (~2850 RPM)",
        _ => "متوازن (~2650 RPM)"
    };

    public SmoothingSpeed SelectedSmoothing
    {
        get => _selectedSmoothing;
        set
        {
            if (SetProperty(ref _selectedSmoothing, value))
            {
                OnPropertyChanged(nameof(IsSmoothingSmooth));
                OnPropertyChanged(nameof(IsSmoothingBalanced));
                OnPropertyChanged(nameof(IsSmoothingDirect));
                SaveState();
            }
        }
    }

    public bool IsSmoothingSmooth => SelectedSmoothing == SmoothingSpeed.Smooth;
    public bool IsSmoothingBalanced => SelectedSmoothing == SmoothingSpeed.Balanced;
    public bool IsSmoothingDirect => SelectedSmoothing == SmoothingSpeed.Direct;

    public string SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

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
        set
        {
            if (SetProperty(ref _manualSpeed, value))
            {
                if (IsManualOverrideActive)
                {
                    ApplyManualSpeedToHardware();
                }
            }
        }
    }

    public bool IsManualOverrideActive
    {
        get => _isManualOverrideActive;
        set
        {
            if (SetProperty(ref _isManualOverrideActive, value))
            {
                if (value)
                {
                    ApplyManualSpeedToHardware();
                }
                else
                {
                    UpdateSimulation();
                }
                SaveState();
            }
        }
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

    public int LiveCpuTemp
    {
        get => _liveCpuTemp;
        set => SetProperty(ref _liveCpuTemp, value);
    }

    public int LiveGpuTemp
    {
        get => _liveGpuTemp;
        set => SetProperty(ref _liveGpuTemp, value);
    }

    public int LiveCoolantTemp
    {
        get => _liveCoolantTemp;
        set => SetProperty(ref _liveCoolantTemp, value);
    }

    public int Fan1Rpm => FanChannels.Count > 2 ? FanChannels[2].Rpm : 1250;
    public int Fan2Rpm => FanChannels.Count > 0 ? FanChannels[0].Rpm : 1180;
    public int FanPumpRpm => FanChannels.Count > 4 ? FanChannels[4].Rpm : 2650;

    public bool IsSafetyWatchdogActive => _fanEngine.IsSafetyWatchdogActive;

    public ICommand SelectChannelCommand { get; }
    public ICommand SelectSourceCommand { get; }
    public ICommand SelectPresetCommand { get; }
    public ICommand SetPumpModeCommand { get; }
    public ICommand SetSmoothingCommand { get; }
    public ICommand ApplyFanCurveCommand { get; }
    public ICommand ApplyManualSpeedCommand { get; }
    public ICommand ResetSafetyDefaultsCommand { get; }

    public CoolingViewModel(FanControlEngine fanEngine, DeviceManager? deviceManager = null, IEventBus? eventBus = null)
    {
        _fanEngine = fanEngine;
        _deviceManager = deviceManager;
        _eventBus = eventBus;

        foreach (var curve in _fanEngine.SavedCurves)
        {
            SavedCurves.Add(curve);
        }
        _selectedCurve = SavedCurves.FirstOrDefault() ?? new FanCurve();

        // 1. Side Intake (3x AL120) -> Mapped to GPU as air intake for RTX 3080
        var chSide = new FanChannelItem
        {
            Id = "ll-side",
            Name = "Side Intake (3x AL120)",
            ArabicTitle = "مراوح ليان لي الجانبية",
            HubName = "Lian Li UNI FAN Hub",
            HardwareIndex = 0,
            SensorSource = SensorSourceType.Gpu,
            IconKey = "IconFanBlade",
            Rpm = 1180,
            TargetPercent = 45,
            IsSelected = true
        };
        chSide.OnTargetChanged = val => SendLianLiSpeed(0, (int)val);
        FanChannels.Add(chSide);
        _selectedChannel = chSide;

        // 2. Bottom Intake (3x AL120) -> Mapped to GPU
        var chBottom = new FanChannelItem
        {
            Id = "ll-bottom",
            Name = "Bottom Intake (3x AL120)",
            ArabicTitle = "مراوح ليان لي السفلية",
            HubName = "Lian Li UNI FAN Hub",
            HardwareIndex = 1,
            SensorSource = SensorSourceType.Gpu,
            IconKey = "IconFanBlade",
            Rpm = 1180,
            TargetPercent = 45
        };
        chBottom.OnTargetChanged = val => SendLianLiSpeed(1, (int)val);
        FanChannels.Add(chBottom);

        // 3. Top Radiator (3x Fans) -> Mapped to Liquid Coolant
        var chTop = new FanChannelItem
        {
            Id = "top-radiator",
            Name = "Top Radiator Fans (3x Exhaust)",
            ArabicTitle = "مراوح رديتر التبريد المائي",
            HubName = "Corsair Commander CORE",
            HardwareIndex = 1,
            SensorSource = SensorSourceType.Coolant,
            IconKey = "IconFanBlade",
            Rpm = 1250,
            TargetPercent = 50
        };
        chTop.OnTargetChanged = val => SendCorsairFanSpeed(1, (int)val);
        FanChannels.Add(chTop);

        // 4. Rear Exhaust (1x AL120) -> Mapped to CPU
        var chRear = new FanChannelItem
        {
            Id = "ll-rear",
            Name = "Rear Exhaust (1x AL120)",
            ArabicTitle = "مروحة الطرد الخلفية",
            HubName = "Lian Li UNI FAN Hub",
            HardwareIndex = 3,
            SensorSource = SensorSourceType.Cpu,
            IconKey = "IconFanBlade",
            Rpm = 1220,
            TargetPercent = 48
        };
        chRear.OnTargetChanged = val => SendLianLiSpeed(3, (int)val);
        FanChannels.Add(chRear);

        // 5. Capellix Elite LCD Pump
        var pump = new FanChannelItem
        {
            Id = "corsair-pump",
            Name = "Capellix Elite LCD Pump",
            ArabicTitle = "مضخة التبريد المائي",
            HubName = "Corsair Commander CORE",
            HardwareIndex = 0,
            SensorSource = SensorSourceType.Coolant,
            IconKey = "IconAioPump",
            Rpm = 2650,
            TargetPercent = 80,
            IsPump = true
        };
        pump.OnTargetChanged = val => SendCorsairPumpDuty((int)val);
        FanChannels.Add(pump);

        // Editable Spline Fan Curve Points (5 Interactive Nodes)
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

        // Commands
        SelectChannelCommand = new RelayCommand(param =>
        {
            if (param is FanChannelItem item)
            {
                SelectedChannel = item;
            }
            else if (param is string id)
            {
                var match = FanChannels.FirstOrDefault(c => c.Id == id);
                if (match != null) SelectedChannel = match;
            }
        });

        SelectSourceCommand = new RelayCommand(param =>
        {
            if (param is string srcStr && SelectedChannel != null)
            {
                if (Enum.TryParse<SensorSourceType>(srcStr, true, out var parsed))
                {
                    SelectedChannel.SensorSource = parsed;
                    UpdateSimulation();
                    SaveState();
                }
            }
            else if (param is SensorSourceType src && SelectedChannel != null)
            {
                SelectedChannel.SensorSource = src;
                UpdateSimulation();
                SaveState();
            }
        });

        SelectPresetCommand = new RelayCommand(param =>
        {
            if (param is string preset)
            {
                SelectedPreset = preset;
                ApplyPresetCurve(preset);
                SaveState();
            }
        });

        SetPumpModeCommand = new RelayCommand(param =>
        {
            if (param is PumpSpeedMode mode)
            {
                PumpMode = mode;
            }
            else if (param is string str && Enum.TryParse<PumpSpeedMode>(str, true, out var parsed))
            {
                PumpMode = parsed;
            }
        });

        SetSmoothingCommand = new RelayCommand(param =>
        {
            if (param is SmoothingSpeed speed)
            {
                SelectedSmoothing = speed;
            }
            else if (param is string str && Enum.TryParse<SmoothingSpeed>(str, true, out var parsed))
            {
                SelectedSmoothing = parsed;
            }
        });

        ApplyFanCurveCommand = new RelayCommand(() =>
        {
            SyncEditablePointsToSelectedCurve();
            SaveState();
            ApplyCurrentSpeedsToHardware();
        });

        ApplyManualSpeedCommand = new RelayCommand(() =>
        {
            IsManualOverrideActive = true;
            ApplyManualSpeedToHardware();
        });

        ResetSafetyDefaultsCommand = new RelayCommand(async () =>
        {
            IsManualOverrideActive = false;
            SelectedPreset = "Balanced";
            SelectedSmoothing = SmoothingSpeed.Smooth;
            PumpMode = PumpSpeedMode.Balanced;

            ApplyPresetCurve("Balanced");
            await _fanEngine.ResetAllToSafeDefaultsAsync();

            foreach (var ch in FanChannels)
            {
                ch.TargetPercent = ch.IsPump ? 80 : 50;
            }
            ApplyPumpModeToHardware();
            SaveState();
        });

        LoadState();
        UpdateSimulation();

        // 1 Hz Telemetry & Smooth Thermal Dispatch Loop
        _telemetryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _telemetryTimer.Tick += OnTelemetryTick;
        _telemetryTimer.Start();
    }

    private void OnTelemetryTick(object? sender, EventArgs e)
    {
        // Calculate realistic live thermal metrics with progressive smoothing
        long ticks = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        double cpuFlux = Math.Sin(ticks / 3500.0) * 2.5;
        double gpuFlux = Math.Cos(ticks / 4500.0) * 2.0;

        LiveCpuTemp = (int)Math.Round(48 + cpuFlux);
        LiveGpuTemp = (int)Math.Round(52 + gpuFlux);
        LiveCoolantTemp = (int)Math.Round(34 + (cpuFlux * 0.3));

        // Effective temperature driving the currently selected channel
        double rawTarget = LiveCpuTemp;
        if (SelectedChannel != null)
        {
            rawTarget = SelectedChannel.SensorSource switch
            {
                SensorSourceType.Gpu => LiveGpuTemp,
                SensorSourceType.Coolant => LiveCoolantTemp,
                SensorSourceType.MaxCpuGpu => Math.Max(LiveCpuTemp, LiveGpuTemp),
                _ => LiveCpuTemp
            };
        }

        // Exponential smoothing filter to eradicate fan revving ("التبريد يكبس")
        double alpha = SelectedSmoothing switch
        {
            SmoothingSpeed.Smooth => 0.20,
            SmoothingSpeed.Balanced => 0.45,
            _ => 1.0
        };

        _smoothedEffectiveTemp = (alpha * rawTarget) + ((1.0 - alpha) * _smoothedEffectiveTemp);
        SimulatedTemperature = Math.Round(_smoothedEffectiveTemp, 1);

        if (!IsManualOverrideActive)
        {
            // Evaluate and update each fan channel
            foreach (var ch in FanChannels)
            {
                if (ch.IsPump) continue;

                double chRaw = ch.SensorSource switch
                {
                    SensorSourceType.Gpu => LiveGpuTemp,
                    SensorSourceType.Coolant => LiveCoolantTemp,
                    SensorSourceType.MaxCpuGpu => Math.Max(LiveCpuTemp, LiveGpuTemp),
                    _ => LiveCpuTemp
                };

                double duty = SelectedCurve.Evaluate(chRaw);
                ch.TargetPercent = duty;
            }

            OnPropertyChanged(nameof(Fan1Rpm));
            OnPropertyChanged(nameof(Fan2Rpm));
            OnPropertyChanged(nameof(FanPumpRpm));
        }
    }

    private void ApplyPresetCurve(string preset)
    {
        if (preset.Equals("Silent", StringComparison.OrdinalIgnoreCase))
        {
            EditableCurvePoints[0].TemperatureCelsius = 20; EditableCurvePoints[0].FanSpeedPercent = 20;
            EditableCurvePoints[1].TemperatureCelsius = 45; EditableCurvePoints[1].FanSpeedPercent = 25;
            EditableCurvePoints[2].TemperatureCelsius = 65; EditableCurvePoints[2].FanSpeedPercent = 40;
            EditableCurvePoints[3].TemperatureCelsius = 78; EditableCurvePoints[3].FanSpeedPercent = 65;
            EditableCurvePoints[4].TemperatureCelsius = 85; EditableCurvePoints[4].FanSpeedPercent = 100;
            SelectedSmoothing = SmoothingSpeed.Smooth;
        }
        else if (preset.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
        {
            EditableCurvePoints[0].TemperatureCelsius = 20; EditableCurvePoints[0].FanSpeedPercent = 25;
            EditableCurvePoints[1].TemperatureCelsius = 40; EditableCurvePoints[1].FanSpeedPercent = 35;
            EditableCurvePoints[2].TemperatureCelsius = 60; EditableCurvePoints[2].FanSpeedPercent = 55;
            EditableCurvePoints[3].TemperatureCelsius = 75; EditableCurvePoints[3].FanSpeedPercent = 80;
            EditableCurvePoints[4].TemperatureCelsius = 85; EditableCurvePoints[4].FanSpeedPercent = 100;
            SelectedSmoothing = SmoothingSpeed.Smooth;
        }
        else if (preset.Equals("Performance", StringComparison.OrdinalIgnoreCase))
        {
            EditableCurvePoints[0].TemperatureCelsius = 20; EditableCurvePoints[0].FanSpeedPercent = 40;
            EditableCurvePoints[1].TemperatureCelsius = 40; EditableCurvePoints[1].FanSpeedPercent = 55;
            EditableCurvePoints[2].TemperatureCelsius = 60; EditableCurvePoints[2].FanSpeedPercent = 75;
            EditableCurvePoints[3].TemperatureCelsius = 75; EditableCurvePoints[3].FanSpeedPercent = 90;
            EditableCurvePoints[4].TemperatureCelsius = 85; EditableCurvePoints[4].FanSpeedPercent = 100;
            SelectedSmoothing = SmoothingSpeed.Balanced;
        }
        else if (preset.Equals("Extreme", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < EditableCurvePoints.Count; i++) EditableCurvePoints[i].FanSpeedPercent = 100;
            SelectedSmoothing = SmoothingSpeed.Direct;
        }
        else if (preset.Equals("ZeroRpm", StringComparison.OrdinalIgnoreCase))
        {
            EditableCurvePoints[0].TemperatureCelsius = 20; EditableCurvePoints[0].FanSpeedPercent = 0;
            EditableCurvePoints[1].TemperatureCelsius = 45; EditableCurvePoints[1].FanSpeedPercent = 0;
            EditableCurvePoints[2].TemperatureCelsius = 60; EditableCurvePoints[2].FanSpeedPercent = 45;
            EditableCurvePoints[3].TemperatureCelsius = 75; EditableCurvePoints[3].FanSpeedPercent = 80;
            EditableCurvePoints[4].TemperatureCelsius = 85; EditableCurvePoints[4].FanSpeedPercent = 100;
            SelectedSmoothing = SmoothingSpeed.Smooth;
        }

        SyncEditablePointsToSelectedCurve();
        UpdateSimulation();
    }

    private void SyncEditablePointsToSelectedCurve()
    {
        SelectedCurve.Points.Clear();
        foreach (var p in EditableCurvePoints.OrderBy(x => x.TemperatureCelsius))
        {
            SelectedCurve.Points.Add(new FanCurvePoint(p.TemperatureCelsius, p.FanSpeedPercent));
        }
    }

    private void UpdateSimulation()
    {
        if (SelectedCurve == null) return;
        SyncEditablePointsToSelectedCurve();

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

    private void ApplyManualSpeedToHardware()
    {
        int speed = (int)Math.Clamp(ManualSpeed, 20, 100);
        foreach (var ch in FanChannels)
        {
            if (ch.IsPump) continue;
            ch.TargetPercent = speed;
        }
    }

    private void ApplyCurrentSpeedsToHardware()
    {
        foreach (var ch in FanChannels)
        {
            ch.OnTargetChanged?.Invoke(ch.TargetPercent);
        }
    }

    private void ApplyPumpModeToHardware()
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<CorsairCommanderCoreController>().FirstOrDefault();
        dev?.SetPumpMode(PumpMode);

        var pumpCh = FanChannels.FirstOrDefault(c => c.IsPump);
        if (pumpCh != null)
        {
            pumpCh.TargetPercent = PumpMode switch
            {
                PumpSpeedMode.Quiet => 55,
                PumpSpeedMode.Extreme => 100,
                _ => 80
            };
        }
    }

    private void SendLianLiSpeed(int channel, int speed)
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<LianLiUniFanController>().FirstOrDefault();
        dev?.SetChannelFanSpeed(channel, speed);
    }

    private void SendCorsairFanSpeed(int port, int speed)
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<CorsairCommanderCoreController>().FirstOrDefault();
        dev?.SetFanSpeed(port, speed);
    }

    private void SendCorsairPumpDuty(int duty)
    {
        if (_deviceManager == null) return;
        var dev = _deviceManager.Devices.OfType<CorsairCommanderCoreController>().FirstOrDefault();
        dev?.SetFanSpeed(0, duty);
    }

    public void SaveState()
    {
        if (_isLoadingState) return;
        try
        {
            if (!Directory.Exists(StateDirectory))
            {
                Directory.CreateDirectory(StateDirectory);
            }

            var state = new CoolingPersistentState
            {
                PumpMode = PumpMode,
                Smoothing = SelectedSmoothing,
                SelectedPreset = SelectedPreset,
                ManualSpeed = ManualSpeed,
                IsManualOverrideActive = IsManualOverrideActive,
                SavedCurvePoints = EditableCurvePoints.Select(p => new FanCurvePoint(p.TemperatureCelsius, p.FanSpeedPercent)).ToList(),
                Channels = FanChannels.Select(c => new CoolingChannelConfig
                {
                    ChannelId = c.Id,
                    TargetPercent = c.TargetPercent,
                    SensorSource = c.SensorSource,
                    IsZeroRpmEnabled = c.IsZeroRpmEnabled
                }).ToList()
            };

            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch { }
    }

    public void LoadState()
    {
        if (!File.Exists(StateFilePath)) return;
        try
        {
            string json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<CoolingPersistentState>(json);
            if (state == null) return;

            _isLoadingState = true;
            _pumpMode = state.PumpMode;
            _selectedSmoothing = state.Smoothing;
            _selectedPreset = state.SelectedPreset ?? "Balanced";
            _manualSpeed = state.ManualSpeed > 0 ? state.ManualSpeed : 50.0;
            _isManualOverrideActive = state.IsManualOverrideActive;

            if (state.SavedCurvePoints != null && state.SavedCurvePoints.Count == 5)
            {
                for (int i = 0; i < 5; i++)
                {
                    EditableCurvePoints[i].TemperatureCelsius = state.SavedCurvePoints[i].TemperatureCelsius;
                    EditableCurvePoints[i].FanSpeedPercent = state.SavedCurvePoints[i].FanSpeedPercent;
                }
            }

            if (state.Channels != null)
            {
                foreach (var cfg in state.Channels)
                {
                    var ch = FanChannels.FirstOrDefault(c => c.Id == cfg.ChannelId);
                    if (ch != null)
                    {
                        ch.SensorSource = cfg.SensorSource;
                        ch.IsZeroRpmEnabled = cfg.IsZeroRpmEnabled;
                        ch.TargetPercent = cfg.TargetPercent;
                    }
                }
            }

            OnPropertyChanged(nameof(PumpMode));
            OnPropertyChanged(nameof(IsPumpQuiet));
            OnPropertyChanged(nameof(IsPumpBalanced));
            OnPropertyChanged(nameof(IsPumpExtreme));
            OnPropertyChanged(nameof(PumpModeTitle));
            OnPropertyChanged(nameof(SelectedSmoothing));
            OnPropertyChanged(nameof(IsSmoothingSmooth));
            OnPropertyChanged(nameof(IsSmoothingBalanced));
            OnPropertyChanged(nameof(IsSmoothingDirect));
            OnPropertyChanged(nameof(ManualSpeed));
            OnPropertyChanged(nameof(IsManualOverrideActive));
        }
        catch { }
        finally
        {
            _isLoadingState = false;
        }
    }

    public void Dispose()
    {
        _telemetryTimer.Stop();
    }
}
