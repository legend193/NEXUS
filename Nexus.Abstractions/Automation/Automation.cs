namespace Nexus.Abstractions.Automation;

public enum AutomationTriggerType
{
    ProcessLaunched,
    ProcessExited,
    CpuTemperatureAbove,
    GpuTemperatureAbove,
    SystemIdle,
    TimeOfDay
}

public enum AutomationActionType
{
    ActivateProfile,
    SetRgbEffect,
    SetFanMode,
    SetAudioOutput,
    SendNotification
}

public class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Rule";
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 1;

    // Trigger
    public AutomationTriggerType TriggerType { get; set; }
    public string TriggerTarget { get; set; } = string.Empty; // e.g. "game.exe", or "75" (degrees)
    
    // Action
    public AutomationActionType ActionType { get; set; }
    public string ActionPayload { get; set; } = string.Empty; // e.g. ProfileId, EffectName, etc.

    public DateTime? LastTriggeredUtc { get; set; }
}

public interface IAutomationEngine
{
    IReadOnlyList<AutomationRule> Rules { get; }
    bool IsRunning { get; }

    event EventHandler<AutomationRule>? RuleTriggered;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task AddRuleAsync(AutomationRule rule);
    Task UpdateRuleAsync(AutomationRule rule);
    Task DeleteRuleAsync(string ruleId);
    Task ToggleRuleAsync(string ruleId, bool isEnabled);
}
