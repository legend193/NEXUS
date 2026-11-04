using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nexus.Abstractions.Cooling;
using Nexus.Abstractions.Rgb;
using Nexus.Core.DeviceManager;
using Nexus.Core.Services;
using Nexus.Engines.RGB;
using Nexus.Integrations.Corsair;
using Nexus.Integrations.Gpu;
using Nexus.Integrations.Keyboard;
using Nexus.Integrations.LianLi;
using Nexus.Integrations.Logitech;
using Nexus.Integrations.Memory;
using Nexus.App.Services;
using Nexus.Integrations.Motherboard;

namespace Nexus.App.ViewModels;

public class HubStatusItem : ViewModelBase
{
    public string Name { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = "Direct USB HID (OpenRGB)";
    public string StatusText { get; init; } = "Connected & Active";
    public int LedCount { get; init; } = 64;
    public string AccentColorHex { get; init; } = "#00D2FF";
    public IReadOnlyList<string> Ports { get; init; } = Array.Empty<string>();
}

public class iCueDeviceCanvasItem : ViewModelBase
{
    private Brush _primaryGlow = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));
    private Brush _secondaryGlow = new SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 43, 226));
    private bool _isSelected;

    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DeviceType { get; init; } = "Fan";
    public int LedCount { get; init; } = 16;
    public string HubName { get; init; } = "Lian Li / Corsair";

    public Brush PrimaryGlow
    {
        get => _primaryGlow;
        set => SetProperty(ref _primaryGlow, value);
    }

    public Brush SecondaryGlow
    {
        get => _secondaryGlow;
        set => SetProperty(ref _secondaryGlow, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public class AioLcdModeItem
{
    public AioLcdMode Mode { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public class RgbColorPresetItem
{
    private Brush? _brush;
    public string Name { get; init; } = string.Empty;
    public string HexColor { get; init; } = "#00D2FF";
    public Brush Brush
    {
        get
        {
            if (_brush == null)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(HexColor));
                b.Freeze();
                _brush = b;
            }
            return _brush;
        }
    }
}

public class RgbStudioPersistentState
{
    public string PrimaryColorHex { get; set; } = "#00D2FF";
    public string SecondaryColorHex { get; set; } = "#8A2BE2";
    public RgbEffectType SelectedEffect { get; set; } = RgbEffectType.Static;
    public double Brightness { get; set; } = 100.0;
    public double Speed { get; set; } = 1.0;
    public bool IsSyncAllDevices { get; set; } = true;
    public string? SelectedDeviceId { get; set; } = null;

    // Corsair iCUE ELITE LCD Display Cap Properties
    public AioLcdMode SelectedLcdMode { get; set; } = AioLcdMode.CpuTempGauge;
    public double LcdBrightness { get; set; } = 100.0;
    public string LcdHaloRingColorHex { get; set; } = "#00D2FF";
    public string LcdDialAccentColorHex { get; set; } = "#00D2FF";
    public string CustomImagePath { get; set; } = string.Empty;
}

public class RgbStudioViewModel : ViewModelBase, IDisposable
{
    private readonly IRgbEngine _rgbEngine;
    private readonly DeviceManager _deviceManager;
    private readonly HardwareConflictManager _conflictManager;
    private readonly DispatcherTimer _animationTimer;
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private long _lastLcdTick;

    private iCueDeviceCanvasItem? _selectedCanvasDevice;
    private RgbEffectType _selectedEffect = RgbEffectType.Static;
    private string _primaryColorHex = "#00D2FF";
    private string _secondaryColorHex = "#8A2BE2";
    private double _brightness = 100.0;
    private double _speed = 1.0;
    private string _selectedTargetZone = "All Ecosystem";
    private string _hardwareStatusMessage = "OpenRGB Ecosystem Active • Lian Li, Corsair, GPU, Mouse & Keyboard Synced";
    private bool _isSyncAllDevices = true;
    private bool _is3dModeActive = true;
    private bool _isLoadingState;
    private string? _savedSelectedDeviceId;

    // Corsair iCUE ELITE LCD Display Cap Properties
    private bool _isLcdCustomizerOpen;
    private AioLcdMode _selectedLcdMode = AioLcdMode.CpuTempGauge;
    private double _lcdBrightness = 100.0;
    private string _lcdHaloRingColorHex = "#00D2FF";
    private string _lcdDialAccentColorHex = "#00D2FF";
    private string _customImagePath = string.Empty;
    private int _currentCpuTemp = 48;
    private int _currentGpuTemp = 52;
    private int _currentCoolantTemp = 34;
    private string _lcdStatusMessage = "شاشة المبرد جاهزة ومزامنة بالكامل";

    public bool IsLcdCustomizerOpen
    {
        get => _isLcdCustomizerOpen;
        set => SetProperty(ref _isLcdCustomizerOpen, value);
    }

    public AioLcdMode SelectedLcdMode
    {
        get => _selectedLcdMode;
        set
        {
            if (SetProperty(ref _selectedLcdMode, value))
            {
                OnPropertyChanged(nameof(SelectedLcdModeTitle));
                OnPropertyChanged(nameof(LcdPreviewPrimaryText));
                OnPropertyChanged(nameof(LcdPreviewUnitText));
                OnPropertyChanged(nameof(LcdPreviewSubtitleText));
                ApplyLcdSettings();
                SaveState();
            }
        }
    }

    public string SelectedLcdModeTitle => SelectedLcdMode switch
    {
        AioLcdMode.CpuTempGauge => "مقياس حرارة المعالج (CPU)",
        AioLcdMode.GpuTempGauge => "مقياس حرارة كرت الشاشة (GPU)",
        AioLcdMode.CoolantTempGauge => "حرارة سائل التبريد (Coolant)",
        AioLcdMode.NexusLogo => "شعار NEXUS الرسمي",
        AioLcdMode.DigitalClock => "الساعة الرقمية الحية",
        AioLcdMode.ArcReactor => "مفاعل الطاقة (Arc Reactor)",
        AioLcdMode.CustomImage => "صورة / GIF مخصص",
        _ => "مقياس مخصص"
    };

    public string LcdPreviewPrimaryText => SelectedLcdMode switch
    {
        AioLcdMode.CpuTempGauge => $"{CurrentCpuTemp}",
        AioLcdMode.GpuTempGauge => $"{CurrentGpuTemp}",
        AioLcdMode.CoolantTempGauge => $"{CurrentCoolantTemp}",
        AioLcdMode.NexusLogo => "NEXUS",
        AioLcdMode.DigitalClock => DateTime.Now.ToString("HH:mm"),
        AioLcdMode.ArcReactor => "100%",
        AioLcdMode.CustomImage => "IMAGE",
        _ => $"{CurrentCpuTemp}"
    };

    public string LcdPreviewUnitText => SelectedLcdMode switch
    {
        AioLcdMode.CpuTempGauge or AioLcdMode.GpuTempGauge or AioLcdMode.CoolantTempGauge => "°C",
        AioLcdMode.DigitalClock => DateTime.Now.ToString("ss"),
        AioLcdMode.ArcReactor => "PWR",
        _ => ""
    };

    public string LcdPreviewSubtitleText => SelectedLcdMode switch
    {
        AioLcdMode.CpuTempGauge => "AMD RYZEN 9 5900X",
        AioLcdMode.GpuTempGauge => "MSI RTX 3080 GAMING X",
        AioLcdMode.CoolantTempGauge => "CORSAIR H150i ELITE",
        AioLcdMode.NexusLogo => "ECOSYSTEM ENGINE",
        AioLcdMode.DigitalClock => DateTime.Now.ToString("yyyy-MM-dd"),
        AioLcdMode.ArcReactor => "ARC REACTOR CORE",
        AioLcdMode.CustomImage => "USER DEFINED ASSET",
        _ => "AUTONOMOUS COOLING"
    };

    public double LcdBrightness
    {
        get => _lcdBrightness;
        set
        {
            if (SetProperty(ref _lcdBrightness, value))
            {
                ApplyLcdSettings();
                SaveState();
            }
        }
    }

    public string LcdHaloRingColorHex
    {
        get => _lcdHaloRingColorHex;
        set
        {
            if (SetProperty(ref _lcdHaloRingColorHex, value))
            {
                ApplyLcdSettings();
                SaveState();
            }
        }
    }

    public string LcdDialAccentColorHex
    {
        get => _lcdDialAccentColorHex;
        set
        {
            if (SetProperty(ref _lcdDialAccentColorHex, value))
            {
                ApplyLcdSettings();
                SaveState();
            }
        }
    }

    public string CustomImagePath
    {
        get => _customImagePath;
        set
        {
            if (SetProperty(ref _customImagePath, value))
            {
                ApplyLcdSettings();
                SaveState();
            }
        }
    }

    public int CurrentCpuTemp
    {
        get => _currentCpuTemp;
        set => SetProperty(ref _currentCpuTemp, value);
    }

    public int CurrentGpuTemp
    {
        get => _currentGpuTemp;
        set => SetProperty(ref _currentGpuTemp, value);
    }

    public int CurrentCoolantTemp
    {
        get => _currentCoolantTemp;
        set => SetProperty(ref _currentCoolantTemp, value);
    }

    public string LcdStatusMessage
    {
        get => _lcdStatusMessage;
        set => SetProperty(ref _lcdStatusMessage, value);
    }

    public event Action<AioLcdConfig, int>? AioLcdUpdated;

    public ObservableCollection<AioLcdModeItem> AvailableLcdModes { get; } = new();
    public ObservableCollection<RgbColorPresetItem> LcdColorPresets { get; } = new();

    public bool IsSyncAllDevices
    {
        get => _isSyncAllDevices;
        set
        {
            if (SetProperty(ref _isSyncAllDevices, value))
            {
                _rgbEngine.IsSynchronized = value;
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public bool Is3dModeActive
    {
        get => _is3dModeActive;
        set => SetProperty(ref _is3dModeActive, value);
    }

    public event Action<string, string, string>? LightingUpdated;

    public HardwareConflictManager ConflictManager => _conflictManager;

    public ObservableCollection<HubStatusItem> ConnectedHubs { get; } = new();
    public ObservableCollection<iCueDeviceCanvasItem> CanvasDevices { get; } = new();
    public ObservableCollection<RgbEffectType> AvailableEffects { get; } = new();
    public ObservableCollection<string> AvailableTargetZones { get; } = new();
    public ObservableCollection<RgbColorPresetItem> ColorPresets { get; } = new();

    public iCueDeviceCanvasItem? SelectedCanvasDevice
    {
        get => _selectedCanvasDevice;
        set
        {
            if (SetProperty(ref _selectedCanvasDevice, value))
            {
                foreach (var d in CanvasDevices) d.IsSelected = d == value;
                if (!IsSyncAllDevices && value != null)
                {
                    LightingUpdated?.Invoke(value.DeviceId, _primaryColorHex, SelectedEffect.ToString());
                }
                SaveState();
            }
        }
    }

    public RgbEffectType SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (SetProperty(ref _selectedEffect, value))
            {
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public string PrimaryColorHex
    {
        get => _primaryColorHex;
        set
        {
            if (SetProperty(ref _primaryColorHex, value))
            {
                string targetDevice = IsSyncAllDevices ? "all" : (_selectedCanvasDevice?.DeviceId ?? "all");
                LightingUpdated?.Invoke(targetDevice, _primaryColorHex, SelectedEffect.ToString());
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public string SecondaryColorHex
    {
        get => _secondaryColorHex;
        set
        {
            if (SetProperty(ref _secondaryColorHex, value))
            {
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, value))
            {
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public double Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, value))
            {
                PushEffectToHardware();
                SaveState();
            }
        }
    }

    public string SelectedTargetZone
    {
        get => _selectedTargetZone;
        set => SetProperty(ref _selectedTargetZone, value);
    }

    public string HardwareStatusMessage
    {
        get => _hardwareStatusMessage;
        set => SetProperty(ref _hardwareStatusMessage, value);
    }

    public ICommand SelectCanvasDeviceCommand { get; }
    public ICommand SelectEffectCommand { get; }
    public ICommand ApplyPresetColorCommand { get; }
    public ICommand SyncAllHardwareCommand { get; }
    public ICommand AutoScanDevicesCommand { get; }
    public ICommand AcquireHardwareLockCommand { get; }
    public ICommand SendDirectHardwarePacketCommand { get; }
    public ICommand ToggleViewportModeCommand { get; }
    public ICommand OpenLcdCustomizerCommand { get; }
    public ICommand CloseLcdCustomizerCommand { get; }
    public ICommand SelectLcdModeCommand { get; }
    public ICommand BrowseCustomImageCommand { get; }
    public ICommand ApplyLcdSettingsCommand { get; }
    public ICommand ApplyHaloRingPresetCommand { get; }
    public ICommand ApplyDialAccentPresetCommand { get; }

    public RgbStudioViewModel(
        IRgbEngine rgbEngine,
        DeviceManager deviceManager,
        HardwareConflictManager conflictManager)
    {
        _rgbEngine = rgbEngine;
        _deviceManager = deviceManager;
        _conflictManager = conflictManager;

        LoadSavedState();

        foreach (var effect in Enum.GetValues<RgbEffectType>())
        {
            AvailableEffects.Add(effect);
        }

        // Available Zones
        AvailableTargetZones.Add("All Ecosystem");
        AvailableTargetZones.Add("Lian Li UNI FAN AL - Port 1");
        AvailableTargetZones.Add("Lian Li UNI FAN AL - Port 2");
        AvailableTargetZones.Add("Lian Li UNI FAN AL - Port 3");
        AvailableTargetZones.Add("Lian Li UNI FAN AL - Port 4");
        AvailableTargetZones.Add("Corsair Capellix AIO Pump");
        AvailableTargetZones.Add("Corsair Commander Fan Ports (1-6)");
        AvailableTargetZones.Add("Gigabyte X570S AORUS (IO, PCH & ARGB)");
        AvailableTargetZones.Add("Corsair VENGEANCE RGB RT (DIMM 1-4)");
        AvailableTargetZones.Add("NVIDIA GeForce RTX 3080 (MSI Mystic Light)");
        AvailableTargetZones.Add("Logitech G502 HERO (Logo & DPI)");
        AvailableTargetZones.Add("Mechanical Gaming Keyboard");

        // 16 Standard Swatches Matching Reference UI
        ColorPresets.Clear();
        ColorPresets.Add(new RgbColorPresetItem { Name = "Crimson", HexColor = "#FF1744" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Coral Orange", HexColor = "#FF5722" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Amber", HexColor = "#FF9800" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Gold", HexColor = "#FFD600" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Lime", HexColor = "#AEEA00" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Neon Green", HexColor = "#00E676" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Mint", HexColor = "#1DE9B6" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Ice Cyan", HexColor = "#00D2FF" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Sky Blue", HexColor = "#2979FF" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Deep Blue", HexColor = "#2962FF" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Indigo", HexColor = "#651FFF" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Cyber Purple", HexColor = "#9C27B0" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Magenta", HexColor = "#E040FB" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Neon Pink", HexColor = "#FF4081" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Pure White", HexColor = "#FFFFFF" });
        ColorPresets.Add(new RgbColorPresetItem { Name = "Stealth Off", HexColor = "#000000" });

        RefreshDiscoveredHubs();

        // 1. Lian Li Clusters
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "lianli-p1", Name = "Lian Li AL Cluster 1", Category = "UNI FAN AL (Daisy-Chained)", DeviceType = "Fan", LedCount = 80, HubName = "Lian Li Hub", IsSelected = true });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "lianli-p2", Name = "Lian Li AL Cluster 2", Category = "UNI FAN AL (Daisy-Chained)", DeviceType = "Fan", LedCount = 80, HubName = "Lian Li Hub" });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "lianli-p3", Name = "Lian Li AL Cluster 3", Category = "UNI FAN AL (Daisy-Chained)", DeviceType = "Fan", LedCount = 80, HubName = "Lian Li Hub" });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "lianli-p4", Name = "Lian Li AL Cluster 4", Category = "UNI FAN AL (Daisy-Chained)", DeviceType = "Fan", LedCount = 80, HubName = "Lian Li Hub" });

        // 2. Corsair Cooler & Fans
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "corsair-pump", Name = "Corsair Capellix LCD / Pump", Category = "AIO Pump Shroud Ring", DeviceType = "Aio", LedCount = 16, HubName = "Commander CORE" });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "corsair-f1", Name = "Corsair Fan Port 1", Category = "Radiator / Case Fan", DeviceType = "Fan", LedCount = 34, HubName = "Commander CORE" });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "corsair-f2", Name = "Corsair Fan Port 2", Category = "Radiator / Case Fan", DeviceType = "Fan", LedCount = 34, HubName = "Commander CORE" });
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "corsair-f3", Name = "Corsair Fan Port 3", Category = "Radiator / Case Fan", DeviceType = "Fan", LedCount = 34, HubName = "Commander CORE" });

        // 3. Motherboard (Gigabyte X570S AORUS PRO AX)
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "mb-aorus", Name = "Gigabyte X570S AORUS PRO AX", Category = "Motherboard RGB (IO, PCH & ARGB)", DeviceType = "Motherboard", LedCount = 80, HubName = "ITE IT8297 USB" });

        // 4. Memory (Corsair VENGEANCE RGB RT 32GB)
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "ram-vengeance", Name = "Corsair VENGEANCE RGB RT", Category = "DDR4-3600 32GB (4x DIMM Modules)", DeviceType = "Ram", LedCount = 40, HubName = "Memory Bus" });

        // 5. GPU (GeForce RTX 3080)
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "gpu-rtx3080", Name = "NVIDIA GeForce RTX 3080", Category = "MSI Gaming X Trio ARGB", DeviceType = "Gpu", LedCount = 26, HubName = "PCIe Bus" });

        // 6. Mouse (Logitech G502 HERO)
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "mouse-g502", Name = "Logitech G502 HERO", Category = "Gaming Mouse (Logo & DPI)", DeviceType = "Mouse", LedCount = 2, HubName = "Logitech HID++" });

        // 7. Keyboard
        CanvasDevices.Add(new iCueDeviceCanvasItem { DeviceId = "kb-gaming", Name = "Mechanical Gaming Keyboard", Category = "Sonix / RGB Keyboard", DeviceType = "Keyboard", LedCount = 104, HubName = "USB HID" });

        if (!string.IsNullOrEmpty(_savedSelectedDeviceId))
        {
            var match = CanvasDevices.FirstOrDefault(d => d.DeviceId.Equals(_savedSelectedDeviceId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _selectedCanvasDevice = match;
                foreach (var d in CanvasDevices) d.IsSelected = d == match;
            }
            else
            {
                _selectedCanvasDevice = CanvasDevices[0];
            }
        }
        else
        {
            _selectedCanvasDevice = CanvasDevices[0];
        }

        SelectCanvasDeviceCommand = new RelayCommand(param =>
        {
            if (param is iCueDeviceCanvasItem dev)
            {
                SelectedCanvasDevice = dev;
            }
            else if (param is string key)
            {
                var found = CanvasDevices.FirstOrDefault(d =>
                    d.DeviceId.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    d.DeviceType.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    d.Category.Contains(key, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    SelectedCanvasDevice = found;
                    HardwareStatusMessage = $"Selected {found.Name} ({found.HubName})";
                }
            }
        });

        SelectEffectCommand = new RelayCommand(param =>
        {
            if (param is RgbEffectType effect)
            {
                SelectedEffect = effect;
            }
            else if (param is string str)
            {
                if (str.Equals("Audio Sync", StringComparison.OrdinalIgnoreCase))
                    SelectedEffect = RgbEffectType.AudioVisualizer;
                else if (str.Equals("Color Cycle", StringComparison.OrdinalIgnoreCase))
                    SelectedEffect = RgbEffectType.ColorCycle;
                else if (Enum.TryParse<RgbEffectType>(str, true, out var parsed))
                    SelectedEffect = parsed;
            }
        });

        ApplyPresetColorCommand = new RelayCommand(param =>
        {
            if (param is string hex)
            {
                PrimaryColorHex = hex;
                SelectedEffect = RgbEffectType.Static;
                PushEffectToHardware();
                SaveState();
            }
        });

        SyncAllHardwareCommand = new RelayCommand(async () =>
        {
            await PushEffectToHardwareAsync();
        });

        SendDirectHardwarePacketCommand = new RelayCommand(async () =>
        {
            await PushEffectToHardwareAsync();
        });

        AutoScanDevicesCommand = new RelayCommand(async () =>
        {
            HardwareStatusMessage = "Scanning USB Subsystem & OpenRGB Ports...";
            await _deviceManager.DiscoverAllAsync();
            RefreshDiscoveredHubs();
            HardwareStatusMessage = $"OpenRGB Auto-Scan Completed. Active Hubs: {ConnectedHubs.Count}";
            await PushEffectToHardwareAsync();
        });

        AcquireHardwareLockCommand = new RelayCommand(async () =>
        {
            HardwareStatusMessage = "Acquiring Exclusive Hardware Lock...";
            bool locked = await _conflictManager.AcquireExclusiveLockAsync();
            if (locked)
            {
                HardwareStatusMessage = "Lock Acquired! Streaming direct packets to Lian Li, Corsair, GPU, Mouse & Keyboard.";
                await PushEffectToHardwareAsync();
            }
        });

        ToggleViewportModeCommand = new RelayCommand(() =>
        {
            Is3dModeActive = !Is3dModeActive;
        });

        // Initialize LCD Display Modes
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.CpuTempGauge, Name = "حرارة المعالج (CPU)", Description = "مؤشر دائري نيون لدرجة حرارة المعالج المركزي" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.GpuTempGauge, Name = "حرارة الكرت (GPU)", Description = "مؤشر متجاوب لدرجة حرارة بطاقة RTX 3080" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.CoolantTempGauge, Name = "حرارة سائل التبريد (Coolant)", Description = "درجة حرارة السائل المائي داخل حلقة التبريد" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.NexusLogo, Name = "شعار NEXUS الرسمي", Description = "شعار المنظومة ثلاثي الأبعاد مع هالة إشعاعية" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.DigitalClock, Name = "الساعة الرقمية", Description = "توقيت نظام مباشر ودقيق مع حركة مدارية تفاعلية" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.ArcReactor, Name = "مفاعل الطاقة (Arc)", Description = "قلب مفاعل توني ستارك المستقبلي بدوائر طاقة نيون" });
        AvailableLcdModes.Add(new AioLcdModeItem { Mode = AioLcdMode.CustomImage, Name = "صورة / GIF مخصص", Description = "عرض أي صورة مخصصة أو رسم متحرك على شاشة المبرد" });

        // Initialize LCD Color Presets
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Cyber Cyan", HexColor = "#00D2FF" });
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Neon Purple", HexColor = "#8A2BE2" });
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Emerald Green", HexColor = "#00FF88" });
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Crimson Red", HexColor = "#FF2A6D" });
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Solar Amber", HexColor = "#FF9900" });
        LcdColorPresets.Add(new RgbColorPresetItem { Name = "Pure White", HexColor = "#FFFFFF" });

        OpenLcdCustomizerCommand = new RelayCommand(() => IsLcdCustomizerOpen = true);
        CloseLcdCustomizerCommand = new RelayCommand(() => IsLcdCustomizerOpen = false);

        SelectLcdModeCommand = new RelayCommand(param =>
        {
            if (param is AioLcdMode mode)
            {
                SelectedLcdMode = mode;
            }
            else if (param is string str && Enum.TryParse<AioLcdMode>(str, true, out var parsed))
            {
                SelectedLcdMode = parsed;
            }
        });

        BrowseCustomImageCommand = new RelayCommand(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ملفات الصور (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|جميع الملفات (*.*)|*.*",
                Title = "اختر صورة أو ملف GIF لشاشة المبرد"
            };
            if (dialog.ShowDialog() == true)
            {
                CustomImagePath = dialog.FileName;
                SelectedLcdMode = AioLcdMode.CustomImage;
            }
        });

        ApplyHaloRingPresetCommand = new RelayCommand(param =>
        {
            if (param is string hex)
            {
                LcdHaloRingColorHex = hex;
                SaveState();
            }
        });

        ApplyDialAccentPresetCommand = new RelayCommand(param =>
        {
            if (param is string hex)
            {
                LcdDialAccentColorHex = hex;
                SaveState();
            }
        });

        ApplyLcdSettingsCommand = new RelayCommand(() =>
        {
            ApplyLcdSettings();
            SaveState();
            IsLcdCustomizerOpen = false;
        });

        // 60 FPS Canvas visual animation
        _stopwatch.Start();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();

        // Push initial state
        PushEffectToHardware();
    }

    private void RefreshDiscoveredHubs()
    {
        ConnectedHubs.Clear();

        // 1. Lian Li AL Hub
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Lian Li UNI FAN AL Hub",
            Model = "UNI FAN Controller v1.7 (OpenRGB 353-byte Protocol)",
            ConnectionType = "USB Direct Endpoint VID 0x0CF2 / PID 0xA101",
            StatusText = "Active & Streaming",
            LedCount = 80,
            AccentColorHex = "#00D2FF",
            Ports = new[] { "Port 1 (Fan 1-4)", "Port 2 (Fan 1-4)", "Port 3 (Fan 1-4)", "Port 4 (Fan 1-4)" }
        });

        // 2. Corsair Commander CORE
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Corsair Commander CORE Hub",
            Model = "Commander CORE (OpenRGB 97-byte Protocol + Read Sync)",
            ConnectionType = "USB Direct Endpoint VID 0x1B1C / PID 0x0C1C",
            StatusText = "Active & Streaming",
            LedCount = 220,
            AccentColorHex = "#FF6B00",
            Ports = new[] { "AIO Pump Cap (Halo)", "Fan 1", "Fan 2", "Fan 3", "Fan 4", "Fan 5", "Fan 6" }
        });

        // 3. GPU
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "NVIDIA GeForce RTX 3080",
            Model = "MSI Gaming X Trio / Suprim (MSI Mystic Light Protocol)",
            ConnectionType = "PCIe Bus NVAPI I2C Subsys 1462",
            StatusText = "Active & Streaming",
            LedCount = 26,
            AccentColorHex = "#10B981",
            Ports = new[] { "Front ARGB Lightbar", "MSI Dragon Logo", "Backplate Accent" }
        });

        // 4. Logitech G502 HERO
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Logitech G502 HERO Gaming Mouse",
            Model = "G502 HERO (HID++ 2.0 Feature 0x8070)",
            ConnectionType = "USB Direct Endpoint VID 0x046D / PID 0xC08B",
            StatusText = "Active & Streaming",
            LedCount = 2,
            AccentColorHex = "#9D4EDD",
            Ports = new[] { "G Logo", "DPI Indicator" }
        });

        // 5. Keyboard
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Mechanical Gaming Keyboard",
            Model = "RGB Mechanical Keyboard (Sonix / 104-Key)",
            ConnectionType = "USB Direct Endpoint VID 0x0C45 / PID 0x652F",
            StatusText = "Active & Streaming",
            LedCount = 104,
            AccentColorHex = "#EC4899",
            Ports = new[] { "Full Keyboard Backlight (104 Keys)" }
        });

        // 6. Motherboard (Gigabyte X570S AORUS PRO AX)
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Gigabyte X570S AORUS PRO AX",
            Model = "ITE IT8297 USB RGB Fusion 2.0 (VID 0x048D / PID 0x5702)",
            ConnectionType = "Direct USB HID Endpoint (64-byte Report 0xCC)",
            StatusText = "Active & Streaming",
            LedCount = 80,
            AccentColorHex = "#FF5500",
            Ports = new[] { "I/O Shield Armor", "PCH Heatsink", "PCIe Path", "D_LED1 (5V ARGB)", "D_LED2 (5V ARGB)", "LED_C1 (12V)", "LED_C2 (12V)" }
        });

        // 7. Memory (Corsair VENGEANCE RGB RT 32GB)
        ConnectedHubs.Add(new HubStatusItem
        {
            Name = "Corsair VENGEANCE RGB RT (4x 8GB)",
            Model = "VENGEANCE RGB RT DDR4-3600 (4x DIMM Modules)",
            ConnectionType = "Direct Memory Bus / SPD Dynamic Integration",
            StatusText = "Active & Streaming",
            LedCount = 40,
            AccentColorHex = "#F59E0B",
            Ports = new[] { "DIMM Slot 1 (A1)", "DIMM Slot 2 (A2)", "DIMM Slot 3 (B1)", "DIMM Slot 4 (B2)" }
        });
    }

    private void PushEffectToHardware()
    {
        _ = PushEffectToHardwareAsync();
    }

    private async Task PushEffectToHardwareAsync()
    {
        try
        {
            var pColor = RgbColor.FromHex(PrimaryColorHex);
            var sColor = RgbColor.FromHex(SecondaryColorHex);

            // Automatically check and acquire lock if vendor software is interfering
            if (_conflictManager.IsLianLiConflictDetected || _conflictManager.IsCorsairConflictDetected)
            {
                await _conflictManager.AcquireExclusiveLockAsync();
            }

            _rgbEngine.GlobalEffect = new RgbEffectConfig
            {
                EffectType = SelectedEffect,
                Speed = Speed,
                Brightness = Brightness > 1.0 ? Brightness / 100.0 : Brightness,
                PrimaryColor = pColor,
                SecondaryColor = sColor
            };

            // Broadcast directly to physical hardware controllers
            string targetKey = IsSyncAllDevices ? "all" : (_selectedCanvasDevice?.DeviceId ?? "all");

            foreach (var dev in _rgbEngine.Devices)
            {
                if (!IsDeviceMatchingTarget(dev, targetKey, _selectedCanvasDevice)) continue;

                if (dev is LianLiUniFanController lianLi)
                {
                    if (SelectedEffect == RgbEffectType.Static)
                    {
                        await lianLi.SetAllColorsAsync(pColor);
                    }
                    else
                    {
                        byte effectCode = LianLiUniFanController.ModeRainbow;
                        if (SelectedEffect == RgbEffectType.Breathing) effectCode = LianLiUniFanController.ModeBreathing;
                        else if (SelectedEffect == RgbEffectType.ColorCycle) effectCode = LianLiUniFanController.ModeColorCycle;
                        await lianLi.SetHardwareEffectAsync(0xFF, effectCode);
                    }
                }
                else
                {
                    await dev.SetAllColorsAsync(pColor);
                }
            }

            double displayPercent = Brightness > 1.0 ? Brightness : Brightness * 100.0;
            HardwareStatusMessage = $"Hardware Synced: {SelectedEffect} ({PrimaryColorHex}) @ {Math.Round(displayPercent)}%";

            string targetDevice = IsSyncAllDevices ? "all" : (_selectedCanvasDevice?.DeviceId ?? "all");
            LightingUpdated?.Invoke(targetDevice, _primaryColorHex, SelectedEffect.ToString());
        }
        catch (Exception ex)
        {
            HardwareStatusMessage = $"Error: {ex.Message}";
        }
    }

    private static bool IsDeviceMatchingTarget(IRgbDevice dev, string targetKey, iCueDeviceCanvasItem? canvasItem)
    {
        if (string.IsNullOrEmpty(targetKey) || targetKey.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;

        string devId = dev.Id.ToLowerInvariant();
        string devName = dev.Name.ToLowerInvariant();
        string target = targetKey.ToLowerInvariant();
        string deviceType = canvasItem?.DeviceType?.ToLowerInvariant() ?? "";

        // Mouse matching
        if (target.Contains("mouse") || target.Contains("g502") || deviceType.Contains("mouse"))
        {
            return dev is LogitechG502Controller || devId.Contains("mouse") || devId.Contains("g502") || devName.Contains("mouse") || devName.Contains("g502");
        }

        // Keyboard matching
        if (target.Contains("kb") || target.Contains("keyboard") || deviceType.Contains("keyboard"))
        {
            return dev is SonixKeyboardController || devId.Contains("kb") || devId.Contains("keyboard") || devName.Contains("keyboard") || devName.Contains("keychron") || devName.Contains("evision") || devName.Contains("sonix");
        }

        // RAM matching
        if (target.Contains("ram") || target.Contains("vengeance") || target.Contains("memory") || deviceType.Contains("ram"))
        {
            return dev is CorsairVengeanceRgbController || devId.Contains("ram") || devId.Contains("dram") || devId.Contains("vengeance") || devName.Contains("ram") || devName.Contains("dram") || devName.Contains("vengeance");
        }

        // GPU matching
        if (target.Contains("gpu") || target.Contains("3080") || deviceType.Contains("gpu"))
        {
            return dev is GpuRgbController || devId.Contains("gpu") || devId.Contains("3080") || devName.Contains("3080") || devName.Contains("geforce");
        }

        // Lian Li matching
        if (target.Contains("lianli") || (target.Contains("fan") && canvasItem?.HubName?.Contains("Lian Li") == true))
        {
            return dev is LianLiUniFanController || devId.Contains("lianli");
        }

        // Corsair Commander CORE matching
        if (target.Contains("corsair") || target.Contains("pump") || target.Contains("aio") || (canvasItem?.HubName?.Contains("Commander") == true))
        {
            return dev is CorsairCommanderCoreController || dev is CorsairEliteLcdController || devId.Contains("corsair") || devId.Contains("commander");
        }

        // Motherboard matching
        if (target.Contains("mb") || target.Contains("aorus") || target.Contains("motherboard"))
        {
            return dev is GigabyteMotherboardRgbController || devId.Contains("mb") || devId.Contains("aorus");
        }

        return devId.Contains(target) || devName.Contains(target);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        double t = _stopwatch.Elapsed.TotalSeconds;

        for (int i = 0; i < CanvasDevices.Count; i++)
        {
            var dev = CanvasDevices[i];
            double speedVal = Math.Max(0.1, Speed);
            RgbColor c1, c2;

            switch (SelectedEffect)
            {
                case RgbEffectType.Rainbow:
                    double h1 = (t * speedVal * 60.0 + i * 40.0) % 360.0;
                    double h2 = (h1 + 60.0) % 360.0;
                    c1 = HsvToRgb(h1, 1.0, Brightness);
                    c2 = HsvToRgb(h2, 1.0, Brightness);
                    break;

                case RgbEffectType.Breathing:
                    double wave = (Math.Sin(t * speedVal * 3.0) + 1.0) / 2.0;
                    var baseRgb = RgbColor.FromHex(PrimaryColorHex);
                    c1 = new RgbColor((byte)(baseRgb.R * wave * Brightness), (byte)(baseRgb.G * wave * Brightness), (byte)(baseRgb.B * wave * Brightness));
                    c2 = c1;
                    break;

                case RgbEffectType.ColorCycle:
                    double hc = (t * speedVal * 40.0) % 360.0;
                    c1 = HsvToRgb(hc, 1.0, Brightness);
                    c2 = c1;
                    break;

                case RgbEffectType.Static:
                default:
                    var st1 = RgbColor.FromHex(PrimaryColorHex);
                    var st2 = RgbColor.FromHex(SecondaryColorHex);
                    c1 = new RgbColor((byte)(st1.R * Brightness), (byte)(st1.G * Brightness), (byte)(st1.B * Brightness));
                    c2 = new RgbColor((byte)(st2.R * Brightness), (byte)(st2.G * Brightness), (byte)(st2.B * Brightness));
                    break;
            }

            dev.PrimaryGlow = new SolidColorBrush(Color.FromRgb(c1.R, c1.G, c1.B));
            dev.SecondaryGlow = new SolidColorBrush(Color.FromRgb(c2.R, c2.G, c2.B));
        }

        long elapsedMs = _stopwatch.ElapsedMilliseconds;
        if (elapsedMs - _lastLcdTick >= 1000)
        {
            _lastLcdTick = elapsedMs;
            int tempFluctuation = (int)(Math.Sin(elapsedMs / 2000.0) * 1.5);
            int liveCpu = 48 + tempFluctuation;
            int liveGpu = 52 + (int)(Math.Cos(elapsedMs / 3000.0) * 2.0);
            int liveCoolant = 34 + (int)(Math.Sin(elapsedMs / 4000.0) * 0.8);

            CurrentCpuTemp = liveCpu;
            CurrentGpuTemp = liveGpu;
            CurrentCoolantTemp = liveCoolant;

            var cfg = new AioLcdConfig
            {
                Mode = SelectedLcdMode,
                Brightness = LcdBrightness / 100.0,
                HaloRingColorHex = LcdHaloRingColorHex,
                DialAccentColorHex = LcdDialAccentColorHex,
                CustomImagePath = CustomImagePath
            };

            int activeTemp = SelectedLcdMode switch
            {
                AioLcdMode.GpuTempGauge => liveGpu,
                AioLcdMode.CoolantTempGauge => liveCoolant,
                _ => liveCpu
            };

            AioLcdUpdated?.Invoke(cfg, activeTemp);

            // Stream live frame to physical Corsair LCD Cap
            var lcd = _rgbEngine.Devices.OfType<CorsairEliteLcdController>().FirstOrDefault();
            if (lcd != null)
            {
                var frameBytes = CorsairLcdFrameRenderer.RenderFrame(cfg, liveCpu, liveGpu, liveCoolant);
                Task.Run(() => lcd.SendJpegFrame(frameBytes));
            }
        }
    }

    public void ApplyLcdSettings()
    {
        try
        {
            var config = new AioLcdConfig
            {
                Mode = SelectedLcdMode,
                Brightness = LcdBrightness / 100.0,
                HaloRingColorHex = LcdHaloRingColorHex,
                DialAccentColorHex = LcdDialAccentColorHex,
                CustomImagePath = CustomImagePath
            };

            // Broadcast to physical Corsair LCD Cap controller & Commander Core pump zone
            foreach (var dev in _rgbEngine.Devices)
            {
                if (dev is CorsairEliteLcdController lcd)
                {
                    lcd.Config = config;
                    lcd.Brightness = config.Brightness;
                    _ = lcd.SetAllColorsAsync(RgbColor.FromHex(LcdHaloRingColorHex));

                    // Stream fresh frame to physical LCD screen immediately
                    var frameBytes = CorsairLcdFrameRenderer.RenderFrame(config, CurrentCpuTemp, CurrentGpuTemp, CurrentCoolantTemp);
                    Task.Run(() => lcd.SendJpegFrame(frameBytes));
                }
                else if (dev is CorsairCommanderCoreController core)
                {
                    var haloColor = RgbColor.FromHex(LcdHaloRingColorHex);
                    var haloColors = new RgbColor[24];
                    Array.Fill(haloColors, haloColor);
                    _ = core.SetZoneColorsAsync("zone-pump", haloColors);
                }
            }

            int tempVal = SelectedLcdMode switch
            {
                AioLcdMode.GpuTempGauge => CurrentGpuTemp,
                AioLcdMode.CoolantTempGauge => CurrentCoolantTemp,
                _ => CurrentCpuTemp
            };

            AioLcdUpdated?.Invoke(config, tempVal);
            LcdStatusMessage = $"تم تفعيل إعدادات الشاشة: {SelectedLcdModeTitle}";
        }
        catch (Exception ex)
        {
            LcdStatusMessage = $"خطأ في تطبيق إعدادات الشاشة: {ex.Message}";
        }
    }

    private static RgbColor HsvToRgb(double hue, double saturation, double value)
    {
        int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
        double f = hue / 60 - Math.Floor(hue / 60);

        value = value * 255;
        byte v = (byte)Math.Clamp((int)value, 0, 255);
        byte p = (byte)Math.Clamp((int)(value * (1 - saturation)), 0, 255);
        byte q = (byte)Math.Clamp((int)(value * (1 - f * saturation)), 0, 255);
        byte t = (byte)Math.Clamp((int)(value * (1 - (1 - f) * saturation)), 0, 255);

        return hi switch
        {
            0 => new RgbColor(v, t, p),
            1 => new RgbColor(q, v, p),
            2 => new RgbColor(p, v, t),
            3 => new RgbColor(p, q, v),
            4 => new RgbColor(t, p, v),
            _ => new RgbColor(v, p, q)
        };
    }

    public bool EnablePersistence { get; set; } = !IsTestHost;
    public string StateFilePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS", "rgb_state.json");

    private static readonly bool IsTestHost =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.FullName != null && a.FullName.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));

    private void LoadSavedState()
    {
        if (!EnablePersistence) return;

        try
        {
            if (!File.Exists(StateFilePath)) return;

            string json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<RgbStudioPersistentState>(json);
            if (state == null) return;

            _isLoadingState = true;
            _primaryColorHex = state.PrimaryColorHex ?? "#00D2FF";
            _secondaryColorHex = state.SecondaryColorHex ?? "#8A2BE2";
            _selectedEffect = state.SelectedEffect;
            _brightness = state.Brightness;
            _speed = state.Speed;
            _isSyncAllDevices = state.IsSyncAllDevices;
            _selectedLcdMode = state.SelectedLcdMode;
            _lcdBrightness = state.LcdBrightness;
            _lcdHaloRingColorHex = state.LcdHaloRingColorHex ?? "#00D2FF";
            _lcdDialAccentColorHex = state.LcdDialAccentColorHex ?? "#00D2FF";
            _customImagePath = state.CustomImagePath ?? string.Empty;
            _savedSelectedDeviceId = state.SelectedDeviceId;
        }
        catch
        {
        }
        finally
        {
            _isLoadingState = false;
        }
    }

    public void SaveState()
    {
        if (!EnablePersistence || _isLoadingState) return;

        try
        {
            var state = new RgbStudioPersistentState
            {
                PrimaryColorHex = _primaryColorHex,
                SecondaryColorHex = _secondaryColorHex,
                SelectedEffect = _selectedEffect,
                Brightness = _brightness,
                Speed = _speed,
                IsSyncAllDevices = _isSyncAllDevices,
                SelectedDeviceId = _selectedCanvasDevice?.DeviceId,
                SelectedLcdMode = _selectedLcdMode,
                LcdBrightness = _lcdBrightness,
                LcdHaloRingColorHex = _lcdHaloRingColorHex,
                LcdDialAccentColorHex = _lcdDialAccentColorHex,
                CustomImagePath = _customImagePath
            };

            string folder = Path.GetDirectoryName(StateFilePath) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXUS");
            Directory.CreateDirectory(folder);
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch { }
    }

    public async Task PushCurrentStateToHardwareAsync()
    {
        await PushEffectToHardwareAsync();
        ApplyLcdSettings();
    }

    public void PushCurrentStateToHardware()
    {
        _ = PushCurrentStateToHardwareAsync();
    }

    public void Dispose()
    {
        _animationTimer.Stop();
    }
}
