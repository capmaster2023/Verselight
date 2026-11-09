using System.ComponentModel;
using System.Diagnostics;

namespace MyVerse;

/// <summary>
/// Pare-feu Windows : règles qui laissent les téléphones joindre MyVerse (TCP 47900-47909, UDP 47901, programme MyVerse.exe),
/// sur tous les profils réseau (le Wi-Fi est souvent classé « public » par Windows).
/// </summary>
public static class Parefeu
{
    static readonly string[] Regles = { "MyVerse (TCP)", "MyVerse (UDP)", "MyVerse (programme)" };
    // anciennes règles de la 3.x, et règles automatiques de Windows (souvent « Bloquer ») au nom du programme
    static readonly string[] Anciennes = { "verselight-bridge.exe", "VerseLight Bridge (TCP)", "VerseLight Bridge (UDP)", "MyVerse OBS (TCP)", "MyVerse", "myverse.exe" };

    static int Netsh(params string[] args)
    {
        var psi = new ProcessStartInfo("netsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(20000);
        return p.HasExited ? p.ExitCode : -1;
    }

    static bool Existe(string nom) => Netsh("advfirewall", "firewall", "show", "rule", "name=" + nom) == 0;

    static string Programme => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MyVerse.exe");

    /// <summary>Les règles de la 4.0 sont en place pour ce MyVerse.exe (marque : chemin du programme).</summary>
    public static bool Present(string? marque)
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            if (!Regles.All(Existe)) return false;
            if (Existe("MyVerse") || Existe("myverse.exe")) return false; // règle automatique de Windows : peut-être « Bloquer »
            return marque == "ok4|" + Programme;
        }
        catch (Win32Exception) { return false; }
    }

    public static string Marque => "ok4|" + Programme;

    /// <summary>Avec les droits d'administrateur (installateur ou « MyVerse.exe --parefeu »).</summary>
    public static int Appliquer()
    {
        foreach (var r in Anciennes.Concat(Regles))
            if (Existe(r)) Netsh("advfirewall", "firewall", "delete", "rule", "name=" + r);
        var ajouts = new[]
        {
            new[] { "advfirewall", "firewall", "add", "rule", "name=MyVerse (TCP)", "dir=in", "action=allow", "protocol=TCP", "localport=47900-47909", "profile=any" },
            new[] { "advfirewall", "firewall", "add", "rule", "name=MyVerse (UDP)", "dir=in", "action=allow", "protocol=UDP", "localport=47901", "profile=any" },
            new[] { "advfirewall", "firewall", "add", "rule", "name=MyVerse (programme)", "dir=in", "action=allow", "program=" + Programme, "enable=yes", "profile=any" },
        };
        foreach (var a in ajouts)
            if (Netsh(a) != 0) return 1;
        return 0;
    }

    /// <summary>
    /// Demande l'autorisation (une fenêtre Windows « Autoriser cette application à apporter des modifications ? »)
    /// et ajoute les règles. Renvoie « ok », « refused » (refusé par l'utilisateur) ou « error ».
    /// </summary>
    public static string Demander()
    {
        try
        {
            var psi = new ProcessStartInfo(Programme, "--parefeu") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p == null) return "error";
            p.WaitForExit(120000);
            if (!p.HasExited || p.ExitCode != 0) return "error";
            return Regles.All(Existe) ? "ok" : "error";
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return "refused"; }
        catch (Win32Exception) { return "error"; }
    }
}
