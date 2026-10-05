using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

/// <summary>הצגת הערות שחרור (Markdown של GitHub) בחלון "מה חדש".</summary>
[Collection("UI")]
public class MarkdownRendererTests
{
    private const string Sample = """
        # תאריכון 0.10.1

        הוידג'ט **לא נעלם** יותר כשפותחים את _התחל_, ו-`Tarichon-Setup-0.10.1.exe` מוכן ~~להורדה~~.
        Windows 11 נבדק.

        ## מה השתנה
        - **הוידג'ט:** עובר עם שורת המשימות לשכבה של התחל
          - גם במסך מלא
          - גם אחרי ש-Explorer עולה מחדש
        - שיטות החישוב: [לוח אור החיים](https://maor.orhachaim.org) ולוח עתים לבינה
        1. ראשון
        2. שני

        - [x] נבדק מול שורת המשימות
        - [ ] בדיקה ידנית

        > הזמנים אינם פסק הלכה.

        ```
        dotnet test
        ```

        | גרסה | תאריך |
        |------|:-----:|
        | 0.10.1 | 6.10.2026 |

        ---
        **Full Changelog**: https://github.com/yosef-chai/Tarichon/compare/v0.10.0...v0.10.1
        """;

    [Fact]
    public void Parse_GitHubMarkdownBlocks()
    {
        List<MdBlock> blocks = MarkdownRenderer.Parse(Sample);

        Assert.Equal(new[]
        {
            typeof(MdHeading), typeof(MdParagraph), typeof(MdHeading), typeof(MdList), typeof(MdList), typeof(MdList),
            typeof(MdQuote), typeof(MdCode), typeof(MdTable), typeof(MdRule), typeof(MdParagraph),
        }, blocks.Select(b => b.GetType()));

        var bullets = (MdList)blocks[3];
        Assert.False(bullets.Ordered);
        Assert.Equal(2, bullets.Items.Count);
        var nested = Assert.IsType<MdList>(bullets.Items[0].Children[1]);
        Assert.Equal(2, nested.Items.Count);

        var numbers = (MdList)blocks[4];
        Assert.True(numbers.Ordered);
        Assert.Equal(1, numbers.Start);

        var tasks = (MdList)blocks[5];
        Assert.Equal(new bool?[] { true, false }, tasks.Items.Select(item => item.Checked));

        var table = (MdTable)blocks[8];
        Assert.Equal(new[] { "גרסה", "תאריך" }, table.Header);
        Assert.Equal(TextAlignment.Center, table.Alignments[1]);
        Assert.Equal("0.10.1", table.Rows[0][0]);

        Assert.Equal("dotnet test", ((MdCode)blocks[7]).Code);
    }

    [Theory]
    [InlineData("שיטות החישוב של הלוחות", FlowDirection.RightToLeft)]
    [InlineData("Windows 11 נבדק עם שורת המשימות", FlowDirection.RightToLeft)] // מתחיל באנגלית, רובו עברית
    [InlineData("Full Changelog: compare versions", FlowDirection.LeftToRight)]
    [InlineData("Fixed the תאריך widget on Windows", FlowDirection.LeftToRight)]
    public void Direction_FollowsTheLanguageOfTheText(string text, FlowDirection expected)
    {
        Assert.Equal(expected, MarkdownRenderer.DetectDirection(text));
    }

    [Fact]
    public void Direction_WithoutLetters_InheritsFromAround()
    {
        Assert.Null(MarkdownRenderer.DetectDirection("0.10.1 — 6.10.2026"));
    }

    [Fact]
    public void Inlines_BoldItalicStrikeCodeAndLinks()
    {
        UiTestHost.Run(() =>
        {
            List<Inline> inlines = MarkdownRenderer.ParseInlines("רגיל **מודגש** ו-*נטוי* ~~מחוק~~ `code.exe` [קישור](https://example.com) https://github.com/x.");

            Assert.Contains(inlines, i => i is Bold b && Text(b) == "מודגש");
            Assert.Contains(inlines, i => i is Italic it && Text(it) == "נטוי");
            Assert.Contains(inlines, i => i is Span s && s.TextDecorations == TextDecorations.Strikethrough && Text(s) == "מחוק");

            Span code = inlines.OfType<Span>().Single(s => Text(s).Contains("code.exe"));
            Assert.Equal(FlowDirection.LeftToRight, code.FlowDirection);

            List<Hyperlink> links = inlines.OfType<Hyperlink>().ToList();
            Assert.Equal(new[] { "https://example.com/", "https://github.com/x" }, links.Select(l => l.NavigateUri.ToString()));
            Assert.Equal("קישור", Text(links[0]));

            // נקודה בסוף כתובת אינה חלק ממנה, וטקסט עם כוכבית בודדת נשאר כמו שהוא.
            Assert.EndsWith(".", Text(inlines.Last()));
            Assert.Equal("2 * 3 = 6", string.Concat(MarkdownRenderer.ParseInlines("2 * 3 = 6").Select(Text)));
            Assert.Equal("snake_case_name", string.Concat(MarkdownRenderer.ParseInlines("snake_case_name").Select(Text)));
        });
    }

    [Fact]
    public void Render_EachBlockInItsLanguageDirection_AndSnapshot()
    {
        UiTestHost.Run(() =>
        {
            FrameworkElement notes = MarkdownRenderer.Render(Sample);
            var panel = Assert.IsType<StackPanel>(notes);
            Assert.Equal(FlowDirection.RightToLeft, panel.FlowDirection);

            // הקישור באנגלית בסוף - משמאל לימין; הפסקאות בעברית - מימין לשמאל.
            Assert.Equal(FlowDirection.LeftToRight, panel.Children.OfType<TextBlock>().Last().FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, panel.Children.OfType<TextBlock>().First().FlowDirection);

            // בלוק קוד תמיד משמאל לימין.
            Assert.Contains(panel.Children.OfType<Border>(), b => b.Child is TextBlock { Text: "dotnet test" } t && t.FlowDirection == FlowDirection.LeftToRight);

            // מארח משמאל לימין, כדי שהצילום לא ייצא מוחזר במראה.
            ResourceDictionary theme = UiTestHost.LoadTheme();
            AppTheme.Apply(theme, false);
            var host = new Border { Width = 520, Padding = new Thickness(16), FlowDirection = FlowDirection.LeftToRight, Child = notes, Resources = theme };
            host.SetResourceReference(Border.BackgroundProperty, "FlyoutBackgroundBrush");
            host.Measure(new Size(520, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            UiTestHost.Snapshot(host, "whats-new");
            Assert.True(host.ActualHeight > 300);
        });
    }

    private static string Text(Inline inline) => inline switch
    {
        Run run => run.Text,
        Span span => string.Concat(span.Inlines.Select(Text)),
        _ => string.Empty,
    };
}
