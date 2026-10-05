using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;

namespace HebrewTaskbarWidget.Controls
{
    /// <summary>
    /// בורר תאריך בפריסה עברית, עם מעבר בין לוח עברי ללוח לועזי. בשני המצבים כל תא מציג
    /// גם את היום המקביל בלוח השני, וימים עם מועד מסומנים בנקודה (שם המועד ב-ToolTip).
    /// הפקד עובד תמיד מול DateTime לועזי; הרשת עצמה נבנית ב-CalendarMonthBuilder.
    /// </summary>
    public partial class HebrewCalendarPicker : UserControl
    {
        /// <summary>גובה תא (42) ועוד השוליים שלו - ראו DayCellButtonStyle.</summary>
        internal const double DayRowHeight = 44;

        private int _displayedYear;
        private int _displayedMonth;
        private bool _suppressModeEvent;

        public DateTime? SelectedDate { get; private set; }

        public CalendarSystem Mode { get; private set; } = CalendarSystem.Hebrew;

        /// <summary>מופעל כשבוחרים יום ברשת (לא בניווט בין חודשים).</summary>
        public event EventHandler<DateTime>? DateSelected;

        /// <summary>מופעל כשהמשתמש מחליף בין לוח עברי ללועזי.</summary>
        public event EventHandler<CalendarSystem>? ModeChanged;

        public HebrewCalendarPicker()
        {
            InitializeComponent();
            SetModeRadio(Mode);
        }

        /// <summary>מציג את החודש שמכיל את התאריך ומסמן אותו כנבחר.</summary>
        public void ShowMonthContaining(DateTime gregorianDate)
        {
            SelectedDate = gregorianDate.Date;
            (_displayedYear, _displayedMonth) = CalendarMonthBuilder.MonthContaining(Mode, gregorianDate);
            Rebuild();
        }

        /// <summary>מחליף לוח בלי להפעיל ModeChanged (לטעינת העדפה שמורה).</summary>
        public void SetMode(CalendarSystem mode)
        {
            Mode = mode;
            SetModeRadio(mode);
            (_displayedYear, _displayedMonth) = CalendarMonthBuilder.MonthContaining(mode, SelectedDate ?? AppTimeService.Today());
            Rebuild();
        }

        private void SetModeRadio(CalendarSystem mode)
        {
            _suppressModeEvent = true;
            HebrewModeRadio.IsChecked = mode == CalendarSystem.Hebrew;
            GregorianModeRadio.IsChecked = mode == CalendarSystem.Gregorian;
            _suppressModeEvent = false;
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressModeEvent)
            {
                return;
            }

            CalendarSystem mode = GregorianModeRadio.IsChecked == true ? CalendarSystem.Gregorian : CalendarSystem.Hebrew;
            if (mode == Mode)
            {
                return;
            }

            // שומרים על החודש שמוצג כרגע: עוברים לחודש בלוח השני שמכיל את אמצע החודש הנוכחי.
            DateTime anchor = SelectedDate ?? AppTimeService.Today();
            if (_displayedYear != 0)
            {
                (DateTime first, DateTime last) = CalendarMonthBuilder.GetMonthRange(Mode, _displayedYear, _displayedMonth);
                if (anchor < first || anchor > last)
                {
                    anchor = first.AddDays((last - first).Days / 2);
                }
            }

            Mode = mode;
            (_displayedYear, _displayedMonth) = CalendarMonthBuilder.MonthContaining(mode, anchor);
            Rebuild();
            ModeChanged?.Invoke(this, mode);
        }

        private void PrevMonthButton_Click(object sender, RoutedEventArgs e) => StepMonth(-1);

        private void NextMonthButton_Click(object sender, RoutedEventArgs e) => StepMonth(1);

        private void StepMonth(int delta)
        {
            (_displayedYear, _displayedMonth) = CalendarMonthBuilder.Step(Mode, _displayedYear, _displayedMonth, delta);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_displayedYear == 0)
            {
                (_displayedYear, _displayedMonth) = CalendarMonthBuilder.MonthContaining(Mode, AppTimeService.Today());
            }

            CalendarMonthView view = CalendarMonthBuilder.Build(Mode, _displayedYear, _displayedMonth);
            MonthYearText.Text = view.Title;
            SubtitleText.Text = view.Subtitle;

            PrevMonthButton.IsEnabled = CalendarMonthBuilder.Step(Mode, _displayedYear, _displayedMonth, -1) != (_displayedYear, _displayedMonth);
            NextMonthButton.IsEnabled = CalendarMonthBuilder.Step(Mode, _displayedYear, _displayedMonth, 1) != (_displayedYear, _displayedMonth);

            // שורות בגובה קבוע (גם אם שורה ריקה בקצה הטווח הנתמך), כדי שגובה הלוח לא ישתנה
            DaysGrid.Children.Clear();
            DaysGrid.RowDefinitions.Clear();
            for (int r = 0; r < view.RowCount; r++)
            {
                DaysGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DayRowHeight) });
            }

            DateTime today = AppTimeService.Today();
            var style = (Style)FindResource("DayCellButtonStyle");

            foreach (CalendarDayCell cell in view.Cells)
            {
                string? holiday = HolidayCalendar.GetDisplayText(cell.Date, HolidayDisplayContext.Popup);

                var button = new Button
                {
                    Content = cell,
                    Style = style,
                    Tag = SelectedDate == cell.Date ? "Selected" : (cell.Date == today ? "Today" : null),
                    ToolTip = holiday,
                };
                AutomationProperties.SetName(button, BuildAccessibleName(cell, holiday));

                DateTime date = cell.Date;
                button.Click += (_, _) => SelectDate(date);

                Grid.SetRow(button, cell.Row);
                Grid.SetColumn(button, cell.Column);
                DaysGrid.Children.Add(button);
            }
        }

        private void SelectDate(DateTime date)
        {
            SelectedDate = date;
            (_displayedYear, _displayedMonth) = CalendarMonthBuilder.MonthContaining(Mode, date);
            Rebuild();
            DateSelected?.Invoke(this, date);
        }

        private static string BuildAccessibleName(CalendarDayCell cell, string? holiday)
        {
            string name = $"{HebrewDateFormatter.Format(cell.Date).BottomLine}, {cell.Date:d/M/yyyy}";
            return holiday is null ? name : $"{name}, {holiday}";
        }
    }
}
