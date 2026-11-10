using MyVerse.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// MyVirtualSound : le mix d'une piste d'OBS (tout ce que le mélangeur y envoie), avec son propre volume et sa sourdine,
/// joué sur le périphérique choisi. Vers « CABLE Input » (VB-CABLE), Zoom ou OBS le reçoivent comme un micro (« CABLE Output »).
/// MyChurch ne mélange rien ici : il recopie la piste d'OBS et applique seulement le volume de sortie.
/// </summary>
public sealed class VirtualSound : IDisposable
{
    sealed class Lecteur : IWaveProvider
    {
        readonly float[] anneau = new float[AudioBlock.Rate * 2]; // 0,5 s stéréo
        int lecture, quantite;
        readonly object verrou = new();
        float[] tampon = new float[4096];
        public volatile float Gain = 1;
        public float Crete;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioBlock.Rate, 2);
        public void Ecrire(float[] bloc)
        {
            lock (verrou)
            {
                foreach (float v in bloc)
                {
                    if (quantite == anneau.Length) { lecture = (lecture + 1) % anneau.Length; quantite--; }
                    anneau[(lecture + quantite) % anneau.Length] = v; quantite++;
                }
                // horloges différentes (OBS et la carte) : au-delà de 200 ms d'avance, on revient à 60 ms
                if (quantite > AudioBlock.Rate * 2 * 2 / 10) { int trop = quantite - AudioBlock.Rate * 2 * 6 / 100; trop -= trop % 2; lecture = (lecture + trop) % anneau.Length; quantite -= trop; }
            }
        }
        public int Read(byte[] buffer, int offset, int count)
        {
            int n = count / 4;
            if (tampon.Length < n) tampon = new float[n];
            float g = Gain, c = 0;
            lock (verrou)
            {
                int dispo = Math.Min(n, quantite);
                for (int i = 0; i < dispo; i++) { tampon[i] = anneau[lecture] * g; lecture = (lecture + 1) % anneau.Length; }
                quantite -= dispo;
                Array.Clear(tampon, dispo, n - dispo);
            }
            for (int i = 0; i < n; i++) { float a = Math.Abs(tampon[i]); if (a > c) c = a; }
            Crete = Math.Max(Crete * .85f, c);
            Buffer.BlockCopy(tampon, 0, buffer, offset, n * 4);
            return count;
        }
    }

    readonly ChurchAudioService audio;
    readonly Lecteur lecteur = new();
    readonly Action<float[]> consommateur;
    WasapiOut? sortie;
    MMDevice? peripherique;
    string signature = "";
    int piste = -1;
    public string Status { get; private set; } = "Désactivée";
    public bool Actif => sortie != null;

    public VirtualSound(ChurchAudioService audio)
    {
        this.audio = audio;
        consommateur = lecteur.Ecrire;
    }

    /// <summary>Niveau de sortie en dBFS (−∞ = silence).</summary>
    public float Level => sortie == null || lecteur.Crete <= 1e-6f ? float.NegativeInfinity : (float)AudioMath.Db(lecteur.Crete);

    /// <summary>Force une nouvelle recherche du câble (après l'installation de VB-CABLE).</summary>
    public void Reset() => Fermer();

    public void Apply(VirtualSoundSettings v)
    {
        lecteur.Gain = v.Muted ? 0 : (float)AudioMath.Linear(v.VolumeDb);
        string voulu = v.Enabled && audio.Running ? v.DeviceId + "|" + v.Track : "";
        if (voulu == signature) return;
        Fermer();
        signature = voulu;
        if (voulu.Length == 0) { Status = v.Enabled ? "En attente du moteur OBS" : "Désactivée"; return; }
        try
        {
            var enumerateur = new MMDeviceEnumerator();
            if (string.IsNullOrEmpty(v.DeviceId))
            {
                // automatique : le câble virtuel installé (VB-CABLE, Virtual Audio Driver, Virtual Cables)
                var cable = VbCable.Entree();
                if (cable == null) { signature = voulu; Status = "Aucun câble audio virtuel : installez VB-CABLE (bouton ci-dessous) ou choisissez une sortie."; return; }
                peripherique = enumerateur.GetDevice(cable.Value.Id);
            }
            else peripherique = v.DeviceId == "default" ? enumerateur.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerateur.GetDevice(v.DeviceId);
            sortie = new WasapiOut(peripherique, AudioClientShareMode.Shared, true, 50);
            sortie.Init(lecteur);
            piste = v.Track - 1;
            audio.AddTrackConsumer(piste, consommateur);
            sortie.Play();
            Status = $"Piste {v.Track} → {peripherique.FriendlyName}";
        }
        catch (Exception e)
        {
            Fermer(); signature = voulu;
            Status = "Sortie impossible : " + e.Message;
            Journal.Erreur("MyVirtualSound", e);
        }
    }

    void Fermer()
    {
        if (piste >= 0) { audio.RemoveTrackConsumer(piste, consommateur); piste = -1; }
        try { sortie?.Stop(); } catch (Exception) { }
        sortie?.Dispose(); sortie = null;
        peripherique?.Dispose(); peripherique = null;
        signature = "";
    }

    public void Dispose() => Fermer();
}
