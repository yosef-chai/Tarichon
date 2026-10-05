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
    public void Guard_KeepsWidgetAboveTaskbar()
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

                // החלון בבעלות שורת המשימות, ולכן Windows עצמו מעלה אותו יחד איתה - בלי רגע מוסתר.
                for (int i = 0; i < 5; i++)
                {
                    RaiseTaskbar(tray);
                    Assert.False(IsAbove(tray, hwnd), "שורת המשימות עלתה מעל החלון");
                }

                // רשת הביטחון, למקרה שאין בעלות (למשל רגע אחרי ש-Explorer עלה מחדש):
                // מבטלים את הבעלות, מעלים את שורת המשימות, והשומר מחזיר את החלון.
                NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);
                RaiseTaskbar(tray);
                Assert.True(IsAbove(tray, hwnd), "הסימולציה לא העלתה את שורת המשימות");

                var stopwatch = Stopwatch.StartNew();
                Assert.True(PumpUntil(() => !IsAbove(tray, hwnd), TimeSpan.FromSeconds(1)), "השומר לא החזיר את החלון מעל שורת המשימות");
                _output.WriteLine($"הוחזר מעל שורת המשימות אחרי {stopwatch.ElapsedMilliseconds} מ\"ש");
                // מיד מאירוע שינוי הסדר, לא מהטיימר (כל 100 מ"ש) ולא כמו הטיימר הישן (150 מ"ש).
                Assert.True(stopwatch.ElapsedMilliseconds < 90, $"{stopwatch.ElapsedMilliseconds} ms");

                // והבעלות חוזרת בבדיקה הבאה של הטיימר.
                Assert.True(PumpUntil(() => NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT) == tray, TimeSpan.FromSeconds(1)), "הבעלות לא חזרה");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Guard_MakesTaskbarTheOwner_WithoutAttachingInput_AndRestoresOnDispose()
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
            IntPtr originalOwner = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT);
            var guard = new TaskbarZOrderGuard(hwnd, window.Dispatcher);

            try
            {
                guard.Start();
                Assert.Equal(tray, NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT));

                // ניתוק שמצליח = התורים היו מחוברים. אחרי Start הם כבר מנותקים.
                uint trayThread = NativeMethods.GetWindowThreadProcessId(tray, out _);
                Assert.False(NativeMethods.AttachThreadInput(NativeMethods.GetCurrentThreadId(), trayThread, false), "תורי הקלט עדיין מחוברים לשורת המשימות");

                guard.Dispose();
                Assert.Equal(originalOwner, NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT));
            }
            finally
            {
                guard.Dispose();
                window.Close();
            }
        });
    }

    /// <summary>
    /// פותח את תפריט ההתחל באמת (מקש Windows) ובודק שהחלון עובר לאותה שכבה
    /// כמו שורת המשימות. רק כשמבקשים במפורש (TARICHON_INTERACTIVE_TESTS=1),
    /// כי זה פותח את התפריט על המסך.
    /// </summary>
    [Fact]
    public void Guard_KeepsWidgetWithTaskbar_WhileStartMenuIsOpen()
    {
        if (Environment.GetEnvironmentVariable("TARICHON_INTERACTIVE_TESTS") != "1")
        {
            return;
        }

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

            bool startOpen = false;
            try
            {
                guard.Start();
                PumpUntil(() => false, TimeSpan.FromMilliseconds(100));

                PressKey(0x5B); // Windows
                startOpen = true;
                PumpUntil(() => Band(tray) != Band(hwnd) || Band(tray) > 1, TimeSpan.FromSeconds(2));
                PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
                uint trayBand = Band(tray);
                uint widgetBand = Band(hwnd);
                bool trayAbove = IsAbove(tray, hwnd);
                _output.WriteLine($"התחל פתוח: שכבת שורת המשימות {trayBand}, שכבת החלון {widgetBand}, שורת המשימות מעליו: {trayAbove}");

                Assert.True(trayBand > 1, "תפריט ההתחל לא נפתח (שורת המשימות לא עברה שכבה)");
                Assert.Equal(trayBand, widgetBand);
                Assert.False(trayAbove);

                PressKey(0x1B); // Escape
                startOpen = false;
                PumpUntil(() => false, TimeSpan.FromMilliseconds(400));
                Assert.False(IsAbove(tray, hwnd), "אחרי סגירת התחל שורת המשימות מעל החלון");
            }
            finally
            {
                if (startOpen)
                {
                    PressKey(0x1B);
                }

                window.Close();
            }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowBand(IntPtr hwnd, out uint band);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    private static uint Band(IntPtr hwnd) => GetWindowBand(hwnd, out uint band) ? band : 0;

    private static void PressKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
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
