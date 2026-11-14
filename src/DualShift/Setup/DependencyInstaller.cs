using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DualShift.Localization;
using DualShift.Updates;
using Microsoft.Win32;
using Nefarius.Drivers.HidHide;
using Nefarius.ViGEm.Client;

namespace DualShift.Setup;

public enum Dependency
{
    ViGEmBus,
    HidHide,
}

/// <summary>
/// Detekce a instalace ovladačů. Instalátor se pokaždé stáhne v nejnovější verzi z oficiálního
/// GitHubu autora (Nefarius) a spustí se jen tehdy, když má jeho platný digitální podpis.
/// </summary>
public static class DependencyInstaller
{
    public static string Repo(Dependency d) => d == Dependency.ViGEmBus ? "nefarius/ViGEmBus" : "nefarius/HidHide";

    public static string ProjectPage(Dependency d) => $"https://github.com/{Repo(d)}/releases/latest";

    public static bool IsInstalled(Dependency d) => d switch
    {
        Dependency.ViGEmBus => IsViGEmInstalled(),
        Dependency.HidHide => IsHidHideInstalled(),
        _ => false,
    };

    public static string DisplayName(Dependency d) => d switch
    {
        Dependency.ViGEmBus => Loc.T("dep_vigem"),
        Dependency.HidHide => Loc.T("dep_hidhide"),
        _ => d.ToString(),
    };

    /// <summary>Verze z „Programy a funkce“; null = není v seznamu nainstalovaných programů.</summary>
    public static Version? InstalledVersion(Dependency d)
    {
        string name = d == Dependency.ViGEmBus ? "ViGEm Bus Driver" : "HidHide";
        foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null) continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                if (app?.GetValue("DisplayName") is string dn && dn.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    app.GetValue("Publisher") is string pub && pub.StartsWith("Nefarius", StringComparison.OrdinalIgnoreCase))
                    return GitHub.ParseVersion(app.GetValue("DisplayVersion") as string);
            }
        }
        return null;
    }

    private static bool IsViGEmInstalled()
    {
        try
        {
            using var client = new ViGEmClient();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHidHideInstalled()
    {
        try
        {
            return new HidHideControlService().IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stáhne nejnovější instalátor z GitHubu, ověří podpis Nefarius a spustí ho s právy správce (UAC).
    /// Soubor leží v nové náhodné složce a od ověření do spuštění je zamčený proti přepsání.
    /// </summary>
    public static async Task<InstallResult> InstallAsync(Dependency d, ReleaseInfo? release = null)
    {
        string? dir = null;
        try
        {
            try { release ??= await GitHub.GetLatestAsync(Repo(d)); }
            catch (Exception ex) { return InstallResult.Failed(Loc.F("upd_err_download", ex.Message)); }

            var asset = GitHub.PickInstaller(release.Assets, RuntimeInformation.OSArchitecture);
            if (asset is null) return InstallResult.Failed(Loc.T("upd_err_noAsset"));

            dir = Directory.CreateTempSubdirectory("DualShift-").FullName;
            string file = Path.Combine(dir, Path.GetFileName(asset.Name));
            try { await GitHub.DownloadAsync(asset, file); }
            catch (Exception ex) { return InstallResult.Failed(Loc.F("upd_err_download", ex.Message)); }

            Process? p;
            // Dokud je soubor otevřený jen pro čtení, nejde přepsat ani smazat.
            await using (var guard = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (asset.Sha256 is { } expected && await GitHub.Sha256Async(guard) != expected)
                    return InstallResult.Failed(Loc.T("upd_err_hash"));
                if (!Signature.IsSignedBy(file, Signature.NefariusSubject))
                    return InstallResult.Failed(Loc.T("upd_err_signature"));
                p = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Verb = "runas" });
            }
            if (p is null) return InstallResult.Failed(Loc.T("dep_notStarted"));

            using (p)
            {
                await p.WaitForExitAsync();
                return p.ExitCode switch
                {
                    0 => IsInstalled(d) ? InstallResult.Ok : InstallResult.NeedsRestart,
                    3010 or 1641 => InstallResult.NeedsRestart,
                    1602 or 1223 => InstallResult.Failed(Loc.T("dep_cancelled")),
                    _ => IsInstalled(d) ? InstallResult.Ok : InstallResult.Failed(Loc.F("dep_exitCode", p.ExitCode)),
                };
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return InstallResult.Failed(Loc.T("dep_uacDenied"));
        }
        catch (Exception ex)
        {
            return InstallResult.Failed(ex.Message);
        }
        finally
        {
            try { if (dir is not null) Directory.Delete(dir, recursive: true); }
            catch { /* instalátor může ještě běžet */ }
        }
    }
}

public sealed record InstallResult(bool Success, bool RestartRequired, string? Error)
{
    public static readonly InstallResult Ok = new(true, false, null);
    public static readonly InstallResult NeedsRestart = new(true, true, null);
    public static InstallResult Failed(string error) => new(false, false, error);
}
