using System.Globalization;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

public class CalendarMonthBuilderTests
{
    private static readonly HebrewCalendar Hebrew = new();

    [Fact]
    public void TishreiTashpaz_HasKnownStartTitleAndGregorianSpan()
    {
        // ראש השנה תשפ"ז חל בשבת, 12 בספטמבר 2026
        CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 1);

        CalendarDayCell first = view.Cells.First(c => c.IsInDisplayedMonth);
        Assert.Equal(new DateTime(2026, 9, 12), first.Date);
        Assert.Equal(6, first.Column);
        Assert.Equal("א'", first.PrimaryLabel);
        Assert.Equal("12", first.SecondaryLabel);

        Assert.Equal("תשרי ה'תשפ\"ז", view.Title);
        Assert.Equal("ספטמבר–אוקטובר 2026", view.Subtitle);
    }

    [Fact]
    public void October2026_ShowsHebrewDaysAndRoshChodeshName()
    {
        CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Gregorian, 2026, 10);

        Assert.Equal("אוקטובר 2026", view.Title);
        Assert.Equal("תשרי–חשון ה'תשפ\"ז", view.Subtitle);

        CalendarDayCell oct1 = view.Cells.Single(c => c.Date == new DateTime(2026, 10, 1));
        Assert.Equal("1", oct1.PrimaryLabel);
        Assert.Equal("כ'", oct1.SecondaryLabel); // 20 בתשרי
        Assert.Equal(4, oct1.Column); // יום חמישי

        // א' חשון תשפ"ז = 12 באוקטובר 2026: בתא מוצג שם החודש במקום "א'"
        CalendarDayCell roshChodesh = view.Cells.Single(c => c.Date == new DateTime(2026, 10, 12));
        Assert.Equal("חשון", roshChodesh.SecondaryLabel);
    }

    [Fact]
    public void HebrewMode_MarksFirstOfGregorianMonth()
    {
        CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 1);
        CalendarDayCell oct1 = view.Cells.Single(c => c.Date == new DateTime(2026, 10, 1));
        Assert.Equal("1/10", oct1.SecondaryLabel);
    }

    [Fact]
    public void MonthSpanningTwoGregorianYears_ShowsBothYears()
    {
        // טבת תשפ"ז: מדצמבר 2026 עד ינואר 2027
        CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 4);
        Assert.Equal("טבת ה'תשפ\"ז", view.Title);
        Assert.Equal("דצמבר 2026–ינואר 2027", view.Subtitle);
    }

    [Fact]
    public void GregorianMonthSpanningTwoHebrewYears_ShowsBothYears()
    {
        // ספטמבר 2026: אלול תשפ"ו ותשרי תשפ"ז
        CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Gregorian, 2026, 9);
        Assert.Equal("אלול ה'תשפ\"ו–תשרי ה'תשפ\"ז", view.Subtitle);
    }

    [Fact]
    public void LeapYear_UsesAdarIAndAdarII()
    {
        // תשפ"ז (5787) היא שנה מעוברת
        Assert.True(Hebrew.IsLeapYear(5787));
        Assert.StartsWith("אדר א'", CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 6).Title);
        Assert.StartsWith("אדר ב'", CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 7).Title);
        Assert.StartsWith("ניסן", CalendarMonthBuilder.Build(CalendarSystem.Hebrew, 5787, 8).Title);
    }

    // כל חודש לועזי בטווח רחב: רשת רציפה, עמודות לפי יום בשבוע, ומספר ימים נכון
    [Fact]
    public void EveryGregorianMonth_1900To2100_BuildsConsistentGrid()
    {
        for (int year = 1900; year <= 2100; year++)
        {
            for (int month = 1; month <= 12; month++)
            {
                CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Gregorian, year, month);
                AssertGridIsConsistent(view);

                List<CalendarDayCell> inMonth = view.Cells.Where(c => c.IsInDisplayedMonth).ToList();
                Assert.Equal(DateTime.DaysInMonth(year, month), inMonth.Count);
                Assert.All(inMonth, c => Assert.Equal((year, month), (c.Date.Year, c.Date.Month)));
                Assert.All(view.Cells, c =>
                {
                    int hebrewDay = Hebrew.GetDayOfMonth(c.Date);
                    int hebrewYear = Hebrew.GetYear(c.Date);
                    string expected = hebrewDay == 1
                        ? HebrewDateFormatter.GetMonthName(Hebrew.GetMonth(c.Date), Hebrew.IsLeapYear(hebrewYear))
                        : HebrewGematria.FormatDay(hebrewDay);
                    Assert.Equal(expected, c.SecondaryLabel);
                });
            }
        }
    }

    [Fact]
    public void EveryHebrewMonth_5660To5860_BuildsConsistentGrid()
    {
        for (int year = 5660; year <= 5860; year++)
        {
            int months = Hebrew.GetMonthsInYear(year);
            for (int month = 1; month <= months; month++)
            {
                CalendarMonthView view = CalendarMonthBuilder.Build(CalendarSystem.Hebrew, year, month);
                AssertGridIsConsistent(view);

                List<CalendarDayCell> inMonth = view.Cells.Where(c => c.IsInDisplayedMonth).ToList();
                Assert.Equal(Hebrew.GetDaysInMonth(year, month), inMonth.Count);
                Assert.All(inMonth, c =>
                {
                    Assert.Equal(year, Hebrew.GetYear(c.Date));
                    Assert.Equal(month, Hebrew.GetMonth(c.Date));
                    Assert.Equal(HebrewGematria.FormatDay(Hebrew.GetDayOfMonth(c.Date)), c.PrimaryLabel);
                });
            }
        }
    }

    [Theory]
    [InlineData(5786, 12, 1, 5787, 1)]  // אלול → תשרי בשנה הבאה (שנה פשוטה)
    [InlineData(5787, 13, 1, 5788, 1)]  // אלול של שנה מעוברת הוא חודש 13
    [InlineData(5787, 1, -1, 5786, 12)] // תשרי → אלול של שנה פשוטה
    [InlineData(5788, 1, -1, 5787, 13)] // תשרי → אלול של שנה מעוברת
    [InlineData(5787, 6, 1, 5787, 7)]   // אדר א' → אדר ב'
    public void HebrewStep_CrossesYearsAndLeapMonths(int year, int month, int delta, int expectedYear, int expectedMonth)
    {
        Assert.Equal((expectedYear, expectedMonth), CalendarMonthBuilder.Step(CalendarSystem.Hebrew, year, month, delta));
    }

    [Theory]
    [InlineData(2026, 12, 1, 2027, 1)]
    [InlineData(2027, 1, -1, 2026, 12)]
    [InlineData(2026, 10, 14, 2027, 12)]
    public void GregorianStep_CrossesYears(int year, int month, int delta, int expectedYear, int expectedMonth)
    {
        Assert.Equal((expectedYear, expectedMonth), CalendarMonthBuilder.Step(CalendarSystem.Gregorian, year, month, delta));
    }

    [Theory]
    [InlineData(CalendarSystem.Hebrew)]
    [InlineData(CalendarSystem.Gregorian)]
    public void StepForwardThenBack_ReturnsToStart(CalendarSystem system)
    {
        (int year, int month) start = CalendarMonthBuilder.MonthContaining(system, new DateTime(2026, 10, 5));

        for (int n = 1; n <= 40; n++)
        {
            (int y, int m) = CalendarMonthBuilder.Step(system, start.year, start.month, n);
            Assert.Equal(start, CalendarMonthBuilder.Step(system, y, m, -n));
        }
    }

    [Theory]
    [InlineData(CalendarSystem.Hebrew)]
    [InlineData(CalendarSystem.Gregorian)]
    public void SupportedRangeEdges_DoNotThrowAndStopNavigation(CalendarSystem system)
    {
        (int y, int m) min = CalendarMonthBuilder.MonthContaining(system, CalendarMonthBuilder.MinSupportedDate);
        (int y, int m) max = CalendarMonthBuilder.MonthContaining(system, CalendarMonthBuilder.MaxSupportedDate);

        // החודש שבקצה עצמו עשוי להיות חלקי; הניווט לא יוצא מעבר לטווח ולא זורק
        (int y, int m) beforeMin = CalendarMonthBuilder.Step(system, min.y, min.m, -1);
        (int y, int m) afterMax = CalendarMonthBuilder.Step(system, max.y, max.m, 1);
        Assert.Equal(min, beforeMin);
        Assert.Equal(max, afterMax);

        (int y, int m) inside = CalendarMonthBuilder.Step(system, min.y, min.m, 1);
        CalendarMonthView view = CalendarMonthBuilder.Build(system, inside.y, inside.m);
        Assert.All(view.Cells, c => Assert.True(CalendarMonthBuilder.IsSupported(c.Date)));
    }

    [Fact]
    public void MonthContaining_ClampsOutOfRangeDates()
    {
        Assert.Equal(
            CalendarMonthBuilder.MonthContaining(CalendarSystem.Hebrew, CalendarMonthBuilder.MaxSupportedDate),
            CalendarMonthBuilder.MonthContaining(CalendarSystem.Hebrew, DateTime.MaxValue));
    }

    [Theory]
    [InlineData(1, "ינואר")]
    [InlineData(5, "מאי")]
    [InlineData(12, "דצמבר")]
    public void GregorianMonthNames_AreHebrew(int month, string expected)
    {
        Assert.Equal(expected, CalendarMonthBuilder.GetGregorianMonthName(month));
    }

    private static void AssertGridIsConsistent(CalendarMonthView view)
    {
        // גובה קבוע: תמיד 6 שורות, בכל חודש ובשני הלוחות
        Assert.Equal(CalendarMonthBuilder.GridRows, view.RowCount);
        Assert.Equal(view.RowCount * 7, view.Cells.Count);

        for (int i = 0; i < view.Cells.Count; i++)
        {
            CalendarDayCell cell = view.Cells[i];
            Assert.Equal(i / 7, cell.Row);
            Assert.Equal(i % 7, cell.Column);
            Assert.Equal((int)cell.Date.DayOfWeek, cell.Column);

            if (i > 0)
            {
                Assert.Equal(view.Cells[i - 1].Date.AddDays(1), cell.Date);
            }
        }

        // הימים מחוץ לחודש רק בתחילת הרשת ובסופה, לא באמצע; החודש מתחיל בשורה הראשונה
        List<bool> flags = view.Cells.Select(c => c.IsInDisplayedMonth).ToList();
        int firstIn = flags.IndexOf(true);
        int lastIn = flags.LastIndexOf(true);
        Assert.True(firstIn < 7);
        Assert.All(flags.Skip(firstIn).Take(lastIn - firstIn + 1), Assert.True);
        (DateTime first, DateTime last) = CalendarMonthBuilder.GetMonthRange(view.System, view.Year, view.Month);
        Assert.Equal((last - first).Days + 1, lastIn - firstIn + 1);
    }
}
