using System.Windows.Media;
using Stellary.PX.Core;

namespace Stellary.PX.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Eyebrow { get; }
    public virtual string Description => "";
    public virtual void Activate() { }
    public virtual void Deactivate() { }
}

public sealed class NavEntry
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required Geometry Icon { get; init; }
    public required PageViewModel Page { get; init; }
}

public sealed class PipelineRow : ObservableObject
{
    private string _detail = "";
    private bool _on;

    public PipelineRow(string label, string hint)
    {
        Label = label;
        Hint = hint;
    }

    public string Label { get; }
    public string Hint { get; }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public bool On { get => _on; set => Set(ref _on, value); }
}
