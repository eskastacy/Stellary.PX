using System.Collections.ObjectModel;
using System.Windows.Threading;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;
using Stellary.PX.Models;

namespace Stellary.PX.ViewModels;

public sealed class SliderSpec : ObservableObject
{
    private readonly Action<double> _apply;
    private double _value;

    public SliderSpec(string label, double value, Action<double> apply)
    {
        Label = label;
        _value = value;
        _apply = apply;
    }

    public string Label { get; }

    public double Value
    {
        get => _value;
        set
        {
            var rounded = Math.Round(value, 0);
            if (!Set(ref _value, rounded)) return;
            OnPropertyChanged(nameof(ValueText));
            _apply(rounded);
        }
    }

    public string ValueText => $"{_value:0}";
}

public sealed class PresetOption : ObservableObject
{
    private bool _isActive;

    public required string Name { get; init; }
    public required string Description { get; init; }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}

public sealed class VisualsViewModel : PageViewModel
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _saveDebounce;
    private readonly Dictionary<string, SliderSpec> _sliders;
    private string _status = "Preset engine ready.";
    private bool _dirty;

    public VisualsViewModel(AppServices services)
    {
        _services = services;
        _sliders = new Dictionary<string, SliderSpec>();
        Sliders = new ObservableCollection<SliderSpec>();

        Presets = new ObservableCollection<PresetOption>
        {
            new() { Name = "Competitive", Description = "Low bloom, hard edges, near-neutral grade." },
            new() { Name = "Cinema", Description = "Warm lift, heavier vignette, light motion trail." },
            new() { Name = "Clean", Description = "Neutral baseline. No bloom, no motion blur." }
        };

        Add("Brightness", () => V.Brightness, v => V.Brightness = v);
        Add("Contrast", () => V.Contrast, v => V.Contrast = v);
        Add("Warmth", () => V.Warmth, v => V.Warmth = v);
        Add("Saturation", () => V.Saturation, v => V.Saturation = v);
        Add("Sharpness", () => V.Sharpness, v => V.Sharpness = v);
        Add("Bloom", () => V.Bloom, v => V.Bloom = v);
        Add("Vignette", () => V.Vignette, v => V.Vignette = v);
        Add("Motion blur", () => V.MotionBlur, v => V.MotionBlur = v);
        Add("Sensitivity", () => V.Sensitivity, v => V.Sensitivity = v);
        Add("HDR strength", () => V.HdrStrength, v => V.HdrStrength = v);

        PresetCommand = new RelayCommand(p => ApplyPreset(p as string ?? "Clean"));
        ExportCommand = new RelayCommand(Export);
        ResetCommand = new RelayCommand(() => ApplyPreset("Clean"));
        OpenPresetFolderCommand = new RelayCommand(OpenPresetFolder);

        _saveDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Flush(), Dispatcher.CurrentDispatcher);

        var payload = Path.Combine(AppContext.BaseDirectory, "Stellary.Render.dll");
        var present = File.Exists(payload);
        RendererStatus = present
            ? "Renderer module present — this build compiles presets, attach is a separate step."
            : "Renderer module not deployed. Presets compile to disk; live attach arrives with the renderer module.";
        AttachText = present ? "module present" : "not attached";
        SyncPresetState();
    }

    public override string Title => "Visual tuning";
    public override string Eyebrow => "SYSTEM";

    public ObservableCollection<SliderSpec> Sliders { get; }
    public ObservableCollection<PresetOption> Presets { get; }
    public RelayCommand PresetCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand OpenPresetFolderCommand { get; }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string RendererStatus { get; }
    public string AttachText { get; }
    public string PresetPath => @"%APPDATA%\Stellary.PX\visuals.json";
    public string PresetPathFull => Paths.VisualsFile;
    public string PresetMeta => "10 parameters · compiles to JSON · saved on change";
    public string ActivePreset => V.Preset;
    public string ActivePresetText => $"preset · {V.Preset}";
    public string PresetSummary => "Brightness · contrast · warmth · saturation · sharpness · bloom · vignette · motion · sensitivity · HDR";

    private VisualSettings V => _services.Settings.Current.Visuals;

    public override void Activate()
    {
        SyncPresetState();
        OnPropertyChanged(nameof(ActivePreset));
        OnPropertyChanged(nameof(ActivePresetText));
    }

    private void SyncPresetState()
    {
        foreach (var preset in Presets)
            preset.IsActive = string.Equals(preset.Name, V.Preset, StringComparison.OrdinalIgnoreCase);
    }

    private void Add(string label, Func<double> get, Action<double> set)
    {
        var spec = new SliderSpec(label, get(), v =>
        {
            set(v);
            _dirty = true;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        });
        _sliders[label] = spec;
        Sliders.Add(spec);
    }

    private void ApplyPreset(string name)
    {
        (string Key, double Value)[] values = name switch
        {
            "Competitive" => new[] { ("Brightness", 50d), ("Contrast", 54d), ("Warmth", 48d), ("Saturation", 56d),
                                      ("Sharpness", 62d), ("Bloom", 0d), ("Vignette", 4d), ("Motion blur", 0d),
                                      ("Sensitivity", 55d), ("HDR strength", 0d) },
            "Cinema" => new[] { ("Brightness", 46d), ("Contrast", 58d), ("Warmth", 60d), ("Saturation", 54d),
                                 ("Sharpness", 28d), ("Bloom", 18d), ("Vignette", 24d), ("Motion blur", 12d),
                                 ("Sensitivity", 50d), ("HDR strength", 20d) },
            _ => new[] { ("Brightness", 50d), ("Contrast", 50d), ("Warmth", 50d), ("Saturation", 50d),
                          ("Sharpness", 35d), ("Bloom", 0d), ("Vignette", 12d), ("Motion blur", 0d),
                          ("Sensitivity", 50d), ("HDR strength", 0d) }
        };

        foreach (var (key, value) in values)
            if (_sliders.TryGetValue(key, out var spec)) spec.Value = value;

        V.Preset = name == "Clean" ? "Clean" : name;
        SyncPresetState();
        OnPropertyChanged(nameof(ActivePreset));
        OnPropertyChanged(nameof(ActivePresetText));
        Flush();
        Status = $"Preset “{V.Preset}” compiled.";
        AppLog.Info("visuals", "preset " + V.Preset);
    }

    private void Flush()
    {
        _saveDebounce.Stop();
        if (!_dirty) return;
        _dirty = false;
        _services.Settings.Save();
        JsonStore.Save(Paths.VisualsFile, V);
    }

    private void Export()
    {
        Flush();
        try
        {
            var dir = Path.Combine(Paths.AppData, "exports");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"stellary-preset-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            JsonStore.Save(file, V);
            Status = "Preset exported: " + Path.GetFileName(file);
            ShellRunner.OpenFolder(dir);
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
    }

    private static void OpenPresetFolder()
    {
        var dir = Path.Combine(Paths.AppData, "exports");
        Directory.CreateDirectory(dir);
        ShellRunner.OpenFolder(dir);
    }
}
