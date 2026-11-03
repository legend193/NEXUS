using Microsoft.Extensions.Logging;
using Nexus.Abstractions.Automation;
using Nexus.Abstractions.EventBus;
using Nexus.Abstractions.Profiles;
using Nexus.Abstractions.Sensors;

namespace Nexus.Core.Automation;

public class AutomationEngine : IAutomationEngine
{
    private readonly List<AutomationRule> _rules = new();
    private readonly object _lock = new();
    private readonly IProfileManager _profileManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AutomationEngine>? _logger;
    private IDisposable? _snapshotSubscription;
    private bool _isRunning;

    public IReadOnlyList<AutomationRule> Rules
    {
        get
        {
            lock (_lock) return _rules.ToList();
        }
    }

    public bool IsRunning => _isRunning;

    public event EventHandler<AutomationRule>? RuleTriggered;

    public AutomationEngine(IProfileManager profileManager, IEventBus eventBus, ILogger<AutomationEngine>? logger = null)
    {
        _profileManager = profileManager;
        _eventBus = eventBus;
        _logger = logger;

        // Sample initial automation rule: Trigger aggressive cooling/gaming when GPU temp goes above 75C
        _rules.Add(new AutomationRule
        {
            Id = "auto-gpu-safety",
            Name = "Emergency Cooling on High GPU Temp",
            IsEnabled = true,
            Priority = 10,
            TriggerType = AutomationTriggerType.GpuTemperatureAbove,
            TriggerTarget = "78",
            ActionType = AutomationActionType.ActivateProfile,
            ActionPayload = "performance-preset"
        });

        _rules.Add(new AutomationRule
        {
            Id = "auto-cpu-safety",
            Name = "High CPU Temp Safety Boost",
            IsEnabled = true,
            Priority = 9,
            TriggerType = AutomationTriggerType.CpuTemperatureAbove,
            TriggerTarget = "80",
            ActionType = AutomationActionType.ActivateProfile,
            ActionPayload = "performance-preset"
        });
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return Task.CompletedTask;
        _isRunning = true;

        _snapshotSubscription = _eventBus.Subscribe<SystemTelemetrySnapshot>(OnTelemetrySnapshotReceived);
        _logger?.LogInformation("Automation Engine started with {Count} rules.", _rules.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        _snapshotSubscription?.Dispose();
        _snapshotSubscription = null;
        _logger?.LogInformation("Automation Engine stopped.");
        return Task.CompletedTask;
    }

    public Task AddRuleAsync(AutomationRule rule)
    {
        lock (_lock) _rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task UpdateRuleAsync(AutomationRule rule)
    {
        lock (_lock)
        {
            var idx = _rules.FindIndex(r => r.Id == rule.Id);
            if (idx >= 0) _rules[idx] = rule;
        }
        return Task.CompletedTask;
    }

    public Task DeleteRuleAsync(string ruleId)
    {
        lock (_lock) _rules.RemoveAll(r => r.Id == ruleId);
        return Task.CompletedTask;
    }

    public Task ToggleRuleAsync(string ruleId, bool isEnabled)
    {
        lock (_lock)
        {
            var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule != null) rule.IsEnabled = isEnabled;
        }
        return Task.CompletedTask;
    }

    private async Task OnTelemetrySnapshotReceived(SystemTelemetrySnapshot snapshot)
    {
        if (!_isRunning) return;

        List<AutomationRule> activeRules;
        lock (_lock)
        {
            activeRules = _rules.Where(r => r.IsEnabled).OrderByDescending(r => r.Priority).ToList();
        }

        foreach (var rule in activeRules)
        {
            bool isTriggered = false;

            if (rule.TriggerType == AutomationTriggerType.CpuTemperatureAbove &&
                snapshot.CpuTemperatureCelsius.HasValue &&
                double.TryParse(rule.TriggerTarget, out var cpuThreshold))
            {
                if (snapshot.CpuTemperatureCelsius.Value >= cpuThreshold)
                {
                    isTriggered = true;
                }
            }
            else if (rule.TriggerType == AutomationTriggerType.GpuTemperatureAbove &&
                     snapshot.GpuTemperatureCelsius.HasValue &&
                     double.TryParse(rule.TriggerTarget, out var gpuThreshold))
            {
                if (snapshot.GpuTemperatureCelsius.Value >= gpuThreshold)
                {
                    isTriggered = true;
                }
            }

            if (isTriggered)
            {
                // Debounce trigger (minimum 15s between re-triggers)
                if (rule.LastTriggeredUtc.HasValue && (DateTime.UtcNow - rule.LastTriggeredUtc.Value).TotalSeconds < 15)
                {
                    continue;
                }

                rule.LastTriggeredUtc = DateTime.UtcNow;
                _logger?.LogInformation("Automation rule '{Name}' triggered!", rule.Name);
                RuleTriggered?.Invoke(this, rule);

                if (rule.ActionType == AutomationActionType.ActivateProfile && !string.IsNullOrEmpty(rule.ActionPayload))
                {
                    await _profileManager.ActivateProfileAsync(rule.ActionPayload);
                }
            }
        }
    }
}
