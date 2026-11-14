using System.Diagnostics;

namespace DualShift.Updates;

/// <summary>
/// Starší instalace ViGEmBus (≤ 1.21.442) a HidHide (≤ 1.2.128) mají plánovanou úlohu, která se ptá
/// na doménu updates.vigem.org – ta už autorovi nepatří a autor doporučuje úlohu odstranit.
/// Aktualizace teď hlídá DualShift, takže úlohu lze bezpečně smazat.
/// </summary>
public static class LegacyUpdater
{
    public const string ElevatedArg = "--fix-legacy-updater";

    /// <summary>Jen tyto úlohy smí elevovaný proces smazat.</summary>
    private static readonly string[] KnownTasks = { "ViGEmBus_Updater", "HidHide_Updater", "nefarius_HidHide_Updater" };

    private static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions");

    internal static bool PointsToRetiredDomain(string iniContent) =>
        iniContent.Contains("vigem.org", StringComparison.OrdinalIgnoreCase);

    /// <summary>Úlohy, které se ptají na starou doménu. Volat mimo UI vlákno.</summary>
    public static IReadOnlyList<string> FindRiskyTasks()
    {
        var result = new List<string>();
        try
        {
            if (!Directory.Exists(InstallRoot)) return result;
            foreach (var ini in Directory.EnumerateFiles(InstallRoot, "*_Updater.ini", SearchOption.AllDirectories))
            {
                if (!PointsToRetiredDomain(File.ReadAllText(ini))) continue;
                string stem = Path.GetFileNameWithoutExtension(ini);
                foreach (var task in new[] { stem, "nefarius_" + stem })
                    if (KnownTasks.Contains(task, StringComparer.OrdinalIgnoreCase) && !result.Contains(task) && TaskExists(task))
                        result.Add(task);
            }
        }
        catch
        {
            // Nečitelná složka – nic nehlásit.
        }
        return result;
    }

    private static bool TaskExists(string name) => RunSchtasks("/Query", "/TN", name) == 0;

    private static int RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.HasExited ? p.ExitCode : -1;
    }

    public static bool Fix(IEnumerable<string> tasks, out string? error) =>
        Elevated.RunSelf(new[] { ElevatedArg }.Concat(tasks), out error);

    /// <summary>Tělo elevovaného procesu: <c>--fix-legacy-updater úloha1 úloha2 …</c>.</summary>
    public static int RunElevatedCommand(string[] args)
    {
        int failed = 0;
        foreach (var task in args.Skip(1).Where(t => KnownTasks.Contains(t, StringComparer.OrdinalIgnoreCase)))
            if (RunSchtasks("/Delete", "/TN", task, "/F") != 0) failed++;
        return failed == 0 ? 0 : 1;
    }
}
