using System;
using Nexus.App.ViewModels;
using Nexus.Engines.Audio;
using Xunit;

namespace Nexus.Tests;

public class SonarPopoutTests
{
    [Fact]
    public void AudioStudioViewModel_PopoutCommands_ManageStateCorrectly()
    {
        var mockAudioEngine = new AudioEngine();
        using var vm = new AudioStudioViewModel(mockAudioEngine);

        Assert.False(vm.IsPopoutActive);
        Assert.True(vm.IsPopoutAlwaysOnTop);

        // Toggle Topmost
        vm.ToggleAlwaysOnTopCommand.Execute(null);
        Assert.False(vm.IsPopoutAlwaysOnTop);

        vm.ToggleAlwaysOnTopCommand.Execute(null);
        Assert.True(vm.IsPopoutAlwaysOnTop);

        // Open Popout
        vm.PopoutMixerCommand.Execute(null);
        Assert.True(vm.IsPopoutActive);

        // Close Popout
        vm.ClosePopoutCommand.Execute(null);
        Assert.False(vm.IsPopoutActive);
    }
}
