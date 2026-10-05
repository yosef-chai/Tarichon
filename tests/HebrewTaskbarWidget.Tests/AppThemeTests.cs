using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

public class AppThemeTests
{
    // רקעים כהים שמשתמש עשוי לבחור, כולל קצוות (שחור מלא, כחול-כהה, סגול-כהה)
    public static TheoryData<bool, string?> Palettes => new()
    {
        { false, null },
        { true, null },
        { true, "#000000" },
        { true, "#202A44" },
        { true, "#301934" },
        { true, "#2B2B2B" },
    };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Palette_DefinesEveryRequiredKey_WithValidColors(bool dark, string? background)
    {
        IReadOnlyDictionary<string, string> palette = AppTheme.GetPalette(dark, background);

        foreach (string key in AppTheme.RequiredKeys)
        {
            Assert.True(palette.ContainsKey(key), $"חסר מפתח {key}");
            Assert.Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", palette[key]);
        }
    }

    // WCAG 2.x AA: טקסט רגיל דורש 4.5:1 לפחות
    [Theory]
    [MemberData(nameof(Palettes))]
    public void TextOnEverySurface_MeetsWcagAA(bool dark, string? background)
    {
        IReadOnlyDictionary<string, string> p = AppTheme.GetPalette(dark, background);

        string[] surfaces = { "WindowBackgroundBrush", "CardBackgroundBrush", "FlyoutBackgroundBrush", "ControlBackgroundBrush", "SubtleFillBrush" };
        string[] texts = { "PrimaryForegroundBrush", "SecondaryForegroundBrush", "AccentForegroundBrush" };

        foreach (string surface in surfaces)
        {
            foreach (string text in texts)
            {
                double ratio = AppTheme.ContrastRatio(p[text], p[surface]);
                Assert.True(ratio >= 4.5, $"{text} על {surface} ({p[text]} / {p[surface]}): {ratio:0.00}");
            }
        }

        AssertContrast(p, "AccentForegroundBrush", "InfoBarBrush");
        AssertContrast(p, "PrimaryForegroundBrush", "InfoBarBrush");
        AssertContrast(p, "OnAccentForegroundBrush", "AccentFillBrush");
        AssertContrast(p, "DangerForegroundBrush", "FlyoutBackgroundBrush");
        AssertContrast(p, "TabItemSelectedForegroundBrush", "TabItemSelectedBackgroundBrush");
        AssertContrast(p, "TabItemForegroundBrush", "TabItemBackgroundBrush");
    }

    // זה הבאג שדווח: טקסט שחור על רקע אפור בתפריט ההקשר במצב כהה
    [Fact]
    public void DarkMenu_UsesLightTextOnDarkSurface()
    {
        IReadOnlyDictionary<string, string> p = AppTheme.GetPalette(dark: true);

        double flyoutLuminanceRatioToBlack = AppTheme.ContrastRatio(p["FlyoutBackgroundBrush"], "#000000");
        double textLuminanceRatioToBlack = AppTheme.ContrastRatio(p["PrimaryForegroundBrush"], "#000000");

        Assert.True(flyoutLuminanceRatioToBlack < 2, "רקע התפריט במצב כהה צריך להיות כהה");
        Assert.True(textLuminanceRatioToBlack > 15, "טקסט התפריט במצב כהה צריך להיות בהיר");
    }

    [Fact]
    public void InvalidDarkBackground_FallsBackToDefault()
    {
        Assert.Equal(AppTheme.DefaultDarkBackgroundHex, AppTheme.GetPalette(true, "not-a-color")["WindowBackgroundBrush"]);
        Assert.Equal(AppTheme.DefaultDarkBackgroundHex, AppTheme.GetPalette(true, "")["WindowBackgroundBrush"]);
    }

    [Theory]
    [InlineData("#000000", 0.0, "#FF000000")]
    [InlineData("#000000", 1.0, "#FFFFFFFF")]
    [InlineData("#808080", 0.5, "#FFC0C0C0")]
    [InlineData("garbage", 0.5, "garbage")]
    public void Lighten_MovesTowardWhite(string input, double amount, string expected)
    {
        Assert.Equal(expected, AppTheme.Lighten(input, amount));
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#777777", "#FFFFFF", 4.48)]
    public void ContrastRatio_MatchesWcagReferenceValues(string a, string b, double expected)
    {
        Assert.Equal(expected, AppTheme.ContrastRatio(a, b), 2);
    }

    // ברירות המחדל ב-XAML חייבות להיות זהות לפלטה הבהירה, אחרת יהיה הבהוב צבע לפני ש-Apply רץ
    [Fact]
    public void SettingsThemeXamlDefaults_MatchLightPalette()
    {
        XDocument doc = XDocument.Load(RepoPaths.SourceFile("Themes", "SettingsTheme.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Dictionary<string, string> xamlBrushes = doc.Root!
            .Elements()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);

        IReadOnlyDictionary<string, string> light = AppTheme.GetPalette(dark: false);

        foreach (string key in AppTheme.RequiredKeys)
        {
            Assert.True(xamlBrushes.ContainsKey(key), $"SettingsTheme.xaml לא מגדיר את {key}");
            Assert.Equal(light[key].ToUpperInvariant(), xamlBrushes[key].ToUpperInvariant());
        }
    }

    // כל חלון בתוכנה חייב למזג את ערכת העיצוב המשותפת ולא להגדיר צבעים קשיחים משלו
    [Theory]
    [InlineData("SettingsWindow.xaml")]
    [InlineData("ZmanimPopup.xaml")]
    [InlineData("AppMessageBoxWindow.xaml")]
    [InlineData("ZmanEditDialog.xaml")]
    [InlineData("ToastNotificationWindow.xaml")]
    [InlineData("MainWindow.xaml")]
    public void EveryWindow_MergesSharedTheme(string file)
    {
        string xaml = File.ReadAllText(RepoPaths.SourceFile(file));
        Assert.Contains("Themes/SettingsTheme.xaml", xaml);
    }

    [Theory]
    [InlineData("ZmanimPopup.xaml")]
    [InlineData("AppMessageBoxWindow.xaml")]
    [InlineData("ZmanEditDialog.xaml")]
    [InlineData("ToastNotificationWindow.xaml")]
    [InlineData("Controls/HebrewCalendarPicker.xaml")]
    public void DialogsAndPopups_HaveNoHardCodedColors(string file)
    {
        string xaml = File.ReadAllText(RepoPaths.SourceFile(file.Split('/')));
        MatchCollection colors = Regex.Matches(xaml, "(Color|Background|Foreground|BorderBrush|Fill)=\"#[0-9A-Fa-f]{6,8}\"");
        Assert.True(colors.Count == 0, $"צבעים קשיחים ב-{file}: {string.Join(", ", colors.Select(m => m.Value))}");
    }

    private static void AssertContrast(IReadOnlyDictionary<string, string> p, string fg, string bg)
    {
        double ratio = AppTheme.ContrastRatio(p[fg], p[bg]);
        Assert.True(ratio >= 4.5, $"{fg} על {bg} ({p[fg]} / {p[bg]}): {ratio:0.00}");
    }
}
