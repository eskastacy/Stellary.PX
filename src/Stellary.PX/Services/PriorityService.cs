using System.Diagnostics;
using Stellary.PX.Core;

namespace Stellary.PX.Services;

public sealed class PriorityService
{
    public StepResult SetAboveNormal(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = ProcessPriorityClass.AboveNormal;
            Infrastructure.AppLog.Info("priority", $"PID {pid} -> AboveNormal");
            return StepResult.Success("Emulator priority", "Above normal");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("priority", $"PID {pid} set failed: {ex.Message}");
            return StepResult.Fail("Emulator priority", "Needs elevation or target closed");
        }
    }

    public StepResult Restore(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = ProcessPriorityClass.Normal;
            Infrastructure.AppLog.Info("priority", $"PID {pid} -> Normal");
            return StepResult.Success("Emulator priority", "Normal");
        }
        catch (Exception ex)
        {
            return StepResult.Fail("Emulator priority", ex.Message);
        }
    }
}
