using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Stellary.PX.ViewModels;

namespace Stellary.PX.Infrastructure;

/// <summary>
/// Renders real pages to PNG straight from the visual tree (--capture). Used for
/// documentation and visual regression checks; unaffected by whatever is on screen.
/// </summary>
internal static class CaptureMode
{
    public static async Task RunAsync(Window window, ShellViewModel shell, string directory, string? onlyPage)
    {
        Directory.CreateDirectory(directory);

        // Captures must not be disturbed by stray clicks or focus changes.
        window.IsHitTestVisible = false;
        window.Topmost = true;

        var pages = shell.NavItems
            .Where(n => onlyPage is null ||
                        string.Equals(n.Key, onlyPage, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Label, onlyPage, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        AppLog.Info("capture", $"dir='{directory}' only='{onlyPage ?? "all"}' pages={pages.Length}");

        foreach (var nav in pages)
        {
            shell.SelectedNav = nav;
            AppLog.Info("capture", $"page='{nav.Key}' selected");
            // Let timers pull real samples so charts and readouts are not empty.
            await SettleAsync(window, nav.Key == "overview" ? 9000 : 700);

            if (!ReferenceEquals(shell.SelectedNav, nav))
            {
                AppLog.Info("capture", $"page='{nav.Key}' lost selection, reselecting");
                shell.SelectedNav = nav;
                await SettleAsync(window, 900);
            }

            var file = Path.Combine(directory, nav.Key + ".png");
            Save(window, file);
            AppLog.Info("capture", $"{file} · selected='{shell.SelectedNav?.Key}'");
        }

        Application.Current.Shutdown();
    }

    private static async Task SettleAsync(Window window, int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(60);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
        }
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(visual);
        var width = (int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0) return;

        var target = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
