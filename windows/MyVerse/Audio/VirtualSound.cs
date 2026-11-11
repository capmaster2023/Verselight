using MyVerse.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVerse.Ui.Audio;

/// <summary>
/// Une sortie MyVirtualSound : le mélange de ses sources (une piste d'OBS), avec son propre volume et sa sourdine,
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
    // null : rien d'ouvert (après Reset, on recherche de nouveau le câble) ; "" : sortie automatique (le câble installé)
    string? signature;
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
        // le périphérique ouvert ne change que si la sortie change ; la piste d'OBS peut changer sans couper le son
        string voulu = v.Enabled && audio.Running && v.Track > 0 ? v.DeviceId : "\u0000";
        if (voulu != signature)
        {
            Fermer();
            signature = voulu;
            if (voulu == "\u0000")
            {
                Status = !v.Enabled ? "Désactivée" : !audio.Running ? "En attente du moteur OBS" : "Plus de piste d'OBS libre (6 mélanges différents au plus)";
                return;
            }
            try
            {
                var enumerateur = new MMDeviceEnumerator();
                if (string.IsNullOrEmpty(v.DeviceId))
                {
                    // automatique : le câble virtuel installé (VB-CABLE, Virtual Audio Driver, Virtual Cables)
                    var cable = VbCable.Entree();
                    if (cable == null) { Status = "Aucun câble audio virtuel : installez VB-CABLE dans Paramètres › MyVirtualSound, ou choisissez une sortie."; return; }
                    peripherique = enumerateur.GetDevice(cable.Value.Id);
                }
                else peripherique = v.DeviceId == "default" ? enumerateur.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerateur.GetDevice(v.DeviceId);
                sortie = new WasapiOut(peripherique, AudioClientShareMode.Shared, true, 50);
                sortie.Init(lecteur);
                sortie.Play();
            }
            catch (Exception e)
            {
                Fermer(); signature = voulu;
                Status = "Sortie impossible : " + e.Message;
                Journal.Erreur("MyVirtualSound", e);
                return;
            }
        }
        if (sortie == null) return;
        if (piste != v.Track - 1)
        {
            if (piste >= 0) audio.RemoveTrackConsumer(piste, consommateur);
            piste = v.Track - 1;
            audio.AddTrackConsumer(piste, consommateur);
        }
        Status = "Active → " + peripherique?.FriendlyName;
    }

    void Fermer()
    {
        if (piste >= 0) { audio.RemoveTrackConsumer(piste, consommateur); piste = -1; }
        try { sortie?.Stop(); } catch (Exception) { }
        sortie?.Dispose(); sortie = null;
        peripherique?.Dispose(); peripherique = null;
        signature = null;
    }

    public void Dispose() => Fermer();
}

/// <summary>Toutes les sorties MyVirtualSound (4 au plus), chacune avec son câble ou sa sortie, actives en même temps si besoin.</summary>
public sealed class SortiesVirtuelles : IDisposable
{
    readonly ChurchAudioService audio;
    readonly Dictionary<string, VirtualSound> sorties = new();
    public SortiesVirtuelles(ChurchAudioService audio) => this.audio = audio;

    public void Apply(IReadOnlyList<VirtualSoundSettings> reglages)
    {
        foreach (var id in sorties.Keys.Where(id => reglages.All(v => v.Id != id)).ToList())
        {
            sorties[id].Dispose();
            sorties.Remove(id);
        }
        foreach (var v in reglages)
        {
            if (!sorties.TryGetValue(v.Id, out var s)) sorties[v.Id] = s = new VirtualSound(audio);
            s.Apply(v);
        }
    }
    public float Level(string id) => sorties.TryGetValue(id, out var s) ? s.Level : float.NegativeInfinity;
    public string Status(string id) => sorties.TryGetValue(id, out var s) ? s.Status : "Désactivée";
    public bool Actif(string id) => sorties.TryGetValue(id, out var s) && s.Actif;
    /// <summary>Force une nouvelle recherche des câbles (après l'installation de VB-CABLE).</summary>
    public void Reset() { foreach (var s in sorties.Values) s.Reset(); }
    public void Dispose() { foreach (var s in sorties.Values) s.Dispose(); sorties.Clear(); }
}
