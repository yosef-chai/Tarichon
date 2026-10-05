using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HebrewTaskbarWidget.Controls;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// בדיקות ממשק: בונות את החלונות האמיתיים (בלי להציג אותם על המסך), מודדות ומציירות,
/// ובודקות צבעים, תוכן והתנהגות. צילומי המצב נשמרים ב-ui-snapshots לבדיקה חזותית.
/// </summary>
[Collection("UI")]
public class UiTests
{
    public UiTests() => TestIsolation.ResetSettings();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContextMenu_UsesThemedSurfaceAndReadableText(bool dark)
    {
        UiTestHost.Run(() =>
        {
            ContextMenu menu = BuildWidgetMenuLikeMainWindow(dark);
            BitmapSource bitmap = UiTestHost.Snapshot(menu, $"context-menu-{(dark ? "dark" : "light")}");

            IReadOnlyDictionary<string, string> palette = AppTheme.GetPalette(dark);
            Color expectedSurface = Parse(palette["FlyoutBackgroundBrush"]);
            Color expectedText = Parse(palette["PrimaryForegroundBrush"]);

            // פיקסל בתוך המשטח, בשוליים הפנימיים (בלי טקסט): 12 צל + 1 גבול + 2
            Color surface = UiTestHost.PixelAt(bitmap, (int)menu.ActualWidth - 15, (int)menu.ActualHeight / 2);
            AssertClose(expectedSurface, surface);

            List<TextBlock> headers = UiTestHost.FindVisualChildren<TextBlock>(menu)
                .Where(t => t.Text is "זמני היום" or "הגדרות" or "התראות" or "אודות" or "יציאה")
                .ToList();
            Assert.Equal(5, headers.Count);
            Assert.All(headers, t => Assert.Equal(expectedText, ((SolidColorBrush)t.Foreground).Color));

            double ratio = AppTheme.ContrastRatio(expectedText.ToString(), expectedSurface.ToString());
            Assert.True(ratio >= 7, $"ניגודיות טקסט התפריט {ratio:0.0}");

            // לכל פריט יש סמליל
            List<TextBlock> glyphs = UiTestHost.FindVisualChildren<TextBlock>(menu)
                .Where(t => t.FontFamily.Source.Contains("Segoe Fluent Icons") && !string.IsNullOrEmpty(t.Text))
                .ToList();
            Assert.Equal(5, glyphs.Count);
        });
    }

    // פינות מעוגלות וצל דורשים חלון שקוף; תפריט שנפתח באמת חייב לקבל Popup עם AllowsTransparency
    [Fact]
    public void ContextMenu_WhenOpened_HostedInTransparentPopup()
    {
        UiTestHost.Run(() =>
        {
            var target = new Window
            {
                Width = 10, Height = 10, Left = -30000, Top = -30000,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            };
            target.Show();
            try
            {
                ContextMenu menu = BuildWidgetMenuLikeMainWindow(dark: true);
                menu.PlacementTarget = target;
                menu.IsOpen = true;
                UiTestHost.DoEvents();

                var popup = Assert.IsType<Popup>(menu.Parent);
                Assert.True(popup.AllowsTransparency);
                Assert.False(menu.HasDropShadow);

                menu.IsOpen = false;
            }
            finally
            {
                target.Close();
            }
        });
    }

    [Fact]
    public void MainWindowXaml_ContextMenuUsesSharedStyleAndIcons()
    {
        string xaml = File.ReadAllText(RepoPaths.SourceFile("MainWindow.xaml"));
        Assert.Contains("Style=\"{DynamicResource AppContextMenuStyle}\"", xaml);
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(xaml, "<MenuItem[^>]*Tag=\"&#x[0-9A-F]{4};\"", System.Text.RegularExpressions.RegexOptions.Singleline).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CalendarPicker_SwitchesBetweenHebrewAndGregorian(bool dark)
    {
        UiTestHost.Run(() =>
        {
            (Border host, HebrewCalendarPicker picker) = BuildPickerHost(dark);
            var selected = new DateTime(2026, 10, 5);
            picker.ShowMonthContaining(selected);
            Layout(host, 320);

            Assert.Equal(CalendarSystem.Hebrew, picker.Mode);
            Assert.Equal("תשרי ה'תשפ\"ז", picker.MonthYearText.Text);
            Assert.Equal("ספטמבר–אוקטובר 2026", picker.SubtitleText.Text);
            // תשרי תשפ"ז מתחיל בשבת ונמשך 30 יום: 6 שורות
            Assert.Equal(42, picker.DaysGrid.Children.Count);
            Assert.Single(DayButtons(picker), b => (string?)b.Tag == "Selected" && ((CalendarDayCell)b.Content).Date == selected);
            UiTestHost.Snapshot(host, $"calendar-hebrew-{(dark ? "dark" : "light")}");

            CalendarSystem? changedTo = null;
            picker.ModeChanged += (_, mode) => changedTo = mode;
            picker.GregorianModeRadio.IsChecked = true;
            Layout(host, 320);

            Assert.Equal(CalendarSystem.Gregorian, changedTo);
            Assert.Equal("אוקטובר 2026", picker.MonthYearText.Text);
            Assert.Equal("תשרי–חשון ה'תשפ\"ז", picker.SubtitleText.Text);
            Assert.Equal("כ'", ((CalendarDayCell)DayButtons(picker).First(b => ((CalendarDayCell)b.Content).Date == new DateTime(2026, 10, 1)).Content).SecondaryLabel);
            UiTestHost.Snapshot(host, $"calendar-gregorian-{(dark ? "dark" : "light")}");

            // ניווט וחזרה
            picker.NextMonthButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("נובמבר 2026", picker.MonthYearText.Text);
            picker.PrevMonthButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("אוקטובר 2026", picker.MonthYearText.Text);

            // בחירת יום
            DateTime? picked = null;
            picker.DateSelected += (_, d) => picked = d;
            DayButtons(picker).First(b => ((CalendarDayCell)b.Content).Date == new DateTime(2026, 10, 20))
                .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(new DateTime(2026, 10, 20), picked);
            Assert.Equal(new DateTime(2026, 10, 20), picker.SelectedDate);
        });
    }

    [Fact]
    public void CalendarPicker_SetModeDoesNotRaiseModeChanged()
    {
        UiTestHost.Run(() =>
        {
            (_, HebrewCalendarPicker picker) = BuildPickerHost(dark: true);
            bool raised = false;
            picker.ModeChanged += (_, _) => raised = true;

            picker.SetMode(CalendarSystem.Gregorian);

            Assert.False(raised);
            Assert.True(picker.GregorianModeRadio.IsChecked);
            Assert.Equal(CalendarSystem.Gregorian, picker.Mode);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZmanimPopup_RendersHeaderZmanimAndCalendar(bool dark)
    {
        UiTestHost.Run(() =>
        {
            var popup = new ZmanimPopup();
            try
            {
                popup.ApplyTheme(dark);
                var root = (FrameworkElement)popup.Content;
                Layout(root);

                Assert.False(string.IsNullOrWhiteSpace(popup.DayHeaderText.Text));
                Assert.False(string.IsNullOrWhiteSpace(popup.HebrewDateHeaderText.Text));
                Assert.Matches(@"^\d{1,2} ב[א-ת]+ \d{4}$", popup.GregorianDateHeaderText.Text);
                Assert.NotEmpty((System.Collections.IEnumerable)popup.ZmanimList.ItemsSource);
                Assert.Equal(Visibility.Collapsed, popup.DatePickerHost.Visibility);

                BitmapSource bitmap = UiTestHost.Snapshot(root, $"zmanim-popup-{(dark ? "dark" : "light")}");
                AssertClose(Parse(AppTheme.GetPalette(dark)["FlyoutBackgroundBrush"]), UiTestHost.PixelAt(bitmap, 16, (int)root.ActualHeight / 2));

                // פתיחת לוח השנה
                popup.DatePickerButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout(root);
                Assert.Equal(Visibility.Visible, popup.DatePickerHost.Visibility);
                Assert.True(popup.DatePickerButton.IsChecked);
                UiTestHost.Snapshot(root, $"zmanim-popup-calendar-{(dark ? "dark" : "light")}");

                // בחירת יום בלוח מעדכנת את הכותרת וסוגרת את הלוח
                string before = popup.HebrewDateHeaderText.Text;
                Button anotherDay = DayButtons(popup.HebrewDatePicker).First(b => (string?)b.Tag != "Selected" && ((CalendarDayCell)b.Content).IsInDisplayedMonth);
                anotherDay.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.NotEqual(before, popup.HebrewDateHeaderText.Text);
                Assert.Equal(Visibility.Collapsed, popup.DatePickerHost.Visibility);
                Assert.False(popup.DatePickerButton.IsChecked);
                Assert.Equal(Visibility.Visible, popup.TodayButton.Visibility);

                // חזרה להיום
                popup.TodayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(before, popup.HebrewDateHeaderText.Text);
                Assert.Equal(Visibility.Collapsed, popup.TodayButton.Visibility);
            }
            finally
            {
                popup.Close();
            }
        });
    }

    [Fact]
    public void ZmanimPopup_DayNavigationMovesOneDay()
    {
        UiTestHost.Run(() =>
        {
            var popup = new ZmanimPopup();
            try
            {
                string today = popup.HebrewDateHeaderText.Text;
                popup.NextDayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(HebrewDateFormatter.Format(AppTimeService.Today().AddDays(1)).BottomLine, popup.HebrewDateHeaderText.Text);
                popup.PrevDayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                popup.PrevDayButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Equal(HebrewDateFormatter.Format(AppTimeService.Today().AddDays(-1)).BottomLine, popup.HebrewDateHeaderText.Text);
                Assert.NotEqual(today, popup.HebrewDateHeaderText.Text);
            }
            finally
            {
                popup.Close();
            }
        });
    }

    [Theory]
    [InlineData(ZmanimPopupAlignment.RightEdge)]
    [InlineData(ZmanimPopupAlignment.LeftEdge)]
    [InlineData(ZmanimPopupAlignment.Center)]
    public void ZmanimPopup_VisibleSurfaceAlignsWithWidget(ZmanimPopupAlignment alignment)
    {
        const double widgetLeft = 1500, widgetWidth = 180, widgetTop = 1040, popupWidth = 344, popupHeight = 600;
        double inset = ZmanimPopup.ShadowInset;

        double left = ZmanimPopup.ComputePopupLeft(alignment, widgetLeft, widgetWidth, popupWidth);
        double visibleLeft = left + inset;
        double visibleRight = left + popupWidth - inset;

        switch (alignment)
        {
            case ZmanimPopupAlignment.RightEdge:
                Assert.Equal(widgetLeft + widgetWidth, visibleRight);
                break;
            case ZmanimPopupAlignment.LeftEdge:
                Assert.Equal(widgetLeft, visibleLeft);
                break;
            default:
                Assert.Equal(widgetLeft + widgetWidth / 2, (visibleLeft + visibleRight) / 2);
                break;
        }

        double visibleBottom = ZmanimPopup.ComputePopupTop(widgetTop, popupHeight) + popupHeight - inset;
        Assert.Equal(widgetTop - 6, visibleBottom);
    }

    [Theory]
    [InlineData(1040, 0, 1040 - 6 - 8 + 24)] // כל הגובה מעל הוידג'ט
    [InlineData(1400, 40, 1400 - 6 - 40 - 8 + 24)]
    [InlineData(300, 0, ZmanimPopup.MinWindowHeight)] // מסך נמוך מאוד: לא פחות מהמינימום
    public void ZmanimPopup_WindowHeightFillsSpaceAboveWidget(double widgetTop, double workTop, double expected)
    {
        double height = ZmanimPopup.ComputeWindowHeight(widgetTop, workTop);
        Assert.Equal(expected, height);

        // ראש המשטח הנראה לא עולה מעל אזור העבודה (חוץ ממקרה המינימום)
        double top = ZmanimPopup.ComputePopupTop(widgetTop, height);
        if (height > ZmanimPopup.MinWindowHeight)
        {
            Assert.Equal(workTop + 8, top + ZmanimPopup.ShadowInset);
        }
    }

    /// <summary>
    /// מניעת ריצוד: כל לחיצה בלוח (ימים, לוח שנה, חודשים, עברי/לועזי) משנה רק את התוכן -
    /// גודל החלון ומיקומו נשארים קבועים, והמשטח נשאר צמוד לתחתית ולא חורג מהחלון.
    /// </summary>
    [Fact]
    public void ZmanimPopup_WindowNeverResizesOrMovesWhenClickingButtons()
    {
        UiTestHost.Run(() =>
        {
            var popup = new ZmanimPopup { Left = -30000, Top = -30000, ShowActivated = false };
            try
            {
                popup.Show();
                UiTestHost.DoEvents();
                var root = (FrameworkElement)popup.Content;

                var initial = new Rect(popup.Left, popup.Top, popup.ActualWidth, popup.ActualHeight);
                Assert.Equal(346, initial.Width);
                double surfaceBottom = SurfaceBottom();

                void Check(string step)
                {
                    UiTestHost.DoEvents();
                    Assert.True(new Rect(popup.Left, popup.Top, popup.ActualWidth, popup.ActualHeight) == initial, $"החלון זז או שינה גודל אחרי: {step}");
                    Assert.True(Math.Abs(surfaceBottom - SurfaceBottom()) < 1, $"תחתית המשטח זזה אחרי: {step} ({surfaceBottom} → {SurfaceBottom()}, חלון {popup.ActualHeight})");
                    Assert.True(root.ActualHeight <= popup.ActualHeight + 0.5, $"התוכן חורג מהחלון אחרי: {step}");
                }

                double SurfaceBottom() => popup.Surface.TranslatePoint(new Point(0, popup.Surface.ActualHeight), popup).Y;

                Click(popup.NextDayButton); Check("יום הבא");
                Click(popup.TodayButton); Check("חזרה להיום");
                Click(popup.DatePickerButton); Check("פתיחת לוח השנה");

                double calendarHeight = popup.DatePickerHost.ActualHeight;
                for (int i = 0; i < 14; i++)
                {
                    Click(popup.HebrewDatePicker.NextMonthButton);
                    Check($"חודש הבא {i}");
                    Assert.Equal(calendarHeight, popup.DatePickerHost.ActualHeight, 1);
                }

                popup.HebrewDatePicker.GregorianModeRadio.IsChecked = true; Check("לועזי");
                Assert.Equal(calendarHeight, popup.DatePickerHost.ActualHeight, 1);
                for (int i = 0; i < 14; i++)
                {
                    Click(popup.HebrewDatePicker.PrevMonthButton);
                    Check($"חודש קודם {i}");
                    Assert.Equal(calendarHeight, popup.DatePickerHost.ActualHeight, 1);
                }

                popup.HebrewDatePicker.HebrewModeRadio.IsChecked = true; Check("עברי");
                Click(popup.ThemeToggleButton); Check("ערכת נושא");
                Click(popup.ThemeToggleButton);
                Click(popup.DatePickerButton); Check("סגירת לוח השנה");
            }
            finally
            {
                popup.Close();
            }
        });
    }

    /// <summary>האזור השקוף שמעל המשטח לא תופס לחיצות - הן עוברות לחלון שמתחת.</summary>
    [Fact]
    public void ZmanimPopup_TransparentAreaAboveSurfaceIsClickThrough()
    {
        UiTestHost.Run(() =>
        {
            Rect work = SystemParameters.WorkArea;
            var popup = new ZmanimPopup { Left = work.Left + 40, Top = work.Top, Height = work.Height, ShowActivated = false, Topmost = true };
            try
            {
                popup.Show();
                UiTestHost.DoEvents();

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(popup).Handle;
                Point surfaceCenter = popup.Surface.TranslatePoint(new Point(popup.Surface.ActualWidth / 2, popup.Surface.ActualHeight / 2), popup);
                double surfaceTop = popup.Surface.TranslatePoint(new Point(0, 0), popup).Y;
                Assert.True(surfaceTop > 60, "המשטח צריך להיות צמוד לתחתית ומעליו אזור שקוף");

                Assert.Equal(handle, RootAt(popup.PointToScreen(surfaceCenter)));
                Assert.NotEqual(handle, RootAt(popup.PointToScreen(new Point(popup.ActualWidth / 2, 20))));
            }
            finally
            {
                popup.Close();
            }
        });

        static IntPtr RootAt(Point screen)
        {
            IntPtr hwnd = WindowFromPoint(new POINT { X = (int)screen.X, Y = (int)screen.Y });
            return GetAncestor(hwnd, 2);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private static void Click(ButtonBase button)
    {
        if (button is ToggleButton toggle)
        {
            toggle.IsChecked = !toggle.IsChecked;
        }

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    [Theory]
    [InlineData(2026, 10, 5, "5 באוקטובר 2026")]
    [InlineData(2027, 1, 31, "31 בינואר 2027")]
    public void ZmanimPopup_FormatsGregorianDateInHebrew(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, ZmanimPopup.FormatGregorianLong(new DateTime(y, m, d)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsWindow_RendersWithSharedPalette(bool dark)
    {
        UiTestHost.Run(() =>
        {
            var window = new SettingsWindow(0);
            try
            {
                AppTheme.Apply(window.Resources, dark);
                var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1000, 720));
                root.Arrange(new Rect(0, 0, 1000, 720));
                root.UpdateLayout();
                UiTestHost.DoEvents();

                var host = new Border { Width = 1000, Height = 720, Background = window.Background };
                window.Content = null;
                host.Child = root;
                host.Resources = window.Resources;
                host.Measure(new Size(1000, 720));
                host.Arrange(new Rect(0, 0, 1000, 720));
                BitmapSource bitmap = UiTestHost.Snapshot(host, $"settings-{(dark ? "dark" : "light")}");

                // אזור ריק בתחתית הניווט הצדדי
                AssertClose(Parse(AppTheme.GetPalette(dark)["WindowBackgroundBrush"]), UiTestHost.PixelAt(bitmap, 120, 600));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void MessageBox_RendersWithSharedButtons()
    {
        UiTestHost.Run(() =>
        {
            ConstructorInfo ctor = typeof(AppMessageBoxWindow).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            var window = (AppMessageBoxWindow)ctor.Invoke(new object?[]
            {
                "לשמור את השינויים לפני היציאה?", "תאריכון", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, null, false, null,
            });
            try
            {
                var root = (FrameworkElement)window.Content;
                Layout(root);
                UiTestHost.Snapshot(root, "message-box");

                List<Button> buttons = window.ButtonsPanel.Children.OfType<Button>().ToList();
                Assert.Equal(new[] { "כן", "לא", "ביטול" }, buttons.Select(b => (string)b.Content));
                Assert.Same(window.FindResource("PrimaryActionButtonStyle"), buttons[0].Style);
                Assert.Same(window.FindResource("ActionButtonStyle"), buttons[1].Style);
                Assert.True(buttons[0].IsDefault);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ZmanEditDialog_RendersWithSharedTheme()
    {
        UiTestHost.Run(() =>
        {
            var dialog = new ZmanEditDialog(
                ZmanimCalendar.NameTzeitHakochavim, null, null, 18, null, null,
                default, _ => new DateTime(2026, 10, 5, 18, 32, 0));
            try
            {
                var root = (FrameworkElement)dialog.Content;
                Layout(root);
                UiTestHost.Snapshot(root, "zman-edit-dialog");

                Assert.Equal("שמירה", dialog.SaveButton.Content);
                Assert.Same(dialog.FindResource("PrimaryActionButtonStyle"), dialog.SaveButton.Style);
                Assert.Same(dialog.FindResource("ToggleSwitchStyle"), dialog.DuplicateToggle.Style);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Toast_RendersWithSharedTheme(bool dark)
    {
        UiTestHost.Run(() =>
        {
            var toast = new ToastNotificationWindow("שקיעת החמה", 10, "18:12", isTest: true, durationSecondsOverride: 60, darkBackgroundOverride: dark);
            try
            {
                var root = (FrameworkElement)toast.Content;
                Layout(root);
                BitmapSource bitmap = UiTestHost.Snapshot(root, $"toast-{(dark ? "dark" : "light")}");
                AssertClose(Parse(AppTheme.GetPalette(dark)["FlyoutBackgroundBrush"]), UiTestHost.PixelAt(bitmap, (int)root.ActualWidth / 2, 16));
            }
            finally
            {
                toast.Close();
            }
        });
    }

    private static ContextMenu BuildWidgetMenuLikeMainWindow(bool dark)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        menu.Resources.MergedDictionaries.Add(UiTestHost.LoadTheme());
        menu.SetResourceReference(FrameworkElement.StyleProperty, "AppContextMenuStyle");

        foreach ((string header, string glyph) in new[] { ("זמני היום", ""), ("הגדרות", ""), ("התראות", "") })
        {
            menu.Items.Add(new MenuItem { Header = header, Tag = glyph });
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "אודות", Tag = "" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "יציאה", Tag = "" });

        AppTheme.Apply(menu.Resources, dark);
        Layout(menu);
        return menu;
    }

    private static (Border Host, HebrewCalendarPicker Picker) BuildPickerHost(bool dark)
    {
        var host = new Border { Padding = new Thickness(8), FlowDirection = FlowDirection.RightToLeft };
        host.Resources.MergedDictionaries.Add(UiTestHost.LoadTheme());
        AppTheme.Apply(host.Resources, dark);
        host.SetResourceReference(Border.BackgroundProperty, "FlyoutBackgroundBrush");

        var picker = new HebrewCalendarPicker();
        host.Child = picker;
        return (host, picker);
    }

    private static IEnumerable<Button> DayButtons(HebrewCalendarPicker picker) => picker.DaysGrid.Children.OfType<Button>();

    private static void Layout(FrameworkElement element, double width = double.PositiveInfinity)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        UiTestHost.DoEvents();
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void AssertClose(Color expected, Color actual)
    {
        int diff = Math.Abs(expected.R - actual.R) + Math.Abs(expected.G - actual.G) + Math.Abs(expected.B - actual.B);
        Assert.True(diff <= 6, $"ציפינו ל-{expected}, התקבל {actual}");
    }
}
