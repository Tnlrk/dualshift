using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using DualShift.Updates;

namespace DualShift.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("v1.5.230.0", "1.5.230.0")]
    [InlineData("v1.22.0", "1.22.0.0")]
    [InlineData("1.2.1", "1.2.1.0")]
    [InlineData("v2.0.0-beta", "2.0.0.0")]
    public void ParseVersion(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), GitHub.ParseVersion(tag));
    }

    [Fact]
    public void VersionsCompareRegardlessOfComponentCount()
    {
        // 1.5.230 z registru vs. v1.5.230.0 z GitHubu = stejná verze, žádná „aktualizace“.
        Assert.Equal(GitHub.ParseVersion("1.5.230"), GitHub.ParseVersion("v1.5.230.0"));
        Assert.True(GitHub.ParseVersion("v1.22.0") > GitHub.ParseVersion("1.21.442"));
    }

    [Fact]
    public void ParseRelease_ReadsDigestAndAssets()
    {
        const string json = """
        {"tag_name":"v1.2.1","html_url":"https://github.com/Tnlrk/dualshift/releases/tag/v1.2.1",
         "assets":[{"name":"DualShift.exe","browser_download_url":"https://github.com/Tnlrk/dualshift/releases/download/v1.2.1/DualShift.exe",
                    "size":123,"digest":"sha256:ABCDEF"}]}
        """;
        using var doc = JsonDocument.Parse(json);
        var r = GitHub.Parse(doc.RootElement);
        Assert.Equal(new Version(1, 2, 1, 0), r.Version);
        Assert.Equal("abcdef", r.Assets.Single().Sha256);
    }

    [Fact]
    public void PickInstaller_ByArchitecture()
    {
        var hidhide = new[]
        {
            new ReleaseAsset("HidHide_1.5.230_x64.exe", "u", 1, null),
            new ReleaseAsset("HidHide_1.5.230_arm64.exe", "u", 1, null),
            new ReleaseAsset("HidHide.pdb", "u", 1, null),
        };
        Assert.Equal("HidHide_1.5.230_x64.exe", GitHub.PickInstaller(hidhide, Architecture.X64)!.Name);
        Assert.Equal("HidHide_1.5.230_arm64.exe", GitHub.PickInstaller(hidhide, Architecture.Arm64)!.Name);

        var vigem = new[] { new ReleaseAsset("ViGEmBus_1.22.0_x64_x86_arm64.exe", "u", 1, null) };
        Assert.NotNull(GitHub.PickInstaller(vigem, Architecture.X64));
        Assert.NotNull(GitHub.PickInstaller(vigem, Architecture.Arm64));
    }

    [Fact]
    public async Task Download_RejectsNonGitHubUrls()
    {
        var evil = new ReleaseAsset("x.exe", "https://evil.example.com/x.exe", 1, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitHub.DownloadAsync(evil, Path.GetTempFileName() + ".x"));
        var http = new ReleaseAsset("x.exe", "http://github.com/x.exe", 1, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitHub.DownloadAsync(http, Path.GetTempFileName() + ".y"));
    }

    [Fact]
    public void LegacyUpdaterDetection()
    {
        Assert.True(LegacyUpdater.PointsToRetiredDomain("URL=https://updates.vigem.org/api/github/ViGEm/ViGEmBus/updates"));
        Assert.False(LegacyUpdater.PointsToRetiredDomain("URL=https://aiu.api.nefarius.systems/api/github/nefarius/HidHide/updates"));
    }

    [Fact]
    public void Signature_RejectsUnsignedFile()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "not a signed exe");
        try { Assert.False(Signature.IsSignedBy(f, Signature.NefariusSubject)); }
        finally { File.Delete(f); }
    }
}
