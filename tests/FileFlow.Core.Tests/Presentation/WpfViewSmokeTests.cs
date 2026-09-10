using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using FileFlow.App.ViewModels;
using FileFlow.App.Views;
using FileFlow.Core.Persistence;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;
using Xunit;

namespace FileFlow.Core.Tests.Presentation;

public sealed class WpfViewSmokeTests
{
    [Fact]
    public async Task Views_load_templates_and_bindings_without_starting_the_application_or_touching_user_storage()
    {
        var finished = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var listener = new BindingErrors();
            try
            {
                // Base Application deliberately avoids FileFlow.App.OnStartup and its real storage/mutex.
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FileFlow.App;component/Resources/Theme.xaml", UriKind.Relative) });
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FileFlow.App;component/Resources/Controls.xaml", UriKind.Relative) });
                application.Resources.Add("BooleanToVisibilityConverter", new BooleanToVisibilityConverter());
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                var dialogs = new WorkflowTests.Dialogs();
                var workflow = new WorkflowTests.Workflow();
                var main = new MainWindowViewModel(new WorkflowTests.Store { Loaded = new(new[] { WorkflowTests.Rule() }, null) }, workflow, dialogs);
                main.Initialize();
                Layout(new FileFlow.App.MainWindow(main), 900, 620);
                Layout(new RuleEditorWindow(new RuleEditorViewModel(WorkflowTests.Rule(), rule => new(new[] { rule }, null), dialogs)), 620, 540);
                Layout(new RuleEditorWindow(new RuleEditorViewModel(new FileRule(), rule => new(new[] { rule }, null), dialogs)), 620, 540);
                foreach (var status in Enum.GetValues<PreviewStatus>())
                    Layout(new RunWindow(new RunViewModel(WorkflowTests.Preview(status), workflow)), 900, 650);
                Assert.Empty(listener.Errors);
                application.Shutdown();
                finished.SetResult(null);
            }
            catch (Exception exception) { finished.SetResult(exception); }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var failure = await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
    }

    private static void Layout(Window window, double width, double height)
    {
        window.ApplyTemplate();
        // EnsureHandle creates a hidden HWND: test native title-bar integration without showing the app.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Assert.Equal(0, DwmGetWindowAttribute(handle, 20, out var darkTitleBar, sizeof(int)));
            Assert.Equal(1, darkTitleBar);
            // Caption/text colors are documented for DwmSetWindowAttribute only;
            // querying them returns E_INVALIDARG even after a successful set.
        }
        var windowBackground = Assert.IsType<SolidColorBrush>(window.Background).Color;
        var primaryText = Assert.IsType<SolidColorBrush>(Application.Current.FindResource("PrimaryTextBrush")).Color;
        Assert.True(Contrast(primaryText, windowBackground) >= 4.5, "Window background does not support the shared theme text.");
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (var heading in Descendants<TextBlock>(content).Where(text => text.FontSize >= 18 && !string.IsNullOrEmpty(text.Text)))
        {
            var foreground = Assert.IsType<SolidColorBrush>(heading.Foreground).Color;
            Assert.True(Contrast(foreground, windowBackground) >= 4.5, $"Heading '{heading.Text}' has insufficient dark-theme contrast.");
        }
        window.Close();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) { var unit = value / 255.0; return unit <= 0.04045 ? unit / 12.92 : Math.Pow((unit + 0.055) / 1.055, 2.4); }
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private sealed class BindingErrors : TraceListener
    {
        public List<string> Errors { get; } = new();
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
