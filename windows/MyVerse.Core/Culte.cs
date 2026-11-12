using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>
/// Le culte (design « Aube ») : compte à rebours d'accueil, heures prévues, éléments faits,
/// résumé de ce qui a été affiché, bibliothèque d'displays.
/// </summary>
public sealed partial class Regie
{
    /// <summary>L'heure (remplaçable pour les tests).</summary>
    public Func<DateTime> Horloge { get; set; } = () => DateTime.Now;

    // ------------------------------------------------------------ compte à rebours d'accueil
    string? compteElement;
    DateTime? finCompte;

    static string MessageCompte(ElementAgenda e) => string.IsNullOrWhiteSpace(e.Texte) ? "Le culte commence dans" : e.Texte.Trim();

    public ElementAgenda AjouterCompteAgenda(int minutes = 5, string? message = null, int position = 0) =>
        Ajouter(new ElementAgenda
        {
            Type = "compte", Titre = "Accueil", Minutes = Math.Clamp(minutes, 1, 120),
            Texte = string.IsNullOrWhiteSpace(message) ? "Le culte commence dans" : message.Trim(),
        }, position);

    public void ModifierCompteAgenda(string elementId, int minutes, string? message)
    {
        var e = Agenda.Elements.FirstOrDefault(x => x.Id == elementId);
        if (e == null || e.Type != "compte") return;
        e.Minutes = Math.Clamp(minutes, 1, 120);
        e.Texte = string.IsNullOrWhiteSpace(message) ? "Le culte commence dans" : message.Trim();
        if (compteElement == e.Id) finCompte = null;
        AgendaModifie();
    }

    static string Duree(TimeSpan t) => $"{(int)Math.Max(0, t.TotalMinutes):00}:{Math.Max(0, t.Seconds):00}";

    void AfficherCompte(ElementAgenda e)
    {
        var maintenant = Horloge();
        // on reprend le compte en cours ; il recommence s'il était fini ou s'il s'agit d'un autre élément
        if (compteElement != e.Id || finCompte is not DateTime f || f <= maintenant)
        {
            compteElement = e.Id;
            finCompte = maintenant.AddMinutes(e.Minutes ?? 5);
        }
        Cible = new CibleCompte(e.Id);
        Oeil?.Invoke(true);
        Etat = Etat with { Mode = "chant", Media = null, Lignes = new[] { MessageCompte(e), Duree(finCompte!.Value - maintenant) }, Ref = "", Second = null };
        Noter("e:" + e.Id, e.Titre, "compte");
        Signaler();
    }

    /// <summary>À appeler chaque seconde : le compte à rebours à l'écran avance. Vrai s'il a changé.</summary>
    public bool TicCompte()
    {
        if (Cible is not CibleCompte cc || finCompte is not DateTime f) return false;
        var e = Agenda.Elements.FirstOrDefault(x => x.Id == cc.Element);
        var lignes = new[] { e != null ? MessageCompte(e) : Etat.Lignes.FirstOrDefault() ?? "", Duree(f - Horloge()) };
        if (Etat.Lignes.SequenceEqual(lignes)) return false;
        Etat = Etat with { Lignes = lignes };
        Signaler();
        return true;
    }

    // ------------------------------------------------------------ heure prévue
    static readonly Regex R_HEURE = new(@"^\s*(\d{1,2})\s*[:h.]\s*(\d{2})?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>« 10:12 », « 10h12 », « 9h » → « 10:12 », « 09:00 ». Vide ou illisible : null.</summary>
    public static string? LireHeure(string? t)
    {
        var m = R_HEURE.Match(t ?? "");
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), mn = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return h <= 23 && mn <= 59 ? $"{h:00}:{mn:00}" : null;
    }

    public void HeureAgenda(string elementId, string? heure)
    {
        var e = Agenda.Elements.FirstOrDefault(x => x.Id == elementId);
        if (e == null) return;
        e.Heure = LireHeure(heure);
        AgendaModifie();
    }

    // ------------------------------------------------------------ ce qui a été affiché (résumé du culte)
    sealed record Vu(string Cle, string Libelle, DateTime Heure, HashSet<string> Diapos, string Sorte);
    readonly List<Vu> vus = new();

    void Noter(string cle, string libelle, string diapo, string sorte = "")
    {
        var v = vus.FirstOrDefault(x => x.Cle == cle);
        if (v == null) vus.Add(v = new Vu(cle, libelle, Horloge(), new HashSet<string>(), sorte));
        v.Diapos.Add(diapo);
    }

    /// <summary>Note ce qui vient d'être affiché hors des textes (versets et strophes).</summary>
    void NoterAffichage()
    {
        if (ElementCourant is { } el && Cible is { } c)
        {
            var lib = el.Type == "chant" && Trouver(el.ChantId) is { } ch ? ch.Title : el.Titre;
            Noter("e:" + el.Id, lib, c.ToString(), el.Type == "chant" ? "chant" : "");
            return;
        }
        switch (Cible)
        {
            case CibleChant cc when Trouver(cc.Id) is { } ch:
                Noter("c:" + cc.Id, ch.Title, "s" + cc.I, "chant");
                break;
            case CibleVerset cv:
                var lib = Livres.Libelle(cv.B, cv.C, cv.V1, cv.V2);
                Noter("v:" + lib, lib, "p" + cv.Part);
                break;
        }
    }

    /// <summary>Cet élément de l'agenda a déjà été à l'écran (pendant cette séance).</summary>
    public bool EstFait(ElementAgenda e) => vus.Any(v => v.Cle == "e:" + e.Id);

    /// <summary>L'heure où l'élément est passé à l'écran pour la première fois.</summary>
    public DateTime? HeureAffiche(ElementAgenda e) => vus.FirstOrDefault(v => v.Cle == "e:" + e.Id)?.Heure;

    /// <summary>« Accueil · Yera, Yera, Yera (4 couplets) · Jean 3:16 »</summary>
    public string Resume() => string.Join(" · ", vus.Select(v =>
        v.Sorte == "chant" && v.Diapos.Count > 1 ? $"{v.Libelle} ({v.Diapos.Count} couplets)" : v.Libelle));

    /// <summary>Le résumé à envoyer (WhatsApp) : une ligne par élément, dans l'ordre.</summary>
    public string ResumePartage()
    {
        var j = Horloge();
        var t = $"{Agenda.Nom} — ce qui a été affiché :\n";
        foreach (var v in vus)
            t += "• " + (v.Sorte == "chant" && v.Diapos.Count > 1 ? $"{v.Libelle} ({v.Diapos.Count} couplets)" : v.Libelle) + "\n";
        return t.TrimEnd() + (vus.Count == 0 ? "\n(rien pour l'instant)" : "") + $"\n\nMyVerse · {j:dd/MM/yyyy}";
    }

    /// <summary>Le déroulé du culte à envoyer (WhatsApp) : heure, type, titre.</summary>
    public string DeroulePartage()
    {
        var t = $"{Agenda.Nom}\n";
        int i = 1;
        foreach (var e in Agenda.Elements)
        {
            var (type, sous) = Description(e);
            var titre = e.Type == "chant" && Trouver(e.ChantId) is { } c ? c.Title : e.Titre;
            t += $"{i++}. {(e.Heure != null ? e.Heure + " — " : "")}{titre} ({type.ToLowerInvariant()}{(e.Type == "chant" ? " · " + sous.Split(" · ")[0] : "")})\n";
        }
        return t.TrimEnd();
    }

    public void OublierResume() => vus.Clear();

    // ------------------------------------------------------------ bibliothèque d'displays
    public IReadOnlyList<Annonce> Annonces => donnees.Annonces;
    public event Action? AnnoncesChange;

    void AnnoncesModifiees()
    {
        donnees.SauverAnnonces();
        try { AnnoncesChange?.Invoke(); } catch (Exception e) { Journal.Erreur("annonces", e); }
    }

    public Annonce NouvelleAnnonce(string titre, string texte)
    {
        var a = new Annonce { Titre = string.IsNullOrWhiteSpace(titre) ? "Annonce" : titre.Trim(), Texte = (texte ?? "").Replace("\r\n", "\n").Trim() };
        donnees.Annonces.Add(a);
        AnnoncesModifiees();
        return a;
    }

    public void ModifierAnnonce(string id, string titre, string texte)
    {
        var a = donnees.Annonces.FirstOrDefault(x => x.Id == id);
        if (a == null) return;
        a.Titre = string.IsNullOrWhiteSpace(titre) ? "Annonce" : titre.Trim();
        a.Texte = (texte ?? "").Replace("\r\n", "\n").Trim();
        AnnoncesModifiees();
    }

    public void SupprimerAnnonce(string id)
    {
        if (donnees.Annonces.RemoveAll(x => x.Id == id) > 0) AnnoncesModifiees();
    }

    /// <summary>L'display entre dans le déroulé (une copie : on peut la retoucher pour ce culte).</summary>
    public ElementAgenda? AjouterAnnonceAgenda(string id, int position = -1) =>
        donnees.Annonces.FirstOrDefault(x => x.Id == id) is { } a
            ? a.Source != null ? Ajouter(new ElementAgenda { Type = "display", Titre = a.Titre, Source = a.Source }, position)
              : AjouterTexteAgenda(a.Titre, a.Texte, position) : null;

    public Annonce NouveauDisplay(string titre, SourceDisplay source)
    {
        if (source.Type is not ("image" or "camera" or "mycam" or "fenetre" or "video")) throw new ArgumentException("Source inconnue.");
        var a = new Annonce { Titre = titre.Trim(), Source = source };
        donnees.Annonces.Add(a);
        AnnoncesModifiees();
        return a;
    }

    void AfficherSource(string id, string titre, SourceDisplay? source)
    {
        if (source == null) { Dire("Cette source Display est absente."); return; }
        if (source.Type == "duo")
        {
            if (donnees.Duos.FirstOrDefault(x => x.Id == source.Chemin) is { } dossier) AfficherDuo(dossier);
            else Dire($"Le duo « {titre} » n'existe plus.");
            return;
        }
        Cible = new CibleDisplay(id);
        Oeil?.Invoke(true);
        Etat = Etat with { Mode = "media", Media = source, Lignes = Array.Empty<string>(), Ref = titre, Second = null };
        Noter("d:" + id, titre, source.Type);
        Signaler();
    }

    /// <summary>Met (ou retire) une source dans la grille de Display.</summary>
    public void GrilleAnnonce(string id, bool dans)
    {
        var a = donnees.Annonces.FirstOrDefault(x => x.Id == id);
        if (a == null || a.Grille == dans) return;
        a.Grille = dans;
        AnnoncesModifiees();
    }

    /// <summary>Le texte de chaque diapositive d'un display texte.</summary>
    public IReadOnlyList<string> TextesAnnonce(Annonce a) => a.Source != null ? new[] { "" } : BlocsTexte(a.Texte);

    public int DiaposAnnonce(Annonce a) => a.Source != null ? 1 : BlocsTexte(a.Texte).Count;

    public void AfficherAnnonce(Annonce a, int i = 0)
    {
        if (a.Source != null) { ElementCourant = null; AfficherSource(a.Id, a.Titre, a.Source); return; }
        var b = BlocsTexte(a.Texte);
        if (b.Count == 0) { Dire($"« {a.Titre} » est vide."); return; }
        i = Math.Clamp(i, 0, b.Count - 1);
        ElementCourant = null;
        Cible = new CibleTexte(a.Id, i);
        Oeil?.Invoke(true);
        Etat = Etat with { Mode = "chant", Media = null, Lignes = b[i].Split('\n').Where(l => l.Trim().Length > 0).ToArray(), Ref = "", Second = null };
        Noter("a:" + a.Id, a.Titre, "t" + i);
        Signaler();
    }

    /// <summary>Suivant / précédent dans un display affichée hors du déroulé.</summary>
    bool BougerAnnonce(int d)
    {
        if (ElementCourant != null || Cible is not CibleTexte ct || donnees.Annonces.FirstOrDefault(a => a.Id == ct.Element) is not { } an) return false;
        int j = ct.I + d;
        if (j >= 0 && j < BlocsTexte(an.Texte).Count) AfficherAnnonce(an, j);
        return true;
    }

    Suivante? ProchaineAnnonce()
    {
        if (ElementCourant != null || Cible is not CibleTexte ct || donnees.Annonces.FirstOrDefault(a => a.Id == ct.Element) is not { } an) return null;
        var b = BlocsTexte(an.Texte);
        return ct.I + 1 < b.Count ? new Suivante(new CibleTexte(an.Id, ct.I + 1), $"{an.Titre} · {ct.I + 2}/{b.Count}", b[ct.I + 1]) : null;
    }
}
