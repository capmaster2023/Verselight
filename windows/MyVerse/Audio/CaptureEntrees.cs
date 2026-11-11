using MyVerse.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Les entrées séparées d'une carte son (MyChurch 3.1) : une carte à 4 entrées (Behringer UMC404HD, Focusrite…) arrive dans Windows
/// comme un seul micro à 4 canaux. MyChurch l'écoute une seule fois (WASAPI partagé, comme OBS), sépare les canaux et envoie
/// chacun à sa bande du mélangeur (source « entrée » d'OBS). Volume, filtres, écoute, vumètres et pistes restent ceux d'OBS.
/// </summary>
public sealed class CaptureEntrees : IDisposable
{
    static readonly Guid FloatIeee = new("00000003-0000-0010-8000-00aa00389b71");
    static readonly Guid Pcm = new("00000001-0000-0010-8000-00aa00389b71");

    sealed class Capteur
    {
        public string Cle = "";
        public WasapiCapture? Capture;
        public MMDevice? Peripherique;
        /// <summary>Les bandes qui écoutent ce périphérique : (identifiant de la source, entrée 1…N).</summary>
        public volatile (string Id, int Entree)[] Bandes = Array.Empty<(string, int)>();
        public float[][] Canaux = Array.Empty<float[]>();
        public string Statut = "";
        public bool Arrete;
    }

    readonly Func<string, float[], int, int, bool> pousser;
    readonly Dictionary<string, Capteur> capteurs = new();
    readonly object verrou = new();

    /// <param name="pousser">Envoie le son d'une entrée à sa bande d'OBS : (source, échantillons, nombre, fréquence).</param>
    public CaptureEntrees(Func<string, float[], int, int, bool> pousser) => this.pousser = pousser;

    static string Cle(string deviceId) => string.IsNullOrEmpty(deviceId) ? "default" : deviceId;

    /// <summary>Le périphérique de capture Windows : celui d'OBS (même identifiant WASAPI) ou le micro par défaut.</summary>
    static MMDevice Ouvrir(string cle)
    {
        using var e = new MMDeviceEnumerator();
        return cle == "default" ? e.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : e.GetDevice(cle);
    }

    /// <summary>Combien d'entrées (canaux) a ce micro ; 0 s'il est absent.</summary>
    public static int Entrees(string deviceId)
    {
        try
        {
            using var d = Ouvrir(Cle(deviceId));
            return d.State == DeviceState.Active ? Math.Max(1, d.AudioClient.MixFormat.Channels) : 0;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>État de l'écoute d'un périphérique (pour l'info-bulle d'une bande) : vide si tout va bien.</summary>
    public string Statut(string deviceId)
    {
        lock (verrou) return capteurs.TryGetValue(Cle(deviceId), out var c) ? c.Statut : "";
    }

    /// <summary>Ouvre, met à jour ou ferme les écoutes d'après les bandes « entrée séparée » du mélangeur.</summary>
    public void Apply(IEnumerable<AudioSource> sources)
    {
        var voulues = sources.Where(s => s.Kind == AudioInputKind.Microphone && s.Channel > 0)
            .GroupBy(s => Cle(s.DeviceId)).ToDictionary(g => g.Key, g => g.Select(s => (s.Id, s.Channel)).ToArray());
        List<Capteur> fermer;
        lock (verrou)
        {
            fermer = capteurs.Values.Where(c => !voulues.ContainsKey(c.Cle) || c.Arrete).ToList();
            foreach (var c in fermer) capteurs.Remove(c.Cle);
        }
        foreach (var c in fermer) Fermer(c);
        foreach (var (cle, bandes) in voulues)
        {
            Capteur? c;
            lock (verrou) capteurs.TryGetValue(cle, out c);
            if (c != null) { c.Bandes = bandes; continue; }
            c = new Capteur { Cle = cle, Bandes = bandes };
            try
            {
                c.Peripherique = Ouvrir(cle);
                var capture = new WasapiCapture(c.Peripherique, false, 30);
                var capteur = c;
                capture.DataAvailable += (_, e) => Recevoir(capteur, capture.WaveFormat, e.Buffer, e.BytesRecorded);
                capture.RecordingStopped += (_, e) =>
                {
                    capteur.Arrete = true;
                    capteur.Statut = e.Exception != null ? "Écoute arrêtée : " + e.Exception.Message : "Écoute arrêtée";
                    if (e.Exception != null) Journal.Erreur("entrées séparées : " + capteur.Peripherique?.FriendlyName, e.Exception);
                };
                c.Capture = capture;
                capture.StartRecording();
                Journal.Info($"entrées séparées : {c.Peripherique.FriendlyName}, {capture.WaveFormat.Channels} canaux à {capture.WaveFormat.SampleRate} Hz, entrées {string.Join(", ", bandes.Select(b => b.Entree))}");
            }
            catch (Exception x)
            {
                c.Statut = "Périphérique indisponible : " + x.Message;
                c.Arrete = true;
                Journal.Erreur("entrées séparées : ouverture de " + cle, x);
                try { c.Capture?.Dispose(); } catch (Exception) { }
                c.Capture = null;
            }
            lock (verrou) capteurs[cle] = c;
        }
    }

    /// <summary>Un bloc du périphérique (canaux entrelacés) : chaque bande reçoit son canal.</summary>
    void Recevoir(Capteur c, WaveFormat f, byte[] buffer, int octets)
    {
        try
        {
            int canaux = f.Channels, taille = f.BitsPerSample / 8;
            if (canaux <= 0 || taille <= 0 || octets <= 0) return;
            int frames = octets / (canaux * taille);
            if (frames <= 0) return;
            bool flottant = f.Encoding == WaveFormatEncoding.IeeeFloat || (f is WaveFormatExtensible x && x.SubFormat == FloatIeee);
            bool entier = f.Encoding == WaveFormatEncoding.Pcm || (f is WaveFormatExtensible y && y.SubFormat == Pcm);
            if (!flottant && !entier) { c.Statut = "Format du périphérique non pris en charge"; return; }
            var bandes = c.Bandes;
            if (c.Canaux.Length < canaux || c.Canaux[0].Length < frames)
                c.Canaux = Enumerable.Range(0, canaux).Select(_ => new float[Math.Max(frames, 4800)]).ToArray();
            // seuls les canaux écoutés sont séparés
            foreach (var k in bandes.Select(b => b.Entree - 1).Distinct())
            {
                if (k < 0 || k >= canaux) continue;
                var dest = c.Canaux[k];
                for (int i = 0; i < frames; i++)
                {
                    int o = (i * canaux + k) * taille;
                    dest[i] = flottant ? (taille == 4 ? BitConverter.ToSingle(buffer, o) : (float)BitConverter.ToDouble(buffer, o)) : taille switch
                    {
                        2 => BitConverter.ToInt16(buffer, o) / 32768f,
                        3 => ((buffer[o] << 8 | buffer[o + 1] << 16 | buffer[o + 2] << 24) >> 8) / 8388608f,
                        4 => BitConverter.ToInt32(buffer, o) / 2147483648f,
                        _ => 0,
                    };
                }
            }
            foreach (var (id, entree) in bandes)
                if (entree >= 1 && entree <= canaux) pousser(id, c.Canaux[entree - 1], frames, f.SampleRate);
            c.Statut = "";
        }
        catch (Exception x) { c.Statut = "Erreur de lecture : " + x.Message; }
    }

    static void Fermer(Capteur c)
    {
        try { c.Capture?.StopRecording(); } catch (Exception) { }
        try { c.Capture?.Dispose(); } catch (Exception) { }
        try { c.Peripherique?.Dispose(); } catch (Exception) { }
        c.Capture = null;
    }

    public void Dispose()
    {
        List<Capteur> tous;
        lock (verrou) { tous = capteurs.Values.ToList(); capteurs.Clear(); }
        foreach (var c in tous) Fermer(c);
    }
}
