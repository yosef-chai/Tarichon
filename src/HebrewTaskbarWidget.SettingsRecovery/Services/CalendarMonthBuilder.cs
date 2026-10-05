using System;
using System.Collections.Generic;
using System.Globalization;

namespace HebrewTaskbarWidget.Services
{
    public enum CalendarSystem
    {
        Hebrew,
        Gregorian,
    }

    /// <summary>תא יום ברשת החודש. Column 0 = יום ראשון (מוצג בצד ימין בפריסה עברית).</summary>
    public sealed record CalendarDayCell(
        DateTime Date,
        int Row,
        int Column,
        string PrimaryLabel,
        string SecondaryLabel,
        bool IsInDisplayedMonth);

    /// <summary>חודש מוצג: כותרת, שורת משנה בלוח השני, ורשת תאים מלאה (כולל ימים מהחודשים הסמוכים).</summary>
    public sealed record CalendarMonthView(
        CalendarSystem System,
        int Year,
        int Month,
        string Title,
        string Subtitle,
        int RowCount,
        IReadOnlyList<CalendarDayCell> Cells);

    /// <summary>
    /// בונה את רשת החודש לבורר התאריך בלוח הזמנים, בלוח עברי או לועזי.
    /// כל תא מציג את היום בלוח הראשי ואת המקביל בלוח השני, כך ששני הלוחות זמינים בו-זמנית.
    /// </summary>
    public static class CalendarMonthBuilder
    {
        private static readonly HebrewCalendar Hebrew = HebrewDateFormatter.Calendar;

        private static readonly string[] GregorianMonthNames =
        {
            "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני",
            "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר",
        };

        /// <summary>טווח התאריכים שבו שני הלוחות מוגדרים (מוגבל ע"י HebrewCalendar של ‎.NET).</summary>
        public static DateTime MinSupportedDate => Hebrew.MinSupportedDateTime.Date;

        public static DateTime MaxSupportedDate => Hebrew.MaxSupportedDateTime.Date;

        public static bool IsSupported(DateTime date) =>
            date.Date >= MinSupportedDate && date.Date <= MaxSupportedDate;

        public static string GetGregorianMonthName(int month) => GregorianMonthNames[month - 1];

        /// <summary>שנה וחודש (בלוח הנתון) שמכילים את התאריך.</summary>
        public static (int Year, int Month) MonthContaining(CalendarSystem system, DateTime date)
        {
            date = Clamp(date);
            return system == CalendarSystem.Hebrew
                ? (Hebrew.GetYear(date), Hebrew.GetMonth(date))
                : (date.Year, date.Month);
        }

        /// <summary>
        /// מקדם/מחזיר חודש אחד או יותר. מחזיר את החודש הנוכחי בלי שינוי אם היעד מחוץ לטווח הנתמך.
        /// בלוח העברי מספר החודשים בשנה משתנה (12 או 13), ולכן ההתקדמות נעשית חודש-חודש.
        /// </summary>
        public static (int Year, int Month) Step(CalendarSystem system, int year, int month, int delta)
        {
            int y = year;
            int m = month;

            try
            {
                for (int i = 0; i < Math.Abs(delta); i++)
                {
                    if (system == CalendarSystem.Gregorian)
                    {
                        m += Math.Sign(delta);
                        if (m < 1) { m = 12; y--; }
                        else if (m > 12) { m = 1; y++; }
                    }
                    else if (delta > 0)
                    {
                        if (m < Hebrew.GetMonthsInYear(y)) { m++; }
                        else { m = 1; y++; }
                    }
                    else
                    {
                        if (m > 1) { m--; }
                        else { y--; m = Hebrew.GetMonthsInYear(y); }
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return (year, month);
            }

            return IsMonthSupported(system, y, m) ? (y, m) : (year, month);
        }

        public static bool IsMonthSupported(CalendarSystem system, int year, int month)
        {
            try
            {
                (DateTime first, DateTime last) = GetMonthRange(system, year, month);
                return IsSupported(first) && IsSupported(last);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        /// <summary>
        /// מספר השורות ברשת קבוע (6 - המקסימום שחודש יכול לתפוס), כדי שגובה הלוח לא ישתנה
        /// במעבר בין חודשים או בין לוח עברי ללועזי. השורות העודפות מתמלאות בימי החודש הבא.
        /// </summary>
        public const int GridRows = 6;

        public static CalendarMonthView Build(CalendarSystem system, int year, int month)
        {
            (DateTime first, DateTime last) = GetMonthRange(system, year, month);

            int leading = (int)first.DayOfWeek;
            const int rowCount = GridRows;

            var cells = new List<CalendarDayCell>(rowCount * 7);
            DateTime gridStart = first.AddDays(-leading);

            for (int index = 0; index < rowCount * 7; index++)
            {
                DateTime date = gridStart.AddDays(index);
                if (!IsSupported(date))
                {
                    continue;
                }

                bool inMonth = date >= first && date <= last;
                cells.Add(new CalendarDayCell(
                    date,
                    index / 7,
                    index % 7,
                    PrimaryLabel(system, date),
                    SecondaryLabel(system, date),
                    inMonth));
            }

            return new CalendarMonthView(system, year, month, BuildTitle(system, year, month), BuildSubtitle(system, first, last), rowCount, cells);
        }

        /// <summary>התאריך הלועזי הראשון והאחרון בחודש המבוקש.</summary>
        public static (DateTime First, DateTime Last) GetMonthRange(CalendarSystem system, int year, int month)
        {
            if (system == CalendarSystem.Gregorian)
            {
                var first = new DateTime(year, month, 1);
                return (first, first.AddDays(DateTime.DaysInMonth(year, month) - 1));
            }

            DateTime hebrewFirst = Hebrew.ToDateTime(year, month, 1, 0, 0, 0, 0);
            int days = Hebrew.GetDaysInMonth(year, month);
            return (hebrewFirst, hebrewFirst.AddDays(days - 1));
        }

        /// <summary>"חשון תשפ"ז" או "אוקטובר 2026".</summary>
        public static string BuildTitle(CalendarSystem system, int year, int month)
        {
            return system == CalendarSystem.Gregorian
                ? $"{GetGregorianMonthName(month)} {year}"
                : $"{HebrewDateFormatter.GetMonthName(month, Hebrew.IsLeapYear(year))} {HebrewGematria.FormatYear(year)}";
        }

        /// <summary>טווח החודשים בלוח השני, למשל "ספטמבר–אוקטובר 2026" או "אלול תשפ"ו–תשרי תשפ"ז".</summary>
        public static string BuildSubtitle(CalendarSystem system, DateTime first, DateTime last)
        {
            if (system == CalendarSystem.Hebrew)
            {
                string firstMonth = GetGregorianMonthName(first.Month);
                string lastMonth = GetGregorianMonthName(last.Month);

                if (first.Year != last.Year)
                {
                    return $"{firstMonth} {first.Year}–{lastMonth} {last.Year}";
                }

                return first.Month == last.Month ? $"{firstMonth} {first.Year}" : $"{firstMonth}–{lastMonth} {first.Year}";
            }

            int firstYear = Hebrew.GetYear(first);
            int lastYear = Hebrew.GetYear(last);
            string firstName = HebrewDateFormatter.GetMonthName(Hebrew.GetMonth(first), Hebrew.IsLeapYear(firstYear));
            string lastName = HebrewDateFormatter.GetMonthName(Hebrew.GetMonth(last), Hebrew.IsLeapYear(lastYear));

            if (firstYear != lastYear)
            {
                return $"{firstName} {HebrewGematria.FormatYear(firstYear)}–{lastName} {HebrewGematria.FormatYear(lastYear)}";
            }

            return firstName == lastName
                ? $"{firstName} {HebrewGematria.FormatYear(firstYear)}"
                : $"{firstName}–{lastName} {HebrewGematria.FormatYear(firstYear)}";
        }

        private static string PrimaryLabel(CalendarSystem system, DateTime date) =>
            system == CalendarSystem.Gregorian
                ? date.Day.ToString(CultureInfo.InvariantCulture)
                : HebrewGematria.FormatDay(Hebrew.GetDayOfMonth(date));

        /// <summary>
        /// היום המקביל בלוח השני. בתחילת חודש בלוח השני מוצג שם החודש (לועזי: "1/11", עברי: "חשון"),
        /// כדי שמעבר החודש יהיה ברור בלי לחשב.
        /// </summary>
        private static string SecondaryLabel(CalendarSystem system, DateTime date)
        {
            if (system == CalendarSystem.Hebrew)
            {
                return date.Day == 1
                    ? $"{date.Day}/{date.Month}"
                    : date.Day.ToString(CultureInfo.InvariantCulture);
            }

            int hebrewDay = Hebrew.GetDayOfMonth(date);
            if (hebrewDay == 1)
            {
                int hebrewYear = Hebrew.GetYear(date);
                return HebrewDateFormatter.GetMonthName(Hebrew.GetMonth(date), Hebrew.IsLeapYear(hebrewYear));
            }

            return HebrewGematria.FormatDay(hebrewDay);
        }

        private static DateTime Clamp(DateTime date)
        {
            if (date.Date < MinSupportedDate)
            {
                return MinSupportedDate;
            }

            return date.Date > MaxSupportedDate ? MaxSupportedDate : date.Date;
        }
    }
}
