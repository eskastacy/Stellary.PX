namespace Stellary.PX.Core;

/// <summary>Outcome of one optimizer step. Failures are values, never exceptions on the UI path.</summary>
public sealed record StepResult(string Label, bool Ok, string Detail)
{
    public static StepResult Success(string label, string detail) => new(label, true, detail);
    public static StepResult Fail(string label, string detail) => new(label, false, detail);
    public static StepResult Skip(string label, string detail) => new(label, true, detail);
}
