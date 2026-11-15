namespace Stellary.PX.Models;

public sealed class RegBackupEntry
{
    public bool Existed { get; set; }
    public int Dword { get; set; }
}

public sealed class VisualSettings
{
    public string Preset { get; set; } = "Clean";
    public double Brightness { get; set; } = 50;
    public double Contrast { get; set; } = 50;
    public double Warmth { get; set; } = 50;
    public double Saturation { get; set; } = 50;
    public double Sharpness { get; set; } = 35;
    public double Bloom { get; set; } = 0;
    public double Vignette { get; set; } = 12;
    public double MotionBlur { get; set; } = 0;
    public double Sensitivity { get; set; } = 50;
    public double HdrStrength { get; set; } = 0;
}

public sealed class AppSettings
{
    public bool AutoOptimizeOnLaunch { get; set; }
    public bool ConfirmActions { get; set; } = true;
    public bool ApplyPowerPlan { get; set; } = true;
    public bool ApplyRegistryTweaks { get; set; } = true;
    public bool LockTimer { get; set; } = true;
    public double TimerResolutionMs { get; set; } = 0.5;
    public bool SoundFeedback { get; set; }
    public string? LastPowerPlanGuid { get; set; }
    public Dictionary<string, RegBackupEntry> RegistryBackup { get; set; } = new();
    public VisualSettings Visuals { get; set; } = new();
}
