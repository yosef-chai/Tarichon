using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HebrewTaskbarWidget.Services
{
    /// <summary>
    /// פלטת הצבעים האחידה של כל חלונות התוכנה (הגדרות, לוח הזמנים, תפריט ההקשר,
    /// הודעות ודיאלוגים). המפתחות זהים למפתחות שב-Themes/SettingsTheme.xaml,
    /// ולכן כל חלון שממזג את המילון הזה ומחיל עליו <see cref="Apply"/> נראה אותו דבר.
    /// </summary>
    public static class AppTheme
    {
        public const string DefaultDarkBackgroundHex = "#1B1C1F";

        /// <summary>מפתחות שכל פלטה חייבת להגדיר. משמש גם את הבדיקות.</summary>
        public static readonly IReadOnlyList<string> RequiredKeys = new[]
        {
            "WindowBackgroundBrush", "PrimaryForegroundBrush", "SecondaryForegroundBrush",
            "AccentForegroundBrush", "AccentFillBrush", "OnAccentForegroundBrush",
            "ControlBackgroundBrush", "ControlBorderBrush",
            "TabItemBackgroundBrush", "TabItemForegroundBrush", "TabItemSelectedBackgroundBrush", "TabItemSelectedForegroundBrush",
            "CheckBoxBoxBrush", "CheckBoxBorderBrush", "CheckMarkBrush",
            "CardBackgroundBrush", "CardBorderBrush", "CardHoverBrush",
            "SubtleFillBrush", "NavSelectedBrush", "FooterBackgroundBrush", "InfoBarBrush",
            "ScrollThumbBrush", "ScrollThumbHoverBrush", "ScrollTrackBrush",
            "FlyoutBackgroundBrush", "FlyoutBorderBrush", "DangerForegroundBrush",
        };

        /// <summary>מחזירה את הפלטה המלאה (מפתח → צבע hex). במצב כהה הרקע נגזר מ-darkBackgroundHex.</summary>
        public static IReadOnlyDictionary<string, string> GetPalette(bool dark, string? darkBackgroundHex = null)
        {
            if (!dark)
            {
                return new Dictionary<string, string>
                {
                    ["WindowBackgroundBrush"] = "#F3F3F3",
                    ["PrimaryForegroundBrush"] = "#1B1C1F",
                    ["SecondaryForegroundBrush"] = "#5B5D63",
                    ["AccentForegroundBrush"] = "#1A5FB4",
                    ["AccentFillBrush"] = "#1A5FB4",
                    ["OnAccentForegroundBrush"] = "#FFFFFF",
                    ["ControlBackgroundBrush"] = "#FFFFFF",
                    ["ControlBorderBrush"] = "#C6C6C9",
                    ["TabItemBackgroundBrush"] = "#E3E3E6",
                    ["TabItemForegroundBrush"] = "#3A3B40",
                    ["TabItemSelectedBackgroundBrush"] = "#FFFFFF",
                    ["TabItemSelectedForegroundBrush"] = "#1B1C1F",
                    ["CheckBoxBoxBrush"] = "#FFFFFF",
                    ["CheckBoxBorderBrush"] = "#8A8B90",
                    ["CheckMarkBrush"] = "#1A5FB4",
                    ["CardBackgroundBrush"] = "#FBFBFC",
                    ["CardBorderBrush"] = "#E3E3E6",
                    ["CardHoverBrush"] = "#F6F6F8",
                    ["SubtleFillBrush"] = "#E9E9EC",
                    ["NavSelectedBrush"] = "#E2E2E7",
                    ["FooterBackgroundBrush"] = "#EBEBEE",
                    ["InfoBarBrush"] = "#E7F0FB",
                    ["ScrollThumbBrush"] = "#8A8B90",
                    ["ScrollThumbHoverBrush"] = "#5B5D63",
                    ["ScrollTrackBrush"] = "#00000000",
                    ["FlyoutBackgroundBrush"] = "#F9F9FA",
                    ["FlyoutBorderBrush"] = "#D5D5D9",
                    ["DangerForegroundBrush"] = "#B3261E",
                };
            }

            string bg = IsValidColor(darkBackgroundHex) ? darkBackgroundHex! : DefaultDarkBackgroundHex;

            return new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = bg,
                ["PrimaryForegroundBrush"] = "#F0F0F0",
                ["SecondaryForegroundBrush"] = "#B0B0B0",
                ["AccentForegroundBrush"] = "#9ECBFF",
                ["AccentFillBrush"] = "#2F6FC2",
                ["OnAccentForegroundBrush"] = "#FFFFFF",
                ["ControlBackgroundBrush"] = Lighten(bg, 0.08),
                ["ControlBorderBrush"] = "#4A4B50",
                ["TabItemBackgroundBrush"] = Lighten(bg, 0.12),
                ["TabItemForegroundBrush"] = "#E6E6E6",
                // תווית נבחרת: רקע בהיר וטקסט כהה, כדי שהניגודיות לא תלויה בצבע הרקע שנבחר.
                ["TabItemSelectedBackgroundBrush"] = "#F0F0F0",
                ["TabItemSelectedForegroundBrush"] = "#1B1C1F",
                ["CheckBoxBoxBrush"] = Lighten(bg, 0.08),
                ["CheckBoxBorderBrush"] = "#8A8B90",
                ["CheckMarkBrush"] = "#9ECBFF",
                ["CardBackgroundBrush"] = Lighten(bg, 0.05),
                ["CardBorderBrush"] = Lighten(bg, 0.13),
                ["CardHoverBrush"] = Lighten(bg, 0.08),
                ["SubtleFillBrush"] = Lighten(bg, 0.10),
                ["NavSelectedBrush"] = Lighten(bg, 0.14),
                ["FooterBackgroundBrush"] = Lighten(bg, 0.03),
                ["InfoBarBrush"] = "#1E3350",
                ["ScrollThumbBrush"] = "#6A6B70",
                ["ScrollThumbHoverBrush"] = "#9A9BA0",
                ["ScrollTrackBrush"] = "#00000000",
                // משטח צף (תפריט, לוח זמנים) בהיר מעט מהרקע, כמו שכבת flyout ב-Windows 11.
                ["FlyoutBackgroundBrush"] = Lighten(bg, 0.06),
                ["FlyoutBorderBrush"] = Lighten(bg, 0.18),
                ["DangerForegroundBrush"] = "#FF9C94",
            };
        }

        /// <summary>מחליפה את כל מברשות הפלטה במילון הנתון. רכיבים שמשתמשים ב-DynamicResource מתעדכנים מיד.</summary>
        public static void Apply(ResourceDictionary target, bool dark, string? darkBackgroundHex = null)
        {
            foreach (KeyValuePair<string, string> entry in GetPalette(dark, darkBackgroundHex))
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.Value));
                brush.Freeze();
                target[entry.Key] = brush;
            }
        }

        /// <summary>מעתיקה את מברשות הפלטה מחלון אחר (למשל דיאלוג שיורש את מצב התצוגה של חלון האב).</summary>
        public static void CopyPalette(ResourceDictionary? source, ResourceDictionary target)
        {
            if (source is null)
            {
                return;
            }

            foreach (string key in RequiredKeys)
            {
                if (source.Contains(key) && source[key] is Brush brush)
                {
                    target[key] = brush;
                }
            }
        }

        /// <summary>מבהיר צבע לכיוון לבן ביחס amount (0-1). צבע לא תקין מוחזר כמו שהוא.</summary>
        public static string Lighten(string hex, double amount)
        {
            if (!IsValidColor(hex))
            {
                return hex;
            }

            Color c = (Color)ColorConverter.ConvertFromString(hex);
            byte Adjust(byte channel) => (byte)Math.Clamp(Math.Round(channel + (255 - channel) * amount), 0, 255);
            return Color.FromRgb(Adjust(c.R), Adjust(c.G), Adjust(c.B)).ToString();
        }

        /// <summary>יחס ניגודיות לפי WCAG 2.x בין שני צבעים אטומים (1 עד 21).</summary>
        public static double ContrastRatio(string foregroundHex, string backgroundHex)
        {
            double l1 = RelativeLuminance((Color)ColorConverter.ConvertFromString(foregroundHex));
            double l2 = RelativeLuminance((Color)ColorConverter.ConvertFromString(backgroundHex));
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte value)
            {
                double s = value / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static bool IsValidColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            try
            {
                ColorConverter.ConvertFromString(hex);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
