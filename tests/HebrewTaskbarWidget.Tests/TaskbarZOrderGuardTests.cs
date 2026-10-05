using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HebrewTaskbarWidget.Interop;
using HebrewTaskbarWidget.Services;
using Xunit;
using Xunit.Abstractions;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// מול שורת המשימות האמיתית: מעלים אותה מעל חלון בדיקה שקוף, בדיוק כמו
/// ש-Explorer עושה בלחיצה עליה, ובודקים שהשומר מחזיר את החלון מעליה.
/// במחשב בלי שורת משימות (שרת בנייה) הבדיקות לא בודקות כלום. באותו אוסף
/// כמו שאר בדיקות הממשק, שרצות על אותו Dispatcher - אחרת הן רצות בתוך
/// ההמתנה כאן ומעכבות אותה.
/// </summary>
[Collection("UI")]
public class TaskbarZOrderGuardTests
{
    private readonly ITestOutputHelper _output;

    public TaskbarZOrderGuardTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guard_PutsWidgetBackAboveTaskbar_AndRespectsPause()
    {
        UiTestHost.Run(() =>
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                return;
            }

            Window window = CreateInvisibleTopmostWindow();
            window.Show();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            using var guard = new TaskbarZOrderGuard(hwnd, window.Dispatcher);

            try
            {
                guard.Start();
                Assert.True(PumpUntil(() => !IsAbove(tray, hwnd), TimeSpan.FromSeconds(1)), "החלון לא עלה מעל שורת המשימות בהפעלה");

                RaiseTaskbar(tray);
                Assert.True(IsAbove(tray, hwnd), "הסימולציה לא העלתה את שורת המשימות");

                var stopwatch = Stopwatch.StartNew();
                Assert.True(PumpUntil(() => !IsAbove(tray, hwnd), TimeSpan.FromSeconds(1)), "השומר לא החזיר את החלון מעל שורת המשימות");
                _output.WriteLine($"הוחזר מעל שורת המשימות אחרי {stopwatch.ElapsedMilliseconds} מ\"ש");
                // מיד מאירוע שינוי הסדר, לא מהטיימר (כל 100 מ"ש) ולא כמו הטיימר הישן (150 מ"ש).
                Assert.True(stopwatch.ElapsedMilliseconds < 90, $"{stopwatch.ElapsedMilliseconds} ms");

                // בזמן שתפריט ההקשר פתוח השומר לא נוגע בסדר השכבות.
                guard.Paused = true;
                RaiseTaskbar(tray);
                PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
                Assert.True(IsAbove(tray, hwnd), "השומר העלה את החלון למרות ההשהיה");

                guard.Paused = false;
                Assert.False(IsAbove(tray, hwnd), "החלון לא הוחזר כשההשהיה הסתיימה");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ChevronLookup_DoesNotBlockTheUiThread()
    {
        UiTestHost.Run(() =>
        {
            if (NativeMethods.FindWindow("Shell_TrayWnd", null) == IntPtr.Zero)
            {
                return;
            }

            // הקריאה הראשונה ממתינה לתוצאה הראשונה מהרקע; אחריה - רק קריאת התוצאה.
            bool found = TaskbarClockLocator.TryLocateChevronButton(out RECT first);

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(found, TaskbarClockLocator.TryLocateChevronButton(out RECT again));
                if (found)
                {
                    Assert.InRange(Math.Abs(first.Left - again.Left), 0, 40);
                }
            }

            double perCall = stopwatch.Elapsed.TotalMilliseconds / 20;
            _output.WriteLine($"נמצא: {found} ({first.Left},{first.Top},{first.Right},{first.Bottom}), {perCall:0.00} מ\"ש לקריאה");
            Assert.True(perCall < 5, $"{perCall:0.00} ms per call");
        });
    }

    private static Window CreateInvisibleTopmostWindow() => new()
    {
        Width = 40,
        Height = 20,
        Left = 0,
        Top = 0,
        WindowStyle = WindowStyle.None,
        AllowsTransparency = true,
        Background = Brushes.Transparent,
        Opacity = 0,
        ShowInTaskbar = false,
        ShowActivated = false,
        Topmost = true,
    };

    /// <summary>מה ש-Explorer עושה בכל לחיצה על שורת המשימות.</summary>
    private static void RaiseTaskbar(IntPtr tray) =>
        NativeMethods.SetWindowPos(tray, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        IntPtr window = lower;
        for (int i = 0; i < 4096; i++)
        {
            window = NativeMethods.GetWindow(window, NativeMethods.GW_HWNDPREV);
            if (window == IntPtr.Zero)
            {
                return false;
            }

            if (window == upper)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>מריץ את ה-Dispatcher (טיימרים ואירועי מערכת) עד שהתנאי מתקיים.</summary>
    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
                (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        return condition();
    }
}
