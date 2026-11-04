using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Abstractions.Cooling;
using Nexus.App.ViewModels;
using Nexus.Core.DeviceManager;
using Nexus.Core.EventBus;
using Nexus.Engines.FanControl;
using Xunit;

namespace Nexus.Tests;

public class CoolingStudioOverhaulTests
{
    [Fact]
    public void FanCurve_ZeroRpm_EvaluatesToZeroBelowThreshold()
    {
        var curve = new FanCurve
        {
            IsZeroRpmEnabled = true,
            ZeroRpmThresholdCelsius = 45.0,
            MinFanSpeedPercent = 25.0,
            Points = new List<FanCurvePoint>
            {
                new(45, 30),
                new(60, 50),
                new(80, 100)
            }
        };

        Assert.Equal(0.0, curve.Evaluate(35.0));
        Assert.Equal(0.0, curve.Evaluate(45.0));
        Assert.True(curve.Evaluate(50.0) > 0);
    }

    [Fact]
    public void FanCurve_EmergencySafetyOverride_Forces100Percent()
    {
        var curve = new FanCurve
        {
            EmergencyTempCelsius = 85.0,
            Points = new List<FanCurvePoint>
            {
                new(30, 20),
                new(70, 60),
                new(84, 75)
            }
        };

        Assert.Equal(100.0, curve.Evaluate(85.0));
        Assert.Equal(100.0, curve.Evaluate(92.0));
    }

    [Fact]
    public void CoolingViewModel_PumpModes_ToggleProperly()
    {
        var bus = new EventBus();
        var fanEngine = new FanControlEngine(bus);
        var devMgr = new DeviceManager(bus);

        using var vm = new CoolingViewModel(fanEngine, devMgr, bus);

        vm.SetPumpModeCommand.Execute(PumpSpeedMode.Quiet);
        Assert.Equal(PumpSpeedMode.Quiet, vm.PumpMode);
        Assert.True(vm.IsPumpQuiet);
        Assert.Contains("2200", vm.PumpModeTitle);

        vm.SetPumpModeCommand.Execute(PumpSpeedMode.Extreme);
        Assert.Equal(PumpSpeedMode.Extreme, vm.PumpMode);
        Assert.True(vm.IsPumpExtreme);
        Assert.Contains("2850", vm.PumpModeTitle);

        vm.SetPumpModeCommand.Execute(PumpSpeedMode.Balanced);
        Assert.Equal(PumpSpeedMode.Balanced, vm.PumpMode);
        Assert.True(vm.IsPumpBalanced);
        Assert.Contains("2650", vm.PumpModeTitle);
    }

    [Fact]
    public void CoolingViewModel_ChannelSelection_SelectsIndividualChannel()
    {
        var bus = new EventBus();
        var fanEngine = new FanControlEngine(bus);
        var devMgr = new DeviceManager(bus);

        using var vm = new CoolingViewModel(fanEngine, devMgr, bus);
        Assert.NotNull(vm.SelectedChannel);

        var bottomCh = vm.FanChannels.First(c => c.Id == "ll-bottom");
        vm.SelectChannelCommand.Execute(bottomCh);

        Assert.Equal("ll-bottom", vm.SelectedChannel.Id);
        Assert.True(bottomCh.IsSelected);
        Assert.False(vm.FanChannels.First(c => c.Id == "ll-side").IsSelected);
    }

    [Fact]
    public void CoolingViewModel_PresetCurves_ApplyConfiguredPoints()
    {
        var bus = new EventBus();
        var fanEngine = new FanControlEngine(bus);
        var devMgr = new DeviceManager(bus);

        using var vm = new CoolingViewModel(fanEngine, devMgr, bus);

        vm.SelectPresetCommand.Execute("Performance");
        Assert.Equal("Performance", vm.SelectedPreset);
        Assert.Equal(40, vm.EditableCurvePoints[0].FanSpeedPercent);
        Assert.Equal(100, vm.EditableCurvePoints[4].FanSpeedPercent);

        vm.SelectPresetCommand.Execute("ZeroRpm");
        Assert.Equal("ZeroRpm", vm.SelectedPreset);
        Assert.Equal(0, vm.EditableCurvePoints[0].FanSpeedPercent);
        Assert.Equal(0, vm.EditableCurvePoints[1].FanSpeedPercent);
    }
}
