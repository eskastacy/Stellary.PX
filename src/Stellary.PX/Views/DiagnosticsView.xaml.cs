using System.Windows.Controls;

namespace Stellary.PX.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        LogBox.TextChanged += (_, _) => LogBox.ScrollToEnd();
    }
}
