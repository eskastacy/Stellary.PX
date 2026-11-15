using System.Text.RegularExpressions;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

public sealed partial class PowerPlanService
{
    public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    public sealed record ActivePlan(string Guid, string Name);

    public ActivePlan QueryActive()
    {
        var r = ShellRunner.Run("powercfg.exe", "/getactivescheme");
        if (!r.Ok) return new ActivePlan("", "Unavailable");
        var m = GuidPattern().Match(r.StdOut);
        if (!m.Success) return new ActivePlan("", "Unknown");
        var name = "Active scheme";
        var paren = r.StdOut.IndexOf('(');
        if (paren > 0)
        {
            var end = r.StdOut.IndexOf(')', paren);
            if (end > paren) name = r.StdOut.Substring(paren + 1, end - paren - 1).Trim();
        }
        return new ActivePlan(m.Value.ToLowerInvariant(), name);
    }

    public StepResult ActivateUltimate(out string? priorGuid)
    {
        var prior = QueryActive();
        priorGuid = prior.Guid.Length == 36 ? prior.Guid : null;

        var set = ShellRunner.Run("powercfg.exe", $"/setactive {UltimateGuid}");
        if (set.Ok) return StepResult.Success("Power plan", "Ultimate Performance");

        // Ultimate is hidden until duplicated on many systems.
        var dup = ShellRunner.Run("powercfg.exe", $"/duplicatescheme {UltimateGuid}");
        if (dup.Ok)
        {
            var m = GuidPattern().Match(dup.StdOut);
            if (m.Success && ShellRunner.Run("powercfg.exe", $"/setactive {m.Value}").Ok)
                return StepResult.Success("Power plan", "Ultimate Performance");
        }

        var high = ShellRunner.Run("powercfg.exe", $"/setactive {HighPerformanceGuid}");
        if (high.Ok) return StepResult.Success("Power plan", "High Performance (fallback)");

        return StepResult.Fail("Power plan", "powercfg refused — run elevated");
    }

    public StepResult Restore(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return StepResult.Skip("Power plan", "No saved scheme");

        var r = ShellRunner.Run("powercfg.exe", $"/setactive {guid}");
        return r.Ok
            ? StepResult.Success("Power plan", "Previous scheme restored")
            : StepResult.Fail("Power plan", "Restore failed — run elevated");
    }
}
