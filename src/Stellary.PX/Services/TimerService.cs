using Stellary.PX.Core;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

/// <summary>Holds a 100ns-unit timer resolution request for the life of the process.</summary>
public sealed class TimerService
{
    private bool _held;
    private double _heldMs;

    public bool IsHeld => _held;
    public double HeldMs => _heldMs;
    public double CurrentMs => NativeMethods.CurrentTimerResolutionMs();

    public StepResult Lock(double milliseconds)
    {
        if (milliseconds is < 0.5 or > 5.0)
            return StepResult.Fail("Timer resolution", "Request outside 0.5–5.0 ms");

        try
        {
            if (_held) Release();

            var units = (uint)Math.Round(milliseconds * 10000.0);
            var status = NativeMethods.NtSetTimerResolution(units, true, out var current);
            if (status != 0)
                return StepResult.Fail("Timer resolution", $"NTSTATUS 0x{status:X8}");

            _held = true;
            _heldMs = current / 10000.0;
            AppLog.Info("timer", $"lock {_heldMs:F3} ms");
            return StepResult.Success("Timer resolution", $"{_heldMs:F3} ms");
        }
        catch (Exception ex)
        {
            return StepResult.Fail("Timer resolution", ex.Message);
        }
    }

    public StepResult Release()
    {
        try
        {
            if (!_held) return StepResult.Skip("Timer resolution", "Not held");
            var status = NativeMethods.NtSetTimerResolution((uint)Math.Round(_heldMs * 10000.0), false, out _);
            _held = false;
            AppLog.Info("timer", "released");
            return status == 0
                ? StepResult.Success("Timer resolution", "Released")
                : StepResult.Fail("Timer resolution", $"NTSTATUS 0x{status:X8}");
        }
        catch (Exception ex)
        {
            return StepResult.Fail("Timer resolution", ex.Message);
        }
    }
}
