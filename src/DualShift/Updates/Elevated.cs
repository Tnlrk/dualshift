using System.ComponentModel;
using System.Diagnostics;
using DualShift.Localization;

namespace DualShift.Updates;

/// <summary>Krátce spuštěná kopie této aplikace s právy správce (UAC) pro jednu konkrétní akci.</summary>
public static class Elevated
{
    public static bool RunSelf(IEnumerable<string> args, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null || !p.WaitForExit(30_000))
            {
                error = Loc.T("hh_notStarted");
                return false;
            }
            if (p.ExitCode != 0) error = Loc.F("hh_failed", p.ExitCode);
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = Loc.T("hh_uacDenied");
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
