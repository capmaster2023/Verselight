using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Outils de texte : accents, normalisation, décodage des fichiers.</summary>
public static class Texte
{
    /// <summary>Retire les accents (é → e, ç → c…).</summary>
    public static string SansAccents(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Minuscules sans accents, apostrophes en espaces, œ → oe (même chose que norm() de la 3.x).</summary>
    public static string Norm(string? s) =>
        SansAccents((s ?? "").ToLowerInvariant())
            .Replace('’', ' ').Replace('\'', ' ').Replace('`', ' ')
            .Replace("œ", "oe").Replace("æ", "ae");

    /// <summary>Lit des octets de fichier texte : UTF-8 (avec ou sans BOM), UTF-16, sinon Windows-1252.</summary>
    public static string Decoder(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        int zeros = 0, n = Math.Min(b.Length, 400);
        for (int i = 0; i < n; i++) if (b[i] == 0) zeros++;
        if (n > 20 && zeros > n / 4) return (b[0] == 0 ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(b);
        try
        {
            return new UTF8Encoding(false, true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            return Latin1252.GetString(b);
        }
    }

    static Encoding? _w1252;
    /// <summary>Windows-1252 (fichiers texte anciens, RTF).</summary>
    public static Encoding Latin1252
    {
        get
        {
            if (_w1252 != null) return _w1252;
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _w1252 = Encoding.GetEncoding(1252);
            }
            catch
            {
                _w1252 = Encoding.Latin1;
            }
            return _w1252;
        }
    }

    /// <summary>Décode les entités HTML/XML (&amp;amp; &amp;#233; &amp;eacute;…).</summary>
    public static string Entites(string s) => System.Net.WebUtility.HtmlDecode(s);

    public static readonly Regex Espaces = new(@"\s+", RegexOptions.Compiled);
}
