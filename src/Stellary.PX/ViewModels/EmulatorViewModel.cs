using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Models;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class DriverRow
{
    public required string Family { get; init; }
    public required string Path { get; init; }
    public required bool Exists { get; init; }
    public required string Version { get; init; }
    public required string Hash { get; init; }
    public required bool Signed { get; init; }
    public string SignedText => !Exists ? "not installed" : Signed ? "trusted signature" : "unsigned";
}

public sealed class EmulatorViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _watch;
    private EmulatorTarget _target = new();
    private bool _scanned;
    private string _targetName = "Scanning…";
    private string _targetState = "—";
    private string _targetPath = "—";
    private string _installDir = "—";
    private string _priority = "—";
    private string _cacheText = "—";
    private string _status = "Ready.";
    private string _uncapStatus = "Uncap engine idle.";
    private bool _uncapActive;

    public EmulatorViewModel(AppServices services)
    {
        _services = services;
        Drivers = new ObservableCollection<DriverRow>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        LaunchBoostedCommand = new AsyncRelayCommand(LaunchBoostedAsync);
        BoostCommand = new AsyncRelayCommand(BoostAsync);
        RestorePriorityCommand = new AsyncRelayCommand(RestoreAsync);
        CleanCacheCommand = new AsyncRelayCommand(CleanAsync);
        OpenInstallCommand = new RelayCommand(OpenInstall);
        _watch = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, (_, _) => RefreshInline(), Dispatcher.CurrentDispatcher);
    }

    public override string Title => "Emulator";
    public override string Eyebrow => "CONTROL";

    public ObservableCollection<DriverRow> Drivers { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LaunchBoostedCommand { get; }
    public AsyncRelayCommand BoostCommand { get; }
    public AsyncRelayCommand RestorePriorityCommand { get; }
    public AsyncRelayCommand CleanCacheCommand { get; }
    public RelayCommand OpenInstallCommand { get; }

    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }
    public string TargetState { get => _targetState; private set => Set(ref _targetState, value); }
    public string TargetPath { get => _targetPath; private set => Set(ref _targetPath, value); }
    public string InstallDir { get => _installDir; private set => Set(ref _installDir, value); }
    public string Priority { get => _priority; private set => Set(ref _priority, value); }
    public string CacheText { get => _cacheText; private set => Set(ref _cacheText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string UncapStatus { get => _uncapStatus; private set => Set(ref _uncapStatus, value); }
    public bool UncapActive { get => _uncapActive; private set => Set(ref _uncapActive, value); }
    public bool Running => _target.Running;
    public bool CanLaunch => _scanned && !_target.Running;

    public override void Activate()
    {
        _watch.Start();
        _ = RefreshAsync();
    }

    public override void Deactivate() => _watch.Stop();

    private void RefreshInline()
    {
        _target = _services.Engine.Emulator.Detect();
        _scanned = true;
        AnnounceTarget();
        TargetName = _target.DisplayName;
        TargetState = _target.Running ? $"Running · PID {_target.Pid}" : "Standby";
        TargetPath = _target.Running
            ? _target.ExecutablePath ?? "path protected by emulator"
            : "no active process";
        InstallDir = _target.InstallDirectory ?? "not detected";
        UpdatePriorityLabel();
        UpdateUncapState();
    }

    private void AnnounceTarget()
    {
        OnPropertyChanged(nameof(Running));
        OnPropertyChanged(nameof(CanLaunch));
    }

    private async Task RefreshAsync()
    {
        var (target, cache, drivers) = await Task.Run(() =>
        {
            var t = _services.Engine.Emulator.Detect();
            var c = _services.Engine.Cache.Measure();
            var d = _services.Engine.Drivers.Inspect();
            return (t, c, d);
        });

        _target = target;
        _scanned = true;
        AnnounceTarget();
        TargetName = target.DisplayName;
        TargetState = target.Running ? $"Running · PID {target.Pid}" : "Standby";
        TargetPath = target.Running
            ? target.ExecutablePath ?? "path protected by emulator"
            : "no active process";
        InstallDir = target.InstallDirectory ?? "not detected";
        UpdatePriorityLabel();
        UpdateUncapState();
        CacheText = $"{CacheService.Format(cache.Bytes)} · {cache.Folders} folders";

        Drivers.Clear();
        foreach (var d in drivers)
        {
            var family = d.Path.Contains("msi5", StringComparison.OrdinalIgnoreCase) ? "MSI App Player 5" : "BlueStacks 5";
            Drivers.Add(new DriverRow
            {
                Family = family,
                Path = d.Path,
                Exists = d.Exists,
                Version = d.Version,
                Hash = d.Sha256,
                Signed = d.Signed
            });
        }

        Status = target.Running ? "Target acquired." : "Waiting for HD-Player.exe. Use Launch boosted.";
    }

    private async Task LaunchBoostedAsync()
    {
        if (!_scanned)
        {
            Status = "Scanning target; try again in a moment.";
            return;
        }
        if (_target.Running)
        {
            Status = "Close BlueStacks first; launch priority only applies to a new process.";
            return;
        }

        var (ok, detail) = await Task.Run(() => _services.Engine.Emulator.LaunchAboveNormal());
        Status = detail;
        if (!ok) return;

        for (var i = 0; i < 45; i++)
        {
            await Task.Delay(1000);
            RefreshInline();
            if (!_target.Running) continue;

            Status = _target.Priority.Contains("Above", StringComparison.OrdinalIgnoreCase)
                ? $"Launched at {_target.Priority}."
                : $"Running at {_target.Priority}; launch priority was not retained.";
            return;
        }

        Status = "Launch requested; HD-Player is still starting.";
    }

    private async Task BoostAsync()
    {
        if (!_target.Running)
        {
            var (config, power) = await Task.Run(() =>
            {
                var c = _services.Engine.Emulator.ApplyHighFps(_target.Family);
                var p = ActivatePowerBoost();
                return (c, p);
            });
            Status = $"{power.Detail} · {config.Detail}";
            return;
        }

        var pid = _target.Pid;
        var family = _target.Family;
        Status = "Boosting priority, CPU clock, and arming 2× FPS…";

        var (priority, uncap, highFps, clock) = await Task.Run(() =>
        {
            var p = _services.Engine.Priority.SetAboveNormal(pid);
            var u = _services.Engine.Uncap.Inject(pid);
            var c = _services.Engine.Emulator.ApplyHighFps(family);
            var k = ActivatePowerBoost();
            return (p, u, c, k);
        });

        // BlueStacks denies PROCESS_SET_INFORMATION to outside handles. The
        // engine is loaded by now, so retry the same click from inside.
        if (!priority.Ok)
        {
            var inProcess = await Task.Run(() => _services.Engine.Uncap.SetPriority(pid, true));
            priority = inProcess.Ok
                ? inProcess
                : StepResult.Fail(priority.Label, $"{priority.Detail} · {inProcess.Detail}");
        }

        Status = $"{priority.Detail} · {clock.Detail} · {uncap.Detail} · {highFps.Detail}";
        UncapStatus = uncap.Detail;
        RefreshInline();
    }

    private async Task RestoreAsync()
    {
        if (!_target.Running)
        {
            var powerOnly = await Task.Run(RestorePowerBoost);
            Status = powerOnly.Detail;
            RefreshInline();
            return;
        }

        var pid = _target.Pid;
        var (priority, uncap, clock) = await Task.Run(() =>
        {
            var p = _services.Engine.Priority.Restore(pid);
            var u = _services.Engine.Uncap.Eject(pid);
            var k = RestorePowerBoost();
            return (p, u, k);
        });

        if (!priority.Ok)
        {
            var inProcess = await Task.Run(() => _services.Engine.Uncap.SetPriority(pid, false));
            priority = inProcess.Ok
                ? inProcess
                : StepResult.Fail(priority.Label, $"{priority.Detail} · {inProcess.Detail}");
        }

        Status = $"{priority.Detail} · {clock.Detail} · {uncap.Detail}";
        UncapStatus = uncap.Detail;
        RefreshInline();
    }

    /// <summary>Ultimate Performance keeps every core at its boost clock.</summary>
    private StepResult ActivatePowerBoost()
    {
        var settings = _services.Settings.Current;
        var result = _services.Engine.Power.ActivateUltimate(out var prior);
        if (result.Ok && string.IsNullOrWhiteSpace(settings.LastPowerPlanGuid) && prior is not null)
        {
            settings.LastPowerPlanGuid = prior;
            _services.Settings.Save();
        }
        return result;
    }

    private StepResult RestorePowerBoost()
    {
        var settings = _services.Settings.Current;
        var result = _services.Engine.Power.Restore(settings.LastPowerPlanGuid);
        if (result.Ok)
        {
            settings.LastPowerPlanGuid = null;
            _services.Settings.Save();
        }
        return result;
    }

    private void UpdatePriorityLabel()
    {
        Priority = _target.Running ? _target.Priority : "—";
        if (!_target.Running) return;

        // The outside query may still say "protected" after the in-process
        // export succeeded; the telemetry flag is the honest source then.
        var telemetry = _services.Engine.Uncap.ReadTelemetry(_target.Pid);
        if (telemetry.Valid && telemetry.IsPriorityAbove)
            Priority = "Above normal · in-process";
    }

    private void UpdateUncapState()
    {
        if (!_target.Running)
        {
            UncapActive = false;
            UncapStatus = "Uncap engine idle — start the emulator.";
            return;
        }

        var telemetry = _services.Engine.Uncap.ReadTelemetry(_target.Pid);
        if (telemetry.Valid)
        {
            UncapActive = telemetry.IsArmed && (telemetry.IsUncapped || telemetry.IsFakeActive);
            if (!telemetry.IsArmed && telemetry.Flags == 0)
                UncapStatus = "Engine loaded · starting…";
            else if (!telemetry.IsArmed)
                UncapStatus = "Engine loaded · disarmed. Boost priority re-arms.";
            else if (telemetry.IsFakeActive)
                UncapStatus = $"{UncapService.FpsText(telemetry)} · 2× live";
            else
                UncapStatus = "Armed · waiting for the FPS counter…";
            return;
        }

        UncapActive = false;
        UncapStatus = _services.Engine.Uncap.IsInjected(_target.Pid)
            ? "Engine loaded · waiting for the first frame."
            : "Not injected.";
    }

    private async Task CleanAsync()
    {
        if (_target.Running)
        {
            Status = "Close the emulator before cleaning QML cache.";
            return;
        }

        var report = await Task.Run(() => _services.Engine.Cache.Measure());
        var confirm = _services.Settings.Current.ConfirmActions;
        if (confirm)
        {
            var answer = MessageBox.Show(
                $"Delete {CacheService.Format(report.Bytes)} of QML cache across {report.Folders} folders?\n\nApplication data and installed games are untouched.",
                "Stellary.PX — QML cache",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        var result = await Task.Run(() => _services.Engine.Cache.Clean(_target.Running));
        Status = result.Detail;
        CacheText = "0 B";
        await RefreshAsync();
    }

    private void OpenInstall()
    {
        var dir = _target.InstallDirectory;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) ShellRunner.OpenFolder(dir);
        else Status = "Install directory not detected.";
    }
}
