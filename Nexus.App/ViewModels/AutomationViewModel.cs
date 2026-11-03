using System.Collections.ObjectModel;
using System.Windows.Input;
using Nexus.Abstractions.Automation;
using Nexus.Core.Automation;

namespace Nexus.App.ViewModels;

public class AutomationRuleCard : ViewModelBase
{
    private readonly AutomationRule _rule;

    public string Id => _rule.Id;
    public string Name => _rule.Name;
    public string TriggerType => _rule.TriggerType.ToString();
    public string ActionType => _rule.ActionType.ToString();
    public string TriggerTarget => _rule.TriggerTarget;
    public string ActionPayload => _rule.ActionPayload;

    public bool IsEnabled
    {
        get => _rule.IsEnabled;
        set
        {
            if (_rule.IsEnabled != value)
            {
                _rule.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string TriggerDescription
    {
        get => _rule.TriggerType switch
        {
            AutomationTriggerType.ProcessLaunched => $"Process '{TriggerTarget}' launches",
            AutomationTriggerType.ProcessExited => $"Process '{TriggerTarget}' exits",
            AutomationTriggerType.CpuTemperatureAbove => $"CPU Temp rises above {TriggerTarget}°C",
            AutomationTriggerType.GpuTemperatureAbove => $"GPU Temp rises above {TriggerTarget}°C",
            AutomationTriggerType.SystemIdle => $"System is idle for {TriggerTarget} seconds",
            _ => $"Condition: {TriggerTarget}"
        };
    }

    public string ActionDescription
    {
        get => _rule.ActionType switch
        {
            AutomationActionType.ActivateProfile => $"Activate Profile '{ActionPayload}'",
            AutomationActionType.SetRgbEffect => $"Set RGB Lighting to '{ActionPayload}'",
            AutomationActionType.SetFanMode => $"Set Fan Mode to '{ActionPayload}'",
            _ => $"Action: {ActionPayload}"
        };
    }

    public AutomationRuleCard(AutomationRule rule)
    {
        _rule = rule;
    }

    public AutomationRule RawRule => _rule;
}

public class AutomationViewModel : ViewModelBase
{
    private readonly AutomationEngine _automationEngine;

    public ObservableCollection<AutomationRuleCard> RuleCards { get; } = new();

    public ICommand ToggleRuleCommand { get; }
    public ICommand AddRuleCommand { get; }

    public AutomationViewModel(AutomationEngine automationEngine)
    {
        _automationEngine = automationEngine;

        RefreshList();

        ToggleRuleCommand = new RelayCommand(async param =>
        {
            if (param is AutomationRuleCard card)
            {
                card.IsEnabled = !card.IsEnabled;
                await _automationEngine.ToggleRuleAsync(card.Id, card.IsEnabled);
            }
        });

        AddRuleCommand = new RelayCommand(async () =>
        {
            var newRule = new AutomationRule
            {
                Name = $"Thermal Safeguard Rule #{RuleCards.Count + 1}",
                TriggerType = AutomationTriggerType.GpuTemperatureAbove,
                TriggerTarget = "78",
                ActionType = AutomationActionType.ActivateProfile,
                ActionPayload = "performance-preset",
                IsEnabled = true
            };
            await _automationEngine.AddRuleAsync(newRule);
            RefreshList();
        });
    }

    private void RefreshList()
    {
        RuleCards.Clear();
        if (_automationEngine.Rules.Count == 0)
        {
            var rule1 = new AutomationRule
            {
                Name = "Cyberpunk 2077 Gaming Trigger",
                TriggerType = AutomationTriggerType.ProcessLaunched,
                TriggerTarget = "Cyberpunk 2077",
                ActionType = AutomationActionType.ActivateProfile,
                ActionPayload = "gaming-preset",
                IsEnabled = true
            };
            var rule2 = new AutomationRule
            {
                Name = "Thermal Cooling Safeguard",
                TriggerType = AutomationTriggerType.CpuTemperatureAbove,
                TriggerTarget = "75",
                ActionType = AutomationActionType.ActivateProfile,
                ActionPayload = "performance-preset",
                IsEnabled = true
            };
            var rule3 = new AutomationRule
            {
                Name = "Idle Power Saver",
                TriggerType = AutomationTriggerType.SystemIdle,
                TriggerTarget = "900",
                ActionType = AutomationActionType.ActivateProfile,
                ActionPayload = "silent-preset",
                IsEnabled = true
            };

            _ = _automationEngine.AddRuleAsync(rule1);
            _ = _automationEngine.AddRuleAsync(rule2);
            _ = _automationEngine.AddRuleAsync(rule3);

            RuleCards.Add(new AutomationRuleCard(rule1));
            RuleCards.Add(new AutomationRuleCard(rule2));
            RuleCards.Add(new AutomationRuleCard(rule3));
            return;
        }

        foreach (var r in _automationEngine.Rules)
        {
            RuleCards.Add(new AutomationRuleCard(r));
        }
    }
}
