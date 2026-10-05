using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using HebrewTaskbarWidget.Interop;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;

namespace HebrewTaskbarWidget
{
    /// <summary>
    /// פריט תצוגה בודד ברשימת הזמנים - עוטף <see cref="ZmanEntry"/> עם מחרוזת
    /// זמן מפורמטת (או "—" אם לא ניתן היה לחשב), נוחה ל-Binding ב-XAML.
    /// </summary>
    public sealed class ZmanDisplayItem
    {
        public required string Name { get; init; }
        public required string DisplayTime { get; init; }

        /// <summary>true עבור הזמן הקרוב ביותר שעוד לא עבר (רק כאשר היום המוצג הוא היום בפועל) - מודגש בצבע ההדגשה.</summary>
        public bool IsNext { get; init; }

        public static ZmanDisplayItem From(ZmanEntry entry, bool isNext) => new()
        {
            Name = ZmanimCalendar.GetPopupDisplayName(entry),
            DisplayTime = entry.Time.HasValue ? AppTimeService.FormatZmanTime(entry.Time.Value) : "—",
            IsNext = isNext,
        };
    }

    /// <summary>
    /// חלונית לוח הזמנים: פרטי היום הנבחר, זמני היום ההלכתיים, ניווט בין ימים ולוח שנה
    /// עברי/לועזי לקפיצה לתאריך. נפתחת בלחיצה על הוידג'ט ונסגרת כשמאבדת פוקוס.
    /// </summary>
    public partial class ZmanimPopup : Window
    {
        /// <summary>השוליים השקופים סביב המשטח (בשביל הצל) - ראו Margin ב-ZmanimPopup.xaml.</summary>
        internal const double ShadowInset = 12.0;

        /// <summary>המרווח הנראה בין תחתית הלוח לראש הוידג'ט.</summary>
        private const double GapAboveWidget = 6.0;

        /// <summary>מרווח מינימלי בין ראש הלוח לראש אזור העבודה של המסך.</summary>
        private const double TopScreenMargin = 8.0;

        /// <summary>גובה מינימלי לחלון, גם במסך נמוך מאוד (הרשימה נגללת).</summary>
        internal const double MinWindowHeight = 360.0;

        private static readonly CultureInfo HebrewCulture = new("he-IL");

        // מיקום החישוב נלקח מהגדרות המשתמש; ברירת המחדל היא ירושלים.
        private GeoLocation _location = SettingsService.BuildLocation();

        private DateTime _selectedDate = AppTimeService.Today();

        private double _widgetLeft;
        private double _widgetWidth;

        private static readonly string[] DayOfWeekNames =
        {
            "יום ראשון", "יום שני", "יום שלישי", "יום רביעי", "יום חמישי", "יום שישי", "יום שבת",
        };

        private GlobalClickWatcher? _clickWatcher;

        // ה-HWND של הוידג'ט נחשב "מוגן": לחיצה עליו לא סוגרת כאן את הפופ-אפ, אלא
        // מגיעה ל-ToggleZmanimPopup שסוגר אותו. אחרת הפופ-אפ היה נסגר ונפתח מחדש מיד.
        private readonly IntPtr _additionalProtectedHandle;

        public ZmanimPopup(IntPtr additionalProtectedHandle = default)
        {
            _additionalProtectedHandle = additionalProtectedHandle;

            InitializeComponent();

            ApplyTheme(SettingsService.Current.ZmanimPopupDarkMode);
            HebrewDatePicker.SetMode(SettingsService.Current.ZmanimPopupCalendarGregorian ? CalendarSystem.Gregorian : CalendarSystem.Hebrew);
            RefreshDisplay();

            SettingsService.SettingsChanged += SettingsService_SettingsChanged;
            Closed += (_, _) =>
            {
                SettingsService.SettingsChanged -= SettingsService_SettingsChanged;
                _clickWatcher?.Dispose();
                _clickWatcher = null;
            };

            // Deactivated לא תמיד עקבי בחלון AllowsTransparency+Topmost, ולכן
            // GlobalClickWatcher משמש רשת ביטחון ברמת Win32. נרשם רק כשיש HWND.
            SourceInitialized += (_, _) =>
            {
                IntPtr myHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _clickWatcher = new GlobalClickWatcher(
                    onClickOutside: () => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // מגן מסגירה כפולה (Deactivated וה-Watcher עשויים לפעול יחד)
                        if (IsLoaded)
                        {
                            Close();
                        }
                    })),
                    getProtectedRootHandles: () => _additionalProtectedHandle != IntPtr.Zero
                        ? new[] { myHandle, _additionalProtectedHandle }
                        : new[] { myHandle });
            };
        }

        private void SettingsService_SettingsChanged(object? sender, EventArgs e)
        {
            _location = SettingsService.BuildLocation();
            Dispatcher.Invoke(() =>
            {
                ApplyTheme(SettingsService.Current.ZmanimPopupDarkMode);
                RefreshDisplay();
            });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.OpenSettings();
            }
        }

        /// <summary>
        /// מחליף בין רקע כהה לבהיר. נשמר מיד (בלי לחכות לפאנל ההגדרות) כדי שההעדפה
        /// תישמר גם אם הפופ-אפ נסגר. אותה העדפה חלה גם על תפריט ההקשר של הוידג'ט.
        /// </summary>
        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            AppSettings settings = SettingsService.Current;
            bool newDarkMode = !settings.ZmanimPopupDarkMode;

            settings.ZmanimPopupDarkMode = newDarkMode;
            SettingsService.Save(settings);

            ApplyTheme(newDarkMode);
        }

        /// <summary>מחילה את הפלטה המשותפת ומעדכנת את סמל הכפתור (שמש = מעבר לבהיר, ירח = מעבר לכהה).</summary>
        internal void ApplyTheme(bool dark)
        {
            AppTheme.Apply(Resources, dark);

            string tip = dark ? "מעבר למצב בהיר" : "מעבר למצב כהה";
            ThemeToggleButton.Content = dark ? "" : "";
            ThemeToggleButton.ToolTip = tip;
            AutomationProperties.SetName(ThemeToggleButton, tip);
        }

        /// <summary>מצב "צמוד לקצה" של הוידג'ט בזמן הפתיחה, כפי שחושב ב-MainWindow - נצרך רק ביישור Auto.</summary>
        private WidgetAttachSide? _widgetEdgeSnapAlignment;

        /// <summary>
        /// ממקמת את הלוח מעל הוידג'ט לפי היישור שבהגדרות: אוטומטי (ממורכז, או צמוד לקצה
        /// אם הוידג'ט צמוד לקצה מסך), ממורכז, צמוד לקצה הימני או צמוד לקצה השמאלי.
        /// יש לקרוא לה לפני Show: החלון נפתח ישר במקומו ובגודלו, ולא זז אחר כך.
        /// </summary>
        public void PositionAboveWidget(double widgetLeft, double widgetTop, double widgetWidth, WidgetAttachSide? widgetEdgeSnapAlignment = null)
        {
            _widgetLeft = widgetLeft;
            _widgetWidth = widgetWidth;
            _widgetEdgeSnapAlignment = widgetEdgeSnapAlignment;

            if (TryGetWorkAreaTop(out double workTop))
            {
                Height = ComputeWindowHeight(widgetTop, workTop);
            }

            Left = ComputeLeft(Width);
            Top = ComputePopupTop(widgetTop, Height);
        }

        /// <summary>כל הגובה הפנוי מראש אזור העבודה ועד מעל הוידג'ט, כולל שולי הצל.</summary>
        internal static double ComputeWindowHeight(double widgetTop, double workAreaTop) =>
            Math.Max(MinWindowHeight, widgetTop - GapAboveWidget - workAreaTop - TopScreenMargin + 2 * ShadowInset);

        /// <summary>מיקום ה-Left של החלון לפי היישור הנוכחי, כולל קיזוז שולי הצל.</summary>
        private double ComputeLeft(double popupWidth)
        {
            ZmanimPopupAlignment alignment = SettingsService.Current.ZmanimPopupAlignment;

            if (alignment == ZmanimPopupAlignment.Auto)
            {
                alignment = _widgetEdgeSnapAlignment switch
                {
                    WidgetAttachSide.Right => ZmanimPopupAlignment.RightEdge,
                    WidgetAttachSide.Left => ZmanimPopupAlignment.LeftEdge,
                    _ => ZmanimPopupAlignment.Center,
                };
            }

            return ComputePopupLeft(alignment, _widgetLeft, _widgetWidth, popupWidth);
        }

        /// <summary>חישוב טהור של Left: המשטח הנראה (בלי שולי הצל) מיושר לוידג'ט.</summary>
        internal static double ComputePopupLeft(ZmanimPopupAlignment alignment, double widgetLeft, double widgetWidth, double popupWidth)
        {
            return alignment switch
            {
                ZmanimPopupAlignment.RightEdge => widgetLeft + widgetWidth - popupWidth + ShadowInset,
                ZmanimPopupAlignment.LeftEdge => widgetLeft - ShadowInset,
                _ => widgetLeft + (widgetWidth / 2.0) - (popupWidth / 2.0),
            };
        }

        /// <summary>חישוב טהור של Top: תחתית המשטח הנראה נמצאת GapAboveWidget פיקסלים מעל הוידג'ט.</summary>
        internal static double ComputePopupTop(double widgetTop, double popupHeight) =>
            widgetTop - popupHeight + ShadowInset - GapAboveWidget;

        /// <summary>ראש אזור העבודה של המסך שעליו הוידג'ט, ביחידות WPF של הוידג'ט.</summary>
        private bool TryGetWorkAreaTop(out double workTopDip)
        {
            workTopDip = 0;
            IntPtr handle = _additionalProtectedHandle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            const uint MonitorDefaultToNearest = 2;
            IntPtr monitor = NativeMethods.MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            // החלון עוד לא מוצג, ולכן קנה המידה נלקח מהוידג'ט (שעל אותו מסך)
            double scaleY = System.Windows.Interop.HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformToDevice.M22
                            ?? VisualTreeHelper.GetDpi(this).DpiScaleY;
            workTopDip = info.rcWork.Top / scaleY;
            return true;
        }

        private void RefreshDisplay()
        {
            HebrewDateDisplay hebrewDisplay = HebrewDateFormatter.Format(_selectedDate);
            DateTime today = AppTimeService.Today();

            string dayName = DayOfWeekNames[(int)_selectedDate.DayOfWeek];
            string? parashaName = ParashaService.GetParashaName(_selectedDate);
            DayHeaderText.Text = parashaName is null ? dayName : $"{dayName} · פרשת {parashaName}";

            HebrewDateHeaderText.Text = hebrewDisplay.BottomLine;
            GregorianDateHeaderText.Text = FormatGregorianLong(_selectedDate);

            string? holidayName = HolidayCalendar.GetDisplayText(_selectedDate, HolidayDisplayContext.Popup);
            HolidayHeaderText.Text = holidayName ?? string.Empty;
            HolidayChip.Visibility = holidayName is null ? Visibility.Collapsed : Visibility.Visible;

            IReadOnlyList<ZmanEntry> zmanim = ZmanimCalendar.Calculate(
                    _selectedDate, _location,
                    SettingsService.Current.CandleLightingMinutesBeforeSunset,
                    SettingsService.Current.TzeitHakochavimMinutesAfterSunset,
                    SettingsService.Current.DefaultZmanCalculationMethod,
                    SettingsService.Current.ZmanCustomizations,
                    SettingsService.Current.ZmanDuplicateRows)
                .Where(z => SettingsService.Current.IsZmanVisible(z.Key))
                .ToList();

            // הזמן "הקרוב" מסומן רק כשמוצג היום עצמו: הראשון ברשימה (הכרונולוגית) שעוד לא הגיע.
            ZmanEntry? nextEntry = null;
            if (_selectedDate.Date == today)
            {
                DateTime now = AppTimeService.Now();
                nextEntry = zmanim.FirstOrDefault(z => z.Time.HasValue && z.Time.Value > now);
            }

            ZmanimList.ItemsSource = zmanim.Select(entry => ZmanDisplayItem.From(entry, isNext: entry == nextEntry)).ToList();

            TodayButton.Visibility = _selectedDate.Date != today ? Visibility.Visible : Visibility.Collapsed;

            HebrewDatePicker.ShowMonthContaining(_selectedDate);
        }

        /// <summary>"5 באוקטובר 2026".</summary>
        internal static string FormatGregorianLong(DateTime date) =>
            $"{date.Day} ב{CalendarMonthBuilder.GetGregorianMonthName(date.Month)} {date.Year.ToString(HebrewCulture)}";

        private void PrevDayButton_Click(object sender, RoutedEventArgs e) => MoveToDate(_selectedDate.AddDays(-1));

        private void NextDayButton_Click(object sender, RoutedEventArgs e) => MoveToDate(_selectedDate.AddDays(1));

        private void TodayButton_Click(object sender, RoutedEventArgs e) => MoveToDate(AppTimeService.Today());

        private void MoveToDate(DateTime date)
        {
            if (!CalendarMonthBuilder.IsSupported(date))
            {
                return;
            }

            _selectedDate = date.Date;
            RefreshDisplay();
        }

        private void DatePickerButton_Click(object sender, RoutedEventArgs e)
        {
            bool willShow = DatePickerHost.Visibility != Visibility.Visible;
            SetDatePickerOpen(willShow);

            if (willShow)
            {
                HebrewDatePicker.ShowMonthContaining(_selectedDate);
            }
        }

        private void SetDatePickerOpen(bool open)
        {
            DatePickerHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            DatePickerButton.IsChecked = open;
        }

        private void HebrewDatePicker_DateSelected(object? sender, DateTime pickedDate)
        {
            if (pickedDate.Date != _selectedDate.Date)
            {
                _selectedDate = pickedDate.Date;
                RefreshDisplay();
            }

            SetDatePickerOpen(false);
        }

        private void HebrewDatePicker_ModeChanged(object? sender, CalendarSystem mode)
        {
            AppSettings settings = SettingsService.Current;
            settings.ZmanimPopupCalendarGregorian = mode == CalendarSystem.Gregorian;
            SettingsService.Save(settings);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DatePickerHost.Visibility == Visibility.Visible)
                {
                    SetDatePickerOpen(false);
                }
                else
                {
                    Close();
                }

                e.Handled = true;
            }
        }

        private void ZmanimPopup_Deactivated(object? sender, EventArgs e)
        {
            // התנהגות פופ-אפ רגילה: נסגר כשלוחצים במקום אחר
            Close();
        }
    }
}
