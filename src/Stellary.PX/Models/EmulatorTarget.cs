namespace Stellary.PX.Models;

public sealed class EmulatorTarget
{
    public bool Running { get; init; }
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "HD-Player.exe";
    public string DisplayName { get; init; } = "Not detected";
    public string? ExecutablePath { get; init; }
    public string Family { get; init; } = "—";
    public string? InstallDirectory { get; init; }
    public string Priority { get; init; } = "—";
    public string State => Running ? $"PID {Pid}" : "Standby";
}
