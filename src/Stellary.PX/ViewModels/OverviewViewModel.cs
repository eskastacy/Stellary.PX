using System.Collections.ObjectModel;
using System.Windows.Threading;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Models;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class OverviewViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _tick;
    private readonly PipelineRow _rowTarget = new("Emulator target", "process discovery");
    private readonly PipelineRow _rowPriority = new("Process priority", "SetPriorityClass");
    private readonly PipelineRow _rowTimer = new("Frame timer", "NtSetTimerResolution");
    private readonly PipelineRow _rowPower = new("Power scheme", "powercfg /setactive");
    private readonly PipelineRow _rowRegistry = new("Gaming registry", "HKCU · GameDVR");

    private bool _busy;
    private string _headline = "Engine stand-by.";
    private string _statusNote = "No pipeline run this session.";
    private string _targetText = "Scanning…";
    private string _cpuText = "—";
    private string _ramText = "—";
    private string _timerText = "—";
    private string _powerText = "—";
    private string _priorityText = "—";
    private string _cacheText = "—";
    private int _frame;
    private bool _engaged;
    private bool _lastRunPartial;
    private bool _runActive;
    private bool _priorityBlocked;
    private int _priorityPid = -1;
    private EmulatorTarget _target = new();

    public OverviewViewModel(AppServices services)
    {
        _services = services;
        ChartCpu = new ObservableCollection<double>();
        ChartRam = new ObservableCollection<double>();
        Pipeline = new ObservableCollection<PipelineRow> { _rowTarget, _rowPriority, _rowTimer, _rowPower, _rowRegistry };

        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !IsBusy);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, OnTick, Dispatcher.CurrentDispatcher);
    }

    public override string Title => "Overview";
    public override string Eyebrow => "CONTROL";

    public ObservableCollection<double> ChartCpu { get; }
    public ObservableCollection<double> ChartRam { get; }
    public ObservableCollection<PipelineRow> Pipeline { get; }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public string PrimaryActionText => "Start optimizer";
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string StatusNote { get => _statusNote; private set => Set(ref _statusNote, value); }
    public string TargetText { get => _targetText; private set => Set(ref _targetText, value); }
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }
    public string RamText { get => _ramText; private set => Set(ref _ramText, value); }
    public string TimerText { get => _timerText; private set => Set(ref _timerText, value); }
    public string PowerText { get => _powerText; private set => Set(ref _powerText, value); }
    public string PriorityText { get => _priorityText; private set => Set(ref _priorityText, value); }
    public string CacheText { get => _cacheText; private set => Set(ref _cacheText, value); }
    public double CpuValue { get => _cpuValue; private set => Set(ref _cpuValue, value); }
    private double _cpuValue;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { StartCommand.RaiseCanExecuteChanged(); RestoreCommand.RaiseCanExecuteChanged(); } } }
    public bool IsEngaged { get => _engaged; private set => Set(ref _engaged, value); }

    public override void Activate()
    {
        _tick.Start();
        RefreshStatic();
    }

    public override void Deactivate() => _tick.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var (cpu, ram, used, total) = NativeMethods.SampleSystem();
        CpuValue = cpu;
        CpuText = $"{cpu:0}%";
        RamText = $"{used:0.0} / {total:0.0} GB";

        ChartCpu.Add(Math.Round(cpu, 1));
        ChartRam.Add(Math.Round(ram, 1));
        while (ChartCpu.Count > 120) ChartCpu.RemoveAt(0);
        while (ChartRam.Count > 120) ChartRam.RemoveAt(0);

        var ms = _services.Engine.Timer.CurrentMs;
        TimerText = ms > 0 ? $"{ms:0.000} ms" : "—";
        _rowTimer.Detail = _services.Engine.Timer.IsHeld
            ? $"{_services.Engine.Timer.HeldMs:0.000} ms held"
            : ms > 0 ? $"{ms:0.000} ms system" : "unavailable";
        _rowTimer.On = _services.Engine.Timer.IsHeld;

        _frame++;
        if (_frame % 4 == 0) RefreshTarget();
        if (_frame % 30 == 0) RefreshPower();
        if (_frame % 8 == 0) RefreshCache();

        UpdateHeadline();
    }

    private void RefreshStatic()
    {
        RefreshTarget();
        RefreshPower();
        RefreshCache();
        var s = _services.Settings.Current;
        _rowRegistry.On = _services.Engine.Registry.AnyBackup(s);
        _rowRegistry.Detail = _rowRegistry.On ? $"{s.RegistryBackup.Count} values tracked" : "not applied";
        UpdateHeadline();
    }

    private void UpdateHeadline()
    {
        if (IsBusy) return;

        IsEngaged = _runActive ||
                    _services.Engine.Timer.IsHeld ||
                    _target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase);

        if (!_target.Running)
            Headline = IsEngaged ? "Pipeline active · no emulator." : "Engine stand-by.";
        else if (IsEngaged && _lastRunPartial)
            Headline = "Pipeline partially engaged.";
        else
            Headline = IsEngaged ? "Pipeline engaged." : "Engine stand-by.";
    }

    private void RefreshTarget()
    {
        _target = _services.Engine.Emulator.Detect();
        if (!_target.Running)
        {
            _priorityPid = -1;
            _priorityBlocked = false;
        }
        else if (_target.Pid != _priorityPid)
        {
            _priorityPid = _target.Pid;
            _priorityBlocked = false;
        }

        // A successful manual boost or an externally raised class clears the block.
        if (_target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase))
            _priorityBlocked = false;

        TargetText = _target.Running ? $"{_target.DisplayName} · PID {_target.Pid}" : "No emulator process";
        PriorityText = _target.Running ? _target.Priority : "—";
        _rowTarget.On = _target.Running;
        _rowTarget.Detail = _target.Running ? $"{_target.DisplayName} · {_target.Pid}" : "waiting for launch";
        _rowPriority.On = _target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase);
        _rowPriority.Detail = _target.Running
            ? _priorityBlocked ? $"{_target.Priority} · boost blocked" : _target.Priority
            : "idle";
    }

    private void RefreshPower()
    {
        try
        {
            var plan = _services.Engine.Power.QueryActive();
            PowerText = plan.Name;
            _rowPower.On = plan.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);
            _rowPower.Detail = plan.Name;
        }
        catch
        {
            PowerText = "unavailable";
        }
    }

    private void RefreshCache()
    {
        try
        {
            var report = _services.Engine.Cache.Measure();
            CacheText = $"{CacheService.Format(report.Bytes)}";
        }
        catch { CacheText = "—"; }
    }

    private async Task RefreshAsync()
    {
        StatusNote = "Re-scanning pipeline…";
        RefreshPower();
        RefreshStatic();
        await Task.CompletedTask;
        StatusNote = "Pipeline re-verified.";
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        Headline = "Engaging pipeline…";
        StatusNote = "Applying timer, priority, power, registry.";
        try
        {
            var report = await _services.Engine.ApplyAsync();
            var applied = report.Steps.Count(s => s.Ok && !s.Skipped);
            var failed = report.Steps.Where(s => !s.Ok).ToList();
            _lastRunPartial = failed.Count > 0;
            _runActive = applied > 0;

            var priority = report.Steps.FirstOrDefault(s => s.Label == "Emulator priority");
            _priorityBlocked = priority is { Ok: false } &&
                               priority.Detail.Contains("Blocked", StringComparison.OrdinalIgnoreCase);

            ApplyRows(report.Steps);
            StatusNote = !report.Target.Running
                ? $"{applied}/{report.Steps.Count} steps applied · emulator not detected"
                : failed.Count > 0
                    ? $"{applied}/{report.Steps.Count} steps applied · {failed[0].Detail}"
                    : $"{applied}/{report.Steps.Count} steps applied · {report.Target.DisplayName}";
            RefreshStatic();
        }
        catch (Exception ex)
        {
            AppLog.Error("overview", ex.Message);
            _lastRunPartial = true;
            StatusNote = "Pipeline failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateHeadline();
        }
    }

    private async Task RestoreAsync()
    {
        IsBusy = true;
        Headline = "Restoring defaults…";
        try
        {
            var report = await _services.Engine.RestoreAsync();
            var failed = report.Steps.Where(s => !s.Ok).ToList();
            ApplyRows(report.Steps, restoring: true);
            _lastRunPartial = false;
            _runActive = false;
            StatusNote = failed.Count == 0
                ? "Defaults restored. Timer released, registry reverted."
                : $"Restore incomplete · {failed[0].Detail}";
            RefreshStatic();
        }
        catch (Exception ex)
        {
            AppLog.Error("overview", ex.Message);
            StatusNote = "Restore failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            UpdateHeadline();
        }
    }

    private void ApplyRows(IReadOnlyList<StepResult> steps, bool restoring = false)
    {
        foreach (var step in steps)
        {
            var row = step.Label switch
            {
                "Emulator priority" => _rowPriority,
                "Timer resolution" => _rowTimer,
                "Power plan" => _rowPower,
                "Gaming registry" => _rowRegistry,
                _ => null
            };
            if (row is null) continue;
            row.Detail = step.Detail;
            row.On = !restoring && step.Ok && !step.Skipped;
        }
    }
}
