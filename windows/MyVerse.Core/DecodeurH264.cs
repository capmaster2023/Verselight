using System.Runtime.InteropServices;

namespace MyVerse;

/// <summary>
/// Décodeur H.264 de MyCam (MyChurch 4.1), comme DroidCam : FFmpeg (libavcodec et libswscale) tel qu'OBS Studio le livre avec MyChurch
/// (obs/bin/64bit : avcodec-62.dll, avutil-60.dll, swscale-9.dll), ou celui du système sous Linux (essais).
/// Chargé à la demande et vérifié sur une mire avant d'être annoncé aux téléphones : sans lui, MyCam reste en JPEG.
/// Une instance par téléphone, utilisée par un seul fil à la fois (l'appelant la verrouille).
/// </summary>
public sealed class DecodeurH264 : IDisposable
{
    // ------------------------------------------------------------ FFmpeg (fonctions publiques, stables depuis FFmpeg 4)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnTrouverDecodeur([MarshalAs(UnmanagedType.LPUTF8Str)] string nom);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnContexte(IntPtr codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnOuvrir(IntPtr ctx, IntPtr codec, ref IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FnLibererRef(ref IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDeux(IntPtr a, IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnAllouer();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FnUn(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnPaquetDonnees(IntPtr paquet, IntPtr donnees, int taille);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnMalloc(nuint taille);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnDictionnaire(ref IntPtr dict, [MarshalAs(UnmanagedType.LPUTF8Str)] string cle, [MarshalAs(UnmanagedType.LPUTF8Str)] string valeur, int drapeaux);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnFormatPixel([MarshalAs(UnmanagedType.LPUTF8Str)] string nom);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnContexteSws(IntPtr ctx, int srcL, int srcH, int srcFormat, int dstL, int dstH, int dstFormat, int drapeaux, IntPtr filtreSrc, IntPtr filtreDst, IntPtr param);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnConvertir(IntPtr ctx, IntPtr plans, IntPtr pas, int y, int h, IntPtr[] dst, int[] pasDst);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FnCoefficients(int espace);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnLireCouleurs(IntPtr ctx, out IntPtr inv, out int plageSrc, out IntPtr table, out int plageDst, out int luminosite, out int contraste, out int saturation);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FnReglerCouleurs(IntPtr ctx, IntPtr inv, int plageSrc, IntPtr table, int plageDst, int luminosite, int contraste, int saturation);

    const int EAGAIN = -11;
    /// <summary>AV_INPUT_BUFFER_PADDING_SIZE : zéros exigés après les données d'un paquet.</summary>
    const int Marge = 64;
    const int SwsBilineaire = 2;
    const int SwsItu601 = 5;
    // AVFrame : data[8] à 0, linesize[8] à 64, width à 104, height à 108, format à 116 (identique de FFmpeg 4 à 8)
    const int OffPas = 64, OffLargeur = 104, OffHauteur = 108, OffFormat = 116;

    sealed class Api
    {
        public string Version = "", Fichier = "";
        public int Bgra;
        public FnTrouverDecodeur TrouverDecodeur = null!;
        public FnContexte Contexte = null!;
        public FnOuvrir Ouvrir = null!;
        public FnLibererRef LibererContexte = null!, LibererPaquet = null!, LibererImage = null!, LibererDict = null!;
        public FnDeux Envoyer = null!, Recevoir = null!;
        public FnAllouer NouveauPaquet = null!, NouvelleImage = null!;
        public FnUn ViderPaquet = null!, ViderImage = null!, Free = null!, LibererSws = null!;
        public FnPaquetDonnees PaquetDonnees = null!;
        public FnMalloc Malloc = null!;
        public FnDictionnaire DictSet = null!;
        public FnContexteSws ContexteSws = null!;
        public FnConvertir Convertir = null!;
        public FnCoefficients Coefficients = null!;
        public FnLireCouleurs LireCouleurs = null!;
        public FnReglerCouleurs ReglerCouleurs = null!;

        static T F<T>(IntPtr lib, string nom) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, nom));

        public static Api Lier(IntPtr codec, IntPtr util, IntPtr sws, string fichier)
        {
            var a = new Api
            {
                Fichier = fichier,
                TrouverDecodeur = F<FnTrouverDecodeur>(codec, "avcodec_find_decoder_by_name"),
                Contexte = F<FnContexte>(codec, "avcodec_alloc_context3"),
                Ouvrir = F<FnOuvrir>(codec, "avcodec_open2"),
                LibererContexte = F<FnLibererRef>(codec, "avcodec_free_context"),
                Envoyer = F<FnDeux>(codec, "avcodec_send_packet"),
                Recevoir = F<FnDeux>(codec, "avcodec_receive_frame"),
                NouveauPaquet = F<FnAllouer>(codec, "av_packet_alloc"),
                LibererPaquet = F<FnLibererRef>(codec, "av_packet_free"),
                ViderPaquet = F<FnUn>(codec, "av_packet_unref"),
                PaquetDonnees = F<FnPaquetDonnees>(codec, "av_packet_from_data"),
                NouvelleImage = F<FnAllouer>(util, "av_frame_alloc"),
                LibererImage = F<FnLibererRef>(util, "av_frame_free"),
                ViderImage = F<FnUn>(util, "av_frame_unref"),
                Malloc = F<FnMalloc>(util, "av_malloc"),
                Free = F<FnUn>(util, "av_free"),
                DictSet = F<FnDictionnaire>(util, "av_dict_set"),
                LibererDict = F<FnLibererRef>(util, "av_dict_free"),
                ContexteSws = F<FnContexteSws>(sws, "sws_getCachedContext"),
                Convertir = F<FnConvertir>(sws, "sws_scale"),
                LibererSws = F<FnUn>(sws, "sws_freeContext"),
                Coefficients = F<FnCoefficients>(sws, "sws_getCoefficients"),
                LireCouleurs = F<FnLireCouleurs>(sws, "sws_getColorspaceDetails"),
                ReglerCouleurs = F<FnReglerCouleurs>(sws, "sws_setColorspaceDetails"),
            };
            a.Bgra = F<FnFormatPixel>(util, "av_get_pix_fmt")("bgra");
            if (a.Bgra < 0) throw new EntryPointNotFoundException("FFmpeg sans le format BGRA");
            uint v = F<FnVersion>(codec, "avcodec_version")();
            a.Version = $"{v >> 16}.{(v >> 8) & 0xFF}.{v & 0xFF}";
            return a;
        }
    }

    /// <summary>
    /// Les trios avcodec / avutil / swscale d'une même version de FFmpeg (avutil = avcodec − 2, swscale = avcodec − 53, de FFmpeg 4 à 8) :
    /// d'abord ceux d'OBS livrés avec MyChurch, puis ceux du système (Linux).
    /// </summary>
    static IEnumerable<(string Codec, string Util, string Sws)> Candidats()
    {
        if (OperatingSystem.IsWindows())
        {
            var dossiers = new List<string>();
            if (Dossier is { Length: > 0 } d) dossiers.Add(d);
            dossiers.Add(Path.Combine(AppContext.BaseDirectory, "obs", "bin", "64bit"));
            if (Environment.GetEnvironmentVariable("MYCHURCH_OBS") is { Length: > 0 } obs) dossiers.Add(Path.Combine(obs, "bin", "64bit"));
            dossiers.Add(AppContext.BaseDirectory);
            foreach (var dossier in dossiers.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
            {
                var versions = Directory.GetFiles(dossier, "avcodec-*.dll")
                    .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f)["avcodec-".Length..], out var m) ? m : 0)
                    .Where(m => m >= 58).OrderByDescending(m => m);
                foreach (var m in versions)
                {
                    var trio = (Path.Combine(dossier, $"avcodec-{m}.dll"), Path.Combine(dossier, $"avutil-{m - 2}.dll"), Path.Combine(dossier, $"swscale-{m - 53}.dll"));
                    if (File.Exists(trio.Item2) && File.Exists(trio.Item3)) yield return trio;
                }
            }
        }
        else
        {
            for (int m = 63; m >= 58; m--)
                yield return ($"libavcodec.so.{m}", $"libavutil.so.{m - 2}", $"libswscale.so.{m - 53}");
        }
    }

    static Api Ouvrir()
    {
        foreach (var (codec, util, sws) in Candidats())
        {
            // avutil d'abord : les deux autres en dépendent (sous Windows, elles sont cherchées dans le même dossier)
            if (!NativeLibrary.TryLoad(util, out var hu) || !NativeLibrary.TryLoad(sws, out var hs) || !NativeLibrary.TryLoad(codec, out var hc)) continue;
            return Api.Lier(hc, hu, hs, Path.GetFileName(codec));
        }
        throw new DllNotFoundException(OperatingSystem.IsWindows()
            ? "FFmpeg d'OBS introuvable (obs\\bin\\64bit\\avcodec-*.dll)"
            : "FFmpeg introuvable (libavcodec.so)");
    }

    /// <summary>Dossier où chercher FFmpeg avant les emplacements habituels (facultatif).</summary>
    public static string? Dossier { get; set; }

    static readonly Lazy<(Api? Api, string Etat)> charge = new(Charger, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>FFmpeg est chargé et décode la mire : le PC peut annoncer le H.264 aux téléphones.</summary>
    public static bool Disponible => charge.Value.Api != null;
    /// <summary>« FFmpeg 62.11.100 (avcodec-62.dll) », ou la raison de l'absence.</summary>
    public static string Etat => charge.Value.Etat;

    static (Api?, string) Charger()
    {
        try
        {
            var api = Ouvrir();
            // la mire : une image clé dont on connaît les couleurs. Le décodeur et la conversion BGRA marchent vraiment, ou rien n'est annoncé.
            using var d = new DecodeurH264(api);
            var px = d.Decoder(Mire[0], 0, Mire[0].Length, out var l, out var h);
            if (px == null || l != 64 || h != 48) return (null, $"FFmpeg {api.Version} ({api.Fichier}) : la mire H.264 ne se décode pas");
            // plein écart attendu ; écart « vidéo » accepté (couleurs un peu plus contrastées, mais l'image passe)
            bool plein = Proche(px, l, 32, 24, CouleursMire[0]);
            if (!plein && !Proche(px, l, 32, 24, (0, 102, 232)))
                return (null, $"FFmpeg {api.Version} ({api.Fichier}) : couleurs de la mire inattendues ({px[(24 * 64 + 32) * 4]},{px[(24 * 64 + 32) * 4 + 1]},{px[(24 * 64 + 32) * 4 + 2]})");
            return (api, $"FFmpeg {api.Version} ({api.Fichier})" + (plein ? "" : ", écart vidéo"));
        }
        catch (Exception e)
        {
            return (null, $"{e.GetType().Name} : {e.Message}");
        }
    }

    /// <summary>Un décodeur pour un téléphone ; null sans FFmpeg.</summary>
    public static DecodeurH264? Creer()
    {
        if (charge.Value.Api is not { } api) return null;
        try { return new DecodeurH264(api); }
        catch (Exception e) when (e is InvalidOperationException or OutOfMemoryException) { Journal.Erreur("MyCam H.264", e); return null; }
    }

    // ------------------------------------------------------------ décodage

    readonly Api f;
    IntPtr ctx, paquet, image, sws, bgra;
    int swsL, swsH, swsFormat = -1, tailleBgra;
    readonly IntPtr[] dst = new IntPtr[4];
    readonly int[] pasDst = new int[4];
    static readonly byte[] zeros = new byte[Marge];

    /// <summary>Paquets refusés par FFmpeg depuis la dernière image décodée.</summary>
    public int Erreurs { get; private set; }

    DecodeurH264(Api api)
    {
        f = api;
        var codec = f.TrouverDecodeur("h264");
        if (codec == IntPtr.Zero) throw new InvalidOperationException("FFmpeg sans décodeur H.264.");
        ctx = f.Contexte(codec);
        if (ctx == IntPtr.Zero) throw new OutOfMemoryException("FFmpeg : contexte du décodeur.");
        var options = IntPtr.Zero;
        // un seul fil et « faible retard » : chaque image sort dès son paquet, sans attendre les suivantes
        f.DictSet(ref options, "threads", "1", 0);
        f.DictSet(ref options, "flags", "+low_delay", 0);
        int r = f.Ouvrir(ctx, codec, ref options);
        if (options != IntPtr.Zero) f.LibererDict(ref options);
        if (r < 0)
        {
            f.LibererContexte(ref ctx);
            throw new InvalidOperationException($"FFmpeg : ouverture du décodeur H.264 impossible ({r}).");
        }
        paquet = f.NouveauPaquet();
        image = f.NouvelleImage();
        if (paquet == IntPtr.Zero || image == IntPtr.Zero)
        {
            Liberer();
            throw new OutOfMemoryException("FFmpeg : paquet ou image.");
        }
    }

    /// <summary>
    /// Une unité d'accès H.264 (Annex B ; les SPS/PPS précèdent les images clés) → l'image décodée en BGRA (largeur × 4 octets par ligne),
    /// ou null : pas encore d'image clé, ou données refusées.
    /// </summary>
    public byte[]? Decoder(byte[] donnees, int debut, int longueur, out int largeur, out int hauteur)
    {
        largeur = hauteur = 0;
        if (ctx == IntPtr.Zero || longueur <= 0 || debut < 0 || debut + longueur > donnees.Length) return null;
        // av_packet_from_data : un tampon av_malloc suivi de 64 zéros, que le paquet libère lui-même
        var tampon = f.Malloc((nuint)(longueur + Marge));
        if (tampon == IntPtr.Zero) return null;
        Marshal.Copy(donnees, debut, tampon, longueur);
        Marshal.Copy(zeros, 0, tampon + longueur, Marge);
        if (f.PaquetDonnees(paquet, tampon, longueur) < 0)
        {
            f.Free(tampon);
            Erreurs++;
            return null;
        }
        int r = f.Envoyer(ctx, paquet);
        f.ViderPaquet(paquet);
        if (r < 0 && r != EAGAIN)
        {
            Erreurs++;
            return null;
        }
        byte[]? sortie = null;
        while (f.Recevoir(ctx, image) >= 0)
        {
            try
            {
                if (Convertir(out var l, out var h) is { } px) { sortie = px; largeur = l; hauteur = h; }
            }
            finally { f.ViderImage(image); }
        }
        if (sortie != null) Erreurs = 0;
        return sortie;
    }

    byte[]? Convertir(out int largeur, out int hauteur)
    {
        largeur = Marshal.ReadInt32(image, OffLargeur);
        hauteur = Marshal.ReadInt32(image, OffHauteur);
        int format = Marshal.ReadInt32(image, OffFormat);
        if (largeur <= 0 || hauteur <= 0 || largeur > 8192 || hauteur > 8192 || format < 0) return null;
        if (sws == IntPtr.Zero || largeur != swsL || hauteur != swsH || format != swsFormat)
        {
            sws = f.ContexteSws(sws, largeur, hauteur, format, largeur, hauteur, f.Bgra, SwsBilineaire, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (sws == IntPtr.Zero)
            {
                swsFormat = -1;
                return null;
            }
            (swsL, swsH, swsFormat) = (largeur, hauteur, format);
            PleinEcart();
        }
        int pas = largeur * 4, taille = pas * hauteur;
        if (tailleBgra < taille)
        {
            if (bgra != IntPtr.Zero) f.Free(bgra);
            // tampon aligné d'av_malloc : swscale y écrit à pleine vitesse
            bgra = f.Malloc((nuint)taille);
            tailleBgra = bgra == IntPtr.Zero ? 0 : taille;
            if (bgra == IntPtr.Zero) return null;
        }
        dst[0] = bgra;
        pasDst[0] = pas;
        if (f.Convertir(sws, image, image + OffPas, 0, hauteur, dst, pasDst) <= 0) return null;
        var px = GC.AllocateUninitializedArray<byte>(taille);
        Marshal.Copy(bgra, px, 0, taille);
        return px;
    }

    /// <summary>
    /// Les téléphones encodent l'image de leur caméra telle quelle : YUV « plein écart » (0 à 255, BT.601), comme le JPEG de MyCam.
    /// Sans ceci, les noirs et les blancs seraient écrasés (écart « vidéo » 16 à 235 supposé).
    /// </summary>
    void PleinEcart()
    {
        if (f.LireCouleurs(sws, out var inv, out _, out var table, out var plageDst, out var lum, out var contraste, out var sat) < 0) return;
        var bt601 = f.Coefficients(SwsItu601);
        f.ReglerCouleurs(sws, bt601 != IntPtr.Zero ? bt601 : inv, 1, table, plageDst, lum, contraste, sat);
    }

    void Liberer()
    {
        if (image != IntPtr.Zero) f.LibererImage(ref image);
        if (paquet != IntPtr.Zero) f.LibererPaquet(ref paquet);
        if (ctx != IntPtr.Zero) f.LibererContexte(ref ctx);
        if (sws != IntPtr.Zero) { f.LibererSws(sws); sws = IntPtr.Zero; }
        if (bgra != IntPtr.Zero) { f.Free(bgra); bgra = IntPtr.Zero; tailleBgra = 0; }
    }

    public void Dispose()
    {
        Liberer();
        GC.SuppressFinalize(this);
    }

    ~DecodeurH264() => Liberer();

    // ------------------------------------------------------------ mire

    /// <summary>
    /// Mire H.264 (64 × 48, profil Baseline, comme les téléphones) : une image clé (SPS, PPS, IDR) puis deux images P.
    /// Couleurs unies connues (YUV 128/64/192, 200/128/128, 60/170/100) : <see cref="CouleursMire"/>.
    /// </summary>
    public static readonly byte[][] Mire =
    {
        Convert.FromBase64String("AAAAAWdCwAraEehAAAADAEAAAA8jxImoAAAAAWjOAU8gAAABZYiEOhHHAAFGmOAAKNEnJyddddddddeA"),
        Convert.FromBase64String("AAAAAUGaIrwpFAAEaqKAAI1UUAARqooAAjVRQABGqigACNVFAAEaqKAAI1UUAARqooAAjVRQABGqigACNVFAAEaqKAAI1UUAARqooAAjVRwABMMjgACYbifE+J8/n8/n8/n8/n8="),
        Convert.FromBase64String("AAAAAUGaQqNxQABarjgACPlHAAEUHE+J8T5/P5/P5/P5/P4="),
    };

    /// <summary>Couleurs (B, V, R) des images de la mire décodées en plein écart BT.601.</summary>
    public static readonly (byte B, byte V, byte R)[] CouleursMire = { (14, 104, 217), (200, 200, 200), (134, 64, 20) };

    /// <summary>Le pixel (x, y) d'une image BGRA est à ± tolérance de la couleur attendue.</summary>
    public static bool Proche(byte[] bgra, int largeur, int x, int y, (byte B, byte V, byte R) c, int tolerance = 12)
    {
        int i = (y * largeur + x) * 4;
        if (i < 0 || i + 3 > bgra.Length) return false;
        return Math.Abs(bgra[i] - c.B) <= tolerance && Math.Abs(bgra[i + 1] - c.V) <= tolerance && Math.Abs(bgra[i + 2] - c.R) <= tolerance;
    }
}
