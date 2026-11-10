using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace MyVerse.Audio;

public sealed record AudioDevice(string Id, string Name);

/// <summary>Où se trouvent libobs et ses modules. Dans MyChurch : le dossier « obs » posé à côté de MyChurch.exe par l'installateur.</summary>
public sealed class ObsPaths
{
    public string Library = "", DataPath = "", Graphics = "";
    public List<(string Bin, string? Data)> Modules = new();
    public string MicrophoneId = "wasapi_input_capture", OutputId = "wasapi_output_capture";
    public bool Present => File.Exists(Library);
    static string S(string p) => p.Replace('\\', '/');

    /// <summary>Disposition du paquet officiel OBS Studio 33 pour Windows : bin/64bit, data/libobs, core/&lt;module&gt;.</summary>
    public static ObsPaths Windows(string racine)
    {
        var p = new ObsPaths
        {
            Library = Path.Combine(racine, "bin", "64bit", "obs.dll"),
            DataPath = S(Path.Combine(racine, "data", "libobs")) + "/",
            Graphics = S(Path.Combine(racine, "bin", "64bit", "libobs-d3d11.dll")),
        };
        foreach (var m in new[] { "win-wasapi", "obs-filters", "obs-ffmpeg" })
            p.Modules.Add((S(Path.Combine(racine, "core", m, m + ".dll")), S(Path.Combine(racine, "core", m, "data"))));
        return p;
    }

    /// <summary>libobs installée par le système (paquet « obs-studio ») : sert aux essais hors Windows.</summary>
    public static ObsPaths? Linux()
    {
        foreach (var lib in new[] { "/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib" })
        {
            var so = Path.Combine(lib, "libobs.so.0");
            if (!File.Exists(so)) continue;
            var p = new ObsPaths { Library = so, DataPath = "/usr/share/obs/libobs/", Graphics = "libobs-opengl", MicrophoneId = "pulse_input_capture", OutputId = "pulse_output_capture" };
            foreach (var m in new[] { "linux-pulseaudio", "obs-filters", "obs-ffmpeg" })
                p.Modules.Add((Path.Combine(lib, "obs-plugins", m + ".so"), "/usr/share/obs/obs-plugins/" + m));
            return p;
        }
        return null;
    }
}

/// <summary>
/// Le mélangeur de MyChurch : une couche mince au-dessus de libobs. Chaque source du mélangeur est une source OBS
/// (micro ou sortie WASAPI, média FFmpeg), chaque filtre un filtre d'obs-filters, l'écoute est le monitoring d'OBS
/// et les pistes 1 à 6 sont ses six mélanges de sortie.
/// </summary>
public sealed class ObsMixer : IDisposable
{
    sealed class Filtre { public IntPtr Ptr; public AudioFilterKind Kind; public string Signature = ""; public bool Enabled = true; }
    sealed class Bande
    {
        public string Id = "";
        public uint Canal;
        public IntPtr Source, Volmeter;
        public AudioInputKind Kind;
        public string Signature = "";
        public AudioMonitoring Monitor;
        public readonly Dictionary<string, Filtre> Filtres = new();
        public volatile float PeakL = float.NegativeInfinity, PeakR = float.NegativeInfinity;
        public long Vu;
    }

    static ObsMixer? courant;
    // gardés en champs statiques : libobs les rappelle depuis ses propres fils
    static readonly LibObs.LogHandler journal = Journaliser;
    static readonly LibObs.VolmeterUpdated vumetre = Vumetre;
    static readonly LibObs.AudioOutputCallback sortie = Sortie;
    static readonly LibObs.EnumAudioDevice enumerer = Enumerer;
    static List<AudioDevice>? enumeration;

    readonly Dictionary<string, Bande> bandes = new();
    readonly ConcurrentDictionary<long, Bande> parCanal = new();
    readonly float[] cretePiste = new float[LibObs.Mixes];
    readonly Action<float[]>?[] consommateurs = new Action<float[]>?[LibObs.Mixes];
    readonly float[][] blocs = Enumerable.Range(0, LibObs.Mixes).Select(_ => new float[AudioBlock.Samples]).ToArray();
    readonly int[] rempli = new int[LibObs.Mixes];
    readonly float[] gauche = new float[8192], droite = new float[8192];
    ObsPaths chemins = new();
    IntPtr audio;
    bool ancienMonitoring;
    string moniteur = "";

    public bool Running { get; private set; }
    /// <summary>Les six pistes sont mélangées (le moteur vidéo interne d'OBS a démarré : il porte l'arbre de mélange).</summary>
    public bool TracksActive { get; private set; }
    public string Version { get; private set; } = "";
    public IReadOnlyList<string> Modules { get; private set; } = Array.Empty<string>();
    public string? LastError { get; private set; }
    /// <summary>Messages d'OBS (niveau, texte) : erreurs, avertissements, informations.</summary>
    public static event Action<int, string>? Log;

    static void Journaliser(int niveau, IntPtr format, IntPtr args, IntPtr _)
    {
        if (niveau > 300) return;
        try
        {
            string texte = LibObs.Format(format, args);
            if (niveau <= 100 && courant != null) courant.LastError = texte;
            Log?.Invoke(niveau, texte);
        }
        catch (Exception) { }
    }
    static float Lire(IntPtr tableau, int i) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(tableau, i * 4));
    static void Vumetre(IntPtr param, IntPtr magnitude, IntPtr peak, IntPtr inputPeak)
    {
        try
        {
            if (courant == null || !courant.parCanal.TryGetValue(param.ToInt64(), out var b)) return;
            float l = Lire(peak, 0), r = Lire(peak, 1);
            if (float.IsNaN(l)) l = float.NegativeInfinity;
            if (float.IsNaN(r) || float.IsNegativeInfinity(r)) r = l;
            b.PeakL = l; b.PeakR = r; b.Vu = Environment.TickCount64;
        }
        catch (Exception) { }
    }
    static bool Enumerer(IntPtr _, IntPtr name, IntPtr id)
    {
        try { enumeration?.Add(new(LibObs.Str(id) ?? "", LibObs.Str(name) ?? "")); } catch (Exception) { }
        return true;
    }
    /// <summary>Un bloc d'une piste, flottant planaire 48 kHz stéréo, sur le fil audio d'OBS.</summary>
    static void Sortie(IntPtr param, UIntPtr mix, IntPtr data)
    {
        try
        {
            var m = courant;
            int i = (int)param.ToInt64();
            if (m == null || i < 0 || i >= LibObs.Mixes || data == IntPtr.Zero) return;
            int frames = Marshal.ReadInt32(data, LibObs.MaxAvPlanes * IntPtr.Size);
            IntPtr pl = Marshal.ReadIntPtr(data, 0), pr = Marshal.ReadIntPtr(data, IntPtr.Size);
            if (frames <= 0 || frames > m.gauche.Length || pl == IntPtr.Zero) return;
            if (pr == IntPtr.Zero) pr = pl;
            Marshal.Copy(pl, m.gauche, 0, frames); Marshal.Copy(pr, m.droite, 0, frames);
            float crete = 0;
            for (int f = 0; f < frames; f++) { float a = Math.Abs(m.gauche[f]), b = Math.Abs(m.droite[f]); if (a > crete) crete = a; if (b > crete) crete = b; }
            m.cretePiste[i] = Math.Max(m.cretePiste[i] * .8f, crete);
            var consommateur = Volatile.Read(ref m.consommateurs[i]);
            if (consommateur == null) { m.rempli[i] = 0; return; }
            var bloc = m.blocs[i]; int n = m.rempli[i];
            for (int f = 0; f < frames; f++)
            {
                bloc[n++] = m.gauche[f]; bloc[n++] = m.droite[f];
                if (n == bloc.Length) { consommateur(bloc); n = 0; }
            }
            m.rempli[i] = n;
        }
        catch (Exception) { }
    }

    /// <summary>Démarre libobs : audio 48 kHz stéréo, modules (périphériques, filtres, média), six pistes.</summary>
    public void Start(ObsPaths p)
    {
        if (Running) return;
        if (!p.Present) throw new FileNotFoundException("Le moteur audio OBS (libobs) est absent : " + p.Library);
        chemins = p;
        LibObs.Load(p.Library);
        if (LibObs.obs_initialized()) throw new InvalidOperationException("libobs est déjà démarrée.");
        courant = this;
        LibObs.base_set_log_handler(journal, IntPtr.Zero);
        if (!LibObs.obs_startup("fr-FR", null, IntPtr.Zero)) throw new InvalidOperationException("libobs n'a pas démarré (obs_startup).");
        Version = LibObs.Str(LibObs.obs_get_version_string()) ?? "";
        if (LibObs.Has("obs_add_data_path")) LibObs.obs_add_data_path(p.DataPath);
        var ai = new LibObs.AudioInfo { SamplesPerSec = AudioBlock.Rate, Speakers = LibObs.SpeakersStereo };
        if (!LibObs.obs_reset_audio(ref ai)) { Arreter(); throw new InvalidOperationException("libobs : audio refusé (obs_reset_audio)."); }
        // Le mélange des pistes est porté par le « canvas » principal d'OBS : une image minuscule suffit.
        try
        {
            var vi = new LibObs.VideoInfo
            {
                GraphicsModule = Marshal.StringToCoTaskMemUTF8(p.Graphics),
                FpsNum = 10, FpsDen = 1, BaseWidth = 64, BaseHeight = 64, OutputWidth = 64, OutputHeight = 64,
                OutputFormat = 2 /* NV12 */, Adapter = 0, GpuConversion = true, Colorspace = 2 /* 709 */, Range = 1 /* partielle */, ScaleType = 3 /* bilinéaire */,
            };
            int rc = LibObs.obs_reset_video(ref vi);
            TracksActive = rc == 0;
            if (rc != 0) Log?.Invoke(200, $"MyChurch : obs_reset_video = {rc} ; vumètres, filtres et écoute fonctionnent, les pistes restent muettes.");
        }
        catch (Exception e) { TracksActive = false; Log?.Invoke(200, "MyChurch : moteur de pistes indisponible : " + e.Message); }
        var charges = new List<string>();
        foreach (var (bin, data) in p.Modules)
        {
            if (!File.Exists(bin)) { Log?.Invoke(200, "MyChurch : module OBS absent : " + bin); continue; }
            try
            {
                if (LibObs.obs_open_module(out var module, bin, data) == 0 && LibObs.obs_init_module(module)) charges.Add(Path.GetFileNameWithoutExtension(bin));
                else Log?.Invoke(200, "MyChurch : module OBS refusé : " + bin);
            }
            catch (Exception e) { Log?.Invoke(100, "MyChurch : module OBS " + bin + " : " + e.Message); }
        }
        Modules = charges;
        try { LibObs.obs_post_load_modules(); } catch (Exception) { }
        ancienMonitoring = !LibObs.Has("obs_source_set_monitoring_enabled");
        audio = LibObs.obs_get_audio();
        for (int i = 0; i < LibObs.Mixes; i++) LibObs.audio_output_connect(audio, (UIntPtr)i, IntPtr.Zero, sortie, (IntPtr)i);
        Running = true;
    }

    // ------------------------------------------------------------ périphériques
    List<AudioDevice> Liste(string sourceId)
    {
        var liste = new List<AudioDevice>();
        if (!Running) return liste;
        IntPtr props = LibObs.obs_get_source_properties(sourceId);
        if (props == IntPtr.Zero) return liste;
        try
        {
            IntPtr prop = LibObs.obs_properties_get(props, "device_id");
            if (prop == IntPtr.Zero) return liste;
            ulong n = LibObs.obs_property_list_item_count(prop).ToUInt64();
            for (ulong i = 0; i < n; i++)
            {
                string id = LibObs.Str(LibObs.obs_property_list_item_string(prop, (UIntPtr)i)) ?? "", nom = LibObs.Str(LibObs.obs_property_list_item_name(prop, (UIntPtr)i)) ?? "";
                if (id.Length > 0 && id != "default") liste.Add(new(id, nom));
            }
        }
        finally { LibObs.obs_properties_destroy(props); }
        return liste;
    }
    /// <summary>Microphones et entrées (sans « périphérique par défaut », que l'interface ajoute elle-même).</summary>
    public List<AudioDevice> Microphones() => Liste(chemins.MicrophoneId);
    /// <summary>Sorties audio dont on peut capter le son (haut-parleurs, casques).</summary>
    public List<AudioDevice> Outputs() => Liste(chemins.OutputId);
    /// <summary>Périphériques où OBS peut faire entendre l'écoute.</summary>
    public List<AudioDevice> MonitoringDevices()
    {
        var liste = new List<AudioDevice>();
        if (!Running) return liste;
        try
        {
            if (!LibObs.obs_audio_monitoring_available()) return liste;
            enumeration = liste;
            LibObs.obs_enum_audio_monitoring_devices(enumerer, IntPtr.Zero);
        }
        catch (Exception) { }
        finally { enumeration = null; }
        liste.RemoveAll(d => d.Id.Length == 0 || d.Id == "default");
        return liste;
    }

    // ------------------------------------------------------------ application des réglages
    static IntPtr Reglages(Action<IntPtr> remplir) { var d = LibObs.obs_data_create(); remplir(d); return d; }
    string TypeSource(AudioInputKind k) => k switch { AudioInputKind.Microphone => chemins.MicrophoneId, AudioInputKind.Sortie => chemins.OutputId, _ => "ffmpeg_source" };
    static IntPtr ReglagesSource(AudioSource s) => Reglages(d =>
    {
        if (s.Kind == AudioInputKind.Media)
        {
            LibObs.obs_data_set_bool(d, "is_local_file", true);
            LibObs.obs_data_set_string(d, "local_file", s.MediaPath);
            LibObs.obs_data_set_bool(d, "looping", s.Loop);
            LibObs.obs_data_set_bool(d, "restart_on_activate", false);
            LibObs.obs_data_set_bool(d, "close_when_inactive", false);
            LibObs.obs_data_set_bool(d, "hw_decode", false);
        }
        else LibObs.obs_data_set_string(d, "device_id", string.IsNullOrEmpty(s.DeviceId) ? "default" : s.DeviceId);
    });
    static string TypeFiltre(AudioFilterKind k)
    {
        string id = k switch { AudioFilterKind.NoiseSuppression => "noise_suppress_filter", AudioFilterKind.NoiseGate => "noise_gate_filter", AudioFilterKind.Compressor => "compressor_filter", _ => "limiter_filter" };
        return LibObs.Str(LibObs.obs_get_latest_input_type_id(id)) ?? id;
    }
    static IntPtr ReglagesFiltre(AudioFilter f) => Reglages(d =>
    {
        switch (f.Kind)
        {
            case AudioFilterKind.NoiseSuppression:
                LibObs.obs_data_set_string(d, "method", f.Method == "speex" ? "speex" : "rnnoise");
                LibObs.obs_data_set_int(d, "suppress_level", (long)Math.Round(f.SuppressDb));
                break;
            case AudioFilterKind.NoiseGate:
                LibObs.obs_data_set_double(d, "open_threshold", f.OpenDb);
                LibObs.obs_data_set_double(d, "close_threshold", f.CloseDb);
                LibObs.obs_data_set_int(d, "attack_time", (long)Math.Round(f.AttackMs));
                LibObs.obs_data_set_int(d, "hold_time", (long)Math.Round(f.HoldMs));
                LibObs.obs_data_set_int(d, "release_time", (long)Math.Round(f.ReleaseMs));
                break;
            case AudioFilterKind.Compressor:
                LibObs.obs_data_set_double(d, "ratio", f.Ratio);
                LibObs.obs_data_set_double(d, "threshold", f.ThresholdDb);
                LibObs.obs_data_set_int(d, "attack_time", (long)Math.Round(f.AttackMs));
                LibObs.obs_data_set_int(d, "release_time", (long)Math.Round(f.ReleaseMs));
                LibObs.obs_data_set_double(d, "output_gain", f.OutputGainDb);
                LibObs.obs_data_set_string(d, "sidechain_source", "none");
                break;
            default:
                LibObs.obs_data_set_double(d, "threshold", f.ThresholdDb);
                LibObs.obs_data_set_int(d, "release_time", (long)Math.Round(f.ReleaseMs));
                break;
        }
    });
    static string Signature(AudioFilter f) => string.Join("|", f.Kind, f.Ratio, f.ThresholdDb, f.AttackMs, f.ReleaseMs, f.OutputGainDb, f.OpenDb, f.CloseDb, f.HoldMs, f.Method, f.SuppressDb);
    static string Signature(AudioSource s) => s.Kind == AudioInputKind.Media ? s.MediaPath + "|" + s.Loop : s.DeviceId;

    /// <summary>Les trois choix d'OBS. Depuis libobs 33 l'écoute est un simple oui/non : « écoute seule » y revient
    /// à écouter la source sans l'envoyer dans aucune piste, ce que fait Apply en vidant ses pistes.</summary>
    void Ecoute(Bande b, AudioMonitoring mode)
    {
        if (b.Monitor == mode) return;
        bool etait = b.Monitor != AudioMonitoring.Off, sera = mode != AudioMonitoring.Off;
        b.Monitor = mode;
        if (ancienMonitoring) LibObs.obs_source_set_monitoring_type(b.Source, (int)mode);
        else if (etait != sera) LibObs.obs_source_set_monitoring_enabled(b.Source, sera);
    }

    /// <summary>Aligne OBS sur les réglages : crée, met à jour ou retire sources et filtres. À appeler après chaque changement.</summary>
    public void Apply(AudioConfiguration c)
    {
        if (!Running) return;
        string cible = string.IsNullOrEmpty(c.MonitorDeviceId) ? "default" : c.MonitorDeviceId;
        if (cible != moniteur)
        {
            try { if (LibObs.obs_audio_monitoring_available()) LibObs.obs_set_audio_monitoring_device(c.MonitorDeviceName ?? "", cible); } catch (Exception) { }
            moniteur = cible;
        }
        foreach (var id in bandes.Keys.Where(id => c.Sources.All(s => s.Id != id)).ToList()) Retirer(id);
        foreach (var s in c.Sources)
        {
            if (bandes.TryGetValue(s.Id, out var b) && b.Kind != s.Kind) { Retirer(s.Id); b = null; }
            string signature = Signature(s);
            if (b == null)
            {
                uint canal = 1;
                while (canal < 64 && parCanal.ContainsKey(canal)) canal++;
                if (canal >= 64) continue;
                IntPtr reglages = ReglagesSource(s);
                IntPtr source = LibObs.obs_source_create(TypeSource(s.Kind), "MyChurch " + s.Id, reglages, IntPtr.Zero);
                LibObs.obs_data_release(reglages);
                if (source == IntPtr.Zero) { Log?.Invoke(100, "MyChurch : source OBS refusée (" + TypeSource(s.Kind) + ")"); continue; }
                b = new Bande { Id = s.Id, Canal = canal, Source = source, Kind = s.Kind, Signature = signature };
                parCanal[canal] = b; bandes[s.Id] = b;
                b.Volmeter = LibObs.obs_volmeter_create(LibObs.FaderLog);
                LibObs.obs_volmeter_add_callback(b.Volmeter, vumetre, (IntPtr)canal);
                LibObs.obs_volmeter_attach_source(b.Volmeter, source);
                LibObs.obs_set_output_source(canal, source);
            }
            else if (b.Signature != signature)
            {
                IntPtr reglages = ReglagesSource(s);
                LibObs.obs_source_update(b.Source, reglages);
                LibObs.obs_data_release(reglages);
                b.Signature = signature;
            }
            LibObs.obs_source_set_volume(b.Source, (float)AudioMath.Linear(s.VolumeDb));
            LibObs.obs_source_set_muted(b.Source, s.Muted);
            LibObs.obs_source_set_flags(b.Source, s.Mono ? LibObs.FlagForceMono : 0);
            LibObs.obs_source_set_audio_mixers(b.Source, s.Monitoring == AudioMonitoring.Only && !ancienMonitoring ? 0 : (uint)s.Tracks & 0x3F);
            Ecoute(b, s.Monitoring);
            foreach (var fid in b.Filtres.Keys.Where(fid => s.Filters.All(f => f.Id != fid || f.Kind != b.Filtres[fid].Kind)).ToList())
            {
                var ancien = b.Filtres[fid];
                LibObs.obs_source_filter_remove(b.Source, ancien.Ptr); LibObs.obs_source_release(ancien.Ptr); b.Filtres.Remove(fid);
            }
            foreach (var f in s.Filters)
            {
                string sf = Signature(f);
                if (!b.Filtres.TryGetValue(f.Id, out var filtre))
                {
                    IntPtr reglages = ReglagesFiltre(f);
                    IntPtr ptr = LibObs.obs_source_create(TypeFiltre(f.Kind), "MyChurch filtre " + f.Id, reglages, IntPtr.Zero);
                    LibObs.obs_data_release(reglages);
                    if (ptr == IntPtr.Zero) { Log?.Invoke(100, "MyChurch : filtre OBS refusé (" + TypeFiltre(f.Kind) + ")"); continue; }
                    LibObs.obs_source_filter_add(b.Source, ptr);
                    filtre = new Filtre { Ptr = ptr, Kind = f.Kind, Signature = sf };
                    b.Filtres[f.Id] = filtre;
                }
                else if (filtre.Signature != sf)
                {
                    IntPtr reglages = ReglagesFiltre(f);
                    LibObs.obs_source_update(filtre.Ptr, reglages);
                    LibObs.obs_data_release(reglages);
                    filtre.Signature = sf;
                }
                if (filtre.Enabled != f.Enabled) { LibObs.obs_source_set_enabled(filtre.Ptr, f.Enabled); filtre.Enabled = f.Enabled; }
            }
        }
    }
    void Retirer(string id)
    {
        if (!bandes.Remove(id, out var b)) return;
        parCanal.TryRemove(b.Canal, out _);
        try
        {
            Ecoute(b, AudioMonitoring.Off);
            LibObs.obs_set_output_source(b.Canal, IntPtr.Zero);
            LibObs.obs_volmeter_remove_callback(b.Volmeter, vumetre, (IntPtr)b.Canal);
            LibObs.obs_volmeter_detach_source(b.Volmeter);
            LibObs.obs_volmeter_destroy(b.Volmeter);
            foreach (var f in b.Filtres.Values) { LibObs.obs_source_filter_remove(b.Source, f.Ptr); LibObs.obs_source_release(f.Ptr); }
            LibObs.obs_source_remove(b.Source);
            LibObs.obs_source_release(b.Source);
        }
        catch (Exception e) { Log?.Invoke(100, "MyChurch : retrait d'une source OBS : " + e.Message); }
    }

    // ------------------------------------------------------------ lecture des niveaux
    /// <summary>Crêtes gauche et droite d'une source en dBFS (−∞ = silence), après volume et filtres : le vumètre d'OBS.</summary>
    public (float L, float R) Level(string id)
    {
        if (!bandes.TryGetValue(id, out var b) || Environment.TickCount64 - b.Vu > 500) return (float.NegativeInfinity, float.NegativeInfinity);
        return (b.PeakL, b.PeakR);
    }
    public bool Has(string id) => bandes.ContainsKey(id);
    /// <summary>Crête d'une piste (0 à 5) en dBFS.</summary>
    public float TrackLevel(int piste)
    {
        if (piste < 0 || piste >= LibObs.Mixes) return float.NegativeInfinity;
        float v = cretePiste[piste];
        return v <= 1e-6f ? float.NegativeInfinity : (float)AudioMath.Db(v);
    }
    readonly object verrouConsommateurs = new();
    /// <summary>Reçoit le son d'une piste par blocs de 10 ms (stéréo entrelacée), sur le fil audio d'OBS. Plusieurs abonnés possibles (enregistrement, MyVirtualSound).</summary>
    public void AddTrackConsumer(int piste, Action<float[]> consommateur)
    {
        if (piste < 0 || piste >= LibObs.Mixes) return;
        lock (verrouConsommateurs) Volatile.Write(ref consommateurs[piste], (Action<float[]>?)Delegate.Combine(consommateurs[piste], consommateur));
    }
    public void RemoveTrackConsumer(int piste, Action<float[]> consommateur)
    {
        if (piste < 0 || piste >= LibObs.Mixes) return;
        lock (verrouConsommateurs) Volatile.Write(ref consommateurs[piste], (Action<float[]>?)Delegate.Remove(consommateurs[piste], consommateur));
    }
    public void RestartMedia(string id)
    {
        if (Running && bandes.TryGetValue(id, out var b) && b.Kind == AudioInputKind.Media) LibObs.obs_source_media_restart(b.Source);
    }

    void Arreter()
    {
        try { LibObs.obs_shutdown(); } catch (Exception) { }
        try { LibObs.base_set_log_handler(null, IntPtr.Zero); } catch (Exception) { }
        Running = false; TracksActive = false;
        if (courant == this) courant = null;
    }
    public void Dispose()
    {
        if (!Running) return;
        try
        {
            for (int i = 0; i < LibObs.Mixes; i++) { Volatile.Write(ref consommateurs[i], null); LibObs.audio_output_disconnect(audio, (UIntPtr)i, sortie, (IntPtr)i); }
            foreach (var id in bandes.Keys.ToList()) Retirer(id);
        }
        catch (Exception) { }
        Arreter();
    }
}
