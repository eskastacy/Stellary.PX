using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private string _targetName = "Scanning…";
    private string _targetState = "—";
    private string _targetPath = "—";
    private string _installDir = "—";
    private string _priority = "—";
    private string _cacheText = "—";
    private string _status = "Ready.";

    public EmulatorViewModel(AppServices services)
    {
        _services = services;
        Drivers = new ObservableCollection<DriverRow>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BoostCommand = new AsyncRelayCommand(BoostAsync);
        RestorePriorityCommand = new AsyncRelayCommand(RestoreAsync);
        CleanCacheCommand = new AsyncRelayCommand(CleanAsync);
        ReplaceDriverCommand = new AsyncRelayCommand(ReplaceDriverAsync);
        OpenInstallCommand = new RelayCommand(OpenInstall);
        _watch = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, (_, _) => RefreshInline(), Dispatcher.CurrentDispatcher);
    }

    public override string Title => "Emulator";
    public override string Eyebrow => "CONTROL";

    public ObservableCollection<DriverRow> Drivers { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand BoostCommand { get; }
    public AsyncRelayCommand RestorePriorityCommand { get; }
    public AsyncRelayCommand CleanCacheCommand { get; }
    public AsyncRelayCommand ReplaceDriverCommand { get; }
    public RelayCommand OpenInstallCommand { get; }

    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }
    public string TargetState { get => _targetState; private set => Set(ref _targetState, value); }
    public string TargetPath { get => _targetPath; private set => Set(ref _targetPath, value); }
    public string InstallDir { get => _installDir; private set => Set(ref _installDir, value); }
    public string Priority { get => _priority; private set => Set(ref _priority, value); }
    public string CacheText { get => _cacheText; private set => Set(ref _cacheText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Running => _target.Running;

    public override void Activate()
    {
        _watch.Start();
        _ = RefreshAsync();
    }

    public override void Deactivate() => _watch.Stop();

    private void RefreshInline()
    {
        _target = _services.Engine.Emulator.Detect();
        TargetName = _target.DisplayName;
        TargetState = _target.Running ? $"Running · PID {_target.Pid}" : "Standby";
        TargetPath = _target.Running
            ? _target.ExecutablePath ?? "path protected by emulator"
            : "no active process";
        InstallDir = _target.InstallDirectory ?? "not detected";
        Priority = _target.Running ? _target.Priority : "—";
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
        TargetName = target.DisplayName;
        TargetState = target.Running ? $"Running · PID {target.Pid}" : "Standby";
        TargetPath = target.Running
            ? target.ExecutablePath ?? "path protected by emulator"
            : "no active process";
        InstallDir = target.InstallDirectory ?? "not detected";
        Priority = target.Running ? target.Priority : "—";
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

        Status = target.Running ? "Target acquired." : "Waiting for HD-Player.exe.";
    }

    private async Task BoostAsync()
    {
        if (!_target.Running)
        {
            Status = "Launch BlueStacks or MSI App Player first.";
            return;
        }
        var result = await Task.Run(() => _services.Engine.Priority.SetAboveNormal(_target.Pid));
        Status = result.Detail;
        RefreshInline();
    }

    private async Task RestoreAsync()
    {
        if (!_target.Running)
        {
            Status = "Target not running.";
            return;
        }
        var result = await Task.Run(() => _services.Engine.Priority.Restore(_target.Pid));
        Status = result.Detail;
        RefreshInline();
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

    private async Task ReplaceDriverAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a compatible, signed BlueStacks driver",
            Filter = "Windows driver (*.sys)|*.sys",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;

        var target = _target.Family.Contains("MSI", StringComparison.OrdinalIgnoreCase)
            ? @"C:\Program Files\BlueStacks_msi5\BstkDrv_msi5.sys"
            : @"C:\Program Files\BlueStacks_nxt\BstkDrv_nxt.sys";

        if (_services.Settings.Current.ConfirmActions)
        {
            var answer = MessageBox.Show(
                $"Replace\n{target}\n\nwith\n{dialog.FileName}\n\nThe existing driver is backed up first. The emulator must be closed.",
                "Stellary.PX — driver repair",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        var result = await Task.Run(() => _services.Engine.Drivers.Replace(target, dialog.FileName, _target.Running));
        Status = result.Detail;
        await RefreshAsync();
    }

    private void OpenInstall()
    {
        var dir = _target.InstallDirectory;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) ShellRunner.OpenFolder(dir);
        else Status = "Install directory not detected.";
    }
}
