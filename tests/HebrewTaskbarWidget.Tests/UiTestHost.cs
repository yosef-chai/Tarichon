using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// מריץ קוד WPF על חוט STA יחיד שחי לאורך כל הריצה (WPF דורש STA, ו-Application אחד לתהליך).
/// בנוסף שומר צילומי מצב של רכיבים לתיקייה ui-snapshots ליד קבצי הבדיקה, לבדיקה חזותית.
/// </summary>
internal static class UiTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(StartDispatcherThread);

    public static string SnapshotDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "ui-snapshots");

    public static void Run(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        SharedDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        failure?.Throw();
    }

    /// <summary>מעבד את כל העבודה הממתינה ב-Dispatcher (טעינה, סידור, Bindings).</summary>
    public static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public static ResourceDictionary LoadTheme() =>
        (ResourceDictionary)Application.LoadComponent(new Uri("/HebrewTaskbarWidget;component/Themes/SettingsTheme.xaml", UriKind.Relative));

    /// <summary>מצייר את הרכיב ל-PNG ומחזיר את התמונה (לבדיקות פיקסלים).</summary>
    public static BitmapSource Snapshot(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        // השוליים (מקום לצל) נכללים, אחרת הקצה הימני והתחתון נחתכים
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth + element.Margin.Left + element.Margin.Right));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight + element.Margin.Top + element.Margin.Bottom));

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        Directory.CreateDirectory(SnapshotDirectory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(SnapshotDirectory, name + ".png"));
        encoder.Save(stream);

        return bitmap;
    }

    public static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    public static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static Dispatcher StartDispatcherThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            // Application יחיד, כדי ש-pack URIs ו-Application.Current יעבדו כמו בתוכנה עצמה
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF UI tests",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
