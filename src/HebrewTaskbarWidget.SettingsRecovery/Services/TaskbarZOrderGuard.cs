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
    /// תפריט ההתחל הוא מקרה אחר: כשהוא פתוח, Windows מעביר את שורת המשימות
    /// ל"שכבה" (Band) של ה-Shell, שמצוירת מעל כל החלונות העליונים הרגילים.
    /// לתוכנה רגילה אין דרך להיכנס לשכבה הזו, ולכן שום העלאה לא עוזרת - זו
    /// הסיבה שהוידג'ט נעלם כל עוד התחל פתוח. אבל חלון שבבעלות (Owner) שורת
    /// המשימות עובר איתה לשכבה, ותמיד נשאר מעליה - ראו <see cref="AttachToTaskbar"/>.
    /// שאר המנגנון נשאר כרשת ביטחון (למשל אם Explorer עולה מחדש).
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
        private IntPtr _originalOwner;
        private bool _originalOwnerSaved;
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

            AttachToTaskbar();
            HookExplorer();
            _timer.Start();
            EnsureAboveTaskbar();
        }

        /// <summary>Explorer עלה מחדש (הודעת TaskbarCreated) - שורת משימות חדשה, תהליך חדש.</summary>
        public void OnTaskbarRecreated()
        {
            AttachToTaskbar();
            HookExplorer();
            BeginBurst();
        }

        /// <summary>
        /// הופך את שורת המשימות לבעלים (Owner) של הוידג'ט. כך הוידג'ט:
        /// - עובר יחד איתה לשכבת ה-Shell כשתפריט ההתחל או החיפוש פתוחים
        ///   (נבדק: בלי בעלים הוידג'ט נשאר בשכבה הרגילה ומוסתר; עם בעלים הוא
        ///   עובר לאותה שכבה). Windows מעביר חלונות בבעלות רק ברגע שהשכבה
        ///   משתנה, ולכן הבעלות צריכה להיות קבועה ולא רק כשהתפריט נפתח.
        /// - נשאר תמיד מעליה בסדר השכבות, בלי מאבק.
        ///
        /// בעלות בין תהליכים מחברת את תורי הקלט של שני התהליכונים, כך שתקיעה
        /// של אחד הייתה תוקעת גם את הקלט של השני. מנתקים את החיבור מיד (נבדק:
        /// המעבר בין השכבות ממשיך לעבוד גם אחרי הניתוק), ובודקים שוב מדי פעם.
        /// כש-Explorer נסגר החלון שלנו לא נהרס - רק הבעלות מתאפסת (נבדק), והיא
        /// מוחזרת לשורת המשימות החדשה.
        /// </summary>
        private void AttachToTaskbar()
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero)
            {
                return;
            }

            IntPtr owner = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT);
            if (!_originalOwnerSaved)
            {
                _originalOwner = owner;
                _originalOwnerSaved = true;
            }

            if (owner != tray)
            {
                NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT, tray);
                PositionDiagnosticsLogger.Log("הוידג'ט הוצמד לשורת המשימות (Owner)");
            }

            DetachInputFrom(tray);
        }

        private void DetachInputFrom(IntPtr window)
        {
            uint otherThread = NativeMethods.GetWindowThreadProcessId(window, out _);
            uint ownThread = NativeMethods.GetWindowThreadProcessId(_hwnd, out _);
            if (otherThread != 0 && otherThread != ownThread)
            {
                // מחזיר false אם לא היו מחוברים - אין נזק בקריאה חוזרת.
                NativeMethods.AttachThreadInput(ownThread, otherThread, false);
            }
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

            // כשתוכנה במסך מלא (סרט, משחק) פעילה, Explorer מוריד את שורת המשימות
            // מ"עליון", והוידג'ט שבבעלותה יורד איתה (נבדק). שם הוא צריך להישאר -
            // מוסתר מאחורי המסך המלא כמו שורת המשימות, ולא לצוף מעליו.
            if (lostTopmost && IsOwnedByNonTopmostTaskbar())
            {
                return false;
            }

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

        private bool IsOwnedByNonTopmostTaskbar()
        {
            IntPtr owner = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT);
            return owner != IntPtr.Zero &&
                   owner == NativeMethods.FindWindow("Shell_TrayWnd", null) &&
                   (NativeMethods.GetWindowLong(owner, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) == 0;
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
            // Explorer עלול לעלות מחדש גם בלי שהודעת TaskbarCreated הגיעה אלינו -
            // הבעלות מתאפסת אז, ומחזירים אותה כאן (בדיקה זולה).
            AttachToTaskbar();
            EnsureAboveTaskbar();

            if (_timer.Interval == BurstInterval && DateTime.UtcNow > _burstUntilUtc)
            {
                _timer.Interval = IdleInterval;
            }

            if (_timer.Interval == IdleInterval)
            {
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

            if (_originalOwnerSaved)
            {
                IntPtr tray = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT);
                NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT, _originalOwner);
                if (tray != IntPtr.Zero && tray != _originalOwner)
                {
                    DetachInputFrom(tray);
                }
            }

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
