# NEXUS Ecosystem Engine

**NEXUS** is an all-in-one, ultra-lightweight, high-performance PC hardware telemetry and synchronization suite built on **.NET 10 LTS (WPF)** for Windows 11.

It provides a modern, unified replacement for heavy vendor software (Corsair iCUE, Lian Li L-Connect, MSI Center / Mystic Light, Gigabyte RGB Fusion, SteelSeries Sonar) with zero background bloat, zero forced telemetry, and direct hardware communication.

---

## Key Modules & Features

### 1. RGB Studio & 3D Rig View
- **Universal Synchronization**: Unified direct-USB and OpenRGB lighting control for Lian Li UNI FAN AL, Corsair Commander CORE, Corsair Vengeance RGB RT DDR4, MSI GeForce RTX 3080 Gaming X Trio, Gigabyte X570S AORUS motherboards, Logitech mice, and mechanical keyboards.
- **Corsair Elite LCD Display Cap Streaming**: Direct 480x480 JPEG streaming to Corsair Capellix AIO pump displays with live CPU/GPU/Coolant gauges, digital clocks, Arc Reactor animations, or custom images/GIFs without iCUE.
- **Hardware Watchdog Keepalive**: Built-in 1.2-second refresh packets that prevent hardware timeout resetting to default white.
- **Persistent State**: Colors, effects, speed, brightness, and LCD profiles persist across application reboots via local configuration.

### 2. Cooling Studio & Autonomous Fan Control
- Real-time sensor monitoring with custom fan/pump RPM curves based on CPU, GPU, or liquid coolant temperatures.
- Hardware fail-safe watchdogs automatically resetting to safe defaults on shutdown.

### 3. Sonar Audio Mixer
- Multi-channel virtual audio routing (Game, Chat, Media, Aux) with independent volume controls and parametric equalizers.

### 4. Hardware Telemetry & Automation
- Real-time CPU, GPU, RAM, Motherboard, and Storage sensors (temperatures, clocks, wattages, voltages).
- Profile automation reacting to foreground games and applications.

---

## Architecture & Tech Stack

- **Target Framework**: .NET 10.0 LTS (`net10.0-windows`)
- **UI Framework**: WPF / Modern Fluent Design
- **Architecture**: Modular multi-project solution (`Nexus.slnx`)
  - `Nexus.Abstractions`: Core domain interfaces and contracts.
  - `Nexus.Core`: Device management, event bus, profile and automation engines.
  - `Nexus.Engines`: RGB, cooling/fan control, audio, and hardware telemetry loops.
  - `Nexus.Integrations`: Native HID, SMBus, I2C, and OpenRGB hardware drivers.
  - `Nexus.Storage`: Local SQLite configuration and persistent state.
  - `Nexus.App`: WPF client application with tray icon and Mini-HUD overlay.
  - `Nexus.Tests`: Comprehensive xUnit test suite.

---

## Getting Started

### Prerequisites
- Windows 10 / 11 (64-bit)
- .NET 10.0 SDK

### Building
```powershell
dotnet build Nexus.slnx
```

### Running Tests
```powershell
dotnet test Nexus.Tests\Nexus.Tests.csproj
```

### Running the Application
```powershell
dotnet run --project Nexus.App\Nexus.App.csproj
```

---

## License
MIT License
