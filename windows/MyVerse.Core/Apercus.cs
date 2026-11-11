namespace MyVerse;

/// <summary>
/// Aperçus : l'écran que donnerait un verset, un chant, une source Display ou un dossier Duo, sans rien afficher
/// (vignettes de la grille, vidéo des enregistrements). Aucun événement, aucune mémoire de ce qui a été affiché.
/// </summary>
public sealed partial class Regie
{
    EtatEcran Neutre() => new() { Theme = Etat.Theme, Taille = Etat.Taille, Presentation = Etat.Presentation };

    public EtatEcran? EtatVerset(int b, int c, int v1, int v2)
    {
        var vs = B.Versets(b, c);
        int n = vs.Count;
        if (n == 0) return null;
        v1 = Math.Clamp(v1, 1, n);
        v2 = Math.Max(v1, Math.Min(n, v2));
        var parts = v1 == v2 ? Morceaux(b, c, v1) : null;
        var t2 = parts == null ? Bibles.Texte2(b, c, v1, v2) : "";
        return Neutre() with
        {
            Mode = "verset",
            Lignes = new[] { parts != null ? parts[0] : B.Texte(b, c, v1, v2) },
            Ref = Livres.Libelle(b, c, v1, v2) + $" ({Bibles.Etiquette()})",
            Second = t2.Length > 0 ? new EtatEcran.TexteSecond(t2) : null,
        };
    }

    public EtatEcran? EtatSection(string id, int i)
    {
        var c = Trouver(id);
        if (c == null || i < 0 || i >= c.Sections.Count) return null;
        return Neutre() with { Mode = "chant", Lignes = c.Sections[i].Text.Split('\n').Where(l => l.Trim().Length > 0).ToArray() };
    }

    /// <summary>Une source Display : son texte, son média, ou le dossier Duo qu'elle représente.</summary>
    public EtatEcran? EtatAnnonce(Annonce a, int i = 0)
    {
        if (a.Source is { Type: "duo" } s) return donnees.Duos.FirstOrDefault(d => d.Id == s.Chemin) is { } dossier ? EtatDuo(dossier) : null;
        if (a.Source != null) return Neutre() with { Mode = "media", Media = a.Source, Ref = a.Titre };
        var blocs = BlocsTexte(a.Texte);
        if (blocs.Count == 0) return null;
        i = Math.Clamp(i, 0, blocs.Count - 1);
        return Neutre() with { Mode = "chant", Lignes = blocs[i].Split('\n').Where(l => l.Trim().Length > 0).ToArray() };
    }

    public EtatEcran? EtatPart(PartDuo p) => p.Type switch
    {
        "display" => donnees.Annonces.FirstOrDefault(a => a.Id == p.Id) is { } a && a.Source?.Type != "duo" ? EtatAnnonce(a) : null,
        "chant" => EtatSection(p.Id, 0),
        "bible" => p.Ref is { Length: >= 4 } r ? EtatVerset(r[0], r[1], Math.Max(1, r[2]), Math.Max(1, r[3] > 0 ? r[3] : r[2])) : null,
        _ => null,
    };

    /// <summary>Le dossier Duo comme sur l'écran principal : l'élément 1 dessous, l'élément 2 par-dessus, à leurs mesures.</summary>
    public EtatEcran? EtatDuo(DossierDuo d)
    {
        if (!d.A.Choisi || !d.B.Choisi) return null;
        Borner(d.A); Borner(d.B);
        if (EtatPart(d.B) is not { } second || EtatPart(d.A) is not { } premier) return null;
        return premier with { Duo = new DuoEcran(second with { Duo = null, Presentation = "plein" }, d.Nom, "libre", 0.35) { Dossier = d.Id, Cadres = Cadres(d) } };
    }
}
