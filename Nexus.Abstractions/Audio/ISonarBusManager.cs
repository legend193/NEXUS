using System.Collections.Generic;

namespace Nexus.Abstractions.Audio;

public enum SonarBusType
{
    Master,
    Game,
    Chat,
    Media,
    Mic,
    Aux
}

public interface ISonarBus
{
    SonarBusType BusType { get; }
    string Name { get; }
    float Volume { get; set; } // 0.0f to 1.0f (or 0-100)
    bool IsMuted { get; set; }
    bool IsSolo { get; set; }
    double PeakLeft { get; }
    double PeakRight { get; }
    string? BoundDeviceId { get; set; }
}

public interface ISonarBusManager
{
    IReadOnlyList<ISonarBus> Buses { get; }
    double ChatMixValue { get; set; } // -1.0 (100% Game, 0% Chat) to 0.0 (Balanced) to +1.0 (0% Game, 100% Chat)
    float GameChatMixMultiplier { get; }
    float ChatChatMixMultiplier { get; }

    void AssignProcessToBus(int processId, SonarBusType busType);
    void AssignProcessNameToBus(string processName, SonarBusType busType);
    SonarBusType GetAssignedBus(int processId);
    float CalculateEffectiveSessionVolume(int processId, float appVolume);
    void UpdatePeaks(SonarBusType busType, double peakLeft, double peakRight);
}
