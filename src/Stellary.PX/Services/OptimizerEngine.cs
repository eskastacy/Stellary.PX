using Stellary.PX.Core;
using Stellary.PX.Models;
using Stellary.PX.Services;

namespace Stellary.PX.Services;

/// <summary>Runs the optimizer pipeline off the UI thread and returns step outcomes.</summary>
public sealed class OptimizerEngine
{
    public SettingsService Settings { get; }
    public EmulatorService Emulator { get; } = new();
    public PriorityService Priority { get; } = new();
    public TimerService Timer { get; } = new();
    public PowerPlanService Power { get; } = new();
    public RegistryTweaksService Registry { get; } = new();
    public CacheService Cache { get; } = new();
    public DriverService Drivers { get; } = new();
    public ToolScanService Tools { get; } = new();
    public UncapService Uncap { get; } = new();

    public OptimizerEngine(SettingsService settings) => Settings = settings;

    public sealed record RunReport(EmulatorTarget Target, IReadOnlyList<StepResult> Steps);

    public async Task<RunReport> ApplyAsync()
    {
        return await Task.Run(() =>
        {
            var s = Settings.Current;
            var steps = new List<StepResult>();
            var target = Emulator.Detect();
            Infrastructure.AppLog.Info("engine", $"apply start (target={target.DisplayName}, running={target.Running})");

            if (target.Running)
            {
                if (target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase))
                    steps.Add(StepResult.Skip("Emulator priority", $"Already {target.Priority}"));
                else
                    steps.Add(Priority.SetAboveNormal(target.Pid));
            }
            else
                steps.Add(StepResult.Skip("Emulator priority", "Target not running"));

            steps.Add(s.LockTimer ? Timer.Lock(s.TimerResolutionMs) : StepResult.Skip("Timer resolution", "Disabled"));

            if (s.ApplyPowerPlan)
            {
                var result = Power.ActivateUltimate(out var prior);
                if (result.Ok && string.IsNullOrWhiteSpace(s.LastPowerPlanGuid) && prior is not null)
                    s.LastPowerPlanGuid = prior;
                steps.Add(result);
            }
            else steps.Add(StepResult.Skip("Power plan", "Disabled"));

            steps.Add(s.ApplyRegistryTweaks ? Registry.Apply(s) : StepResult.Skip("Gaming registry", "Disabled"));

            Settings.Save();
            Infrastructure.AppLog.Info("engine", "apply complete");
            return new RunReport(target, steps);
        });
    }

    public async Task<RunReport> RestoreAsync()
    {
        return await Task.Run(() =>
        {
            var s = Settings.Current;
            var steps = new List<StepResult>();
            var target = Emulator.Detect();

            if (target.Running && target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase))
                steps.Add(Priority.Restore(target.Pid));
            else if (target.Running)
                steps.Add(StepResult.Skip("Emulator priority", $"No boost to restore ({target.Priority})"));
            else
                steps.Add(StepResult.Skip("Emulator priority", "Target not running"));

            steps.Add(Timer.Release());
            var powerRestore = Power.Restore(s.LastPowerPlanGuid);
            if (powerRestore.Ok) s.LastPowerPlanGuid = null;
            steps.Add(powerRestore);
            steps.Add(Registry.Restore(s));
            Settings.Save();

            Infrastructure.AppLog.Info("engine", "restore complete");
            return new RunReport(target, steps);
        });
    }
}
