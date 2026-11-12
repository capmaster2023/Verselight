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

    /// <summary>
    /// Une diapositive (du déroulé, d'un chant, d'un verset, d'une source Display) telle qu'elle sortirait à l'écran, sans l'afficher :
    /// vignettes de la page Culte et aperçu « Suivant » (MyChurch 3.2). <paramref name="e"/> : l'élément du déroulé, s'il est connu.
    /// </summary>
    public EtatEcran? EtatCible(Cible? c, ElementAgenda? e = null)
    {
        switch (c)
        {
            case CibleChant cc:
                return EtatSection(cc.Id, cc.I);
            case CibleVerset cv:
                if (cv.Parts > 1 && Morceaux(cv.B, cv.C, cv.V1) is { } m)
                    return Neutre() with
                    {
                        Mode = "verset",
                        Lignes = new[] { m[Math.Clamp(cv.Part, 0, m.Count - 1)] },
                        Ref = Livres.Libelle(cv.B, cv.C, cv.V1, cv.V1) + $" ({Bibles.Etiquette()})",
                    };
                return EtatVerset(cv.B, cv.C, cv.V1, cv.V2);
            case CibleTexte ct:
                if ((e?.Id == ct.Element ? e : ElementDuDeroule(ct.Element)) is { } et)
                {
                    var blocs = BlocsTexte(et.Texte);
                    return ct.I >= 0 && ct.I < blocs.Count ? Neutre() with { Mode = "chant", Lignes = blocs[ct.I].Split('\n').Where(l => l.Trim().Length > 0).ToArray() } : null;
                }
                return donnees.Annonces.FirstOrDefault(a => a.Id == ct.Element) is { } an ? EtatAnnonce(an, ct.I) : null;
            case CibleCompte co:
                return (e?.Id == co.Element ? e : ElementDuDeroule(co.Element)) is { } ec
                    ? Neutre() with { Mode = "chant", Lignes = new[] { MessageCompte(ec), $"{Math.Clamp(ec.Minutes ?? 5, 1, 120):00}:00" } }
                    : null;
            case CibleDisplay cd:
                if ((e?.Id == cd.Id ? e : ElementDuDeroule(cd.Id)) is { } ed)
                {
                    if (ed.Source is not { } s) return null;
                    if (s.Type == "duo") return donnees.Duos.FirstOrDefault(d => d.Id == s.Chemin) is { } dossier ? EtatDuo(dossier) : null;
                    return Neutre() with { Mode = "media", Media = s, Ref = ed.Titre };
                }
                return donnees.Annonces.FirstOrDefault(a => a.Id == cd.Id) is { } ad ? EtatAnnonce(ad) : null;
            case CibleDefaut cf:
                if (cf.Id == "fond") return EtatFond(Neutre());
                return (e?.Id == cf.Id ? e : ElementDuDeroule(cf.Id)) is { Type: "principal" } ep ? EtatPrincipal(ep, Neutre()) : null;
        }
        return null;
    }

    ElementAgenda? ElementDuDeroule(string id) => Agenda.Elements.FirstOrDefault(x => x.Id == id);

    /// <summary>Le dossier Duo comme sur l'écran principal : l'élément 1 dessous, l'élément 2 par-dessus, à leurs mesures.</summary>
    public EtatEcran? EtatDuo(DossierDuo d)
    {
        if (!d.A.Choisi || !d.B.Choisi) return null;
        Borner(d.A); Borner(d.B);
        if (EtatPart(d.B) is not { } second || EtatPart(d.A) is not { } premier) return null;
        return premier with { Duo = new DuoEcran(second with { Duo = null, Presentation = "plein" }, d.Nom, "libre", 0.35) { Dossier = d.Id, Cadres = Cadres(d) } };
    }
}
