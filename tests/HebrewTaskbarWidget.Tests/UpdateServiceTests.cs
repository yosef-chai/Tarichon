using HebrewTaskbarWidget.Services;
using Xunit;

namespace HebrewTaskbarWidget.Tests;

public class UpdateServiceTests
{
    [Fact]
    public void ChecksForUpdatesInTheFork()
    {
        Assert.Equal("yosef-chai", UpdateService.GitHubOwner);
        Assert.Equal("https://github.com/yosef-chai/Tarichon", UpdateService.RepositoryUrl);
    }

    /// <summary>הסדר שבו GitHub מחזיר את קבצי ה-Release (לפי שם): ה-Full מופיע לפני הרגיל.</summary>
    [Fact]
    public void PrefersRegularInstallerOverFullOne()
    {
        var assets = new (string, string?)[]
        {
            ("Tarichon-Portable.zip", "https://x/portable.zip"),
            ("Tarichon-Setup-0.9.3-Full.exe", "https://x/full.exe"),
            ("Tarichon-Setup-0.9.3.exe", "https://x/setup.exe"),
        };

        Assert.Equal("https://x/setup.exe", UpdateService.SelectInstallerAssetUrl(assets));
    }

    [Fact]
    public void FallsBackToFullInstallerWhenItIsTheOnlyOne()
    {
        var assets = new (string, string?)[]
        {
            ("Tarichon-Portable.zip", "https://x/portable.zip"),
            ("Tarichon-Setup-0.9.3-Full.exe", "https://x/full.exe"),
        };

        Assert.Equal("https://x/full.exe", UpdateService.SelectInstallerAssetUrl(assets));
    }

    [Fact]
    public void NoInstallerMeansNoUpdateOffered()
    {
        Assert.Null(UpdateService.SelectInstallerAssetUrl(new (string, string?)[] { ("Tarichon-Portable.zip", "https://x/p.zip") }));
    }
}
