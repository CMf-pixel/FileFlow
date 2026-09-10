using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FileFlow.App.Infrastructure;

/// <summary>Styles the native Windows caption; leaves system sizing, buttons and rendering intact.</summary>
public static class NativeWindowChrome
{
    public static readonly DependencyProperty UseDarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "UseDarkTitleBar", typeof(bool), typeof(NativeWindowChrome), new PropertyMetadata(false, OnChanged));

    public static bool GetUseDarkTitleBar(DependencyObject target) => (bool)target.GetValue(UseDarkTitleBarProperty);
    public static void SetUseDarkTitleBar(DependencyObject target, bool value) => target.SetValue(UseDarkTitleBarProperty, value);

    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window) return;
        window.SourceInitialized -= OnSourceInitialized;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += OnSourceInitialized;
    }

    private static void OnSourceInitialized(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= OnSourceInitialized;
        Apply(window);
    }

    private static void Apply(Window window)
    {
        // These documented caption attributes start at Windows 11 build 22000.
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        // Older Windows and high-contrast mode retain their standard native caption.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = GetUseDarkTitleBar(window);
        SetAttribute(handle, 20, enabled ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        // Explicit caption colors keep the app dark even when Windows uses a light app theme.
        SetAttribute(handle, 35, enabled ? ColorRef(window.Background) : -1); // DWMWA_CAPTION_COLOR
        SetAttribute(handle, 36, enabled ? ColorRef(window.Foreground) : -1); // DWMWA_TEXT_COLOR
    }

    private static int ColorRef(Brush brush) => brush is SolidColorBrush solid
        ? solid.Color.R | (solid.Color.G << 8) | (solid.Color.B << 16)
        : -1; // DWMWA_COLOR_DEFAULT

    private static void SetAttribute(IntPtr handle, int attribute, int value)
    {
        var result = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        if (result < 0) Trace.TraceInformation("Native caption attribute {0} was unavailable (0x{1:X8}).", attribute, result);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
