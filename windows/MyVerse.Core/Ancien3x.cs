using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Lecture du stockage local de MyVerse 3.x (profil WebView2 : base LevelDB de Chromium), pour reprendre
/// chants et réglages quand la copie myverse-donnees.json n'existe pas (3.x antérieure à la 3.0.4).
/// Lecture seule : rien n'est modifié dans l'ancien profil.
/// </summary>
public static class Ancien3x
{
    const string ORIGINE = "http://127.0.0.1:47836";

    /// <summary>
    /// Dossiers « Local Storage\leveldb » de la 3.x : profil WebView2 dans %APPDATA%\MyVerse.exe (ou VerseLight.exe),
    /// ou à côté du programme ; le chemin exact varie selon WebView2 (EBWebView\Default…) : on le cherche.
    /// </summary>
    public static IEnumerable<string> Dossiers()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bases = new[]
        {
            Path.Combine(appdata, "MyVerse.exe"),
            Path.Combine(appdata, "VerseLight.exe"),
            Path.Combine(local, "Programs", "MyVerse", "MyVerse.exe.WebView2"),
            Path.Combine(local, "Programs", "MyVerse", "VerseLight.exe.WebView2"),
        };
        foreach (var b in bases)
            foreach (var d in Chercher(b, 5)) yield return d;
    }

    static IEnumerable<string> Chercher(string dossier, int profondeur)
    {
        if (profondeur < 0 || !Directory.Exists(dossier)) yield break;
        var nom = Path.GetFileName(dossier);
        if (nom.Equals("leveldb", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(Path.GetDirectoryName(dossier) ?? "").Equals("Local Storage", StringComparison.OrdinalIgnoreCase))
        {
            yield return dossier;
            yield break;
        }
        string[] sous;
        try { sous = Directory.GetDirectories(dossier); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        // les caches de WebView2 sont grands et inutiles ici
        foreach (var s in sous.Where(x => !Regex.IsMatch(Path.GetFileName(x), "cache|crashpad|shader", RegexOptions.IgnoreCase)).OrderBy(x => Path.GetFileName(x) == "EBWebView" ? 0 : 1))
            foreach (var d in Chercher(s, profondeur - 1)) yield return d;
    }

    /// <summary>Clés verselight-* et myverse-* du stockage local de la 3.x (vide si introuvable).</summary>
    public static Dictionary<string, string>? LireStockageLocal()
    {
        foreach (var d in Dossiers())
        {
            try
            {
                if (!Directory.Exists(d)) continue;
                var r = LireDossier(d);
                if (r.Count > 0) return r;
            }
            catch (Exception e) { Journal.Erreur("lecture du profil de la 3.x", e); }
        }
        return null;
    }

    /// <summary>Lit une base LevelDB de stockage local et renvoie les clés de MyVerse (dernière valeur de chaque clé).</summary>
    public static Dictionary<string, string> LireDossier(string dossier)
    {
        var tout = new Dictionary<byte[], (ulong Seq, byte[]? Val)>(new OctetsEgaux());
        void Mettre(byte[] cle, ulong seq, byte[]? val)
        {
            if (!tout.TryGetValue(cle, out var x) || seq >= x.Seq) tout[cle] = (seq, val);
        }
        // copie des fichiers : la 3.x peut être ouverte (fichiers verrouillés en écriture, lecture partagée possible)
        foreach (var f in Directory.GetFiles(dossier))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".log" or ".ldb" or ".sst")) continue;
            byte[] octets;
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                octets = new byte[fs.Length];
                fs.ReadExactly(octets);
            }
            catch (IOException) { continue; }
            try
            {
                if (ext == ".log") LireJournal(octets, Mettre);
                else LireTable(octets, Mettre);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException)
            {
                Journal.Erreur($"profil 3.x : fichier {Path.GetFileName(f)} illisible ({e.Message})");
            }
        }
        var res = new Dictionary<string, string>();
        var prefixe = Encoding.ASCII.GetBytes("_" + ORIGINE + "\0");
        foreach (var (cle, (_, val)) in tout)
        {
            if (val == null || cle.Length <= prefixe.Length || !cle.AsSpan(0, prefixe.Length).SequenceEqual(prefixe)) continue;
            var nom = Chaine(cle.AsSpan(prefixe.Length));
            if (nom == null || !(nom.StartsWith("verselight-") || nom.StartsWith("myverse-")) || nom == "myverse-journal") continue;
            var v = Chaine(val);
            if (v != null) res[nom] = v;
        }
        return res;
    }

    /// <summary>Chaîne du stockage local de Chromium : 0x01 + Latin-1, ou 0x00 + UTF-16LE.</summary>
    static string? Chaine(ReadOnlySpan<byte> b)
    {
        if (b.Length == 0) return null;
        if (b[0] == 1) return Encoding.Latin1.GetString(b[1..]);
        if (b[0] == 0 && (b.Length - 1) % 2 == 0) return Encoding.Unicode.GetString(b[1..]);
        return null;
    }

    sealed class OctetsEgaux : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? a, byte[]? b) => a != null && b != null && a.AsSpan().SequenceEqual(b);
        public int GetHashCode(byte[] o) { var h = new HashCode(); h.AddBytes(o); return h.ToHashCode(); }
    }

    // ------------------------------------------------------------ outils
    static uint Varint32(ReadOnlySpan<byte> b, ref int i)
    {
        uint r = 0;
        for (int s = 0; s <= 28; s += 7)
        {
            byte x = b[i++];
            r |= (uint)(x & 0x7f) << s;
            if (x < 0x80) return r;
        }
        throw new InvalidDataException("varint");
    }
    static ulong Varint64(ReadOnlySpan<byte> b, ref int i)
    {
        ulong r = 0;
        for (int s = 0; s <= 63; s += 7)
        {
            byte x = b[i++];
            r |= (ulong)(x & 0x7f) << s;
            if (x < 0x80) return r;
        }
        throw new InvalidDataException("varint");
    }

    // ------------------------------------------------------------ journal (.log) : lots d'écritures
    public static void LireJournal(byte[] f, Action<byte[], ulong, byte[]?> mettre)
    {
        const int BLOC = 32768;
        var lot = new List<byte>();
        int pos = 0;
        while (pos + 7 <= f.Length)
        {
            int resteBloc = BLOC - pos % BLOC;
            if (resteBloc < 7) { pos += resteBloc; continue; }
            int len = f[pos + 4] | (f[pos + 5] << 8);
            byte type = f[pos + 6];
            if (type == 0 && len == 0) { pos += resteBloc; continue; } // bourrage
            if (pos + 7 + len > f.Length) break;
            var morceau = f.AsSpan(pos + 7, len);
            pos += 7 + len;
            switch (type)
            {
                case 1: lot.Clear(); lot.AddRange(morceau.ToArray()); Lot(lot.ToArray(), mettre); lot.Clear(); break; // entier
                case 2: lot.Clear(); lot.AddRange(morceau.ToArray()); break; // début
                case 3: lot.AddRange(morceau.ToArray()); break; // milieu
                case 4: lot.AddRange(morceau.ToArray()); Lot(lot.ToArray(), mettre); lot.Clear(); break; // fin
                default: lot.Clear(); break;
            }
        }
    }

    static void Lot(byte[] b, Action<byte[], ulong, byte[]?> mettre)
    {
        if (b.Length < 12) return;
        ulong seq = BinaryPrimitives.ReadUInt64LittleEndian(b);
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8));
        int i = 12;
        for (uint k = 0; k < n && i < b.Length; k++)
        {
            byte type = b[i++];
            int lk = (int)Varint32(b, ref i);
            var cle = b.AsSpan(i, lk).ToArray();
            i += lk;
            if (type == 1)
            {
                int lv = (int)Varint32(b, ref i);
                var val = b.AsSpan(i, lv).ToArray();
                i += lv;
                mettre(cle, seq + k, val);
            }
            else mettre(cle, seq + k, null);
        }
    }

    // ------------------------------------------------------------ tables (.ldb)
    public static void LireTable(byte[] f, Action<byte[], ulong, byte[]?> mettre)
    {
        if (f.Length < 48) return;
        var pied = f.AsSpan(f.Length - 48);
        if (BinaryPrimitives.ReadUInt64LittleEndian(pied[40..]) != 0xdb4775248b80fb57UL) throw new InvalidDataException("table LevelDB inconnue");
        int i = 0;
        Varint64(pied, ref i); Varint64(pied, ref i); // métadonnées
        long io = (long)Varint64(pied, ref i), it = (long)Varint64(pied, ref i);
        var index = Bloc(f, io, it);
        foreach (var (_, poignee) in Entrees(index))
        {
            int j = 0;
            long o = (long)Varint64(poignee, ref j), t = (long)Varint64(poignee, ref j);
            foreach (var (cleInterne, val) in Entrees(Bloc(f, o, t)))
            {
                if (cleInterne.Length < 8) continue;
                ulong queue = BinaryPrimitives.ReadUInt64LittleEndian(cleInterne.AsSpan(cleInterne.Length - 8));
                var cle = cleInterne.AsSpan(0, cleInterne.Length - 8).ToArray();
                mettre(cle, queue >> 8, (queue & 0xff) == 1 ? val : null);
            }
        }
    }

    static byte[] Bloc(byte[] f, long o, long t)
    {
        if (o < 0 || t < 0 || o + t + 5 > f.Length) throw new InvalidDataException("bloc hors du fichier");
        var brut = f.AsSpan((int)o, (int)t);
        byte compression = f[o + t];
        return compression switch
        {
            0 => brut.ToArray(),
            1 => Snappy(brut),
            _ => throw new InvalidDataException("compression " + compression + " non prise en charge"),
        };
    }

    static IEnumerable<(byte[] Cle, byte[] Val)> Entrees(byte[] b)
    {
        if (b.Length < 4) yield break;
        int nRestart = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(b.Length - 4));
        int fin = b.Length - 4 - 4 * nRestart;
        if (fin < 0) yield break;
        int i = 0;
        byte[] precedente = Array.Empty<byte>();
        while (i < fin)
        {
            int partage = (int)Varint32(b, ref i), propre = (int)Varint32(b, ref i), lv = (int)Varint32(b, ref i);
            var cle = new byte[partage + propre];
            Array.Copy(precedente, cle, partage);
            Array.Copy(b, i, cle, partage, propre);
            i += propre;
            var val = b.AsSpan(i, lv).ToArray();
            i += lv;
            precedente = cle;
            yield return (cle, val);
        }
    }

    /// <summary>Décompression Snappy (format brut, utilisé par LevelDB).</summary>
    public static byte[] Snappy(ReadOnlySpan<byte> s)
    {
        int i = 0;
        int n = (int)Varint32(s, ref i);
        var o = new byte[n];
        int p = 0;
        while (i < s.Length)
        {
            byte tag = s[i++];
            int len, off;
            switch (tag & 3)
            {
                case 0:
                    len = tag >> 2;
                    if (len >= 60)
                    {
                        int k = len - 59;
                        len = 0;
                        for (int j = 0; j < k; j++) len |= s[i++] << (8 * j);
                    }
                    len++;
                    s.Slice(i, len).CopyTo(o.AsSpan(p));
                    i += len; p += len;
                    continue;
                case 1:
                    len = 4 + ((tag >> 2) & 7);
                    off = ((tag >> 5) << 8) | s[i++];
                    break;
                case 2:
                    len = (tag >> 2) + 1;
                    off = s[i] | (s[i + 1] << 8);
                    i += 2;
                    break;
                default:
                    len = (tag >> 2) + 1;
                    off = (int)BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(i, 4));
                    i += 4;
                    break;
            }
            if (off <= 0 || off > p) throw new InvalidDataException("snappy : décalage invalide");
            for (int j = 0; j < len; j++) o[p + j] = o[p - off + j]; // copie qui peut se chevaucher
            p += len;
        }
        if (p != n) throw new InvalidDataException("snappy : taille incorrecte");
        return o;
    }
}
