using System.IO;

namespace HebrewTaskbarWidget.Tests;

/// <summary>נתיבים לקבצי המקור בריפו, מחושבים מתיקיית הפלט של הבדיקות.</summary>
internal static class RepoPaths
{
    public static string SourceDirectory { get; } = FindSourceDirectory();

    public static string SourceFile(params string[] parts) => Path.Combine(new[] { SourceDirectory }.Concat(parts).ToArray());

    private static string FindSourceDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "HebrewTaskbarWidget.SettingsRecovery");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("לא נמצאה תיקיית המקור src/HebrewTaskbarWidget.SettingsRecovery");
    }
}
