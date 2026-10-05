using System.IO;
using System.Runtime.CompilerServices;
using HebrewTaskbarWidget.Models;
using HebrewTaskbarWidget.Services;

namespace HebrewTaskbarWidget.Tests;

/// <summary>
/// רץ לפני כל בדיקה: מפנה את קובץ ההגדרות לתיקייה זמנית ואת ההפעלה האוטומטית למפתח בדיקות,
/// כדי שהבדיקות לא ייגעו בהגדרות האמיתיות של המשתמש או בוידג'ט שרץ אצלו.
/// </summary>
internal static class TestIsolation
{
    public const string TestRunKeyPath = @"Software\TarichonTests\Run";

    public static string SettingsDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "TarichonTests", Environment.ProcessId.ToString());

    /// <summary>כל בדיקה מתחילה מברירות המחדל, בלי שאריות מבדיקה קודמת.</summary>
    public static void ResetSettings()
    {
        SettingsService.Save(new AppSettings());
        StartupService.SetEnabled(false);
    }

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(SettingsDirectory);
        SettingsService.IsolateForTests(SettingsDirectory);
        StartupService.RunKeyPathOverride = TestRunKeyPath;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\TarichonTests", throwOnMissingSubKey: false);
                Directory.Delete(SettingsDirectory, recursive: true);
            }
            catch (Exception)
            {
                // ניקוי בלבד - לא מפיל את הריצה
            }
        };
    }
}
