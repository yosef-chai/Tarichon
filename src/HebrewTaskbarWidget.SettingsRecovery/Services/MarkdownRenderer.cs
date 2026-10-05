using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace HebrewTaskbarWidget.Services
{
    // --- מודל הבלוקים של המסמך (פנימי, נבדק ב-MarkdownRendererTests) ---

    internal abstract record MdBlock;

    internal sealed record MdHeading(int Level, string Text) : MdBlock;

    internal sealed record MdParagraph(string Text) : MdBlock;

    internal sealed record MdCode(string Code) : MdBlock;

    internal sealed record MdQuote(List<MdBlock> Children) : MdBlock;

    internal sealed record MdList(bool Ordered, int Start, List<MdListItem> Items) : MdBlock;

    internal sealed record MdListItem(List<MdBlock> Children, bool? Checked);

    internal sealed record MdRule : MdBlock;

    internal sealed record MdTable(List<string> Header, List<TextAlignment?> Alignments, List<List<string>> Rows) : MdBlock;

    /// <summary>
    /// מציג Markdown בסגנון GitHub (כמו הערות השחרור ב-GitHub Releases) כרכיבי
    /// WPF: כותרות, פסקאות, רשימות (כולל מקוננות, ממוספרות ורשימות משימות),
    /// ציטוטים, בלוקי קוד, טבלאות, קו מפריד, ובתוך הטקסט - מודגש, נטוי, קו
    /// חוצה, קוד וקישורים.
    ///
    /// הכיוון נקבע לכל בלוק בנפרד לפי השפה שלו - כמו dir="auto" ב-GitHub, אבל
    /// לפי רוב האותיות ולא רק לפי הראשונה: פסקה בעברית שמתחילה במילה באנגלית
    /// (למשל שם של תוכנה) עדיין מימין לשמאל. בלוק קוד וקוד בתוך שורה תמיד
    /// משמאל לימין, כדי ששמות קבצים ומספרי גרסה לא יתהפכו בתוך טקסט עברי.
    ///
    /// כל בלוק הוא TextBlock נפרד עם FlowDirection משלו (ולא FlowDocument אחד),
    /// כך שהיישור והתבליטים נכונים בכל כיוון.
    /// </summary>
    public static class MarkdownRenderer
    {
        private const double BaseFontSize = 12.5;
        private const double BaseLineHeight = 19;
        private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");

        // --- ניתוח (Parsing) ---

        private static readonly Regex FenceRegex = new(@"^ {0,3}(`{3,}|~{3,})");
        private static readonly Regex HeadingRegex = new(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$");
        private static readonly Regex RuleRegex = new(@"^ {0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$");
        private static readonly Regex QuoteRegex = new(@"^ {0,3}> ?(.*)$");
        private static readonly Regex ListItemRegex = new(@"^( *)([-*+]|\d{1,9}[.)])( +|$)(.*)$");
        private static readonly Regex TableDelimiterRegex = new(@"^ *\|? *:?-+:? *(\| *:?-+:? *)*\|? *$");
        private static readonly Regex SetextRegex = new(@"^ {0,3}(=+|-+) *$");
        private static readonly Regex TaskRegex = new(@"^\[([ xX])\][ \t]+(.*)$");
        private static readonly Regex HtmlCommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline);
        private static readonly Regex HtmlTagRegex = new(@"^</?([A-Za-z][A-Za-z0-9]*)\b[^>]*>");
        private static readonly Regex AutolinkRegex = new(@"^<((?:https?|mailto):[^\s<>]+)>");
        private static readonly Regex BareUrlRegex = new(@"^(?:https?://|www\.)[^\s<]+");

        internal static List<MdBlock> Parse(string markdown)
        {
            string text = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
            text = HtmlCommentRegex.Replace(text, string.Empty);
            return ParseBlocks(text.Split('\n').ToList());
        }

        private static List<MdBlock> ParseBlocks(List<string> lines)
        {
            var blocks = new List<MdBlock>();
            int i = 0;

            while (i < lines.Count)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                Match match = FenceRegex.Match(line);
                if (match.Success)
                {
                    string fence = match.Groups[1].Value;
                    int indent = LeadingSpaces(line);
                    var code = new List<string>();
                    i++;
                    while (i < lines.Count && !IsClosingFence(lines[i], fence))
                    {
                        code.Add(RemoveIndent(lines[i], indent));
                        i++;
                    }

                    i++; // הגדר הסוגר (או סוף המסמך)
                    blocks.Add(new MdCode(string.Join("\n", code)));
                    continue;
                }

                match = HeadingRegex.Match(line);
                if (match.Success)
                {
                    blocks.Add(new MdHeading(match.Groups[1].Length, match.Groups[2].Value.Trim()));
                    i++;
                    continue;
                }

                if (RuleRegex.IsMatch(line))
                {
                    blocks.Add(new MdRule());
                    i++;
                    continue;
                }

                if (QuoteRegex.IsMatch(line))
                {
                    var inner = new List<string>();
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        Match quote = QuoteRegex.Match(lines[i]);
                        if (quote.Success)
                        {
                            inner.Add(quote.Groups[1].Value);
                        }
                        else if (!StartsBlock(lines[i]))
                        {
                            inner.Add(lines[i]); // המשך "עצל" של הפסקה שבציטוט
                        }
                        else
                        {
                            break;
                        }

                        i++;
                    }

                    blocks.Add(new MdQuote(ParseBlocks(inner)));
                    continue;
                }

                if (ListItemRegex.IsMatch(line))
                {
                    blocks.Add(ParseList(lines, ref i));
                    continue;
                }

                if (i + 1 < lines.Count && line.Contains('|') && lines[i + 1].Contains('-') && TableDelimiterRegex.IsMatch(lines[i + 1]))
                {
                    blocks.Add(ParseTable(lines, ref i));
                    continue;
                }

                // פסקה: עד שורה ריקה או תחילת בלוק אחר. "===" או "---" מתחתיה הופכים אותה לכותרת.
                var paragraph = new List<string> { line.Trim() };
                i++;
                bool becameHeading = false;
                while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    Match setext = SetextRegex.Match(lines[i]);
                    if (setext.Success)
                    {
                        blocks.Add(new MdHeading(setext.Groups[1].Value[0] == '=' ? 1 : 2, string.Join(" ", paragraph)));
                        becameHeading = true;
                        i++;
                        break;
                    }

                    if (StartsBlock(lines[i]))
                    {
                        break;
                    }

                    // רווחים בסוף השורה נשמרים - שניים או יותר הם ירידת שורה מפורשת.
                    paragraph.Add(lines[i].TrimStart());
                    i++;
                }

                if (!becameHeading)
                {
                    blocks.Add(new MdParagraph(string.Join("\n", paragraph)));
                }
            }

            return blocks;
        }

        private static MdList ParseList(List<string> lines, ref int i)
        {
            Match first = ListItemRegex.Match(lines[i]);
            string firstMarker = first.Groups[2].Value;
            bool ordered = char.IsDigit(firstMarker[0]);
            char kind = firstMarker[^1]; // '-', '*', '+', '.' או ')'
            int start = ordered && int.TryParse(firstMarker[..^1], out int number) ? number : 1;
            int baseIndent = first.Groups[1].Length;
            var items = new List<MdListItem>();

            while (i < lines.Count)
            {
                Match match = ListItemRegex.Match(lines[i]);
                if (!match.Success || match.Groups[1].Length > baseIndent + 1 || match.Groups[2].Value[^1] != kind)
                {
                    break;
                }

                int spacesAfterMarker = match.Groups[3].Length;
                int contentIndent = match.Groups[1].Length + match.Groups[2].Length + (spacesAfterMarker is >= 1 and <= 4 ? spacesAfterMarker : 1);
                var itemLines = new List<string> { match.Groups[4].Value };
                i++;

                bool previousBlank = false;
                while (i < lines.Count)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        itemLines.Add(string.Empty);
                        previousBlank = true;
                        i++;
                        continue;
                    }

                    int indent = LeadingSpaces(line);
                    bool nestedList = indent > baseIndent + 1 && ListItemRegex.IsMatch(line);
                    if (indent >= contentIndent || nestedList)
                    {
                        itemLines.Add(RemoveIndent(line, contentIndent));
                    }
                    else if (!previousBlank && !StartsBlock(line))
                    {
                        itemLines.Add(line.TrimStart()); // המשך "עצל" של השורה
                    }
                    else
                    {
                        break;
                    }

                    previousBlank = false;
                    i++;
                }

                while (itemLines.Count > 1 && itemLines[^1].Length == 0)
                {
                    itemLines.RemoveAt(itemLines.Count - 1);
                }

                bool? isChecked = null;
                Match task = TaskRegex.Match(itemLines[0]);
                if (task.Success)
                {
                    isChecked = task.Groups[1].Value != " ";
                    itemLines[0] = task.Groups[2].Value;
                }

                items.Add(new MdListItem(ParseBlocks(itemLines), isChecked));

                // שורה ריקה בין פריטים מותרת - ממשיכים אם אחריה בא פריט נוסף מאותה רשימה.
                int next = i;
                while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next]))
                {
                    next++;
                }

                if (next < lines.Count && ListItemRegex.Match(lines[next]) is { Success: true } sibling &&
                    sibling.Groups[1].Length <= baseIndent + 1 && sibling.Groups[2].Value[^1] == kind)
                {
                    i = next;
                }
            }

            return new MdList(ordered, start, items);
        }

        private static MdTable ParseTable(List<string> lines, ref int i)
        {
            List<string> header = SplitTableRow(lines[i]);
            List<TextAlignment?> alignments = SplitTableRow(lines[i + 1]).Select(cell =>
            {
                bool left = cell.StartsWith(':');
                bool right = cell.EndsWith(':');
                return left && right ? TextAlignment.Center : right ? TextAlignment.Right : left ? (TextAlignment?)TextAlignment.Left : null;
            }).ToList();

            i += 2;
            var rows = new List<List<string>>();
            while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|'))
            {
                rows.Add(SplitTableRow(lines[i]));
                i++;
            }

            return new MdTable(header, alignments, rows);
        }

        private static List<string> SplitTableRow(string line)
        {
            string row = line.Trim();
            if (row.StartsWith('|'))
            {
                row = row[1..];
            }

            if (row.EndsWith('|') && !row.EndsWith("\\|", StringComparison.Ordinal))
            {
                row = row[..^1];
            }

            var cells = new List<string>();
            var cell = new StringBuilder();
            bool inCode = false;
            for (int k = 0; k < row.Length; k++)
            {
                char c = row[k];
                if (c == '\\' && k + 1 < row.Length && row[k + 1] == '|')
                {
                    cell.Append('|');
                    k++;
                }
                else if (c == '`')
                {
                    inCode = !inCode;
                    cell.Append(c);
                }
                else if (c == '|' && !inCode)
                {
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else
                {
                    cell.Append(c);
                }
            }

            cells.Add(cell.ToString().Trim());
            return cells;
        }

        private static bool StartsBlock(string line) =>
            FenceRegex.IsMatch(line) || HeadingRegex.IsMatch(line) || RuleRegex.IsMatch(line) ||
            QuoteRegex.IsMatch(line) || ListItemRegex.IsMatch(line);

        private static bool IsClosingFence(string line, string fence)
        {
            string trimmed = line.Trim();
            return trimmed.Length >= fence.Length && trimmed.All(c => c == fence[0]);
        }

        private static int LeadingSpaces(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }

            return count;
        }

        private static string RemoveIndent(string line, int count) => line[Math.Min(count, LeadingSpaces(line))..];

        // --- כיוון לפי שפה ---

        /// <summary>
        /// כיוון הטקסט לפי רוב האותיות: עברית/ערבית מול שאר האותיות. null אם אין
        /// בו אותיות בכלל (מספרים וסימנים בלבד) - אז הוא יורש את הכיוון שמסביב.
        /// </summary>
        internal static FlowDirection? DetectDirection(string text)
        {
            int rtl = 0;
            int ltr = 0;
            foreach (char c in text)
            {
                if (IsRtlLetter(c))
                {
                    rtl++;
                }
                else if (char.IsLetter(c))
                {
                    ltr++;
                }
            }

            if (rtl == 0 && ltr == 0)
            {
                return null;
            }

            return rtl >= ltr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        private static bool IsRtlLetter(char c) =>
            c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

        /// <summary>הטקסט שנראה על המסך (בלי סימני Markdown, כתובות קישורים וקוד) - לקביעת הכיוון.</summary>
        private static string VisibleText(string markdown)
        {
            string text = Regex.Replace(markdown, @"`+[^`]*`+", " ");
            text = Regex.Replace(text, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            text = Regex.Replace(text, @"(?:https?://|www\.)\S+", " ");
            return text;
        }

        private static string VisibleText(MdBlock block) => block switch
        {
            MdHeading h => VisibleText(h.Text),
            MdParagraph p => VisibleText(p.Text),
            MdQuote q => string.Join(" ", q.Children.Select(VisibleText)),
            MdList l => string.Join(" ", l.Items.SelectMany(item => item.Children).Select(VisibleText)),
            MdTable t => VisibleText(string.Join(" ", t.Header.Concat(t.Rows.SelectMany(r => r)))),
            _ => string.Empty,
        };

        // --- הצגה ---

        /// <summary>
        /// בונה את רכיבי התצוגה. הצבעים מגיעים ממשאבי הערכה (PrimaryForegroundBrush
        /// וכו'), כך שהם מתחלפים יחד עם מצב בהיר/כהה של החלון.
        /// </summary>
        public static FrameworkElement Render(string markdown, FlowDirection defaultDirection = FlowDirection.RightToLeft)
        {
            var root = new StackPanel { FlowDirection = defaultDirection };
            root.SetResourceReference(TextElement.ForegroundProperty, "PrimaryForegroundBrush");
            AddBlocks(root, Parse(markdown), listDepth: 0);
            return root;
        }

        /// <summary>listDepth: בתוך כמה רשימות הבלוקים נמצאים (0 = לא ברשימה).</summary>
        private static void AddBlocks(StackPanel target, List<MdBlock> blocks, int listDepth)
        {
            bool insideList = listDepth > 0;
            for (int k = 0; k < blocks.Count; k++)
            {
                FrameworkElement element = RenderBlock(blocks[k], listDepth);
                bool isLast = k == blocks.Count - 1;
                double top = blocks[k] is MdHeading && k > 0 ? (insideList ? 4 : 14) : 0;
                double bottom = isLast ? 0 : blocks[k] switch
                {
                    MdHeading => 6,
                    _ when insideList => 4,
                    _ => 10,
                };
                element.Margin = new Thickness(0, top, 0, bottom);
                target.Children.Add(element);
            }
        }

        private static FrameworkElement RenderBlock(MdBlock block, int listDepth)
        {
            FrameworkElement element = block switch
            {
                MdHeading heading => RenderHeading(heading),
                MdParagraph paragraph => RenderText(paragraph.Text),
                MdCode code => RenderCode(code),
                MdQuote quote => RenderQuote(quote),
                MdList list => RenderList(list, listDepth),
                MdTable table => RenderTable(table),
                _ => RenderRule(),
            };

            if (block is not MdCode && DetectDirection(VisibleText(block)) is FlowDirection direction)
            {
                element.FlowDirection = direction;
            }

            return element;
        }

        private static TextBlock RenderText(string markdown)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = BaseFontSize,
                LineHeight = BaseLineHeight,
            };
            textBlock.Inlines.AddRange(ParseInlines(markdown));
            return textBlock;
        }

        private static FrameworkElement RenderHeading(MdHeading heading)
        {
            TextBlock text = RenderText(heading.Text);
            text.FontWeight = heading.Level <= 3 ? FontWeights.SemiBold : FontWeights.Bold;
            text.FontSize = heading.Level switch
            {
                1 => 19,
                2 => 16.5,
                3 => 14.5,
                _ => BaseFontSize,
            };
            text.LineHeight = double.NaN;

            if (heading.Level > 2)
            {
                return text;
            }

            // כמו ב-GitHub: קו דק מתחת לכותרות הראשיות.
            var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 4), Child = text };
            border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return border;
        }

        private static FrameworkElement RenderCode(MdCode code)
        {
            var text = new TextBlock
            {
                Text = code.Code,
                FontFamily = MonoFont,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.LeftToRight,
            };
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Child = text,
                FlowDirection = FlowDirection.LeftToRight,
            };
            border.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
            return border;
        }

        private static FrameworkElement RenderQuote(MdQuote quote)
        {
            var panel = new StackPanel();
            AddBlocks(panel, quote.Children, listDepth: 0);

            // הפס בצד שבו מתחיל הטקסט (ימין בעברית) - Thickness מתהפך עם FlowDirection.
            var border = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2), Child = panel };
            border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
            border.SetResourceReference(TextElement.ForegroundProperty, "SecondaryForegroundBrush");
            return border;
        }

        private static FrameworkElement RenderRule()
        {
            var rule = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
            rule.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return rule;
        }

        private static FrameworkElement RenderList(MdList list, int depth)
        {
            var panel = new StackPanel();
            for (int k = 0; k < list.Items.Count; k++)
            {
                MdListItem item = list.Items[k];
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var marker = new TextBlock
                {
                    Text = item.Checked switch
                    {
                        true => "☑",
                        false => "☐",
                        null => list.Ordered ? $"{list.Start + k}." : (depth % 3) switch { 0 => "•", 1 => "◦", _ => "▪" },
                    },
                    FontSize = BaseFontSize,
                    LineHeight = BaseLineHeight,
                    MinWidth = 12,
                    Margin = new Thickness(0, 0, 7, 0),
                };
                if (item.Checked is not null)
                {
                    marker.FontFamily = new FontFamily("Segoe UI Symbol");
                }

                // רשימה מקוננת מקבלת תבליט אחר לפי עומק הקינון.
                var content = new StackPanel();
                AddBlocks(content, item.Children, listDepth: depth + 1);

                Grid.SetColumn(marker, 0);
                Grid.SetColumn(content, 1);
                grid.Children.Add(marker);
                grid.Children.Add(content);
                grid.Margin = new Thickness(depth == 0 ? 2 : 0, 0, 0, k == list.Items.Count - 1 ? 0 : 3);

                if (DetectDirection(string.Join(" ", item.Children.Select(VisibleText))) is FlowDirection direction)
                {
                    grid.FlowDirection = direction;
                }

                panel.Children.Add(grid);
            }

            return panel;
        }

        private static FrameworkElement RenderTable(MdTable table)
        {
            int columns = Math.Max(table.Header.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
            var grid = new Grid();
            for (int c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var allRows = new List<List<string>> { table.Header };
            allRows.AddRange(table.Rows);
            for (int r = 0; r < allRows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < columns; c++)
                {
                    string cellText = c < allRows[r].Count ? allRows[r][c] : string.Empty;
                    TextBlock text = RenderText(cellText);
                    if (r == 0)
                    {
                        text.FontWeight = FontWeights.SemiBold;
                    }

                    if (c < table.Alignments.Count && table.Alignments[c] is TextAlignment alignment)
                    {
                        text.TextAlignment = alignment;
                    }

                    var cell = new Border
                    {
                        BorderThickness = new Thickness(0, 0, c == columns - 1 ? 0 : 1, r == allRows.Count - 1 ? 0 : 1),
                        Padding = new Thickness(8, 4, 8, 4),
                        Child = text,
                    };
                    cell.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                    if (r == 0)
                    {
                        cell.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
                    }

                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }

            var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = grid };
            border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            return border;
        }

        // --- טקסט בתוך שורה ---

        internal static List<Inline> ParseInlines(string text)
        {
            var result = new List<Inline>();
            var plain = new StringBuilder();

            void Flush()
            {
                if (plain.Length > 0)
                {
                    result.Add(new Run(plain.ToString()));
                    plain.Clear();
                }
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (c == '\\' && i + 1 < text.Length)
                {
                    if (text[i + 1] == '\n')
                    {
                        Flush();
                        result.Add(new LineBreak());
                        i += 2;
                        continue;
                    }

                    if (char.IsPunctuation(text[i + 1]) || char.IsSymbol(text[i + 1]))
                    {
                        plain.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                }

                if (c == '\n')
                {
                    // כמו בהערות שחרור ב-GitHub: ירידת שורה במקור היא ירידת שורה בתצוגה.
                    while (plain.Length > 0 && plain[^1] == ' ')
                    {
                        plain.Length--;
                    }

                    Flush();
                    result.Add(new LineBreak());
                    i++;
                    continue;
                }

                if (c == '`')
                {
                    int ticks = CountRun(text, i, '`');
                    int close = FindBacktickClose(text, i + ticks, ticks);
                    if (close >= 0)
                    {
                        Flush();
                        string code = text[(i + ticks)..close].Replace('\n', ' ');
                        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0)
                        {
                            code = code[1..^1];
                        }

                        result.Add(CodeSpan(code));
                        i = close + ticks;
                        continue;
                    }

                    plain.Append('`', ticks);
                    i += ticks;
                    continue;
                }

                if (c == '!' && i + 1 < text.Length && text[i + 1] == '[' && TryParseLink(text, i + 1, out string alt, out string imageUrl, out int imageEnd))
                {
                    Flush();
                    result.Add(MakeLink(new List<Inline> { new Run(string.IsNullOrWhiteSpace(alt) ? imageUrl : alt) }, imageUrl));
                    i = imageEnd;
                    continue;
                }

                if (c == '[' && TryParseLink(text, i, out string label, out string url, out int linkEnd))
                {
                    Flush();
                    result.Add(MakeLink(ParseInlines(label), url));
                    i = linkEnd;
                    continue;
                }

                if (c == '<')
                {
                    string rest = text[i..];
                    Match autolink = AutolinkRegex.Match(rest);
                    if (autolink.Success)
                    {
                        Flush();
                        string target = autolink.Groups[1].Value;
                        result.Add(MakeLink(new List<Inline> { new Run(target) }, target));
                        i += autolink.Length;
                        continue;
                    }

                    Match tag = HtmlTagRegex.Match(rest);
                    if (tag.Success)
                    {
                        // תגיות HTML לא מוצגות; <br> הוא ירידת שורה.
                        if (tag.Groups[1].Value.Equals("br", StringComparison.OrdinalIgnoreCase))
                        {
                            Flush();
                            result.Add(new LineBreak());
                        }

                        i += tag.Length;
                        continue;
                    }
                }

                if ((c == 'h' || c == 'w') && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
                {
                    Match bare = BareUrlRegex.Match(text[i..]);
                    if (bare.Success)
                    {
                        string target = bare.Value.TrimEnd('.', ',', ':', ';', '!', '?', '\'', '"', ')');
                        Flush();
                        result.Add(MakeLink(new List<Inline> { new Run(target) }, target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + target : target));
                        i += target.Length;
                        continue;
                    }
                }

                if (c is '*' or '_' or '~')
                {
                    int run = CountRun(text, i, c);
                    int use = c == '~' ? run : Math.Min(run, 3);
                    bool canOpen = (c != '~' || run <= 2) &&
                                   i + run < text.Length && !char.IsWhiteSpace(text[i + run]) &&
                                   (c != '_' || i == 0 || !char.IsLetterOrDigit(text[i - 1]));
                    if (canOpen && run <= 3)
                    {
                        int close = FindClosingDelimiter(text, i + use, c, use);
                        if (close > i + use)
                        {
                            Flush();
                            Span span = use switch
                            {
                                _ when c == '~' => new Span { TextDecorations = TextDecorations.Strikethrough },
                                3 => new Bold(new Italic()),
                                2 => new Bold(),
                                _ => new Italic(),
                            };

                            Span innermost = span is Bold { Inlines.FirstInline: Italic italic } ? italic : span;
                            innermost.Inlines.AddRange(ParseInlines(text[(i + use)..close]));
                            result.Add(span);
                            i = close + use;
                            continue;
                        }
                    }

                    plain.Append(c, run);
                    i += run;
                    continue;
                }

                plain.Append(c);
                i++;
            }

            Flush();
            return result;
        }

        private static int CountRun(string text, int start, char c)
        {
            int end = start;
            while (end < text.Length && text[end] == c)
            {
                end++;
            }

            return end - start;
        }

        private static int FindBacktickClose(string text, int from, int ticks)
        {
            int j = from;
            while (j < text.Length)
            {
                if (text[j] == '`')
                {
                    int run = CountRun(text, j, '`');
                    if (run == ticks)
                    {
                        return j;
                    }

                    j += run;
                }
                else
                {
                    j++;
                }
            }

            return -1;
        }

        /// <summary>
        /// הסוגר של הדגשה: רצף של אותו תו באורך זהה בדיוק, שלפניו אין רווח (ולא
        /// בתוך קוד). רצפים באורך אחר שייכים להדגשות מקוננות ומדלגים עליהם.
        /// </summary>
        private static int FindClosingDelimiter(string text, int from, char c, int length)
        {
            int j = from;
            while (j < text.Length)
            {
                char current = text[j];
                if (current == '\\')
                {
                    j += 2;
                    continue;
                }

                if (current == '`')
                {
                    int ticks = CountRun(text, j, '`');
                    int close = FindBacktickClose(text, j + ticks, ticks);
                    j = close >= 0 ? close + ticks : j + ticks;
                    continue;
                }

                if (current == c)
                {
                    int run = CountRun(text, j, c);
                    bool afterText = !char.IsWhiteSpace(text[j - 1]);
                    bool wordEnd = c != '_' || j + run >= text.Length || !char.IsLetterOrDigit(text[j + run]);
                    if (run == length && afterText && wordEnd)
                    {
                        return j;
                    }

                    j += run;
                    continue;
                }

                j++;
            }

            return -1;
        }

        private static bool TryParseLink(string text, int open, out string label, out string url, out int end)
        {
            label = string.Empty;
            url = string.Empty;
            end = open;

            int depth = 0;
            int j = open;
            for (; j < text.Length; j++)
            {
                if (text[j] == '\\')
                {
                    j++;
                    continue;
                }

                if (text[j] == '[')
                {
                    depth++;
                }
                else if (text[j] == ']' && --depth == 0)
                {
                    break;
                }
            }

            if (j >= text.Length || j + 1 >= text.Length || text[j + 1] != '(')
            {
                return false;
            }

            int labelEnd = j;
            int parens = 0;
            int k = j + 1;
            for (; k < text.Length; k++)
            {
                if (text[k] == '(')
                {
                    parens++;
                }
                else if (text[k] == ')' && --parens == 0)
                {
                    break;
                }
                else if (text[k] == '\n')
                {
                    return false;
                }
            }

            if (k >= text.Length)
            {
                return false;
            }

            string destination = text[(j + 2)..k].Trim();
            int titleStart = destination.IndexOfAny(new[] { ' ', '\t' });
            if (destination.StartsWith('<') && destination.IndexOf('>') is int closeAngle and > 0)
            {
                destination = destination[1..closeAngle];
            }
            else if (titleStart > 0)
            {
                destination = destination[..titleStart];
            }

            label = text[(open + 1)..labelEnd];
            url = destination;
            end = k + 1;
            return true;
        }

        private static Inline CodeSpan(string code)
        {
            // רווח צר מכל צד כ"ריפוד", וכיוון משמאל לימין כדי ששם קובץ או גרסה לא יתהפכו.
            var span = new Span(new Run(" " + code + " "))
            {
                FontFamily = MonoFont,
                FontSize = BaseFontSize - 0.5,
                FlowDirection = FlowDirection.LeftToRight,
            };
            span.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillBrush");
            return span;
        }

        private static Inline MakeLink(List<Inline> content, string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto))
            {
                // קישור יחסי או לא תקין - מוצג כטקסט בלבד.
                var span = new Span();
                span.Inlines.AddRange(content);
                return span;
            }

            var link = new Hyperlink { NavigateUri = uri, TextDecorations = null, Cursor = Cursors.Hand, ToolTip = uri.ToString() };
            link.Inlines.AddRange(content);
            link.SetResourceReference(TextElement.ForegroundProperty, "AccentForegroundBrush");
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;
            link.RequestNavigate += (_, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch
                {
                    // אין דפדפן ברירת מחדל וכו' - לא קריטי.
                }

                e.Handled = true;
            };
            return link;
        }
    }
}
