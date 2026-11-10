using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using NAudio.CoreAudioApi;

namespace MyVerse.Ui.Audio;

/// <summary>
/// VB-CABLE (VB-Audio Software, www.vb-cable.com, donationware) : le câble audio virtuel qui fait de MyVirtualSound un micro
/// pour Zoom, OBS ou un navigateur. MyChurch ne le contient pas : il télécharge le paquet officiel sur vb-audio.com
/// et lance son installateur, avec l'accord de l'utilisateur (droits administrateur).
/// </summary>
public static class VbCable
{
    public const string Site = "https://vb-audio.com/Cable/";
    public const string Paquet = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip";
    public const string Mention = "VB-CABLE est un logiciel de VB-Audio Software (www.vb-cable.com), distribué en donationware : chacun paie ce qu'il veut s'il le trouve utile.";

    /// <summary>Entrées de câbles virtuels reconnus : VB-CABLE (« CABLE Input »), Virtual Audio Driver (VirtualDrivers, MIT), Virtual Cables.</summary>
    static readonly string[] Cables = { "CABLE Input", "Virtual Audio Driver", "Virtual Cable" };

    /// <summary>La sortie d'un câble virtuel installé, où MyVirtualSound envoie le son (VB-CABLE d'abord). null si aucun câble n'est installé.</summary>
    public static (string Id, string Nom)? Entree()
    {
        try
        {
            using var enumerateur = new MMDeviceEnumerator();
            var trouves = new List<(string Id, string Nom, int Rang)>();
            foreach (var d in enumerateur.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (d)
                {
                    int rang = Array.FindIndex(Cables, c => d.FriendlyName.Contains(c, StringComparison.OrdinalIgnoreCase));
                    if (rang >= 0) trouves.Add((d.ID, d.FriendlyName, rang));
                }
            if (trouves.Count > 0) { var t = trouves.MinBy(x => x.Rang); return (t.Id, t.Nom); }
        }
        catch (Exception e) { Journal.Erreur("détection d'un câble audio virtuel", e); }
        return null;
    }
    /// <summary>VB-CABLE lui-même (pour le bouton d'installation).</summary>
    public static bool Installe
    {
        get
        {
            try
            {
                using var enumerateur = new MMDeviceEnumerator();
                foreach (var d in enumerateur.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    using (d) if (d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }
    }

    /// <summary>Télécharge le paquet officiel et lance l'installateur 64 bits. Retourne false si l'utilisateur refuse ou si le téléchargement échoue (la page officielle s'ouvre alors).</summary>
    public static async Task<bool> InstallerAsync(IProgress<string>? etat = null)
    {
        string dossier = Path.Combine(Path.GetTempPath(), "MyChurch-VBCABLE-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dossier);
            etat?.Report("Téléchargement de VB-CABLE depuis vb-audio.com…");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MyChurch/" + App.Version);
            string zip = Path.Combine(dossier, "VBCABLE_Driver_Pack.zip");
            await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(Paquet));
            ZipFile.ExtractToDirectory(zip, Path.Combine(dossier, "pack"));
            string? setup = Directory.EnumerateFiles(Path.Combine(dossier, "pack"), "VBCABLE_Setup_x64.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException("Installateur VB-CABLE absent du paquet téléchargé.");
            etat?.Report("Installation de VB-CABLE : acceptez la demande de Windows, puis cliquez « Install Driver ».");
            using var p = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup)! });
            if (p != null) await p.WaitForExitAsync();
            for (int i = 0; i < 20 && !Installe; i++) await Task.Delay(500);
            etat?.Report(Installe ? "VB-CABLE est installé." : "VB-CABLE n'est pas encore visible : redémarrez l'ordinateur si l'installateur l'a demandé.");
            return Installe;
        }
        catch (System.ComponentModel.Win32Exception) { etat?.Report("Installation annulée."); return false; }
        catch (Exception e)
        {
            Journal.Erreur("installation de VB-CABLE", e);
            etat?.Report("Téléchargement impossible : la page officielle de VB-CABLE s'ouvre.");
            try { Process.Start(new ProcessStartInfo(Site) { UseShellExecute = true }); } catch (Exception) { }
            return false;
        }
        finally { try { Directory.Delete(dossier, true); } catch (Exception) { } }
    }
}
