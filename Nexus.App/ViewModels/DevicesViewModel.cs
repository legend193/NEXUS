using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Devices;
using Nexus.Core.DeviceManager;
using Nexus.Integrations.Logitech;

namespace Nexus.App.ViewModels;

public class DeviceDisplayCard
{
    public IDevice RawDevice { get; init; } = null!;
    public string Name => RawDevice.Name;
    public string Manufacturer => RawDevice.Manufacturer;
    public string Model => RawDevice.Model;
    public string SerialNumber => RawDevice.SerialNumber;
    public string FirmwareVersion => RawDevice.FirmwareVersion ?? "v1.0";
    public string StatusText => RawDevice.Status.ToString();
    public string Provider => RawDevice.IntegrationProvider;
    public string IconKey { get; init; } = "IconHardware";
    public string InterfaceBus { get; init; } = "USB HID Direct";
    public string CapabilitiesSummary { get; init; } = "RGB Control";
}

public class DevicesViewModel : ViewModelBase
{
    private readonly DeviceManager _deviceManager;
    private DeviceDisplayCard? _selectedDevice;
    private LogitechG502Controller? _mouseController;

    private int _currentDpi = 1600;
    private int _customDpi = 1600;
    private int _selectedPollingRate = 1000;
    private bool _isMouseConnected;

    public ObservableCollection<DeviceDisplayCard> DeviceCards { get; } = new();

    public DeviceDisplayCard? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }

    public int CurrentDpi
    {
        get => _currentDpi;
        set
        {
            if (SetProperty(ref _currentDpi, value))
            {
                CustomDpi = value;
            }
        }
    }

    public int CustomDpi
    {
        get => _customDpi;
        set => SetProperty(ref _customDpi, value);
    }

    public int SelectedPollingRate
    {
        get => _selectedPollingRate;
        set => SetProperty(ref _selectedPollingRate, value);
    }

    public bool IsMouseConnected
    {
        get => _isMouseConnected;
        set => SetProperty(ref _isMouseConnected, value);
    }

    public ObservableCollection<int> DpiStages { get; } = new() { 400, 800, 1600, 3200, 6400 };
    public ObservableCollection<int> PollingRates { get; } = new() { 125, 250, 500, 1000 };

    public ICommand RefreshDevicesCommand { get; }
    public ICommand SetDpiStageCommand { get; }
    public ICommand ApplyCustomDpiCommand { get; }
    public ICommand SetPollingRateCommand { get; }

    public DevicesViewModel(DeviceManager deviceManager)
    {
        _deviceManager = deviceManager;

        RefreshDevicesCommand = new RelayCommand(async () =>
        {
            await _deviceManager.DiscoverAllAsync();
            RefreshList();
        });

        SetDpiStageCommand = new RelayCommand(param =>
        {
            if (param != null && int.TryParse(param.ToString(), out int stageDpi))
            {
                CurrentDpi = stageDpi;
                if (_mouseController != null)
                {
                    _ = _mouseController.SetDpiAsync(stageDpi);
                }
            }
        });

        ApplyCustomDpiCommand = new RelayCommand(async () =>
        {
            CurrentDpi = CustomDpi;
            if (_mouseController != null)
            {
                await _mouseController.SetDpiAsync(CustomDpi);
            }
        });

        SetPollingRateCommand = new RelayCommand(param =>
        {
            if (param != null && int.TryParse(param.ToString(), out int rate))
            {
                SelectedPollingRate = rate;
                if (_mouseController != null)
                {
                    _ = _mouseController.SetPollingRateAsync(rate);
                }
            }
        });

        _deviceManager.DeviceAdded += (_, _) => RefreshList();
        RefreshList();
    }

    private void RefreshList()
    {
        DeviceCards.Clear();
        _mouseController = null;

        foreach (var dev in _deviceManager.Devices)
        {
            string icon = "IconHardware";
            string iface = "USB HID Direct";
            string caps = "RGB & Fan Control";

            if (dev is LogitechG502Controller mouseCtrl)
            {
                _mouseController = mouseCtrl;
                IsMouseConnected = true;
                _currentDpi = mouseCtrl.CurrentDpi;
                _customDpi = mouseCtrl.CurrentDpi;
                _selectedPollingRate = mouseCtrl.PollingRate;
                OnPropertyChanged(nameof(CurrentDpi));
                OnPropertyChanged(nameof(CustomDpi));
                OnPropertyChanged(nameof(SelectedPollingRate));
            }

            if (dev.Name.Contains("Lian Li", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconFanBlade";
                iface = "USB HID (VID 0x0CF2 / PID 0xA101)";
                caps = "4x Fan Clusters + ARGB PWM";
            }
            else if (dev.Name.Contains("Corsair", StringComparison.OrdinalIgnoreCase) && dev.Type != DeviceType.Memory)
            {
                icon = "IconAioPump";
                iface = "USB HID (VID 0x1B1C / PID 0x0C1C)";
                caps = "Capellix Pump + 6 Fan Ports";
            }
            else if (dev.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconGpuCard";
                iface = "PCI Express 4.0 x16 (NVAPI I2C)";
                caps = "Mystic Light RGB & Fan Profiles";
            }
            else if (dev.Name.Contains("G502", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconMouseDevice";
                iface = "Logitech HID++ 2.0 (1000 Hz)";
                caps = "HERO 25.6K Sensor + RGB Logo/DPI";
            }
            else if (dev.Name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconKeyboardDevice";
                iface = "Sonix HID (COL04 Matrix)";
                caps = "Full Per-Key RGB Dynamic Matrix";
            }
            else if (dev.Name.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("AORUS", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconHardware";
                iface = "USB HID (VID 0x048D / PID 0x5702)";
                caps = "ITE IT8297 8-Zone RGB + Digital ARGB";
            }
            else if (dev.Name.Contains("Vengeance", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("RAM", StringComparison.OrdinalIgnoreCase) || dev.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
            {
                icon = "IconRamStick";
                iface = "Corsair iCUE Protocol / SMBus";
                caps = "4x DIMM 10-LED Addressable Lightbar";
            }

            DeviceCards.Add(new DeviceDisplayCard
            {
                RawDevice = dev,
                IconKey = icon,
                InterfaceBus = iface,
                CapabilitiesSummary = caps
            });
        }

        if (SelectedDevice == null && DeviceCards.Count > 0)
        {
            SelectedDevice = DeviceCards[0];
        }
    }
}
