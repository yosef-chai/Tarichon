using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// בדיקות לחלון ההגדרות האמיתי: טעינה ושמירה של כל ההגדרות, שינויים שקרו מחוץ לחלון,
/// הפעלה עם Windows ושדה החיפוש. ההגדרות והרישום מבודדים - ראו TestIsolation.
/// </summary>
[Collection("UI")]
public class SettingsWindowTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    // ערכים "ריקים" שהחלון ממלא בערך המקביל שמוצג בו - שקולים בפועל
    private static readonly Dictionary<string, (string? From, string To)> EquivalentFill = new()
    {
        [nameof(AppSettings.FontFamilyName)] = (null, "\"Segoe UI\""),
    };

    public SettingsWindowTests() => TestIsolation.ResetSettings();

    [Fact]
    public void SaveWithoutChanges_KeepsEveryDefaultValue()
    {
        AssertRoundTrip(new AppSettings());
    }

    [Fact]
    public void SaveWithoutChanges_KeepsEveryCustomizedValue()
    {
        AssertRoundTrip(BuildCustomizedSettings());
    }

    [Fact]
    public void Save_KeepsPopupPreferencesChangedWhileWindowWasOpen()
    {
        SettingsService.Save(new AppSettings { ZmanimPopupDarkMode = true, ZmanimPopupCalendarGregorian = false });

        WithWindow(window =>
        {
            // הכפתורים בלוח הזמנים שומרים ישר להגדרות, בזמן שחלון ההגדרות פתוח
            AppSettings live = SettingsService.Current;
            live.ZmanimPopupDarkMode = false;
            live.ZmanimPopupCalendarGregorian = true;
            SettingsService.Save(live);

            Click(window.ApplyButton);

            Assert.False(SettingsService.Current.ZmanimPopupDarkMode);
            Assert.True(SettingsService.Current.ZmanimPopupCalendarGregorian);
        });
    }

    [Fact]
    public void Save_KeepsWidgetDraggedWhileWindowWasOpen()
    {
        SettingsService.Save(new AppSettings());

        WithWindow(window =>
        {
            AppSettings live = SettingsService.Current;
            live.PositionMode = WidgetPositionMode.FreeDrag;
            live.FreeDragLeft = 700;
            live.FreeDragTop = 1000;
            SettingsService.Save(live);

            Click(window.ApplyButton);

            Assert.Equal(WidgetPositionMode.FreeDrag, SettingsService.Current.PositionMode);
            Assert.Equal(700, SettingsService.Current.FreeDragLeft);
            Assert.Equal(1000, SettingsService.Current.FreeDragTop);
        });
    }

    [Fact]
    public void Save_PositionChosenInWindowWinsOverEarlierDrag()
    {
        SettingsService.Save(new AppSettings { PositionMode = WidgetPositionMode.FreeDrag, FreeDragLeft = 700, FreeDragTop = 1000 });

        WithWindow(window =>
        {
            window.PositionChevronRadio.IsChecked = true;
            Click(window.ApplyButton);

            Assert.Equal(WidgetPositionMode.ChevronAttached, SettingsService.Current.PositionMode);
        });
    }

    [Fact]
    public void StartWithWindows_RegistersTheWidgetAndNotTheSettingsTool()
    {
        SettingsService.Save(new AppSettings());
        StartupService.SetEnabled(false);

        WithWindow(window =>
        {
            Assert.False(window.StartWithWindowsCheckBox.IsChecked);
            window.StartWithWindowsCheckBox.IsChecked = true;
            Click(window.ApplyButton);
        });

        Assert.True(StartupService.IsEnabled());
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(TestIsolation.TestRunKeyPath))
        {
            string value = (string)key!.GetValue("HebrewTaskbarWidget")!;
            Assert.Contains("HebrewTaskbarWidget.exe\"", value);
            Assert.DoesNotContain("HebrewTaskbarWidgetSettings", value);
            Assert.EndsWith(StartupService.AutoStartArgument, value);
        }

        WithWindow(window =>
        {
            Assert.True(window.StartWithWindowsCheckBox.IsChecked);
            window.StartWithWindowsCheckBox.IsChecked = false;
            Click(window.ApplyButton);
        });

        Assert.False(StartupService.IsEnabled());
    }

    [Fact]
    public void Save_ClearsUnsavedIndicator_AndChangeSetsIt()
    {
        SettingsService.Save(new AppSettings());

        WithWindow(window =>
        {
            Assert.Equal(Visibility.Collapsed, window.UnsavedChangesIndicator.Visibility);

            // שינוי סדר הפריטים בשולחן העבודה נעשה בכפתורים ולא בפקד קלט - גם הוא שינוי
            List<string> before = SettingsService.Current.OverlayItemOrder.ToList();
            Button down = window.OverlayShowOrderPanel.Children.OfType<FrameworkElement>()
                .SelectMany(UiTestHost.FindLogicalChildren<Button>)
                .First(b => (string)b.Content == "▼" && b.IsEnabled);
            Click(down);
            Assert.Equal(Visibility.Visible, window.UnsavedChangesIndicator.Visibility);

            Click(window.ApplyButton);
            Assert.Equal(Visibility.Collapsed, window.UnsavedChangesIndicator.Visibility);
            Assert.NotEqual(before, SettingsService.Current.OverlayItemOrder);
            Assert.Equal(before.OrderBy(x => x), SettingsService.Current.OverlayItemOrder.OrderBy(x => x));
        });
    }

    [Theory]
    [InlineData("0", 6)]
    [InlineData("-5", 6)]
    [InlineData("NaN", 12)]
    [InlineData("abc", 12)]
    [InlineData("14,5", 14.5)]
    [InlineData("5000", 200)]
    public void Save_FontSizeIsAlwaysValid(string typed, double expected)
    {
        SettingsService.Save(new AppSettings { UseCustomFont = true });

        WithWindow(window =>
        {
            window.FontSizeTextBox.Text = typed;
            Click(window.ApplyButton);
            Assert.Equal(expected, SettingsService.Current.FontSize);
        });
    }

    [Fact]
    public void Save_InvalidCoordinatesKeepPreviousValues()
    {
        SettingsService.Save(new AppSettings { LocationName = "מיקום מותאם אישית", Latitude = 40.71, Longitude = -74.0 });

        WithWindow(window =>
        {
            window.LatitudeTextBox.Text = "צפון";
            window.LongitudeTextBox.Text = "-73,9";
            Click(window.ApplyButton);

            Assert.Equal(40.71, SettingsService.Current.Latitude);
            Assert.Equal(-73.9, SettingsService.Current.Longitude);
        });
    }

    /// <summary>שמירה של הגדרה אחרת לא מזיזה את השעון הידני אחורה לשעה שבה נפתח החלון.</summary>
    [Fact]
    public void Save_DoesNotReanchorManualClockUnlessEdited()
    {
        long baseTicks = new DateTime(2026, 4, 1, 12, 0, 0).Ticks;
        long setAt = DateTime.UtcNow.AddHours(-3).Ticks;
        SettingsService.Save(new AppSettings { UseManualDateTime = true, ManualDateTimeBaseTicks = baseTicks, ManualDateTimeSetAtUtcTicks = setAt });

        WithWindow(window =>
        {
            window.ShowSecondsCheckBox.IsChecked = true;
            Click(window.ApplyButton);
            Assert.Equal(baseTicks, SettingsService.Current.ManualDateTimeBaseTicks);
            Assert.Equal(setAt, SettingsService.Current.ManualDateTimeSetAtUtcTicks);

            window.ManualTimeTextBox.Text = "08:30";
            Click(window.ApplyButton);
            Assert.Equal(new TimeSpan(8, 30, 0), new DateTime(SettingsService.Current.ManualDateTimeBaseTicks).TimeOfDay);
        });
    }

    [Fact]
    public void Load_SeparatorsFollowTheirParentToggle()
    {
        SettingsService.Save(new AppSettings { ShowGregorianClock = false, ShowHolidayPanel = true });

        WithWindow(window =>
        {
            Assert.False(window.ShowGregorianSeparatorCheckBox.IsEnabled);
            Assert.True(window.ShowHolidaySeparatorCheckBox.IsEnabled);
        });
    }

    [Fact]
    public void Search_SkipsCardsHiddenByCurrentChoices()
    {
        var settings = new AppSettings();
        settings.Holidays.Minhag = CommunityMinhag.Ashkenaz;
        SettingsService.Save(settings);

        WithWindow(window =>
        {
            window.SettingsSearchBox.Text = "מימונה";
            UiTestHost.DoEvents();
            Assert.Empty(window.SearchResultsList.Items);
            Assert.Equal(Visibility.Visible, window.SearchNoResultsText.Visibility);
        });
    }

    [Theory]
    [InlineData(CommunityMinhag.Ashkenaz)]
    [InlineData(CommunityMinhag.Sephardi)]
    public void MinhagCards_FollowTheChosenMinhag(CommunityMinhag minhag)
    {
        var settings = new AppSettings();
        settings.Holidays.Minhag = minhag;
        SettingsService.Save(settings);

        WithWindow(window =>
        {
            bool ashkenaz = minhag == CommunityMinhag.Ashkenaz;
            Assert.Equal(ashkenaz ? Visibility.Visible : Visibility.Collapsed, window.YomKippurKatanCard.Visibility);
            Assert.Equal(ashkenaz ? Visibility.Visible : Visibility.Collapsed, window.BehabCard.Visibility);
            Assert.Equal(ashkenaz ? Visibility.Collapsed : Visibility.Visible, window.CommunityCustomsCard.Visibility);
            Assert.Equal(ashkenaz ? "ליל סליחות" : "תחילת הסליחות מב' באלול", window.SelichotCard.Header);
        });
    }

    [Fact]
    public void RestoreDefaults_ResetsEverythingIncludingStartup()
    {
        StartupService.SetEnabled(false);
        SettingsService.Save(BuildCustomizedSettings());

        WithWindow(window =>
        {
            window.ResetControlsToDefaults();
            Assert.Equal(Visibility.Visible, window.UnsavedChangesIndicator.Visibility);
            Click(window.ApplyButton);
        });

        AppSettings defaults = new();
        AppSettings saved = SettingsService.Current;
        Assert.Equal(defaults.FontSize, saved.FontSize);
        Assert.Equal(defaults.LocationName, saved.LocationName);
        Assert.Equal(defaults.NotificationsEnabled, saved.NotificationsEnabled);
        Assert.Equal(defaults.Holidays.Minhag, saved.Holidays.Minhag);
        Assert.True(StartupService.IsEnabled());
        StartupService.SetEnabled(false);
    }

    /// <summary>הטקסט המוקלד מתחיל ליד הסמליל, באותה נקודה כמו טקסט הרמז - לא באמצע השדה.</summary>
    [Fact]
    public void SearchBox_TypedTextStartsNextToTheIcon()
    {
        SettingsService.Save(new AppSettings());

        WithWindow(window =>
        {
            TextBox box = window.SettingsSearchBox;
            box.Focus();
            box.Text = "שבת";
            UiTestHost.DoEvents();

            // ב-RTL הקואורדינטות נמדדות מהקצה הימני; התו הראשון צמוד לריפוד
            double textStart = box.GetRectFromCharacterIndex(0).X;
            double expected = box.BorderThickness.Left + box.Padding.Left;
            Assert.InRange(textStart, expected - 1, expected + 4);
            Assert.InRange(window.SettingsSearchPlaceholder.Margin.Left, expected - 2, expected + 2);

            // כל שדות הטקסט: הריפוד לא מוכפל
            var plain = new TextBox { Text = "אבג", Width = 120 };
            ((Panel)box.Parent).Children.Add(plain);
            UiTestHost.DoEvents();
            Assert.InRange(plain.GetRectFromCharacterIndex(0).X, plain.Padding.Left, plain.Padding.Left + 4);
        });
    }

    [Theory]
    [InlineData("שבתות מיוחדות", 1)]
    [InlineData("השתקה בשבתות", 3)]
    [InlineData("עומר", 1)]
    [InlineData("נודניק", 3)]
    [InlineData("Explorer", 0)]
    [InlineData("איפוס", 5)]
    public void Search_FindsSettingAndNavigatesToItsPage(string query, int expectedTab)
    {
        SettingsService.Save(new AppSettings());

        WithWindow(window =>
        {
            window.SettingsSearchBox.Text = query;
            UiTestHost.DoEvents();

            Assert.True(window.SearchResultsList.Items.Count > 0, $"לא נמצאו תוצאות ל-{query}");
            window.SettingsSearchBox.RaiseEvent(new System.Windows.Input.KeyEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.SettingsSearchBox)!, 0, System.Windows.Input.Key.Enter)
            { RoutedEvent = UIElement.PreviewKeyDownEvent });
            UiTestHost.DoEvents();

            Assert.Equal(expectedTab, window.MainTabControl.SelectedIndex);
        });
    }

    private static void AssertRoundTrip(AppSettings original)
    {
        StartupService.SetEnabled(original.StartWithWindows);
        SettingsService.Save(original);
        JsonObject expected = ToJson(original);

        WithWindow(window => Click(window.ApplyButton));

        AppSettings saved = SettingsService.Current;
        JsonObject actual = ToJson(saved);
        List<string> differences = expected
            .Where(p => p.Key is not nameof(AppSettings.ZmanNotificationRules) and not nameof(AppSettings.NotificationVoiceKitFolderName))
            .Where(p => !JsonNode.DeepEquals(p.Value, actual[p.Key]))
            .Where(p => !(EquivalentFill.TryGetValue(p.Key, out var fill) && p.Value is null && actual[p.Key]?.ToJsonString() == fill.To))
            .Select(p => $"{p.Key}: {p.Value?.ToJsonString()} → {actual[p.Key]?.ToJsonString()}")
            .ToList();

        Assert.True(differences.Count == 0, "שמירה בלי שינוי שינתה ערכים:\n" + string.Join("\n", differences));

        // כללי ההתראה: החלון משלים כלל כבוי לכל זמן שאין לו כלל, אבל כל כלל קיים נשמר כמו שהוא
        foreach (ZmanNotificationRule rule in original.ZmanNotificationRules)
        {
            ZmanNotificationRule match = Assert.Single(saved.ZmanNotificationRules, r => r.ZmanName == rule.ZmanName);
            Assert.Equal(JsonSerializer.Serialize(rule, Json), JsonSerializer.Serialize(match, Json));
        }

        Assert.All(saved.ZmanNotificationRules.Where(r => original.ZmanNotificationRules.All(o => o.ZmanName != r.ZmanName)), r => Assert.False(r.Enabled));

        // חבילת הקול: אם לא נבחרה, נבחרת הראשונה שמותקנת
        if (original.NotificationVoiceKitFolderName is not null)
        {
            Assert.Equal(original.NotificationVoiceKitFolderName, saved.NotificationVoiceKitFolderName);
        }
    }

    private static JsonObject ToJson(AppSettings settings) =>
        JsonNode.Parse(JsonSerializer.Serialize(settings, Json))!.AsObject();

    private static AppSettings BuildCustomizedSettings()
    {
        var s = new AppSettings
        {
            SettingsPanelDarkMode = true,
            ShowWidget = false,
            PositionMode = WidgetPositionMode.CustomEdgeOffset,
            CustomOffsetSide = WidgetAttachSide.Right,
            CustomOffsetPixels = 321,
            LockWidgetPosition = true,
            LockOverlayPosition = true,
            ZmanimPopupAlignment = ZmanimPopupAlignment.LeftEdge,
            UseCustomFont = true,
            FontFamilyName = "Arial",
            FontSize = 14.5,
            UseCustomTextColor = true,
            CustomTextColorHex = "#FFCC00",
            StartWithWindows = false,
            CheckForUpdates = false,
            LastUpdateCheckUtc = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            LastKnownExplorerStartTimeUtc = new DateTime(2026, 10, 4, 7, 30, 0, DateTimeKind.Utc),
            ExplorerAutoLaunchMode = ExplorerAutoLaunchMode.Never,
            ShowGregorianClock = true,
            GregorianClockSide = WidgetAttachSide.Right,
            ShowGregorianSeparator = false,
            GregorianDateFormat = "yyyy-MM-dd",
            ShowHolidayPanel = true,
            HolidayPanelSide = HolidayPanelPosition.FarLeft,
            ShowHolidaySeparator = false,
            UseCustomBackgroundColor = true,
            WidgetBackgroundColorHex = "#334455",
            WidgetBackgroundOpacity = 0.8,
            UseWidgetBorder = true,
            WidgetBorderColorHex = "#00FF00",
            WidgetBorderThickness = 2,
            LocationName = "חיפה",
            Latitude = 32.794,
            Longitude = 34.9896,
            ElevationMeters = 100,
            CandleLightingMinutesBeforeSunset = 30,
            TzeitHakochavimMinutesAfterSunset = 20,
            DefaultZmanCalculationMethod = ZmanCalculationMethod.ItimLeBina,
            CandleLightingByLuach = false,
            RoundZmanimLechumra = false,
            HebrewDayChangeMode = HebrewDayChangeMode.AtSunset,
            Use12HourFormat = true,
            ShowSecondsInTime = true,
            NotificationsEnabled = true,
            DisableNotificationsOnShabbatAndChagim = false,
            NotificationShowPopup = true,
            NotificationToastDurationSeconds = 25,
            SnoozeDurationMinutes = 7,
            NotificationToastDarkBackground = false,
            NotificationToastPositionMode = ToastPositionMode.Custom,
            NotificationToastCustomX = 333,
            NotificationToastCustomY = 444,
            NotificationPlaySound = true,
            NotificationSoundSource = NotificationSoundSourceMode.Fixed,
            ZmanimPopupDarkMode = false,
            ZmanimPopupCalendarGregorian = true,
            OverlayEnabled = true,
            OverlayShowTime = false,
            OverlayShowHoliday = false,
            OverlayPositionMode = OverlayPosition.Custom,
            OverlayCustomX = 55,
            OverlayCustomY = 66,
            OverlayFontFamilyName = "Tahoma",
            OverlayFontSize = 30,
            OverlayTextColorHex = "#ABCDEF",
            OverlayAlwaysOnTop = true,
            OverlayTimeStyle = new OverlayItemStyle { UseCustomStyle = true, FontFamilyName = "Consolas", FontSize = 40, ColorHex = "#FF0000" },
        };

        s.Holidays.Region = HolidayRegionMode.Diaspora;
        s.Holidays.Purim = PurimObservanceMode.Walled;
        s.Holidays.Minhag = CommunityMinhag.Sephardi;
        s.Holidays.ShowFasts = false;
        s.Holidays.ShowOmer = true;
        s.Holidays.ShowShabbatMevarchim = true;
        s.Holidays.ShowIsruChag = true;
        s.Holidays.ShowLeilSelichot = true;
        s.Holidays.ShowCommunityCustoms = false;
        s.Holidays.ShowDayNumbers = false;
        s.Holidays.WidgetPrimaryOnly = false;

        s.VisibleZmanNames = new List<string> { ZmanimCalendar.NameTzeitHakochavim };
        s.ZmanCustomizations.Add(new ZmanCustomization { BaseZmanName = ZmanimCalendar.NameTzeitHakochavim, CustomName = "צאת", MethodOverride = ZmanCalculationMethod.OrHaChaim });
        s.ZmanNotificationRules[0].Enabled = true;
        s.ZmanNotificationRules[0].MinutesBefore = 75;
        s.OverlayItemOrder.Reverse();
        s.AdvancedNotificationRules.Add(new AdvancedNotificationRule
        {
            ZmanName = ZmanimCalendar.NameTzeitHakochavim,
            MinutesBefore = 40,
            ShowPopup = true,
            ToastDurationSeconds = 9,
            ToastDarkBackground = false,
            PlaySound = true,
            SoundSource = NotificationSoundSourceMode.Fixed,
            FixedSoundName = "Asterisk",
        });

        return s;
    }

    private static void WithWindow(Action<SettingsWindow> action)
    {
        UiTestHost.Run(() =>
        {
            var window = new SettingsWindow(0) { Left = -30000, Top = -30000, ShowActivated = false };
            try
            {
                window.Show();
                UiTestHost.DoEvents();
                action(window);
            }
            finally
            {
                // סגירה בלי שאלת "לשמור שינויים?"
                typeof(SettingsWindow).GetMethod("SetUnsavedChanges", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .Invoke(window, new object[] { false });
                window.Close();
            }
        });
    }

    private static void Click(ButtonBase button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        UiTestHost.DoEvents();
    }
}
