using System.Text.Json.Serialization;

namespace MyVerse;

/// <summary>Un des deux éléments d'un dossier Duo : une source Display, un chant ou un passage, avec sa place à l'écran.</summary>
public sealed class PartDuo
{
    /// <summary>"display", "chant", "bible" ; vide tant que rien n'est choisi.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("ref"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int[]? Ref { get; set; }
    [JsonPropertyName("titre")] public string Titre { get; set; } = "";
    /// <summary>Coin haut gauche et largeur, en parts de l'écran (la hauteur suit : même forme que l'écran).</summary>
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("l")] public double L { get; set; } = 0.5;
    [JsonIgnore] public bool Choisi => Type.Length > 0;
}

/// <summary>Un dossier de l'onglet Duo : deux éléments affichés ensemble.</summary>
public sealed class DossierDuo
{
    [JsonPropertyName("id")] public string Id { get; set; } = Chant.NouvelId();
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("a")] public PartDuo A { get; set; } = new() { X = 0, Y = 0.25, L = 0.5 };
    [JsonPropertyName("b")] public PartDuo B { get; set; } = new() { X = 0.5, Y = 0.25, L = 0.5 };
    [JsonIgnore] public string Description => (A.Choisi ? A.Titre : "(vide)") + "  +  " + (B.Choisi ? B.Titre : "(vide)");
}

public sealed partial class Regie
{
    public IReadOnlyList<DossierDuo> Duos => donnees.Duos;
    public event Action? DuosChange;

    public DossierDuo NouveauDuo(string nom)
    {
        var d = new DossierDuo { Nom = string.IsNullOrWhiteSpace(nom) ? "Duo " + (donnees.Duos.Count + 1) : nom.Trim() };
        donnees.Duos.Add(d);
        donnees.SauverDuos();
        DuosChange?.Invoke();
        return d;
    }

    static void Borner(PartDuo p)
    {
        p.L = Math.Clamp(p.L, 0.1, 1);
        p.X = Math.Clamp(p.X, 0, 1 - p.L);
        p.Y = Math.Clamp(p.Y, 0, 1 - p.L);
    }

    static double[] Cadres(DossierDuo d) => new[] { d.A.X, d.A.Y, d.A.L, d.B.X, d.B.Y, d.B.L };

    /// <summary>Enregistre un dossier modifié ; s'il est à l'écran, ses mesures s'appliquent tout de suite.</summary>
    public void EnregistrerDuo(DossierDuo d, bool liste = true)
    {
        Borner(d.A); Borner(d.B);
        donnees.SauverDuos();
        bool annonces = false;
        foreach (var a in donnees.Annonces.Where(a => a.Source is { Type: "duo" } s && s.Chemin == d.Id && a.Titre != d.Nom)) { a.Titre = d.Nom; annonces = true; }
        if (annonces) AnnoncesModifiees();
        if (liste) DuosChange?.Invoke();
        if (Etat.Duo is { Place: "libre" } e && e.Dossier == d.Id)
        {
            Etat = Etat with { Duo = e with { Libelle = d.Nom, Cadres = Cadres(d) } };
            Signaler();
        }
    }

    public void SupprimerDuo(string id)
    {
        if (donnees.Duos.RemoveAll(d => d.Id == id) == 0) return;
        donnees.SauverDuos();
        if (donnees.Annonces.RemoveAll(a => a.Source is { Type: "duo" } s && s.Chemin == id) > 0) AnnoncesModifiees();
        DuosChange?.Invoke();
        if (Etat.Duo is { Place: "libre" } e && e.Dossier == id) Effacer();
    }

    /// <summary>Met le dossier dans Display (bibliothèque et grille) : un clic l'envoie en direct.</summary>
    public Annonce AjouterDuoDisplay(DossierDuo d)
    {
        var a = donnees.Annonces.FirstOrDefault(x => x.Source is { Type: "duo" } s && s.Chemin == d.Id);
        if (a == null)
        {
            a = new Annonce { Titre = d.Nom, Source = new SourceDisplay { Type = "duo", Chemin = d.Id } };
            donnees.Annonces.Add(a);
        }
        a.Titre = d.Nom;
        a.Grille = true;
        AnnoncesModifiees();
        return a;
    }

    bool AfficherPart(PartDuo p)
    {
        Cible = null;
        switch (p.Type)
        {
            case "display":
                if (donnees.Annonces.FirstOrDefault(a => a.Id == p.Id) is { } a && a.Source?.Type != "duo") AfficherAnnonce(a);
                break;
            case "chant":
                AfficherSection(p.Id, 0);
                break;
            case "bible":
                if (p.Ref is { Length: >= 4 } r) AfficherVerset(r[0], r[1], Math.Max(1, r[2]), Math.Max(1, r[3] > 0 ? r[3] : r[2]));
                break;
        }
        return Cible != null && !Etat.Vide;
    }

    /// <summary>Affiche les deux éléments du dossier ensemble. « Suivant » fait avancer le premier.</summary>
    public bool AfficherDuo(DossierDuo d)
    {
        if (!d.A.Choisi || !d.B.Choisi) { Dire($"Le duo « {d.Nom} » n'a pas encore ses deux éléments."); return false; }
        Borner(d.A); Borner(d.B);
        var avant = Etat;
        var cibleAvant = Cible;
        bool ok = false;
        muet = true;
        dansDuo = true;
        try
        {
            Etat = Etat with { Duo = null };
            if (AfficherPart(d.B))
            {
                var second = Etat with { Duo = null, Presentation = "plein" };
                Etat = Etat with { Duo = new DuoEcran(second, d.Nom, "libre", 0.35) { Dossier = d.Id, Cadres = Cadres(d) } };
                ok = AfficherPart(d.A);
            }
            if (!ok) { Etat = avant; Cible = cibleAvant; }
            else ElementCourant = null;
        }
        finally { muet = false; }
        try
        {
        if (!ok) { Dire($"Un élément du duo « {d.Nom} » n'existe plus : choisissez-le à nouveau dans l'onglet Duo."); return false; }
        Oeil?.Invoke(true);
        Signaler();
        return true;
        }
        finally { dansDuo = false; }
    }
    bool dansDuo;
    Cible? cibleDuo;

    /// <summary>Autre chose que le premier élément du dossier arrive à l'écran : le dossier s'en va.</summary>
    void LacherDuoLibre()
    {
        if (Etat.Duo is not { Place: "libre" } e) return;
        var d = donnees.Duos.FirstOrDefault(x => x.Id == e.Dossier);
        // un clic sur une autre source, ou sur l'élément seul, remplace le dossier ; seul Suivant / Précédent le garde
        // rien de nouveau à l'écran (fond, taille, mesures) : le dossier reste
        if (ReferenceEquals(Cible, cibleDuo)) return;
        bool meme = (avance || dansDuo) && d != null && !Etat.Vide && d.A.Type switch
        {
            "chant" => Cible is CibleChant c && c.Id == d.A.Id,
            "bible" => Cible is CibleVerset v && d.A.Ref is { Length: >= 2 } r && v.B == r[0] && v.C == r[1],
            "display" => Cible is CibleTexte t && t.Element == d.A.Id || Cible is CibleDisplay s && s.Id == d.A.Id,
            _ => false,
        };
        if (meme) cibleDuo = Cible;
        else { Etat = Etat with { Duo = null }; cibleDuo = null; }
    }
}
