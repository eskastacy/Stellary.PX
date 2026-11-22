using Stellary.PX.Core;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

public sealed class PriorityService
{
    public StepResult SetAboveNormal(int pid)
    {
        var (ok, error) = NativeMethods.TrySetPriority(pid, aboveNormal: true);
        if (ok)
        {
            AppLog.Info("priority", $"PID {pid} -> AboveNormal");
            return StepResult.Success("Emulator priority", "Above normal");
        }

        AppLog.Warn("priority", $"PID {pid} set failed (win32 {error})");
        return StepResult.Fail("Emulator priority", Explain(error));
    }

    public StepResult Restore(int pid)
    {
        var (ok, error) = NativeMethods.TrySetPriority(pid, aboveNormal: false);
        if (ok)
        {
            AppLog.Info("priority", $"PID {pid} -> Normal");
            return StepResult.Success("Emulator priority", "Normal");
        }

        AppLog.Warn("priority", $"PID {pid} restore failed (win32 {error})");
        return StepResult.Fail("Emulator priority", Explain(error));
    }

    private static string Explain(int error) => error switch
    {
        5 => "Direct handle denied · engine path unavailable",
        87 => "Target closed",
        1168 => "Target closed",
        -1 => "Priority probe failed",
        _ => $"Windows error {error}"
    };
}
