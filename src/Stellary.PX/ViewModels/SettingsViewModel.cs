using System.Windows;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private string _status = "Settings are written to disk immediately.";

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        OpenDataCommand = new RelayCommand(() => ShellRunner.OpenFolder(Paths.AppData));
        OpenBrandingCommand = new RelayCommand(() => ShellRunner.OpenFolder(AppContext.BaseDirectory));
        ReloadCommand = new RelayCommand(() => { _services.Settings.Reload(); RaiseAll(); Status = "Settings reloaded."; });
        ElevateCommand = new RelayCommand(() =>
        {
            if (ShellRunner.RelaunchElevated())
            {
                Application.Current.Shutdown();
            }
            else Status = "Elevation cancelled.";
        });
        OpenLinkCommand = new RelayCommand(p => { if (p is string url) ShellRunner.OpenExternal(url); });
        CopyHwidCommand = new RelayCommand(() => { try { Clipboard.SetText(Hwid); Status = "Machine ID copied."; } catch { } });
    }

    public override string Title => "Settings";
    public override string Eyebrow => "TOOLS";

    public RelayCommand OpenDataCommand { get; }
    public RelayCommand OpenBrandingCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand ElevateCommand { get; }
    public RelayCommand OpenLinkCommand { get; }
    public RelayCommand CopyHwidCommand { get; }

    public string Hwid => IdentityService.Hwid();
    public string Version => $"v{_services.Version}";
    public string DataPath => Paths.AppData;
    public string BrandingPath => Paths.BrandingFile;
    public string ElevationText => NativeMethods.IsElevated() ? "Elevated" : "Standard user";
    public bool IsElevated => NativeMethods.IsElevated();
    public bool CanElevate => !NativeMethods.IsElevated();
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DeveloperName => _services.Branding.DeveloperName;
    public string DeveloperBrand => _services.Branding.DeveloperBrand;
    public string DeveloperHandle => _services.Branding.DeveloperHandle;
    public IReadOnlyList<SocialLink> Links => _services.Branding.Links;

    public bool AutoOptimize
    {
        get => _services.Settings.Current.AutoOptimizeOnLaunch;
        set { _services.Settings.Current.AutoOptimizeOnLaunch = value; Save(); OnPropertyChanged(); Status = value ? "Auto-optimize armed for next launch." : "Auto-optimize disarmed."; }
    }

    public bool ConfirmActions
    {
        get => _services.Settings.Current.ConfirmActions;
        set { _services.Settings.Current.ConfirmActions = value; Save(); OnPropertyChanged(); }
    }

    private void Save()
    {
        _services.Settings.Save();
        Status = "Saved.";
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(AutoOptimize));
        OnPropertyChanged(nameof(ConfirmActions));
        OnPropertyChanged(nameof(Hwid));
    }
}
