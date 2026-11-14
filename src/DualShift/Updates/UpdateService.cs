using System.Diagnostics;
using System.Reflection;
using DualShift.Localization;
using DualShift.Setup;

namespace DualShift.Updates;

public enum UpdateComponent
{
    App,
    ViGEmBus,
    HidHide,
}

/// <summary>Stav jedné součásti: co je nainstalované a co je nejnovější na GitHubu.</summary>
public sealed record ComponentStatus(UpdateComponent Kind, bool Installed, Version? InstalledVersion, ReleaseInfo? Latest)
{
    public bool UpdateAvailable =>
        Installed && Latest is not null && (InstalledVersion is null || Latest.Version > InstalledVersion);
}

/// <summary>
/// Kontrola aktualizací aplikace a ovladačů (při spuštění a na tlačítko) a samotná aktualizace aplikace.
/// Metody volat z UI vlákna; <see cref="Changed"/> se volá na stejném vlákně.
/// </summary>
public sealed class UpdateService
{
    public const string AppRepo = "Tnlrk/dualshift";
    public const string AppAssetName = "DualShift.exe";
    public const string AfterUpdateArg = "--after-update";

    private readonly Dictionary<UpdateComponent, ComponentStatus> _status = new();

    public event Action? Changed;

    public bool IsChecking { get; private set; }
    public bool IsBusy { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public string? LastError { get; private set; }
    public IReadOnlyList<string> LegacyTasks { get; private set; } = Array.Empty<string>();

    public UpdateService() => RefreshLocal();

    public static Version CurrentAppVersion =>
        GitHub.Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    public ComponentStatus this[UpdateComponent c] => _status[c];

    public IEnumerable<ComponentStatus> All => _status.Values.OrderBy(s => s.Kind);

    /// <summary>Je co aktualizovat nebo opravit?</summary>
    public bool AnythingToDo => _status.Values.Any(s => s.UpdateAvailable) || LegacyTasks.Count > 0;

    /// <summary>Krátký seznam pro upozornění, např. „DualShift 1.3.0, HidHide 1.6.0“.</summary>
    public string Summary()
    {
        var parts = _status.Values.Where(s => s.UpdateAvailable)
            .Select(s => $"{Name(s.Kind)} {Short(s.Latest!.Version)}").ToList();
        if (LegacyTasks.Count > 0) parts.Add(Loc.T("note_legacyItem"));
        return string.Join(", ", parts);
    }

    public static string Name(UpdateComponent c) => c switch
    {
        UpdateComponent.App => "DualShift",
        UpdateComponent.ViGEmBus => "ViGEmBus",
        _ => "HidHide",
    };

    public static string Short(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

    public static Dependency? AsDependency(UpdateComponent c) => c switch
    {
        UpdateComponent.ViGEmBus => Dependency.ViGEmBus,
        UpdateComponent.HidHide => Dependency.HidHide,
        _ => null,
    };

    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        Changed?.Invoke();
    }

    /// <summary>Znovu zjistí, co je nainstalované (po instalaci ovladače), bez dotazu na GitHub.</summary>
    public void RefreshLocal()
    {
        _status[UpdateComponent.App] = new(UpdateComponent.App, true, CurrentAppVersion, Latest(UpdateComponent.App));
        foreach (var d in new[] { Dependency.ViGEmBus, Dependency.HidHide })
        {
            var c = d == Dependency.ViGEmBus ? UpdateComponent.ViGEmBus : UpdateComponent.HidHide;
            var version = DependencyInstaller.InstalledVersion(d);
            bool installed = version is not null || DependencyInstaller.IsInstalled(d);
            _status[c] = new(c, installed, version, Latest(c));
        }
        Changed?.Invoke();
    }

    private ReleaseInfo? Latest(UpdateComponent c) => _status.TryGetValue(c, out var s) ? s.Latest : null;

    /// <summary>Zeptá se GitHubu na nejnovější verze. Chyby sítě se jen zapíšou do <see cref="LastError"/>.</summary>
    public async Task CheckAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        LastError = null;
        Changed?.Invoke();
        try
        {
            var app = GitHub.GetLatestAsync(AppRepo);
            var vigem = GitHub.GetLatestAsync(DependencyInstaller.Repo(Dependency.ViGEmBus));
            var hidhide = GitHub.GetLatestAsync(DependencyInstaller.Repo(Dependency.HidHide));
            var legacy = Task.Run(LegacyUpdater.FindRiskyTasks);

            var releases = new Dictionary<UpdateComponent, ReleaseInfo?>
            {
                [UpdateComponent.App] = await TryGet(app),
                [UpdateComponent.ViGEmBus] = await TryGet(vigem),
                [UpdateComponent.HidHide] = await TryGet(hidhide),
            };
            LegacyTasks = await legacy;

            foreach (var (c, r) in releases)
                if (r is not null) _status[c] = _status[c] with { Latest = r };
            LastCheck = DateTime.Now;
        }
        finally
        {
            IsChecking = false;
            RefreshLocal();
        }
    }

    private async Task<ReleaseInfo?> TryGet(Task<ReleaseInfo> task)
    {
        try
        {
            return await task;
        }
        catch (Exception ex)
        {
            LastError ??= ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Stáhne nový DualShift.exe, ověří kontrolní součet zveřejněný GitHubem, vymění ho za běžící exe
    /// a spustí novou verzi. Po úspěchu musí volající aplikaci ukončit.
    /// </summary>
    public async Task<InstallResult> UpdateAppAsync()
    {
        var release = _status[UpdateComponent.App].Latest;
        var asset = release?.Assets.FirstOrDefault(a => a.Name.Equals(AppAssetName, StringComparison.OrdinalIgnoreCase));
        if (release is null || asset is null) return InstallResult.Failed(Loc.T("upd_err_noAsset"));
        if (asset.Sha256 is null) return InstallResult.Failed(Loc.T("upd_err_noHash"));

        string exe = Environment.ProcessPath!;
        string fresh = exe + ".new", old = exe + ".old";
        try
        {
            File.Delete(fresh);
            File.Delete(old);
        }
        catch (Exception ex)
        {
            return InstallResult.Failed(Loc.F("upd_err_folder", ex.Message));
        }

        try
        {
            await GitHub.DownloadAsync(asset, fresh);
            await using (var s = File.OpenRead(fresh))
                if (await GitHub.Sha256Async(s) != asset.Sha256)
                {
                    File.Delete(fresh);
                    return InstallResult.Failed(Loc.T("upd_err_hash"));
                }
        }
        catch (UnauthorizedAccessException ex)
        {
            return InstallResult.Failed(Loc.F("upd_err_folder", ex.Message));
        }
        catch (Exception ex)
        {
            try { File.Delete(fresh); } catch { /* nic */ }
            return InstallResult.Failed(Loc.F("upd_err_download", ex.Message));
        }

        try
        {
            // Běžící exe jde přejmenovat (ne smazat) – starý se uklidí při příštím spuštění.
            File.Move(exe, old);
            File.Move(fresh, exe);
        }
        catch (Exception ex)
        {
            if (!File.Exists(exe) && File.Exists(old)) File.Move(old, exe);
            return InstallResult.Failed(Loc.F("upd_err_folder", ex.Message));
        }

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(AfterUpdateArg);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi)?.Dispose();
        return InstallResult.Ok;
    }

    /// <summary>Uklidit po aktualizaci (starý exe vedle nového).</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                File.Delete(exe + ".old");
                File.Delete(exe + ".new");
            }
        }
        catch
        {
            // Starý proces možná ještě doběhává – zkusí se příště.
        }
    }

    /// <summary>Nová verze po aktualizaci počká, než se stará ukončí (a uvolní mutex).</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        int i = Array.IndexOf(args, AfterUpdateArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid)) return;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(15_000);
        }
        catch
        {
            // Už neběží.
        }
    }
}
