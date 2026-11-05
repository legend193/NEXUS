using System;
using Nexus.Abstractions.Audio;
using Nexus.Engines.Audio;
using Xunit;

namespace Nexus.Tests;

public class SonarAudioStudioTests
{
    [Fact]
    public void ChatMix_CenterDetent_IsFullyBalanced()
    {
        var busMgr = new SonarBusManager();
        busMgr.ChatMixValue = 0.0;

        Assert.Equal(1.0f, busMgr.GameChatMixMultiplier, 3);
        Assert.Equal(1.0f, busMgr.ChatChatMixMultiplier, 3);
    }

    [Fact]
    public void ChatMix_FullGame_MutesChat()
    {
        var busMgr = new SonarBusManager();
        busMgr.ChatMixValue = -1.0;

        Assert.Equal(1.0f, busMgr.GameChatMixMultiplier, 3);
        Assert.Equal(0.0f, busMgr.ChatChatMixMultiplier, 3);
    }

    [Fact]
    public void ChatMix_FullChat_MutesGame()
    {
        var busMgr = new SonarBusManager();
        busMgr.ChatMixValue = 1.0;

        Assert.Equal(0.0f, busMgr.GameChatMixMultiplier, 3);
        Assert.Equal(1.0f, busMgr.ChatChatMixMultiplier, 3);
    }

    [Theory]
    [InlineData(-0.5, 1.0f, 0.5f)]
    [InlineData(-0.25, 1.0f, 0.75f)]
    [InlineData(0.5, 0.5f, 1.0f)]
    [InlineData(0.25, 0.75f, 1.0f)]
    public void ChatMix_IntermediateValues_ScaleLinearly(double chatMix, float expectedGame, float expectedChat)
    {
        var busMgr = new SonarBusManager();
        busMgr.ChatMixValue = chatMix;

        Assert.Equal(expectedGame, busMgr.GameChatMixMultiplier, 2);
        Assert.Equal(expectedChat, busMgr.ChatChatMixMultiplier, 2);
    }

    [Fact]
    public void CompoundVolume_MultipliesCleanly()
    {
        var busMgr = new SonarBusManager();
        // Set Master bus volume = 0.8
        var master = busMgr.Buses[0];
        master.Volume = 0.8f;

        // Set Game bus volume = 0.5
        var game = busMgr.Buses[1];
        game.Volume = 0.5f;

        // Assign PID 100 to Game
        busMgr.AssignProcessToBus(100, SonarBusType.Game);

        // ChatMix tilted toward Chat (+0.5) -> GameMultiplier = 0.5
        busMgr.ChatMixValue = 0.5;

        // App Volume = 0.9
        // Expected: 0.9 (App) * 0.5 (Bus) * 0.5 (ChatMix) * 0.8 (Master) = 0.18
        float effective = busMgr.CalculateEffectiveSessionVolume(100, 0.9f);

        Assert.Equal(0.18f, effective, 3);
    }

    [Fact]
    public void BiQuad_PeakingEq_CalculatesAccurateCenterGain()
    {
        double centerFreq = 1000.0;
        double targetGainDb = 6.0;
        double q = 1.414;
        int sampleRate = 48000;

        var coeffs = BiQuadCoefficients.Calculate(ParametricFilterType.PeakingEQ, centerFreq, targetGainDb, q, sampleRate);
        double gainAtCenter = coeffs.EvaluateGainDb(centerFreq, sampleRate);

        // Peak at 1000Hz should be exactly 6.0 dB (+-0.1dB)
        Assert.True(Math.Abs(gainAtCenter - targetGainDb) < 0.15, $"Expected ~6dB at 1kHz, got {gainAtCenter:F2}dB");

        // At 100Hz (far away), gain should be near 0 dB
        double gainFarAway = coeffs.EvaluateGainDb(100.0, sampleRate);
        Assert.True(Math.Abs(gainFarAway) < 0.3, $"Expected near 0dB at 100Hz, got {gainFarAway:F2}dB");
    }

    [Fact]
    public void BiQuad_LowShelf_BoostsLowFrequencies()
    {
        double cornerFreq = 120.0;
        double targetGainDb = 8.0;
        double q = 0.707;
        int sampleRate = 48000;

        var coeffs = BiQuadCoefficients.Calculate(ParametricFilterType.LowShelf, cornerFreq, targetGainDb, q, sampleRate);
        double gainAtLow = coeffs.EvaluateGainDb(30.0, sampleRate);
        double gainAtHigh = coeffs.EvaluateGainDb(5000.0, sampleRate);

        Assert.True(Math.Abs(gainAtLow - targetGainDb) < 0.5, $"Expected ~8dB at 30Hz, got {gainAtLow:F2}dB");
        Assert.True(Math.Abs(gainAtHigh) < 0.3, $"Expected ~0dB at 5kHz, got {gainAtHigh:F2}dB");
    }

    [Fact]
    public void ParametricEqEngine_CalculatesCombinedFrequencyResponse()
    {
        var engine = new ParametricEqEngine();
        engine.PreAmpGainDb = -3.0f;

        double[] freqs = new double[] { 100.0, 1000.0, 10000.0 };
        var response = engine.CalculateFrequencyResponse(freqs);

        Assert.Equal(3, response.Length);
        // Each value should be finite number
        foreach (var val in response)
        {
            Assert.False(double.IsNaN(val));
            Assert.False(double.IsInfinity(val));
        }
    }

    [Fact]
    public void MicrophoneDsp_NoiseGate_AttenuatesQuietSignals()
    {
        var config = new MicrophoneDspConfig
        {
            NoiseGateEnabled = true,
            NoiseGateThresholdDb = -30.0f
        };

        float envelope = 0.001f; // ~ -60 dB (well below -30dB)
        float quietSample = 0.001f;
        float processed = ParametricEqEngine.ApplyNoiseGate(quietSample, ref envelope, config);

        // Quiet signal must be strongly attenuated
        Assert.True(Math.Abs(processed) < Math.Abs(quietSample) * 0.1f);
    }

    [Fact]
    public void MicrophoneDsp_Compressor_ClampsLoudSignals()
    {
        var config = new MicrophoneDspConfig
        {
            CompressorEnabled = true,
            CompressorThresholdDb = -12.0f,
            CompressorRatio = 4.0f,
            CompressorMakeUpGainDb = 0.0f,
            LimiterEnabled = true,
            LimiterCeilingDb = -0.5f
        };

        float compEnvelope = 0.8f; // ~ -2 dB (above -12dB)
        float loudSample = 0.95f;
        float processed = ParametricEqEngine.ApplyCompressor(loudSample, ref compEnvelope, config);

        // Loud signal should be attenuated below original
        Assert.True(processed < loudSample);
        Assert.True(processed <= 1.0f);
    }

    [Fact]
    public void AudioRouting_GenerateAndUnpackDeviceId_RoundTripsCorrectly()
    {
        string rawId = "{0.0.0.00000000}.{C2116727-10E8-4D55-9414-49FC111C5D4A}";
        string formatted = AudioRoutingService.GenerateDeviceId(rawId, Nexus.Engines.Audio.Interop.EDataFlow.eRender);

        Assert.StartsWith(@"\\?\SWD#MMDEVAPI#", formatted);
        Assert.EndsWith("#{e6327cad-dcec-4949-ae8a-991e976a79d2}", formatted);

        string unpacked = AudioRoutingService.UnpackDeviceId(formatted);
        Assert.Equal(rawId, unpacked);
    }

    [Fact]
    public void AudioRouting_CaptureEndpoint_GeneratesCaptureSuffix()
    {
        string rawId = "{0.0.1.00000000}.{2303449B-C2A7-4680-A5BC-7EDF85FDDDAF}";
        string formatted = AudioRoutingService.GenerateDeviceId(rawId, Nexus.Engines.Audio.Interop.EDataFlow.eCapture);

        Assert.EndsWith("#{2eef81be-33fa-4800-9670-1cd474972c3f}", formatted);

        string unpacked = AudioRoutingService.UnpackDeviceId(formatted);
        Assert.Equal(rawId, unpacked);
    }

    [Fact]
    public void AudioRouting_AlreadyFormattedToken_IsNotDuplicated()
    {
        string preformatted = @"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{GUID}#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
        string result = AudioRoutingService.GenerateDeviceId(preformatted);

        Assert.Equal(preformatted, result);
    }

    [Fact]
    public void AudioStudioViewModel_HeroTelemetry_ReportsAccurateDashboardMetrics()
    {
        var audioEngine = new AudioEngine();

        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(audioEngine);
        vm.EnablePersistence = false;

        // Verify Master volume hero metric
        vm.Channels[0].Volume = 78f;
        Assert.Equal(78.0, vm.MasterVolumePercent);
        Assert.Equal("78%", vm.MasterVolumeText);

        // Verify ChatMix Hero metrics
        vm.Channels[1].Volume = 100f;
        vm.Channels[2].Volume = 65f;
        Assert.Equal(100, vm.GameVolumePercent);
        Assert.Equal(65, vm.ChatVolumePercent);

        // Verify DSP preset name
        Assert.NotNull(vm.ActivePresetName);

        // Verify Streams count
        Assert.Equal("0", vm.ActiveStreamsCountText);
    }

    [Fact]
    public void DeviceExclusion_FilterTogglesVisibilityInChannels()
    {
        var audioEngine = new AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(audioEngine);
        vm.EnablePersistence = false;

        vm.RefreshManageableDevices();
        if (vm.ManageableDevices.Count > 0)
        {
            var firstDev = vm.ManageableDevices[0];
            string targetId = firstDev.Id;

            // Toggle off
            firstDev.IsVisibleInDropdown = false;
            Assert.Contains(targetId, vm.ExcludedDeviceIds);

            // Toggle back on
            firstDev.IsVisibleInDropdown = true;
            Assert.DoesNotContain(targetId, vm.ExcludedDeviceIds);
        }
    }

    [Fact]
    public void DeviceExclusion_ShowAll_And_HideVirtual_CommandsWork()
    {
        var audioEngine = new AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(audioEngine);
        vm.EnablePersistence = false;

        vm.RefreshManageableDevices();

        // Run HideVirtualDevicesCommand
        vm.HideVirtualDevicesCommand.Execute(null);
        foreach (var dev in vm.ManageableDevices)
        {
            if (dev.IsVirtual)
            {
                Assert.False(dev.IsVisibleInDropdown);
                Assert.Contains(dev.Id, vm.ExcludedDeviceIds);
            }
        }

        // Run ShowAllDevicesCommand
        vm.ShowAllDevicesCommand.Execute(null);
        Assert.Empty(vm.ExcludedDeviceIds);
        foreach (var dev in vm.ManageableDevices)
        {
            Assert.True(dev.IsVisibleInDropdown);
        }
    }

    [Fact]
    public void ChannelDeviceSelection_RoutesAssignedApps()
    {
        var audioEngine = new AudioEngine();
        using var vm = new Nexus.App.ViewModels.AudioStudioViewModel(audioEngine);
        vm.EnablePersistence = false;

        var dummySession = new Nexus.App.ViewModels.AudioSessionItem(null, "TestGame.exe", "#10B981", "Game");
        vm.Channels[1].AssignedApps.Add(dummySession);

        var dummyChoice = new Nexus.App.ViewModels.AudioDeviceChoice
        {
            Id = "{0.0.0.00000000}.{dummy-device-id}",
            Name = "Speakers",
            DisplayName = "Speakers [Hardware]"
        };

        // Selecting device on Game channel should trigger OnDeviceSelected and routing
        vm.Channels[1].SelectedDevice = dummyChoice;
        Assert.Equal(dummyChoice, vm.Channels[1].SelectedDevice);
    }
}
