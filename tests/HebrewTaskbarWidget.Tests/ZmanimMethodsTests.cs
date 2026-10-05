using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// שיטות החישוב מול זמנים שפורסמו בפועל. ההפרש המותר הוא דקה: הלוחות
/// מחשבים שבירה מקומית לכל עיר וחודש, וגובה לפי מפה טופוגרפית.
/// </summary>
public class ZmanimMethodsTests
{
    private static readonly GeoLocation Jerusalem = new()
    {
        Name = "ירושלים", LatitudeDegrees = 31.7683, LongitudeDegrees = 35.2137, ElevationMeters = 754, TimeZoneId = "Israel Standard Time",
    };

    // עתים לבינה מחשב את ירושלים לכיכר השבת, בגובה 800 מ'.
    private static readonly GeoLocation JerusalemKikarHaShabbat = new()
    {
        Name = "ירושלים", LatitudeDegrees = 31.7889, LongitudeDegrees = 35.2153, ElevationMeters = 800, TimeZoneId = "Israel Standard Time",
    };

    private static readonly GeoLocation TelAviv = new()
    {
        Name = "תל אביב - יפו", LatitudeDegrees = 32.0853, LongitudeDegrees = 34.7818, ElevationMeters = 60, TimeZoneId = "Israel Standard Time",
    };

    private static Dictionary<string, DateTime?> Raw(ZmanCalculationMethod method, DateTime date, GeoLocation location) =>
        ZmanimCalendar.Calculate(date, location, new ZmanimOptions { Method = method, RoundLechumra = false }, forceIncludeConditional: true)
            .ToDictionary(e => e.Key, e => e.Time);

    private static void AssertNear(string expected, DateTime? actual, string zman, double toleranceMinutes = 1.0)
    {
        Assert.True(actual.HasValue, $"{zman}: no time");
        TimeSpan expectedTime = TimeSpan.Parse(expected);
        double diff = Math.Abs((actual!.Value.TimeOfDay - expectedTime).TotalMinutes);
        Assert.True(diff <= toleranceMinutes, $"{zman}: expected {expected}, got {actual.Value:HH:mm:ss} ({diff:0.00} min)");
    }

    [Fact]
    public void ItimLeBina_Jerusalem_MatchesPublishedLuach()
    {
        // itimlabina.co.il, ירושלים, כ"ה תשרי תשפ"ז. הלוח קוטם לדקה ומסמן
        // חצי ראשון/שני שלה; כאן אמצע חצי הדקה שפורסם.
        var day = Raw(ZmanCalculationMethod.ItimLeBina, new DateTime(2026, 10, 6), JerusalemKikarHaShabbat);
        AssertNear("08:47:15", day[ZmanimCalendar.NameSofZmanKriatShmaMga], "ק\"ש מג\"א 90 במעלות", 0.5);
        AssertNear("09:31:45", day[ZmanimCalendar.NameSofZmanKriatShmaGra], "ק\"ש גר\"א", 0.5);
        AssertNear("10:30:15", day[ZmanimCalendar.NameSofZmanTefilaGra], "תפילה גר\"א", 0.5);
        AssertNear("12:27:15", day[ZmanimCalendar.NameChatzot], "חצות", 0.5);
        AssertNear("12:57:15", day[ZmanimCalendar.NameMinchaGedola], "מנחה גדולה", 0.5);
        AssertNear("18:22:15", day[ZmanimCalendar.NameShkia], "שקיעה מהגובה", 0.25);

        var friday = Raw(ZmanCalculationMethod.ItimLeBina, new DateTime(2026, 10, 9), JerusalemKikarHaShabbat);
        AssertNear("17:38:45", friday[ZmanimCalendar.NameCandleLighting], "הדלקת נרות (40 מהגובה)", 0.5);

        var shabbat = Raw(ZmanCalculationMethod.ItimLeBina, new DateTime(2026, 10, 10), JerusalemKikarHaShabbat);
        AssertNear("18:49:15", shabbat[ZmanimCalendar.NameTzeitShabbat], "צאת השבת 8.5°", 0.5);
    }

    [Fact]
    public void ItimLeBina_CityCustoms()
    {
        var bneiBrak = new GeoLocation { Name = "בני ברק", LatitudeDegrees = 32.0807, LongitudeDegrees = 34.8338, ElevationMeters = 30, TimeZoneId = "Israel Standard Time" };
        var friday = Raw(ZmanCalculationMethod.ItimLeBina, new DateTime(2026, 10, 9), bneiBrak);

        // בבני ברק השקיעה במישור והדלקת הנרות 22 דקות לפניה.
        Assert.Equal(22, (friday[ZmanimCalendar.NameShkia]!.Value - friday[ZmanimCalendar.NameCandleLighting]!.Value).TotalMinutes, 0);
        Assert.Equal(22, ZmanimMethods.LuachCandleLightingMinutes(ZmanCalculationMethod.ItimLeBina, bneiBrak));
        Assert.Equal(40, ZmanimMethods.LuachCandleLightingMinutes(ZmanCalculationMethod.ItimLeBina, Jerusalem));
        Assert.False(ZmanimMethods.GetItimProfile(bneiBrak).SunsetFromElevation);
        Assert.True(ZmanimMethods.GetItimProfile(Jerusalem).SunsetFromElevation);
    }

    [Theory]
    // maor.orhachaim.org - עלות השחר, צאת הכוכבים, ק"ש מג"א, ק"ש גר"א, תפילה גר"א.
    [InlineData(2026, 6, 21, "04:01", "20:09", "08:21", "09:04", "10:16")]
    [InlineData(2026, 12, 21, "05:27", "16:55", "08:32", "09:03", "09:54")]
    [InlineData(2026, 10, 5, "05:18", "18:38", "08:53", "09:28", "10:28")]
    [InlineData(2026, 3, 20, "04:24", "18:10", "08:05", "08:42", "09:43")]
    public void OrHaChaim_Jerusalem_MatchesPublishedLuach(int y, int m, int d, string alot, string tzeit, string shmaMga, string shmaGra, string tefilaGra)
    {
        var options = new ZmanimOptions { Method = ZmanCalculationMethod.OrHaChaim };
        var day = ZmanimCalendar.Calculate(new DateTime(y, m, d), Jerusalem, options, forceIncludeConditional: true)
            .ToDictionary(e => e.Key, e => e.Time);

        // הלוח מעגל לחומרא, כמו התוכנה; הנץ והשקיעה שלו מהנקודה הגבוהה בעיר,
        // ולכן ייתכן הפרש של דקה.
        AssertNear(alot, day[ZmanimCalendar.NameAlotHaShachar], "עלות השחר");
        AssertNear(tzeit, day[ZmanimCalendar.NameTzeitHakochavim], "צאת הכוכבים");
        AssertNear(shmaMga, day[ZmanimCalendar.NameSofZmanKriatShmaMga], "ק\"ש מג\"א");
        AssertNear(shmaGra, day[ZmanimCalendar.NameSofZmanKriatShmaGra], "ק\"ש גר\"א");
        AssertNear(tefilaGra, day[ZmanimCalendar.NameSofZmanTefilaGra], "תפילה גר\"א");
    }

    [Theory]
    [InlineData(2026, 6, 21, "04:07", "20:08", "08:25", "09:08", "10:19")]
    [InlineData(2026, 12, 21, "05:35", "16:54", "08:36", "09:07", "09:57")]
    [InlineData(2026, 10, 4, "05:24", "18:37", "08:56", "09:32", "10:31")]
    [InlineData(2026, 3, 20, "04:30", "18:08", "08:09", "08:45", "09:46")]
    public void OrHaChaim_TelAviv_MatchesPublishedLuach(int y, int m, int d, string alot, string tzeit, string shmaMga, string shmaGra, string tefilaGra)
    {
        var options = new ZmanimOptions { Method = ZmanCalculationMethod.OrHaChaim };
        var day = ZmanimCalendar.Calculate(new DateTime(y, m, d), TelAviv, options, forceIncludeConditional: true)
            .ToDictionary(e => e.Key, e => e.Time);

        AssertNear(alot, day[ZmanimCalendar.NameAlotHaShachar], "עלות השחר");
        AssertNear(tzeit, day[ZmanimCalendar.NameTzeitHakochavim], "צאת הכוכבים");
        AssertNear(shmaMga, day[ZmanimCalendar.NameSofZmanKriatShmaMga], "ק\"ש מג\"א");
        AssertNear(shmaGra, day[ZmanimCalendar.NameSofZmanKriatShmaGra], "ק\"ש גר\"א");
        AssertNear(tefilaGra, day[ZmanimCalendar.NameSofZmanTefilaGra], "תפילה גר\"א");
    }

    [Fact]
    public void OrHaChaim_DefinitionsInSeasonalMinutes()
    {
        var day = Raw(ZmanCalculationMethod.OrHaChaim, new DateTime(2026, 10, 5), Jerusalem);

        // מעלות השחר עד רבנו תם: 12 + 1.2 + 1.2 = 14.4 שעות זמניות.
        DateTime alot = day[ZmanimCalendar.NameAlotHaShachar]!.Value;
        double hour = (day[ZmanimCalendar.NameRabbeinuTam]!.Value - alot).TotalMinutes / 14.4;
        DateTime sunrise = alot.AddMinutes(1.2 * hour);
        DateTime sunset = day[ZmanimCalendar.NameShkia]!.Value;
        Assert.True(Math.Abs((sunset - sunrise).TotalMinutes / 12 - hour) < 0.1);

        // העיגול כאן לדקה הקרובה, ולכן ההפרש עד חצי דקה.
        void Expect(string name, DateTime expected) =>
            Assert.True(Math.Abs((day[name]!.Value - expected).TotalMinutes) <= 0.5, $"{name}: {day[name]:HH:mm:ss} vs {expected:HH:mm:ss}");

        Expect(ZmanimCalendar.NameMisheyakir, sunrise.AddMinutes(-1.1 * hour));
        Expect(ZmanimCalendar.NameSofZmanKriatShmaMga, sunrise.AddMinutes(2.4 * hour));
        Expect(ZmanimCalendar.NameTzeitHakochavim, sunset.AddMinutes(0.225 * hour));
        Expect(ZmanimCalendar.NamePelagHaMincha, sunset.AddMinutes((0.225 - 1.25) * hour));
        Expect(ZmanimCalendar.NameRabbeinuTam, sunset.AddMinutes(1.2 * hour));
        Expect(ZmanimCalendar.NameTzeitShabbat, sunset.AddMinutes(30));
    }

    [Fact]
    public void OrHaChaim_DisplayedNetz_IsSeaLevelAndNotBeforePrintedVisibleSunrise()
    {
        // הנץ הנראה שבלוח: ירושלים 6:34¾ (5.10.2026), תל אביב 6:42 (4.10.2026).
        // בירושלים הנץ במישור קרוב אליו; בתל אביב ההרים במזרח מאחרים את הנץ
        // הנראה בכ-5 דקות, ואת זה אי אפשר לחשב בלי מפה טופוגרפית.
        AssertNear("06:34:45", Raw(ZmanCalculationMethod.OrHaChaim, new DateTime(2026, 10, 5), Jerusalem)[ZmanimCalendar.NameNetz], "הנץ ירושלים", 1.5);
        DateTime telAviv = Raw(ZmanCalculationMethod.OrHaChaim, new DateTime(2026, 10, 4), TelAviv)[ZmanimCalendar.NameNetz]!.Value;
        Assert.True(telAviv.TimeOfDay <= TimeSpan.Parse("06:42"), $"{telAviv:HH:mm:ss}");
    }

    [Fact]
    public void Rounding_Lechumra_EndsDownStartsUp()
    {
        var time = new DateTime(2026, 10, 5, 9, 28, 40);
        Assert.Equal(new DateTime(2026, 10, 5, 9, 28, 0), ZmanimCalendar.Round(time, ZmanimCalendar.NameSofZmanKriatShmaGra, lechumra: true));
        Assert.Equal(new DateTime(2026, 10, 5, 9, 29, 0), ZmanimCalendar.Round(time, ZmanimCalendar.NameTzeitHakochavim, lechumra: true));
        Assert.Equal(new DateTime(2026, 10, 5, 9, 29, 0), ZmanimCalendar.Round(time, ZmanimCalendar.NameSofZmanKriatShmaGra, lechumra: false));
        Assert.Equal(new DateTime(2026, 10, 5, 9, 28, 0), ZmanimCalendar.Round(new DateTime(2026, 10, 5, 9, 28, 0), ZmanimCalendar.NameNetz, lechumra: true));
    }

    [Fact]
    public void SunEvent_IncludesRefraction()
    {
        // NOAA: ירושלים 6.10.2026, זריחה 06:36 ושקיעה 18:18 בגובה פני הים.
        TimeZoneInfo israel = TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time");
        DateTime? sunrise = AstronomicalCalculator.CalculateSunEvent(new DateTime(2026, 10, 6), 31.7889, 35.2153, AstronomicalCalculator.SunriseZenith, true, israel);
        DateTime? sunset = AstronomicalCalculator.CalculateSunEvent(new DateTime(2026, 10, 6), 31.7889, 35.2153, AstronomicalCalculator.SunriseZenith, false, israel);
        AssertNear("06:36:06", sunrise, "זריחה", 0.1);
        AssertNear("18:18:01", sunset, "שקיעה", 0.1);
    }

    [Fact]
    public void ConditionalZmanim_OnlyOnTheirDays()
    {
        var options = new ZmanimOptions { Method = ZmanCalculationMethod.OrHaChaim };
        List<string> Keys(DateTime date) => ZmanimCalendar.Calculate(date, Jerusalem, options).Select(e => e.Key).ToList();

        // יום שלישי רגיל: בלי חמץ, בלי הדלקת נרות ובלי צאת שבת.
        List<string> weekday = Keys(new DateTime(2026, 10, 6));
        Assert.DoesNotContain(ZmanimCalendar.NameSofZmanAchilatChametz, weekday);
        Assert.DoesNotContain(ZmanimCalendar.NameCandleLighting, weekday);
        Assert.DoesNotContain(ZmanimCalendar.NameTzeitShabbat, weekday);
        Assert.Contains(ZmanimCalendar.NameMisheyakir, weekday);

        Assert.Contains(ZmanimCalendar.NameCandleLighting, Keys(new DateTime(2026, 10, 9)));
        Assert.Contains(ZmanimCalendar.NameTzeitShabbat, Keys(new DateTime(2026, 10, 10)));

        // ערב פסח תשפ"ז: י"ד ניסן = 21.4.2027.
        List<string> erevPesach = Keys(new DateTime(2027, 4, 21));
        Assert.Contains(ZmanimCalendar.NameSofZmanAchilatChametz, erevPesach);
        Assert.Contains(ZmanimCalendar.NameSofZmanBiurChametz, erevPesach);
    }

    [Fact]
    public void EveryMethod_ProducesEveryZman()
    {
        foreach (ZmanCalculationMethod method in ZmanimMethods.All)
        {
            foreach (GeoLocation location in new[] { Jerusalem, TelAviv })
            {
                var day = Raw(method, new DateTime(2026, 6, 21), location);
                foreach (string name in ZmanimCalendar.AllZmanNames)
                {
                    Assert.True(day.TryGetValue(name, out DateTime? time) && time.HasValue, $"{method} {location.Name}: {name}");
                }

                Assert.True(day[ZmanimCalendar.NameAlotHaShachar] < day[ZmanimCalendar.NameMisheyakir]);
                Assert.True(day[ZmanimCalendar.NameMisheyakir] < day[ZmanimCalendar.NameNetz]);
                Assert.True(day[ZmanimCalendar.NameSofZmanKriatShmaMga] < day[ZmanimCalendar.NameSofZmanKriatShmaGra]);
                Assert.True(day[ZmanimCalendar.NameChatzot] < day[ZmanimCalendar.NameMinchaGedola]);
                Assert.True(day[ZmanimCalendar.NameShkia] < day[ZmanimCalendar.NameTzeitHakochavim]);
            }
        }
    }

    [Fact]
    public void DuplicateRow_UsesItsOwnMethod()
    {
        var options = new ZmanimOptions
        {
            Method = ZmanCalculationMethod.Gra,
            RoundLechumra = false,
            DuplicateRows = new[] { new ZmanDuplicateRow { Id = "dup", BaseZmanName = ZmanimCalendar.NameAlotHaShachar, CustomName = "עה\"ש (עתים לבינה)", Method = ZmanCalculationMethod.ItimLeBina } },
        };

        var entries = ZmanimCalendar.Calculate(new DateTime(2026, 10, 6), JerusalemKikarHaShabbat, options).ToDictionary(e => e.Key, e => e.Time);
        var itim = Raw(ZmanCalculationMethod.ItimLeBina, new DateTime(2026, 10, 6), JerusalemKikarHaShabbat);
        Assert.Equal(itim[ZmanimCalendar.NameAlotHaShachar], entries["dup"]);
        Assert.True(entries["dup"] < entries[ZmanimCalendar.NameAlotHaShachar]); // 19.8° לפני 16.1°
    }
}
