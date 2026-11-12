namespace MyVerse;

/// <summary>
/// Écran par défaut (MyChurch 3.3).
/// Le déroulé peut contenir des « écrans principaux » (logo, image, caméra, fenêtre, texte de bienvenue, fond seul) : quand l'un
/// d'eux passe à l'écran, il y reste et devient l'écran par défaut. Dans la colonne de l'écran en direct, on choisit l'écran par
/// défaut parmi eux, le fond seul ou noir. « Retirer de l'écran » (Échap) revient alors direct dessus au lieu de vider l'écran.
/// L'écran par défaut est pour la salle : OBS, la caméra virtuelle et la page publique restent vides, comme avant.
/// </summary>
public sealed partial class Regie
{
    /// <summary>Ce qui était à l'écran avant « Retirer » : Suivant reprend après, Précédent le remet.</summary>
    Cible? cibleRetiree;
    ElementAgenda? elementRetire;
    /// <summary>L'écran par défaut est arrivé par « Retirer » (et non affiché depuis le déroulé).</summary>
    bool defautApresRetrait;

    bool SurDefautApresRetrait => defautApresRetrait && Cible is CibleDefaut;

    void OublierReprise()
    {
        cibleRetiree = null;
        elementRetire = null;
    }

    // ------------------------------------------------------------ le choix
    /// <summary>L'écran par défaut en vigueur : « noir », « fond » ou l'id d'un écran principal du déroulé (noir s'il n'y est plus).</summary>
    public string EcranDefaut
    {
        get
        {
            var d = donnees.Reglages.EcranDefaut;
            if (d is "noir" or "fond") return d;
            return !string.IsNullOrEmpty(d) && ElementDuDeroule(d) is { Type: "principal" } ? d : "noir";
        }
    }

    /// <summary>« Noir », « Fond seul » ou le titre de l'écran principal choisi.</summary>
    public string LibelleEcranDefaut => EcranDefaut switch
    {
        "noir" => "Noir",
        "fond" => "Fond seul",
        var id => ElementDuDeroule(id)?.Titre ?? "Noir",
    };

    /// <summary>Les écrans principaux du déroulé choisi, dans l'ordre du culte.</summary>
    public IReadOnlyList<ElementAgenda> EcransPrincipaux => Agenda.Elements.Where(e => e.Type == "principal").ToList();

    /// <summary>
    /// Choisit l'écran par défaut (colonne de l'écran en direct) : « noir », « fond » ou un écran principal du déroulé.
    /// Si l'écran par défaut est déjà à l'écran (après « Retirer »), ou si l'écran est vide, le nouveau s'affiche tout de suite.
    /// </summary>
    public void ChoisirEcranDefaut(string id)
    {
        if (id is not ("noir" or "fond") && ElementDuDeroule(id) is not { Type: "principal" }) return;
        donnees.Reglages.EcranDefaut = id;
        donnees.SauverReglages();
        if (SurDefautApresRetrait || (Cible == null && Etat.Vide))
        {
            Vider(false);
            if (MontrerDefaut()) defautApresRetrait = true;
            else OublierReprise();
        }
        Signaler();
    }

    /// <summary>
    /// Un écran principal dans le déroulé : une source Display (image, caméra, fenêtre, vidéo), un display texte (sa première diapositive),
    /// ou rien (le fond seul : l'habillage sans texte). Null si la source n'existe pas, est un dossier Duo ou est un texte vide.
    /// </summary>
    public ElementAgenda? AjouterPrincipalAgenda(string? annonceId, int position = -1)
    {
        if (annonceId == null) return Ajouter(new ElementAgenda { Type = "principal", Titre = "Fond seul" }, position);
        if (donnees.Annonces.FirstOrDefault(x => x.Id == annonceId) is not { } a) return null;
        if (a.Source is { Type: "duo" }) { Dire("Un dossier Duo ne peut pas servir d'écran principal."); return null; }
        if (a.Source == null && BlocsTexte(a.Texte).Count == 0) { Dire($"« {a.Titre} » est vide."); return null; }
        return Ajouter(new ElementAgenda
        {
            Type = "principal", Titre = a.Titre.Length > 0 ? a.Titre : "Écran principal",
            Source = a.Source, Texte = a.Source == null ? a.Texte : null,
        }, position);
    }

    /// <summary>« Image », « Caméra », « Texte », « Fond seul »… (déroulé).</summary>
    static string SortePrincipal(ElementAgenda e) => e.Source?.Type switch
    {
        "image" => "Image",
        "camera" => "Caméra",
        "mycam" => "MyCam",
        "fenetre" => "Capture de fenêtre",
        "video" => "Vidéo",
        _ => BlocsTexte(e.Texte).Count > 0 ? "Texte" : "Fond seul",
    };

    // ------------------------------------------------------------ ce qui s'affiche
    /// <summary>L'écran d'un écran principal : son média, la première diapositive de son texte, ou le fond seul.</summary>
    EtatEcran EtatPrincipal(ElementAgenda e, EtatEcran b)
    {
        if (e.Source is { Type: "image" or "camera" or "mycam" or "fenetre" or "video" } s)
            return b with { Mode = "media", Media = s, Lignes = Array.Empty<string>(), Ref = e.Titre, Second = null };
        var blocs = BlocsTexte(e.Texte);
        if (blocs.Count > 0)
            return b with { Mode = "chant", Media = null, Lignes = blocs[0].Split('\n').Where(l => l.Trim().Length > 0).ToArray(), Ref = "", Second = null };
        return EtatFond(b);
    }

    /// <summary>Le fond seul : l'habillage choisi, sans texte.</summary>
    static EtatEcran EtatFond(EtatEcran b) => b with { Mode = "fond", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null };

    /// <summary>
    /// Ce qui part vers OBS, la caméra virtuelle et la page publique : l'écran par défaut est pour la salle, eux restent vides
    /// (le contenu épinglé du Duo reste), comme quand on retirait les paroles avant la 3.3.
    /// </summary>
    public EtatEcran EtatDiffusion(EtatEcran e) =>
        Cible is CibleDefaut ? e with { Mode = "vide", Media = null, Lignes = Array.Empty<string>(), Ref = "", Second = null } : e;

    /// <summary>Un écran principal affiché depuis le déroulé (clic, Suivant) : il reste à l'écran et devient l'écran par défaut.</summary>
    void AfficherPrincipal(ElementAgenda e)
    {
        donnees.Reglages.EcranDefaut = e.Id;
        donnees.SauverReglages();
        Cible = new CibleDefaut(e.Id);
        defautApresRetrait = false;
        OublierReprise();
        // pour la salle : la source OBS reste cachée (sauf contenu épinglé)
        Oeil?.Invoke(Etat.Duo != null);
        Etat = EtatPrincipal(e, Etat);
        Noter("e:" + e.Id, e.Titre, "principal");
        Signaler();
    }

    /// <summary>Met l'écran par défaut sur l'écran vidé (sans signaler). Faux pour « noir » : l'écran reste vide.</summary>
    bool MontrerDefaut()
    {
        var id = EcranDefaut;
        if (id == "noir") return false;
        if (id == "fond") Etat = EtatFond(Etat);
        else if (ElementDuDeroule(id) is { } e) Etat = EtatPrincipal(e, Etat);
        else return false;
        Cible = new CibleDefaut(id);
        ElementCourant = null;
        defautApresRetrait = true;
        return true;
    }

    // ------------------------------------------------------------ reprendre après « Retirer »
    bool EnReprise(out Cible c, out ElementAgenda? el)
    {
        c = cibleRetiree!;
        el = elementRetire;
        return SurDefautApresRetrait && cibleRetiree != null;
    }

    /// <summary>Exécute <paramref name="f"/> comme si ce qui a été retiré était encore à l'écran.</summary>
    T AvecPosition<T>(Cible c, ElementAgenda? el, Func<T> f)
    {
        var (cible, element) = (Cible, ElementCourant);
        Cible = c;
        ElementCourant = el;
        try { return f(); }
        finally { Cible = cible; ElementCourant = element; }
    }

    /// <summary>Sur l'écran par défaut après « Retirer » : Suivant affiche ce qui vient après ce qui a été retiré, Précédent le remet.</summary>
    bool Reprise(int d)
    {
        if (!EnReprise(out var c, out var el)) return false;
        if (d < 0)
        {
            OublierReprise();
            Remettre(c, el);
            return true;
        }
        // ni chant, ni verset, ni texte, ni élément du déroulé (source Display seule) : rien après, l'écran par défaut reste
        if (el == null && c is not (CibleChant or CibleVerset or CibleTexte)) return true;
        var avant = Etat;
        var (cible, element) = (Cible, ElementCourant);
        Cible = c;
        ElementCourant = el;
        BougerDepuis(d);
        if (ReferenceEquals(Etat, avant))
        {
            // fin du déroulé, du chant ou du livre : l'écran par défaut reste
            Cible = cible;
            ElementCourant = element;
        }
        else OublierReprise();
        return true;
    }

    /// <summary>Réaffiche ce qui a été retiré.</summary>
    void Remettre(Cible c, ElementAgenda? el)
    {
        if (el != null && Diapos(el).FirstOrDefault(x => MemeCible(x.Cible, c)) is { } dp)
        {
            AfficherDiapo(el, dp);
            return;
        }
        switch (c)
        {
            case CibleChant cc: AfficherSection(cc.Id, cc.I); break;
            case CibleVerset cv: AfficherVerset(cv.B, cv.C, cv.V1, cv.V2, cv.Part); break;
            case CibleTexte ct when donnees.Annonces.FirstOrDefault(a => a.Id == ct.Element) is { } an: AfficherAnnonce(an, ct.I); break;
            case CibleDisplay cd when donnees.Annonces.FirstOrDefault(a => a.Id == cd.Id) is { } ad: AfficherAnnonce(ad); break;
        }
    }

    /// <summary>
    /// La diapositive du déroulé qui viendra avec Suivant (élément et position), y compris sur l'écran par défaut après « Retirer ».
    /// Null hors du déroulé ou à la fin.
    /// </summary>
    public (ElementAgenda E, int I)? SuivanteDeroule()
    {
        var (c, el) = EnReprise(out var cr, out var er) ? (cr, er) : (Cible, ElementCourant);
        if (c == null || el == null) return null;
        var d = Diapos(el);
        int pos = d.FindIndex(x => MemeCible(x.Cible, c));
        if (pos < 0) return null;
        if (pos + 1 < d.Count) return (el, pos + 1);
        var l = Agenda.Elements;
        for (int k = l.IndexOf(el) + 1; k >= 1 && k < l.Count; k++)
            if (Diapos(l[k]).Count > 0) return (l[k], 0);
        return null;
    }
}
