using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class DiagnosticsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private string _systemText = "—";
    private string _hashText = "—";
    private string _sumsText = "—";
    private string _logText = "";
    private string _status = "Ready.";
    private string _scanText = "Not scanned.";

    public DiagnosticsViewModel(AppServices services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CopyReportCommand = new RelayCommand(CopyReport);
        OpenLogsCommand = new RelayCommand(() => ShellRunner.OpenFolder(Paths.Logs));
        OpenDataCommand = new RelayCommand(() => ShellRunner.OpenFolder(Paths.AppData));
        Tools = new ObservableCollection<string>();
    }

    public override string Title => "Diagnostics";
    public override string Eyebrow => "TOOLS";

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand CopyReportCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenDataCommand { get; }
    public ObservableCollection<string> Tools { get; }

    public string SystemText { get => _systemText; private set => Set(ref _systemText, value); }
    public string HashText { get => _hashText; private set => Set(ref _hashText, value); }
    public string SumsText { get => _sumsText; private set => Set(ref _sumsText, value); }
    public string LogText { get => _logText; private set => Set(ref _logText, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ScanSummary { get => _scanText; private set => Set(ref _scanText, value); }

    public override void Activate() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        var (sys, hash, sums, log, tools) = await Task.Run(() =>
        {
            var (_, _, used, total) = NativeMethods.SampleSystem();
            var self = IntegrityService.InspectSelf();
            var sysText = new StringBuilder()
                .AppendLine($"OS            {RuntimeInformation.OSDescription}")
                .AppendLine($"Architecture  {RuntimeInformation.OSArchitecture} · {RuntimeInformation.ProcessArchitecture} process")
                .AppendLine($"Runtime       .NET {Environment.Version}")
                .AppendLine($"Logical CPUs  {Environment.ProcessorCount}")
                .AppendLine($"Memory        {used:0.0} / {total:0.0} GB")
                .AppendLine($"Uptime        {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss}")
                .AppendLine($"Elevation     {(NativeMethods.IsElevated() ? "Administrator" : "Standard user")}")
                .AppendLine($"Machine ID    {IdentityService.Hwid()}")
                .ToString();

            var found = _services.Engine.Tools.Scan();
            var toolLines = found.Count == 0
                ? new List<string> { "No known instrumentation processes running." }
                : found.Select(t => $"{t.Name}  ·  PID {t.Pid}").ToList();

            return (sysText, self.Sha256, $"checksum file: {self.SumsState}", string.Join(Environment.NewLine, AppLog.Tail(160)), toolLines);
        });

        SystemText = sys;
        HashText = hash;
        SumsText = sums;
        LogText = log;
        Tools.Clear();
        foreach (var t in tools) Tools.Add(t);
        ScanSummary = tools.Count == 1 && tools[0].StartsWith("No known") ? "0 tools detected" : $"{tools.Count} tools detected";
        Status = $"Scanned {DateTime.Now:HH:mm:ss}";
    }

    private void CopyReport()
    {
        var report = string.Join(Environment.NewLine,
            "Stellary.PX diagnostics",
            $"version {_services.Version}",
            $"machine {IdentityService.Hwid()}",
            "",
            SystemText,
            "self sha256: " + HashText,
            SumsText,
            "",
            "tools:",
            string.Join(Environment.NewLine, Tools));
        try
        {
            Clipboard.SetText(report);
            Status = "Report copied to clipboard.";
        }
        catch (Exception ex)
        {
            Status = "Clipboard failed: " + ex.Message;
        }
    }
}
