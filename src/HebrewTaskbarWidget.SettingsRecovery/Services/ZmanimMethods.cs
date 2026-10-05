using System;
using System.Collections.Generic;
using HebrewTaskbarWidget.Models;

namespace HebrewTaskbarWidget.Services
{
    /// <summary>
    /// מנהג העיר בלוח עתים לבינה, לפי נתוני הלוחות המודפסים שבאתר הלוח
    /// (itimlabina.co.il). רק מה שמשתנה בין ערים ונחוץ לזמנים שהתוכנה מציגה.
    /// </summary>
    public sealed record ItimCityProfile(
        bool SunsetFromElevation,
        int CandleLightingMinutes,
        bool CandleLightingFromElevation,
        double ShabbatEndDegrees = 8.5,
        int? ShabbatEndMinutesAfterSunset = null,
        int ShabbatEndExtraMinutes = 0,
        double MisheyakirDegrees = 11.5);

    /// <summary>
    /// הזמנים המחושבים של שיטה אחת ליום אחד, לפני עיגול ולפני התאמות המשתמש.
    /// </summary>
    internal sealed class MethodTimes
    {
        public Dictionary<string, DateTime?> Times { get; } = new();

        /// <summary>השקיעה שממנה נמדדת הדלקת הנרות בשיטה הזו.</summary>
        public DateTime? CandleLightingSunset { get; init; }

        /// <summary>כמה דקות לפני השקיעה מדליקים לפי הלוח, או null בשיטות שאינן לוח.</summary>
        public int? LuachCandleLightingMinutes { get; init; }
    }

    /// <summary>
    /// ההגדרות ההלכתיות של כל שיטת חישוב. כל ההגדרות של הלוחות נבדקו מול
    /// זמנים שפורסמו בפועל (ראו ZmanimMethodsTests):
    ///
    /// אור החיים - לפי הקוד של "זמני יוסף" (Zemaneh-Yosef, שנבנה לשחזר את
    /// הלוח ונבדק מול צוות הלוח) ולפי אתר הלוח (maor.orhachaim.org). שעה
    /// זמנית אחת היא (שקיעה - הנץ) / 12, מהנץ והשקיעה מהגובה. כל השאר נגזר
    /// ממנה: עלות השחר 72 דקות זמניות לפני הנץ, משיכיר 66, צאת הכוכבים 13.5
    /// אחרי השקיעה, רבנו תם 72. פלג המנחה לפי ילקוט יוסף: 1.25 שעות לפני צאת
    /// הכוכבים. הדלקת נרות 20 דקות (40 בירושלים), צאת שבת 30 דקות.
    ///
    /// עתים לבינה - לפי מאגר ההגדרות של הלוח (itimlabina.co.il) ומבוא הדיוק
    /// של הרב גלויברמן. "X דקות במעלות" הוא עומק השמש שבו היא נמצאת X דקות
    /// לפני הזריחה ביום השוויון בירושלים, ונמדד מתחת לאופק הנראה (כלומר
    /// בתוספת 0.8333° של שבירה וחצי קוטר): 90 = 19.80°, 72 = 16.05°, 18 =
    /// 4.66°. מעלות "רגילות" (6.45°, 8.5°, 11.5°) הן גיאומטריות. השעות
    /// הזמניות וחצות מהנץ והשקיעה המישוריים, גם בירושלים.
    /// </summary>
    public static class ZmanimMethods
    {
        /// <summary>השיטות שאפשר לבחור, לפי סדר הופעתן בתיבות הבחירה.</summary>
        public static readonly IReadOnlyList<ZmanCalculationMethod> All = new[]
        {
            ZmanCalculationMethod.OrHaChaim,
            ZmanCalculationMethod.ItimLeBina,
        };

        /// <summary>
        /// השיטות הישנות (זווית שמש, 72 דקות זמניות) הוסרו. הגדרות שנשמרו
        /// איתן מחושבות לפי לוח אור החיים.
        /// </summary>
        public static ZmanCalculationMethod Normalize(ZmanCalculationMethod method) =>
            IsLuach(method) ? method : ZmanCalculationMethod.OrHaChaim;

        // ערכי "במעלות" של עתים לבינה, כפי שהם שמורים במאגר הלוח (מתחת לאופק הנראה).
        private const double Itim90MinutesStored = 18.9712;
        private const double Itim72MinutesStored = 15.2193;
        private const double Itim18MinutesStored = 3.8217;

        public static string Label(ZmanCalculationMethod method) => Normalize(method) switch
        {
            ZmanCalculationMethod.ItimLeBina => "לוח עתים לבינה",
            _ => "לוח אור החיים",
        };

        /// <summary>סיומת קצרה לשם שורה כפולה, למשל "סוף זמן ק"ש (אור החיים)".</summary>
        public static string ShortLabel(ZmanCalculationMethod method) => Normalize(method) switch
        {
            ZmanCalculationMethod.ItimLeBina => "עתים לבינה",
            _ => "אור החיים",
        };

        public static string Description(ZmanCalculationMethod method) => Normalize(method) switch
        {
            ZmanCalculationMethod.OrHaChaim =>
                "כמו בלוח של ישיבת אור החיים (מרן הרב עובדיה יוסף): הכל בדקות זמניות מהנץ והשקיעה מהגובה. " +
                "עלות השחר 72, טלית ותפילין 66, צאת הכוכבים 13.5, רבנו תם 72 דקות זמניות. הנץ המוצג - במישור. " +
                "הדלקת נרות 20 דקות (בירושלים 40), צאת שבת 30 דקות.",
            ZmanCalculationMethod.ItimLeBina =>
                "כמו בלוח עתים לבינה: עלות השחר ויום המג\"א לפי 90 דקות במעלות (בחו\"ל 72), טלית ותפילין 11.5°, " +
                "שעות זמניות מהנץ והשקיעה במישור, צאת הכוכבים 18 דקות במעלות, צאת שבת 8.5°. " +
                "השקיעה והדלקת הנרות לפי מנהג העיר בלוח.",
            _ => string.Empty,
        };

        /// <summary>האם השיטה משחזרת לוח שלם (ולא רק עלות השחר וצאת הכוכבים).</summary>
        public static bool IsLuach(ZmanCalculationMethod method) =>
            method is ZmanCalculationMethod.OrHaChaim or ZmanCalculationMethod.ItimLeBina;

        // --- מנהגי ערים בעתים לבינה ---
        // המפתח הוא שם המיקום כפי שהוא מופיע ברשימת הערים בהגדרות. עיר שאינה
        // ברשימה (או מיקום ידני) מקבלת את ברירת המחדל של הלוח.

        private static readonly ItimCityProfile ItimIsraelDefault = new(false, 20, false);
        private static readonly ItimCityProfile ItimAbroadDefault = new(false, 18, false);

        private static readonly Dictionary<string, ItimCityProfile> ItimCities = new()
        {
            ["ירושלים"] = new(true, 40, true),
            ["תל אביב - יפו"] = new(false, 22, false),
            ["חיפה"] = new(true, 30, true),
            ["ראשון לציון"] = new(false, 22, false),
            ["פתח תקווה"] = new(false, 40, false),
            ["אשדוד"] = new(true, 22, true),
            ["נתניה"] = new(false, 22, false),
            ["באר שבע"] = new(true, 20, true),
            ["בני ברק"] = new(false, 22, false),
            ["אשקלון"] = new(true, 20, true),
            ["רחובות"] = new(false, 22, false),
            ["הרצליה"] = new(false, 22, false),
            ["חדרה"] = new(false, 30, false, ShabbatEndMinutesAfterSunset: 45),
            ["לוד"] = new(false, 30, false),
            ["רעננה"] = new(false, 22, false),
            ["טבריה"] = new(false, 30, false),
            ["צפת"] = new(true, 30, true),
            ["בית שמש"] = new(true, 40, true),
            ["קריית גת"] = new(false, 22, false),
            ["עכו"] = new(false, 30, false),
            ["עפולה"] = new(false, 30, false),
            ["כרמיאל"] = new(true, 30, true),
            ["קריית שמונה"] = new(false, 30, false),
            ["אריאל"] = new(true, 30, true),
            ["ביתר עילית"] = new(true, 40, true),
            ["אלעד"] = new(true, 30, true),
            ["מודיעין עילית"] = new(true, 30, true, MisheyakirDegrees: 10.5),
            ["מעלה אדומים"] = new(false, 40, false),
            ["דימונה"] = new(true, 22, true),
            ["לונדון, אנגליה"] = new(false, 15, false),
            ["מנצ'סטר, אנגליה"] = new(false, 15, false, ShabbatEndDegrees: 8, MisheyakirDegrees: 11),
            ["פריז, צרפת"] = new(false, 18, false, MisheyakirDegrees: 11),
            ["יוהנסבורג, דרום אפריקה"] = new(false, 18, false, ShabbatEndDegrees: 7.083, ShabbatEndExtraMinutes: 3, MisheyakirDegrees: 10.5),
            ["בואנוס איירס, ארגנטינה"] = new(false, 18, false, ShabbatEndMinutesAfterSunset: 45),
        };

        /// <summary>מנהג העיר בעתים לבינה עבור מיקום נתון (או ברירת המחדל של הלוח).</summary>
        public static ItimCityProfile GetItimProfile(GeoLocation location)
        {
            if (ItimCities.TryGetValue(location.Name, out ItimCityProfile? profile))
            {
                return profile;
            }

            return HolidayOptions.IsIsraelTimeZone(location.TimeZoneId) ? ItimIsraelDefault : ItimAbroadDefault;
        }

        /// <summary>כמה דקות לפני השקיעה מדליקים נרות לפי הלוח, או null אם השיטה אינה לוח.</summary>
        public static int LuachCandleLightingMinutes(ZmanCalculationMethod method, GeoLocation location) => Normalize(method) switch
        {
            ZmanCalculationMethod.ItimLeBina => GetItimProfile(location).CandleLightingMinutes,
            _ => HolidayOptions.IsJerusalem(location.Name) ? 40 : 20,
        };

        internal static MethodTimes Compute(ZmanCalculationMethod method, DateTime date, GeoLocation location, TimeZoneInfo timeZone)
        {
            var sun = new SunCalculator(date, location, timeZone);

            return Normalize(method) switch
            {
                ZmanCalculationMethod.ItimLeBina => ComputeItimLeBina(sun, location),
                _ => ComputeOrHaChaim(sun, location),
            };
        }

        private static MethodTimes ComputeOrHaChaim(SunCalculator sun, GeoLocation location)
        {
            double dip = AppDipDegrees(location.ElevationMeters);
            DateTime? sunrise = sun.Event(AstronomicalCalculator.SunriseZenith + dip, rising: true);
            DateTime? sunset = sun.Event(AstronomicalCalculator.SunriseZenith + dip, rising: false);

            var result = new MethodTimes
            {
                CandleLightingSunset = sunset,
                LuachCandleLightingMinutes = LuachCandleLightingMinutes(ZmanCalculationMethod.OrHaChaim, location),
            };
            Dictionary<string, DateTime?> t = result.Times;

            double? hour = ShaahZmanit(sunrise, sunset);
            if (hour is null)
            {
                FillMissing(t);
                return result;
            }

            DateTime sr = sunrise!.Value;
            DateTime ss = sunset!.Value;
            double h = hour.Value;

            // עלות השחר 72 דקות זמניות לפני הנץ, וצאת הכוכבים של יום המג"א 72
            // אחרי השקיעה. שעה זמנית מג"א = 14.4 / 12 = 1.2 שעות גר"א.
            DateTime alot = sr.AddMinutes(-1.2 * h);
            double mgaHour = 1.2 * h;
            DateTime tzeit = ss.AddMinutes(0.225 * h);

            t[ZmanimCalendar.NameAlotHaShachar] = alot;
            t[ZmanimCalendar.NameMisheyakir] = sr.AddMinutes(-1.1 * h);

            // הלוח מדפיס את הנץ הנראה (לוח ביכורי יוסף), שתלוי בהרים שמסביב.
            // הנץ מהגובה מוקדם ממנו בכמה דקות, והנץ במישור קרוב אליו ואינו
            // מוקדם ממנו - ולכן הוא המוצג. השעות הזמניות נשארות מהנץ מהגובה.
            t[ZmanimCalendar.NameNetz] = sun.Event(AstronomicalCalculator.SunriseZenith, rising: true) ?? sr;
            t[ZmanimCalendar.NameSofZmanKriatShmaMga] = alot.AddMinutes(3 * mgaHour);
            t[ZmanimCalendar.NameSofZmanTefilaMga] = alot.AddMinutes(4 * mgaHour);
            t[ZmanimCalendar.NameSofZmanAchilatChametz] = alot.AddMinutes(4 * mgaHour);
            t[ZmanimCalendar.NameSofZmanBiurChametz] = alot.AddMinutes(5 * mgaHour);
            AddGraDay(t, sr, ss, minchaGedolaAtLeastHalfHour: true);

            // פלג המנחה לפי ילקוט יוסף: שעה ורבע זמנית לפני צאת הכוכבים.
            t[ZmanimCalendar.NamePelagHaMincha] = tzeit.AddMinutes(-1.25 * h);
            t[ZmanimCalendar.NameShkia] = ss;
            t[ZmanimCalendar.NameTzeitHakochavim] = tzeit;
            t[ZmanimCalendar.NameTzeitShabbat] = ss.AddMinutes(30);
            t[ZmanimCalendar.NameRabbeinuTam] = ss.AddMinutes(1.2 * h);

            return result;
        }

        private static MethodTimes ComputeItimLeBina(SunCalculator sun, GeoLocation location)
        {
            ItimCityProfile profile = GetItimProfile(location);
            bool israel = HolidayOptions.IsIsraelTimeZone(location.TimeZoneId);

            DateTime? seaSunrise = sun.Event(AstronomicalCalculator.SunriseZenith, rising: true);
            DateTime? seaSunset = sun.Event(AstronomicalCalculator.SunriseZenith, rising: false);
            DateTime? elevationSunset = sun.Event(
                AstronomicalCalculator.SunriseZenith + AstronomicalCalculator.GeometricHorizonDipDegrees(location.ElevationMeters), rising: false);

            DateTime? shkia = profile.SunsetFromElevation ? elevationSunset : seaSunset;
            var result = new MethodTimes
            {
                CandleLightingSunset = profile.CandleLightingFromElevation ? elevationSunset : seaSunset,
                LuachCandleLightingMinutes = profile.CandleLightingMinutes,
            };
            Dictionary<string, DateTime?> t = result.Times;

            // יום המג"א מעלות השחר עד צאת הכוכבים, באותה זווית בשני הצדדים.
            double mgaDepression = (israel ? Itim90MinutesStored : Itim72MinutesStored) + AstronomicalCalculator.RefractionAndSemidiameterDegrees;
            DateTime? alot = sun.Degrees(mgaDepression, rising: true);
            DateTime? mgaEnd = sun.Degrees(mgaDepression, rising: false);

            t[ZmanimCalendar.NameAlotHaShachar] = alot;
            t[ZmanimCalendar.NameMisheyakir] = sun.Degrees(profile.MisheyakirDegrees, rising: true);
            t[ZmanimCalendar.NameNetz] = seaSunrise;
            AddMgaDay(t, alot, mgaEnd);
            AddGraDay(t, seaSunrise, seaSunset, minchaGedolaAtLeastHalfHour: true);
            t[ZmanimCalendar.NamePelagHaMincha] = Hours(seaSunrise, seaSunset, 10.75);
            t[ZmanimCalendar.NameShkia] = shkia;
            t[ZmanimCalendar.NameTzeitHakochavim] = sun.Degrees(Itim18MinutesStored + AstronomicalCalculator.RefractionAndSemidiameterDegrees, rising: false);
            t[ZmanimCalendar.NameTzeitShabbat] = profile.ShabbatEndMinutesAfterSunset is int minutes
                ? seaSunset?.AddMinutes(minutes)
                : sun.Degrees(profile.ShabbatEndDegrees, rising: false)?.AddMinutes(profile.ShabbatEndExtraMinutes);
            t[ZmanimCalendar.NameRabbeinuTam] = shkia?.AddMinutes(72);

            return result;
        }

        /// <summary>זמני הגר"א (ק"ש, תפילה, חצות, מנחה) מנץ ושקיעה נתונים.</summary>
        private static void AddGraDay(Dictionary<string, DateTime?> t, DateTime? sunrise, DateTime? sunset, bool minchaGedolaAtLeastHalfHour)
        {
            t[ZmanimCalendar.NameSofZmanKriatShmaGra] = Hours(sunrise, sunset, 3);
            t[ZmanimCalendar.NameSofZmanTefilaGra] = Hours(sunrise, sunset, 4);

            DateTime? chatzot = Hours(sunrise, sunset, 6);
            t[ZmanimCalendar.NameChatzot] = chatzot;

            DateTime? minchaGedola = Hours(sunrise, sunset, 6.5);
            if (minchaGedolaAtLeastHalfHour && chatzot is not null && minchaGedola is not null)
            {
                // בחורף חצי שעה זמנית קצרה מחצי שעה רגילה - ממתינים למאוחר מביניהם.
                DateTime halfHourAfterChatzot = chatzot.Value.AddMinutes(30);
                if (halfHourAfterChatzot > minchaGedola.Value)
                {
                    minchaGedola = halfHourAfterChatzot;
                }
            }

            t[ZmanimCalendar.NameMinchaGedola] = minchaGedola;
            t[ZmanimCalendar.NameMinchaKetana] = Hours(sunrise, sunset, 9.5);
        }

        /// <summary>זמני המג"א (ק"ש, תפילה, חמץ) מיום שבין עלות השחר לצאת הכוכבים.</summary>
        private static void AddMgaDay(Dictionary<string, DateTime?> t, DateTime? alot, DateTime? tzeit)
        {
            t[ZmanimCalendar.NameSofZmanKriatShmaMga] = Hours(alot, tzeit, 3);
            t[ZmanimCalendar.NameSofZmanTefilaMga] = Hours(alot, tzeit, 4);
            t[ZmanimCalendar.NameSofZmanAchilatChametz] = Hours(alot, tzeit, 4);
            t[ZmanimCalendar.NameSofZmanBiurChametz] = Hours(alot, tzeit, 5);
        }

        private static void FillMissing(Dictionary<string, DateTime?> t)
        {
            foreach (string name in ZmanimCalendar.AllZmanNames)
            {
                t.TryAdd(name, null);
            }
        }

        private static double? ShaahZmanit(DateTime? start, DateTime? end) =>
            start is null || end is null ? null : (end.Value - start.Value).TotalMinutes / 12.0;

        private static DateTime? Hours(DateTime? start, DateTime? end, double hours)
        {
            double? hour = ShaahZmanit(start, end);
            return hour is null ? null : start!.Value.AddMinutes(hour.Value * hours);
        }

        /// <summary>
        /// תוספת הגובה ששימשה בתוכנה מאז ומעולם (0.0347 * שורש הגובה, במעלות).
        /// היא גדולה מעט מהנוסחה הגיאומטרית, ומשחזרת טוב יותר את הנץ והשקיעה
        /// מהגובה של אור החיים (שמחושבים מהנקודה הגבוהה בעיר).
        /// </summary>
        private static double AppDipDegrees(double elevationMeters) =>
            elevationMeters <= 0 ? 0 : 0.0347 * Math.Sqrt(elevationMeters);

        /// <summary>עטיפה קטנה לחישובי השמש של יום ומיקום אחד.</summary>
        private readonly struct SunCalculator
        {
            private readonly DateTime _date;
            private readonly GeoLocation _location;
            private readonly TimeZoneInfo _timeZone;

            public SunCalculator(DateTime date, GeoLocation location, TimeZoneInfo timeZone)
            {
                _date = date;
                _location = location;
                _timeZone = timeZone;
            }

            public DateTime? Event(double zenith, bool rising) =>
                AstronomicalCalculator.CalculateSunEvent(_date, _location.LatitudeDegrees, _location.LongitudeDegrees, zenith, rising, _timeZone);

            /// <summary>מרכז השמש במעלות נתונות מתחת לאופק הגיאומטרי.</summary>
            public DateTime? Degrees(double depression, bool rising) =>
                Event(AstronomicalCalculator.GeometricZenith + depression, rising);
        }
    }
}
