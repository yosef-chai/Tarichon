using System;
using System.Collections.Generic;
using System.Linq;
using HebrewTaskbarWidget.Models;

namespace HebrewTaskbarWidget.Services
{
    /// <summary>
    /// זמן הלכתי בודד להצגה ברשימה.
    /// </summary>
    public sealed class ZmanEntry
    {
        /// <summary>
        /// מפתח יציב לזיהוי הזמן הזה (לצורך התאמת כללי התראה/נראות ברחבי
        /// האפליקציה) - השם הקנוני הרגיל (כמו ZmanimCalendar.NameAlotHaShachar)
        /// לזמן "רגיל", או Id ייחודי (ZmanDuplicateRow.Id) לשורת "זמן כפול".
        /// **לא** משתנה גם אם למשתמש יש שם מותאם אישית לזמן הזה - כך ששינוי
        /// שם לא "שובר" כללי התראה/נראות קיימים. לא מוצג למשתמש ישירות.
        /// </summary>
        public required string Key { get; init; }

        /// <summary>השם המוצג בפועל למשתמש (בלוח הזמנים, בהתראות) - השם הקנוני, או שם מותאם אישית אם הוגדר (ראו AppSettings.ZmanCustomizations/ZmanDuplicateRows).</summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// השם הקנוני "האמיתי" לצורך הכרזה קולית - תמיד שם הזמן הבסיסי
        /// (למשל ZmanimCalendar.NameAlotHaShachar), **גם** לשורת "זמן כפול"
        /// (ששני העותקים - הראשי והכפול - חולקים את אותו VoiceKey). הסיבה:
        /// קובצי ההקראה הקוליים מוקלטים מראש לפי השם הקנוני בלבד - אין
        /// הקלטה לשם מותאם אישית שהמשתמש הזין, אז אין דרך להשמיע אותו
        /// בפועל; הפתרון הסביר היחיד הוא להמשיך ולהקריא את שם הזמן המקורי,
        /// גם כששמו המוצג (DisplayName) שונה.
        /// </summary>
        public required string VoiceKey { get; init; }

        public DateTime? Time { get; init; }
    }

    /// <summary>
    /// ההגדרות שמשפיעות על חישוב הזמנים. נבנות מההגדרות השמורות
    /// (<see cref="FromSettings"/>), או מהערכים שבעריכה בחלון ההגדרות.
    /// </summary>
    public sealed record ZmanimOptions
    {
        public ZmanCalculationMethod Method { get; init; } = ZmanCalculationMethod.OrHaChaim;
        public int CandleLightingMinutesBeforeSunset { get; init; } = 40;
        public bool CandleLightingByLuach { get; init; } = true;
        public int? TzeitHakochavimMinutesAfterSunset { get; init; }
        public bool RoundLechumra { get; init; } = true;
        public IReadOnlyList<ZmanCustomization> Customizations { get; init; } = Array.Empty<ZmanCustomization>();
        public IReadOnlyList<ZmanDuplicateRow> DuplicateRows { get; init; } = Array.Empty<ZmanDuplicateRow>();

        public static ZmanimOptions FromSettings(AppSettings settings) => new()
        {
            Method = settings.DefaultZmanCalculationMethod,
            CandleLightingMinutesBeforeSunset = settings.CandleLightingMinutesBeforeSunset,
            CandleLightingByLuach = settings.CandleLightingByLuach,
            TzeitHakochavimMinutesAfterSunset = settings.TzeitHakochavimMinutesAfterSunset,
            RoundLechumra = settings.RoundZmanimLechumra,
            Customizations = settings.ZmanCustomizations ?? new List<ZmanCustomization>(),
            DuplicateRows = settings.ZmanDuplicateRows ?? new List<ZmanDuplicateRow>(),
        };
    }

    /// <summary>
    /// שכבת החישוב ההלכתי: בונה את רשימת זמני היום לפי שיטת החישוב שנבחרה
    /// (ההגדרות של כל שיטה ב-<see cref="ZmanimMethods"/>), עם ההתאמות של
    /// המשתמש: שם ושיטה לכל זמן, שורות כפולות, הדלקת נרות, צאת הכוכבים
    /// בדקות, ועיגול לחומרא. אינה פוסקת הלכה למעשה.
    /// </summary>
    public static class ZmanimCalendar
    {
        // שמות קבועים לכל זמן, כדי שגם פאנל ההגדרות (רשימת הזמנים להתראה)
        // וגם שכבת החישוב עצמה ישתמשו באותן מחרוזות בדיוק (מונע חוסר-התאמה).
        // השמות הם גם המפתחות השמורים בהגדרות - אסור לשנות שם קיים.
        public const string NameAlotHaShachar = "עלות השחר (16.1°)";
        public const string NameMisheyakir = "זמן טלית ותפילין";
        public const string NameNetz = "הנץ החמה";
        public const string NameSofZmanKriatShmaMga = "סוף זמן ק\"ש (מג\"א)";
        public const string NameSofZmanKriatShmaGra = "סוף זמן ק\"ש (גר\"א)";
        public const string NameSofZmanTefilaMga = "סוף זמן תפילה (מג\"א)";
        public const string NameSofZmanTefilaGra = "סוף זמן תפילה (גר\"א)";
        public const string NameSofZmanAchilatChametz = "סוף זמן אכילת חמץ";
        public const string NameSofZmanBiurChametz = "סוף זמן שריפת חמץ";
        public const string NameChatzot = "חצות היום והלילה";
        public const string NameMinchaGedola = "מנחה גדולה";
        public const string NameMinchaKetana = "מנחה קטנה";
        public const string NamePelagHaMincha = "פלג המנחה";
        public const string NameShkia = "שקיעת החמה";
        public const string NameTzeitHakochavim = "צאת הכוכבים";
        public const string NameTzeitShabbat = "צאת השבת והחג";
        public const string NameRabbeinuTam = "רבנו תם (72 דקות)";
        public const string NameCandleLighting = "הדלקת נרות";

        /// <summary>כל שמות הזמנים לפי סדר הופעתם ברשימה.</summary>
        public static readonly IReadOnlyList<string> AllZmanNames = new[]
        {
            NameAlotHaShachar, NameMisheyakir, NameNetz,
            NameSofZmanKriatShmaMga, NameSofZmanKriatShmaGra,
            NameSofZmanTefilaMga, NameSofZmanTefilaGra,
            NameSofZmanAchilatChametz, NameSofZmanBiurChametz,
            NameChatzot, NameMinchaGedola, NameMinchaKetana, NamePelagHaMincha,
            NameCandleLighting, NameShkia, NameTzeitHakochavim, NameTzeitShabbat, NameRabbeinuTam,
        };

        /// <summary>
        /// זמנים שמופיעים מעצמם רק בימים שהם שייכים אליהם, בלי הגדרה ידנית
        /// ובלי שורה ברשימות שבהגדרות: זמני החמץ בערב פסח, ורבנו תם במוצאי
        /// שבת וחג (אם צאת השבת והחג מוצג).
        /// </summary>
        public static bool IsShownAutomatically(string baseName) =>
            baseName is NameSofZmanAchilatChametz or NameSofZmanBiurChametz or NameRabbeinuTam;

        /// <summary>
        /// לפי איזה זמן נקבע אם הזמן הזה מוצג (ראו AppSettings.IsZmanVisible):
        /// בדרך כלל הזמן עצמו, רבנו תם לפי צאת השבת והחג, ו-null לזמני החמץ
        /// (מוצגים תמיד).
        /// </summary>
        public static string? VisibilityKey(string zmanKey) => zmanKey switch
        {
            NameSofZmanAchilatChametz or NameSofZmanBiurChametz => null,
            NameRabbeinuTam => NameTzeitShabbat,
            _ => zmanKey,
        };

        /// <summary>
        /// זמנים שהם "סוף" של משהו (או שמוקדם בהם מחמיר) - מעגלים אותם למטה.
        /// כל השאר הם "התחלה" ומעוגלים למעלה. כך נוהגים אור החיים ועתים לבינה
        /// (במצב "לחומרא").
        /// </summary>
        private static readonly HashSet<string> RoundDownNames = new()
        {
            NameAlotHaShachar,
            NameSofZmanKriatShmaMga, NameSofZmanKriatShmaGra,
            NameSofZmanTefilaMga, NameSofZmanTefilaGra,
            NameSofZmanAchilatChametz, NameSofZmanBiurChametz,
            NameChatzot, NameShkia, NameCandleLighting,
        };

        /// <summary>
        /// שם הזמן להצגה - גרסה "פשוטה" המקבלת רק את השם הקנוני (למשל
        /// משורת כלל התראה, ZmanRuleRow.ZmanName) - משמשת בעיקר בתצוגות
        /// "נסיון"/תצוגה מקדימה בפאנל ההגדרות, שאין להן גישה לישות ZmanEntry
        /// המלאה (ולכן לא יכולות לשקף שם מותאם אישית, אם יש). לתצוגה אמיתית
        /// (לוח הזמנים, התראה בפועל) יש להשתמש בעומס-היתר המקבל ZmanEntry.
        /// </summary>
        public static string GetPopupDisplayName(string canonicalName) => DefaultDisplayName(canonicalName);

        /// <summary>
        /// שם הזמן להצגה בלוח הזמנים (הפופ-אפ)/בהתראות בפועל - ה-DisplayName
        /// של הערך, חוץ מ"עלות השחר" ו"רבנו תם" (כשלא הוגדר להם שם מותאם
        /// אישית): הפירוט הטכני שבשם הקנוני (16.1°, 72 דקות) נכון רק לחלק
        /// מהשיטות, ולכן מושמט בתצוגה היומיומית.
        /// </summary>
        public static string GetPopupDisplayName(ZmanEntry entry)
        {
            if (entry.DisplayName == entry.Key)
            {
                return DefaultDisplayName(entry.Key);
            }

            return entry.DisplayName;
        }

        private static string DefaultDisplayName(string canonicalName) => canonicalName switch
        {
            NameAlotHaShachar => "עלות השחר",
            NameRabbeinuTam => "רבנו תם",
            _ => canonicalName,
        };

        /// <summary>
        /// השם שמוצג בהגדרות ליד תיבת הסימון - השם הקנוני, אלא אם הפירוט
        /// שבו לא מתאים לשיטה שנבחרה (למשל "16.1°", שלא נכון לאף אחד מהלוחות).
        /// </summary>
        public static string GetSettingsDisplayName(string canonicalName, ZmanCalculationMethod method)
        {
            bool canonicalDetailFits = canonicalName switch
            {
                NameAlotHaShachar => false,
                NameRabbeinuTam => ZmanimMethods.Normalize(method) == ZmanCalculationMethod.ItimLeBina,
                _ => true,
            };

            return canonicalDetailFits ? canonicalName : DefaultDisplayName(canonicalName);
        }

        /// <summary>
        /// זמני היום לתאריך. forSettingsList = הרשימות שבהגדרות: כל הזמנים
        /// שאפשר להגדיר (כולל הדלקת נרות וצאת השבת גם ביום חול), בלי הזמנים
        /// שמופיעים מעצמם (ראו <see cref="IsShownAutomatically"/>).
        /// </summary>
        public static IReadOnlyList<ZmanEntry> Calculate(
            DateTime date, GeoLocation location, ZmanimOptions options, bool forSettingsList = false)
        {
            TimeZoneInfo timeZone = ResolveTimeZone(location.TimeZoneId);

            // כל שיטה מחושבת רק אם צריך אותה: השיטה הכללית תמיד, ושיטות אחרות
            // רק אם זמן כלשהו דורס אליהן או שיש שורה כפולה בשיטה כזו.
            var byMethod = new Dictionary<ZmanCalculationMethod, MethodTimes>();
            MethodTimes Times(ZmanCalculationMethod m)
            {
                if (!byMethod.TryGetValue(m, out MethodTimes? times))
                {
                    times = ZmanimMethods.Compute(m, date, location, timeZone);
                    byMethod[m] = times;
                }

                return times;
            }

            MethodTimes general = Times(options.Method);

            Dictionary<string, ZmanCustomization> customizationByBase = new();
            foreach (ZmanCustomization c in options.Customizations)
            {
                customizationByBase[c.BaseZmanName] = c;
            }

            var duplicateByBase = new Dictionary<string, ZmanDuplicateRow>();
            foreach (ZmanDuplicateRow dup in options.DuplicateRows)
            {
                duplicateByBase[dup.BaseZmanName] = dup;
            }

            bool isErevPesach = IsErevPesach(date);
            bool isMotzaeiShabbatOrYomTov = HolidayCalendar.IsShabbatOrYomTov(date) && !HolidayCalendar.IsShabbatOrYomTov(date.AddDays(1));

            var entries = new List<ZmanEntry>();

            foreach (string baseName in AllZmanNames)
            {
                bool include = forSettingsList
                    ? !IsShownAutomatically(baseName)
                    : baseName switch
                    {
                        NameSofZmanAchilatChametz or NameSofZmanBiurChametz => isErevPesach,
                        NameTzeitShabbat or NameRabbeinuTam => isMotzaeiShabbatOrYomTov,
                        _ => true,
                    };

                if (!include)
                {
                    continue;
                }

                if (baseName == NameCandleLighting)
                {
                    AddCandleLighting(entries, date, options, general, customizationByBase, forSettingsList);
                    continue;
                }

                ZmanCalculationMethod effectiveMethod = options.Method;
                if (customizationByBase.TryGetValue(baseName, out ZmanCustomization? customization) &&
                    customization.MethodOverride is ZmanCalculationMethod overrideMethod)
                {
                    effectiveMethod = overrideMethod;
                }

                string displayName = ResolveDisplayName(baseName, customizationByBase);
                DateTime? primaryTime = ResolveZmanTime(baseName, Times(effectiveMethod), options.TzeitHakochavimMinutesAfterSunset, options.RoundLechumra);
                var primaryEntry = new ZmanEntry { Key = baseName, DisplayName = displayName, VoiceKey = baseName, Time = primaryTime };

                // לזמנים שמופיעים מעצמם אין שורה בהגדרות, ולכן גם לא שורה כפולה.
                if (!IsShownAutomatically(baseName) && duplicateByBase.TryGetValue(baseName, out ZmanDuplicateRow? dup))
                {
                    // "צאת הכוכבים" הכפול לא מקבל את הדריסה הידנית (דקות-אחרי-
                    // שקיעה) - זו רלוונטית רק לשורה ה"ראשית", כדי שהכפילה תישאר
                    // תמיד מבוססת-שיטה טהורה (אחרת שתי השורות היו יכולות לצאת
                    // זהות, בניגוד לכל המטרה של הכפלה).
                    DateTime? duplicateTime = ResolveZmanTime(baseName, Times(dup.Method), tzeitMinutesAfterSunset: null, options.RoundLechumra);

                    // VoiceKey = baseName גם כאן (לא dup.Id!) - אין הקלטה קולית
                    // לשם מותאם אישית; שתי השורות (ראשית וכפולה) מקריאות את
                    // אותו שם זמן בסיסי בפועל, ראו תיעוד ZmanEntry.VoiceKey.
                    var duplicateEntry = new ZmanEntry { Key = dup.Id, DisplayName = dup.CustomName, VoiceKey = baseName, Time = duplicateTime };

                    // הראשון ברשימה - הזמן המוקדם יותר (אם שניהם ידועים).
                    bool duplicateFirst = duplicateTime.HasValue && (!primaryTime.HasValue || duplicateTime.Value < primaryTime.Value);
                    entries.Add(duplicateFirst ? duplicateEntry : primaryEntry);
                    entries.Add(duplicateFirst ? primaryEntry : duplicateEntry);
                }
                else
                {
                    entries.Add(primaryEntry);
                }
            }

            return entries;
        }

        /// <summary>
        /// הדלקת נרות - רק בערבי שבת וחג (אלא אם forceInclude, לרשימות שבהגדרות).
        /// לפני השקיעה של השיטה הכללית: בלוח - לפי מנהג העיר שבו (אם המשתמש
        /// לא ביקש אחרת), ובשאר השיטות לפי מספר הדקות שבהגדרות. במוצאי שבת
        /// שלפני חג ובליל יום טוב שני - אחרי צאת השבת, מאש קיימת.
        /// </summary>
        private static void AddCandleLighting(
            List<ZmanEntry> entries, DateTime date, ZmanimOptions options, MethodTimes general,
            Dictionary<string, ZmanCustomization> customizationByBase, bool forceInclude)
        {
            CandleLightingKind kind = forceInclude ? CandleLightingKind.BeforeSunset : HolidayCalendar.GetCandleLighting(date);
            if (kind == CandleLightingKind.None || general.CandleLightingSunset is null)
            {
                return;
            }

            string displayName = ResolveDisplayName(NameCandleLighting, customizationByBase);
            DateTime? time;
            if (kind == CandleLightingKind.AfterTzeit)
            {
                time = Round(general.Times.GetValueOrDefault(NameTzeitShabbat), NameTzeitShabbat, options.RoundLechumra);
                displayName = $"{displayName} (מאש קיימת)";
            }
            else
            {
                time = Round(general.CandleLightingSunset.Value.AddMinutes(-EffectiveCandleLightingMinutes(options, general)), NameCandleLighting, options.RoundLechumra);
            }

            entries.Add(new ZmanEntry { Key = NameCandleLighting, DisplayName = displayName, VoiceKey = NameCandleLighting, Time = time });
        }

        private static int EffectiveCandleLightingMinutes(ZmanimOptions options, MethodTimes general) =>
            options.CandleLightingByLuach && general.LuachCandleLightingMinutes is int luachMinutes
                ? luachMinutes
                : Math.Max(0, options.CandleLightingMinutesBeforeSunset);

        private static bool IsErevPesach(DateTime date)
        {
            HebrewDate hebrew = HebrewCalendarMath.FromGregorian(date);
            return hebrew.Month == HebrewMonth.Nisan && hebrew.Day == 14;
        }

        private static string ResolveDisplayName(string baseName, Dictionary<string, ZmanCustomization> customizationByBase)
        {
            if (customizationByBase.TryGetValue(baseName, out ZmanCustomization? customization) &&
                !string.IsNullOrWhiteSpace(customization.CustomName))
            {
                return customization.CustomName!;
            }

            return baseName;
        }

        private static DateTime? ResolveZmanTime(string baseName, MethodTimes times, int? tzeitMinutesAfterSunset, bool roundLechumra)
        {
            // "צאת הכוכבים": אם המשתמש הגדיר "דקות אחרי השקיעה" מפורש (ראו
            // AppSettings.TzeitHakochavimMinutesAfterSunset) - זה מה שמוצג,
            // במקום החישוב מבוסס-השיטה. משפיע רק על הזמן *המוצג* - לא על
            // זמנים אחרים (כמו סוף זמן ק"ש מג"א), שממשיכים להתבסס על השיטה.
            if (baseName == NameTzeitHakochavim && tzeitMinutesAfterSunset.HasValue)
            {
                DateTime? shkia = times.Times.GetValueOrDefault(NameShkia);
                return Round(shkia?.AddMinutes(Math.Max(0, tzeitMinutesAfterSunset.Value)), baseName, roundLechumra);
            }

            return Round(times.Times.GetValueOrDefault(baseName), baseName, roundLechumra);
        }

        /// <summary>
        /// עיגול לדקה שלמה. לחומרא: סוף זמן למטה, תחילת זמן למעלה (ראו
        /// <see cref="RoundDownNames"/>). אחרת - לדקה הקרובה.
        /// </summary>
        internal static DateTime? Round(DateTime? time, string baseName, bool lechumra)
        {
            if (time is null)
            {
                return null;
            }

            const long ticksPerMinute = TimeSpan.TicksPerMinute;
            long ticks = time.Value.Ticks;
            long floor = ticks - ticks % ticksPerMinute;

            long rounded;
            if (!lechumra)
            {
                rounded = ticks - floor >= ticksPerMinute / 2 ? floor + ticksPerMinute : floor;
            }
            else if (RoundDownNames.Contains(baseName) || floor == ticks)
            {
                rounded = floor;
            }
            else
            {
                rounded = floor + ticksPerMinute;
            }

            return new DateTime(rounded, time.Value.Kind);
        }

        internal static TimeZoneInfo ResolveTimeZone(string timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // fallback לשם ה-IANA, למקרה שהריצה על .NET עם מסד נתוני אזורי-זמן שונה
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");
                }
                catch
                {
                    return TimeZoneInfo.Local;
                }
            }
        }
    }
}
