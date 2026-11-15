using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Services;

namespace Stellary.PX.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly AppServices _services;
    private NavEntry? _selectedNav;
    private object? _currentPage;
    private ImageSource? _avatar;

    public ShellViewModel(AppServices services)
    {
        _services = services;
        Branding = services.Branding;

        var overview = new OverviewViewModel(services);
        var emulator = new EmulatorViewModel(services);
        var system = new SystemViewModel(services);
        var visuals = new VisualsViewModel(services);
        var diagnostics = new DiagnosticsViewModel(services);
        var settings = new SettingsViewModel(services);

        NavItems = new List<NavEntry>
        {
            new() { Key = "overview", Label = "Overview", Category = "CONTROL", Icon = Geo("M3 3h7v9H3z M14 3h7v5h-7z M14 12h7v9h-7z M3 16h7v5H3z"), Page = overview },
            new() { Key = "emulator", Label = "Emulator", Category = "CONTROL", Icon = Geo("M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M12 18h.01"), Page = emulator },
            new() { Key = "system", Label = "System tuning", Category = "SYSTEM", Icon = Geo("M4 4h16v16H4z M9 9h6v6H9z M9 2v2 M15 2v2 M9 20v2 M15 20v2 M2 9h2 M2 15h2 M20 9h2 M20 15h2"), Page = system },
            new() { Key = "visuals", Label = "Visual tuning", Category = "SYSTEM", Icon = Geo("M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6"), Page = visuals },
            new() { Key = "diagnostics", Label = "Diagnostics", Category = "TOOLS", Icon = Geo("M3 12h4l3-9 4 18 3-9h4"), Page = diagnostics },
            new() { Key = "settings", Label = "Settings", Category = "TOOLS", Icon = Geo("M12 15.5a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M19.4 15a1.7 1.7 0 0 0 .34 1.87l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.7 1.7 0 0 0-1.87-.34 1.7 1.7 0 0 0-1.03 1.56V21a2 2 0 1 1-4 0v-.09A1.7 1.7 0 0 0 9 19.4a1.7 1.7 0 0 0-1.87.34l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-1.56-1.03H3a2 2 0 1 1 0-4h.09A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.34-1.87l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1.03-1.56V3a2 2 0 1 1 4 0v.09A1.7 1.7 0 0 0 15 4.6a1.7 1.7 0 0 0 1.87-.34l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.7 1.7 0 0 0 19.4 9v.09a1.7 1.7 0 0 0 1.56 1.03H21a2 2 0 1 1 0 4h-.09a1.7 1.7 0 0 0-1.56 1.03z"), Page = settings }
        };

        NavView = CollectionViewSource.GetDefaultView(NavItems);
        NavView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavEntry.Category)));
        SelectedNav = NavItems[0];

        Version = $"v{services.Version}";
        ElevationText = NativeMethods.IsElevated() ? "Administrator" : "Standard user";
        MachineHwid = IdentityService.Hwid();

        var avatarPath = Path.Combine(AppContext.BaseDirectory, Branding.AvatarFile.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(avatarPath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(avatarPath);
                bmp.EndInit();
                bmp.Freeze();
                _avatar = bmp;
            }
            catch { }
        }

        OpenLinkCommand = new RelayCommand(p =>
        {
            if (p is string url) ShellRunner.OpenExternal(url);
        });
        OpenBrandingCommand = new RelayCommand(() => ShellRunner.OpenFolder(AppContext.BaseDirectory));
    }

    public BrandingConfig Branding { get; }
    public string Version { get; }
    public string ElevationText { get; }
    public string MachineHwid { get; }
    public string LinktreeUrl => Branding.Url("Linktree");
    public string WhatsAppUrl => Branding.Url("WhatsApp");
    public string TikTokUrl => Branding.Url("TikTok");
    public string DiscordUrl => Branding.Url("Discord");
    public string InstagramUrl => Branding.Url("Instagram");
    public ImageSource? Avatar => _avatar;
    public string Initials => "ES";
    public bool HasAvatar => _avatar is not null;

    public IReadOnlyList<NavEntry> NavItems { get; }
    public ICollectionView NavView { get; }
    public RelayCommand OpenLinkCommand { get; }
    public RelayCommand OpenBrandingCommand { get; }

    public NavEntry? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (ReferenceEquals(_selectedNav, value)) return;
            _selectedNav?.Page.Deactivate();
            if (!Set(ref _selectedNav, value) || value is null) return;
            Infrastructure.AppLog.Info("nav", $"selected '{value.Key}'");
            CurrentPage = value.Page;
            value.Page.Activate();
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(CurrentPageEyebrow));
        }
    }

    public object? CurrentPage { get => _currentPage; private set => Set(ref _currentPage, value); }
    public string CurrentPageTitle => _selectedNav?.Page.Title ?? "Overview";
    public string CurrentPageEyebrow => _selectedNav?.Page.Eyebrow ?? "CONTROL";

    public void RefreshBranding()
    {
        // branding.json is read at launch; expose the folder so edits are one file away.
    }

    private static Geometry Geo(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }
}
