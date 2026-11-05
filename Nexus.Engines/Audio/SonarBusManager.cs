using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Nexus.Abstractions.Audio;

namespace Nexus.Engines.Audio;

public class SonarBus : ISonarBus
{
    public SonarBusType BusType { get; }
    public string Name { get; }
    public float Volume { get; set; } = 1.0f; // 0.0 to 1.0
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public double PeakLeft { get; private set; }
    public double PeakRight { get; private set; }
    public string? BoundDeviceId { get; set; }

    public SonarBus(SonarBusType busType, string name, float initialVol = 1.0f)
    {
        BusType = busType;
        Name = name;
        Volume = initialVol;
    }

    public void SetPeaks(double left, double right)
    {
        PeakLeft = left;
        PeakRight = right;
    }
}

public class SonarBusManager : ISonarBusManager
{
    private readonly List<SonarBus> _buses = new();
    private readonly ConcurrentDictionary<int, SonarBusType> _processBusMap = new();
    private readonly ConcurrentDictionary<string, SonarBusType> _processNameBusMap = new(StringComparer.OrdinalIgnoreCase);

    private double _chatMixValue = 0.0; // -1.0 to +1.0
    private float _gameMultiplier = 1.0f;
    private float _chatMultiplier = 1.0f;

    public IReadOnlyList<ISonarBus> Buses => _buses;

    public double ChatMixValue
    {
        get => _chatMixValue;
        set
        {
            _chatMixValue = Math.Clamp(value, -1.0, 1.0);
            CalculateMultipliers();
        }
    }

    public float GameChatMixMultiplier => _gameMultiplier;
    public float ChatChatMixMultiplier => _chatMultiplier;

    public SonarBusManager()
    {
        _buses.Add(new SonarBus(SonarBusType.Master, "Master", 0.85f));
        _buses.Add(new SonarBus(SonarBusType.Game, "Game", 0.80f));
        _buses.Add(new SonarBus(SonarBusType.Chat, "Chat", 0.85f));
        _buses.Add(new SonarBus(SonarBusType.Media, "Media", 0.60f));
        _buses.Add(new SonarBus(SonarBusType.Mic, "Mic", 0.85f));
        _buses.Add(new SonarBus(SonarBusType.Aux, "Aux", 0.50f));

        CalculateMultipliers();
    }

    private void CalculateMultipliers()
    {
        // Algorithm:
        // When ChatMix <= 0 (toward Game): Game remains at 100%, Chat attenuates down to 0% at -1.0
        // When ChatMix > 0 (toward Chat): Chat remains at 100%, Game attenuates down to 0% at +1.0
        if (_chatMixValue <= 0.0)
        {
            _gameMultiplier = 1.0f;
            _chatMultiplier = (float)(1.0 + _chatMixValue);
        }
        else
        {
            _chatMultiplier = 1.0f;
            _gameMultiplier = (float)(1.0 - _chatMixValue);
        }

        _gameMultiplier = Math.Clamp(_gameMultiplier, 0.0f, 1.0f);
        _chatMultiplier = Math.Clamp(_chatMultiplier, 0.0f, 1.0f);
    }

    public void AssignProcessToBus(int processId, SonarBusType busType)
    {
        _processBusMap[processId] = busType;
    }

    public void AssignProcessNameToBus(string processName, SonarBusType busType)
    {
        _processNameBusMap[processName] = busType;
    }

    public SonarBusType GetAssignedBus(int processId)
    {
        if (_processBusMap.TryGetValue(processId, out var bus))
        {
            return bus;
        }

        return SonarBusType.Game;
    }

    public SonarBusType GetAssignedBus(string processName, int processId)
    {
        if (_processBusMap.TryGetValue(processId, out var bus))
        {
            return bus;
        }

        if (!string.IsNullOrEmpty(processName) && _processNameBusMap.TryGetValue(processName, out var mapped))
        {
            _processBusMap[processId] = mapped;
            return mapped;
        }

        // Auto-assign based on heuristic
        var autoBus = GuessBusForProcess(processName);
        _processBusMap[processId] = autoBus;
        return autoBus;
    }

    private static SonarBusType GuessBusForProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return SonarBusType.Game;

        string name = processName.ToLowerInvariant();
        if (name.Contains("discord") || name.Contains("teams") || name.Contains("skype") ||
            name.Contains("slack") || name.Contains("telegram") || name.Contains("zoom") ||
            name.Contains("whatsapp") || name.Contains("viber"))
        {
            return SonarBusType.Chat;
        }

        if (name.Contains("spotify") || name.Contains("chrome") || name.Contains("edge") ||
            name.Contains("firefox") || name.Contains("brave") || name.Contains("vlc") ||
            name.Contains("itunes") || name.Contains("music") || name.Contains("tidal") ||
            name.Contains("foobar2000") || name.Contains("aimp"))
        {
            return SonarBusType.Media;
        }

        return SonarBusType.Game;
    }

    public float CalculateEffectiveSessionVolume(int processId, float appVolume)
    {
        var master = _buses.FirstOrDefault(b => b.BusType == SonarBusType.Master);
        if (master?.IsMuted == true) return 0f;

        float masterVol = master?.Volume ?? 1.0f;
        var busType = GetAssignedBus(processId);
        var bus = _buses.FirstOrDefault(b => b.BusType == busType);

        if (bus?.IsMuted == true) return 0f;
        float busVol = bus?.Volume ?? 1.0f;

        float chatMixFactor = 1.0f;
        if (busType == SonarBusType.Game) chatMixFactor = _gameMultiplier;
        else if (busType == SonarBusType.Chat) chatMixFactor = _chatMultiplier;

        // Any solo bus active?
        bool anySolo = _buses.Any(b => b.IsSolo);
        if (anySolo && bus?.IsSolo != true && busType != SonarBusType.Master)
        {
            return 0f;
        }

        float effective = appVolume * busVol * chatMixFactor * masterVol;
        return Math.Clamp(effective, 0.0f, 1.0f);
    }

    public void UpdatePeaks(SonarBusType busType, double peakLeft, double peakRight)
    {
        var bus = _buses.FirstOrDefault(b => b.BusType == busType);
        bus?.SetPeaks(peakLeft, peakRight);
    }
}
