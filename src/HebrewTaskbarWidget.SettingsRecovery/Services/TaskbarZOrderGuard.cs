using System;
using System.Windows.Threading;
using HebrewTaskbarWidget.Interop;

namespace HebrewTaskbarWidget.Services
{
    /// <summary>
    /// שומר שהוידג'ט יישאר מעל שורת המשימות.
    ///
    /// שורת המשימות היא בעצמה חלון "עליון" (Topmost), ובכל פעם שהיא מופעלת -
    /// לחיצה על התחל, על סמל או על שטח ריק, ופתיחה של תוכנה - Explorer מעלה
    /// אותה לראש החלונות העליונים, מעל הוידג'ט. עד עכשיו טיימר החזיר את הוידג'ט
    /// למעלה כל 150 מ"ש בלי לבדוק כלום, אבל הטיימר רץ על אותו תהליכון כמו
    /// מדידת מיקום השעון (UI Automation מול Explorer), ובדיוק כש-Explorer
    /// עסוק המדידה נתקעה, הטיימר לא רץ, והוידג'ט נשאר מוסתר.
    ///
    /// עכשיו, בנוסף לכך שהמדידה רצה ברקע (ראו TaskbarClockLocator):
    /// - אירועי מערכת מפעילים בדיקה מיד: שינוי בסדר השכבות של החלונות
    ///   הראשיים (את העלאת שורת המשימות Windows מדווח עליו כשינוי סדר של
    ///   "שולחן העבודה", לא של שורת המשימות עצמה), החלפת החלון הפעיל, והצגת
    ///   חלונות של Explorer. אחריהם בדיקות צפופות (כל 16 מ"ש) במשך שנייה
    ///   וחצי - כי Explorer מעלה את שורת המשימות שוב כמה רגעים אחרי האירוע.
    /// - בין האירועים - בדיקה כל 100 מ"ש, כרשת ביטחון.
    /// - הבדיקה עצמה זולה (מעבר על החלונות שמעל הוידג'ט), והוידג'ט מועלה רק
    ///   אם שורת המשימות באמת מעליו או שהוא איבד את מצב ה"עליון". כך אין
    ///   הצפה של קריאות - הבעיה שבגללה ניסיון קודם עם אירועי מערכת (0.5.4)
    ///   הוסר.
    ///
    /// רץ על תהליכון הממשק של הוידג'ט: שינוי סדר השכבות של חלון מתבצע תמיד
    /// בתהליכון שיצר אותו, כך שאין טעם להריץ אותו במקום אחר.
    /// </summary>
    internal sealed class TaskbarZOrderGuard : IDisposable
    {
        private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan BurstInterval = TimeSpan.FromMilliseconds(16);
        private static readonly TimeSpan BurstDuration = TimeSpan.FromMilliseconds(1500);

        // החלונות שמעל הוידג'ט הם רק חלונות עליונים אחרים - בדרך כלל מעטים.
        private const int MaxWindowsToWalk = 1024;
        private const int MaxRaisesPerSecond = 20;

        private readonly IntPtr _hwnd;
        private readonly DispatcherTimer _timer;

        // שמירת הפניה ל-delegate: אחרת ה-GC עלול לאסוף אותו בזמן שה-Hook עדיין פעיל.
        private readonly NativeMethods.WinEventDelegate _winEventProc;

        private IntPtr _foregroundHook;
        private IntPtr _reorderHook;
        private IntPtr _explorerHook;
        private IntPtr _desktop;
        private uint _explorerProcessId;
        private DateTime _burstUntilUtc;
        private DateTime _lastLogUtc;
        private DateTime _raiseWindowStartUtc;
        private int _raisesInWindow;
        private bool _paused;
        private bool _disposed;

        public TaskbarZOrderGuard(IntPtr hwnd, Dispatcher dispatcher)
        {
            _hwnd = hwnd;
            _winEventProc = OnWinEvent;
            _timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = IdleInterval };
            _timer.Tick += (_, _) => OnTimerTick();
        }

        /// <summary>
        /// בזמן שתפריט ההקשר של הוידג'ט פתוח לא מעלים את הוידג'ט - התפריט
        /// עצמו אינו "עליון", והוידג'ט היה מסתיר אותו.
        /// </summary>
        public bool Paused
        {
            get => _paused;
            set
            {
                _paused = value;
                if (!value)
                {
                    EnsureAboveTaskbar();
                }
            }
        }

        public void Start()
        {
            if (_disposed || _foregroundHook != IntPtr.Zero)
            {
                return;
            }

            _desktop = NativeMethods.GetDesktopWindow();
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // מכל התהליכים: גם תפריט ההתחל והחיפוש (תהליכים נפרדים) מעלים את שורת המשימות.
            _reorderHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER,
                IntPtr.Zero, _winEventProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

            HookExplorer();
            _timer.Start();
            EnsureAboveTaskbar();
        }

        /// <summary>Explorer עלה מחדש (הודעת TaskbarCreated) - שורת משימות חדשה, תהליך חדש.</summary>
        public void OnTaskbarRecreated()
        {
            HookExplorer();
            BeginBurst();
        }

        /// <summary>
        /// מעלה את הוידג'ט אם שורת המשימות מעליו (או אם איבד את מצב ה"עליון").
        /// מחזיר true אם הועלה.
        /// </summary>
        public bool EnsureAboveTaskbar()
        {
            if (_disposed || _paused || !NativeMethods.IsWindowVisible(_hwnd))
            {
                return false;
            }

            bool lostTopmost = (NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) == 0;
            if (!lostTopmost && !IsTaskbarAbove())
            {
                return false;
            }

            // כל העלאה של הוידג'ט היא בעצמה שינוי סדר שמפעיל אירוע. אם תוכנה אחרת
            // מתעקשת להיות מעליו, זה היה הופך ללולאה - אז לכל היותר 20 בשנייה.
            DateTime now = DateTime.UtcNow;
            if (now - _raiseWindowStartUtc > TimeSpan.FromSeconds(1))
            {
                _raiseWindowStartUtc = now;
                _raisesInWindow = 0;
            }

            if (_raisesInWindow >= MaxRaisesPerSecond)
            {
                return false;
            }

            _raisesInWindow++;

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

            if (now - _lastLogUtc > TimeSpan.FromSeconds(1))
            {
                _lastLogUtc = now;
                PositionDiagnosticsLogger.Log(lostTopmost
                    ? "הוידג'ט איבד את מצב העליון - הוחזר"
                    : "שורת המשימות עלתה מעל הוידג'ט - הוידג'ט הוחזר מעליה");
            }

            return true;
        }

        /// <summary>האם שורת המשימות הראשית נמצאת מעל הוידג'ט בסדר השכבות.</summary>
        private bool IsTaskbarAbove()
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                return false;
            }

            IntPtr window = _hwnd;
            for (int i = 0; i < MaxWindowsToWalk; i++)
            {
                window = NativeMethods.GetWindow(window, NativeMethods.GW_HWNDPREV);
                if (window == IntPtr.Zero)
                {
                    return false;
                }

                if (window == tray)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            bool relevant = eventType switch
            {
                // רק שינוי סדר של החלונות הראשיים (ילדי שולחן העבודה), לא של רכיבים בתוך חלון.
                NativeMethods.EVENT_OBJECT_REORDER => hwnd == _desktop,
                // מ-Explorer מגיעים גם אירועים של רכיבים בתוך חלונות - רק חלונות שלמים.
                NativeMethods.EVENT_OBJECT_SHOW => idObject == NativeMethods.OBJID_WINDOW && idChild == 0,
                _ => true,
            };

            if (!relevant)
            {
                return;
            }

            EnsureAboveTaskbar();
            BeginBurst();
        }

        private void BeginBurst()
        {
            _burstUntilUtc = DateTime.UtcNow + BurstDuration;
            if (_timer.Interval != BurstInterval)
            {
                _timer.Interval = BurstInterval;
            }
        }

        private void OnTimerTick()
        {
            EnsureAboveTaskbar();

            if (_timer.Interval == BurstInterval && DateTime.UtcNow > _burstUntilUtc)
            {
                _timer.Interval = IdleInterval;
            }

            if (_timer.Interval == IdleInterval)
            {
                // Explorer עלול לעלות מחדש גם בלי שהודעת TaskbarCreated הגיעה אלינו.
                HookExplorer();
            }
        }

        /// <summary>
        /// אירועי הצגת חלונות - רק מהתהליך של Explorer (שורת המשימות, תפריטים
        /// ותצוגות מקדימות שלה), כדי לא לקבל אירועים מכל המערכת.
        /// </summary>
        private void HookExplorer()
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            uint processId = 0;
            if (tray != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(tray, out processId);
            }

            if (processId == _explorerProcessId && (_explorerHook != IntPtr.Zero || processId == 0))
            {
                return;
            }

            if (_explorerHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_explorerHook);
                _explorerHook = IntPtr.Zero;
            }

            _explorerProcessId = processId;
            if (processId != 0)
            {
                _explorerHook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
                    IntPtr.Zero, _winEventProc, processId, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();

            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }

            if (_reorderHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_reorderHook);
                _reorderHook = IntPtr.Zero;
            }

            if (_explorerHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_explorerHook);
                _explorerHook = IntPtr.Zero;
            }
        }
    }
}
