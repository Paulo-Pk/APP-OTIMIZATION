using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using PCControlUltimate.App.ViewModels;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => ApplyWindows11PurpleFrame();
    }

    private void PeripheralTestArea_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border area || area.DataContext is not MainViewModel viewModel) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        viewModel.RecordPeripheralKey(key.ToString());
        e.Handled = true;
    }

    private void PeripheralTestArea_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border area || area.DataContext is not MainViewModel viewModel) return;
        System.Windows.Input.Keyboard.Focus(area);
        viewModel.RecordPeripheralMouseButton(e.ChangedButton.ToString());
    }

    private void PeripheralTestArea_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border area && area.DataContext is MainViewModel viewModel)
            viewModel.RecordPeripheralMouseMove();
    }

    private void PeripheralTestArea_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is System.Windows.Controls.Border area && area.DataContext is MainViewModel viewModel)
            viewModel.RecordPeripheralMouseWheel();
    }

    private void ApplyWindows11PurpleFrame()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // DWMWA_BORDER_COLOR (34) accepts a COLORREF in 0x00BBGGRR order.
            uint purpleColorRef = 0x00EA3393;
            _ = DwmSetWindowAttribute(hwnd, 34, ref purpleColorRef, sizeof(uint));
            // DWMWA_WINDOW_CORNER_PREFERENCE (33): DWMWCP_ROUND (2).
            int roundedCorners = 2;
            _ = DwmSetWindowAttribute(hwnd, 33, ref roundedCorners, sizeof(int));
        }
        catch
        {
            // Older Windows versions may not support DWM accent attributes; keep native frame.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    public MainWindow() : this(new MainViewModel())
    {
    }
}