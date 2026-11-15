using System.Windows.Threading;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class SystemViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _watch;
    private string _powerName = "—";
    private string _timerText = "—";
    private string _registryText = "—";
    private string _status = "Ready.";
    private double _timerMs;
    private string _timerState = "—";
    private string _powerState = "no switch recorded";

    public SystemViewModel(AppServices services)
    {
        _services = services;
        _timerMs = services.Settings.Current.TimerResolutionMs;

        ActivateCommand = new AsyncRelayCommand(ActivateUltimateAsync);
        RestorePowerCommand = new AsyncRelayCommand(RestorePowerAsync);
        LockTimerCommand = new AsyncRelayCommand(LockTimerAsync);
        ReleaseTimerCommand = new AsyncRelayCommand(ReleaseTimerAsync);
        ApplyRegistryCommand = new RelayCommand(ApplyRegistry);
        RestoreRegistryCommand = new RelayCommand(RestoreRegistry);
        OpenBackupsCommand = new RelayCommand(OpenBackups);

        _watch = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => UpdateReadouts(), Dispatcher.CurrentDispatcher);
    }

    public override string Title => "System tuning";
    public override string Eyebrow => "SYSTEM";

    public AsyncRelayCommand ActivateCommand { get; }
    public AsyncRelayCommand RestorePowerCommand { get; }
    public AsyncRelayCommand LockTimerCommand { get; }
    public AsyncRelayCommand ReleaseTimerCommand { get; }
    public RelayCommand ApplyRegistryCommand { get; }
    public RelayCommand RestoreRegistryCommand { get; }
    public RelayCommand OpenBackupsCommand { get; }

    public string PowerName { get => _powerName; private set => Set(ref _powerName, value); }
    public string TimerText { get => _timerText; private set => Set(ref _timerText, value); }
    public string RegistryText { get => _registryText; private set => Set(ref _registryText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string TimerState { get => _timerState; private set => Set(ref _timerState, value); }
    public string PowerState { get => _powerState; private set => Set(ref _powerState, value); }
    public string BackupPath => Paths.Backups;

    public bool ApplyPowerPlan
    {
        get => _services.Settings.Current.ApplyPowerPlan;
        set { _services.Settings.Current.ApplyPowerPlan = value; Save(); OnPropertyChanged(); }
    }

    public bool ApplyRegistryTweaks
    {
        get => _services.Settings.Current.ApplyRegistryTweaks;
        set { _services.Settings.Current.ApplyRegistryTweaks = value; Save(); OnPropertyChanged(); }
    }

    public bool LockTimerOnStart
    {
        get => _services.Settings.Current.LockTimer;
        set { _services.Settings.Current.LockTimer = value; Save(); OnPropertyChanged(); }
    }

    public double TimerMs
    {
        get => _timerMs;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.5, 2.0), 1);
            if (!Set(ref _timerMs, clamped)) return;
            _services.Settings.Current.TimerResolutionMs = clamped;
            Save();
        }
    }

    public override void Activate()
    {
        _watch.Start();
        UpdateReadouts();
    }

    public override void Deactivate() => _watch.Stop();

    private void UpdateReadouts()
    {
        try
        {
            var plan = _services.Engine.Power.QueryActive();
            PowerName = plan.Name;
        }
        catch { PowerName = "unavailable"; }

        var ms = _services.Engine.Timer.CurrentMs;
        TimerText = ms > 0 ? $"{ms:0.000} ms" : "unavailable";
        TimerState = _services.Engine.Timer.IsHeld
            ? $"{ms:0.000} ms · held for this session"
            : ms > 0 ? $"{ms:0.000} ms · system default" : "unavailable";

        var s = _services.Settings.Current;
        RegistryText = _services.Engine.Registry.AnyBackup(s)
            ? $"{s.RegistryBackup.Count} values tracked for restore"
            : "Not applied";
        PowerState = string.IsNullOrWhiteSpace(s.LastPowerPlanGuid)
            ? "no switch recorded"
            : "previous scheme captured";
    }

    private async Task ActivateUltimateAsync()
    {
        Status = "Requesting Ultimate Performance…";
        var result = await Task.Run(() => _services.Engine.Power.ActivateUltimate(out _));
        Status = result.Detail;
        UpdateReadouts();
    }

    private async Task RestorePowerAsync()
    {
        var guid = _services.Settings.Current.LastPowerPlanGuid;
        var result = await Task.Run(() => _services.Engine.Power.Restore(guid));
        Status = result.Detail;
        UpdateReadouts();
    }

    private async Task LockTimerAsync()
    {
        var ms = _timerMs;
        var result = await Task.Run(() => _services.Engine.Timer.Lock(ms));
        Status = result.Detail;
        UpdateReadouts();
    }

    private async Task ReleaseTimerAsync()
    {
        var result = await Task.Run(() => _services.Engine.Timer.Release());
        Status = result.Detail;
        UpdateReadouts();
    }

    private void ApplyRegistry()
    {
        var result = _services.Engine.Registry.Apply(_services.Settings.Current);
        _services.Settings.Save();
        Status = result.Detail;
        UpdateReadouts();
    }

    private void RestoreRegistry()
    {
        var result = _services.Engine.Registry.Restore(_services.Settings.Current);
        _services.Settings.Save();
        Status = result.Detail;
        UpdateReadouts();
    }

    private void OpenBackups()
    {
        Paths.EnsureShared();
        ShellRunner.OpenFolder(Paths.Backups);
    }

    private void Save() => _services.Settings.Save();
}
