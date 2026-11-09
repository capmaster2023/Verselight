using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyVerse;

/// <summary>Un élément de l'agenda : un chant, un passage de la Bible ou un texte (display, prière…).</summary>
public sealed class ElementAgenda
{
    [JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SourceDisplay? Source { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = Chant.NouvelId();
    [JsonPropertyName("type")] public string Type { get; set; } = "texte"; // chant, bible, texte
    [JsonPropertyName("titre")] public string Titre { get; set; } = "";
    [JsonPropertyName("chant"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ChantId { get; set; }
    /// <summary>Livre, chapitre, premier et dernier verset (0, 0 : chapitre entier).</summary>
    [JsonPropertyName("ref"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int[]? Ref { get; set; }
    /// <summary>Texte libre : une diapositive par paragraphe (ligne vide entre deux). Compte à rebours : le message.</summary>
    [JsonPropertyName("texte"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Texte { get; set; }
    /// <summary>Heure prévue (« 10:12 »), facultative.</summary>
    [JsonPropertyName("heure"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Heure { get; set; }
    /// <summary>Compte à rebours : durée en minutes.</summary>
    [JsonPropertyName("minutes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Minutes { get; set; }
}

/// <summary>Une display de la bibliothèque (onglet Display) : titre et texte, une diapositive par paragraphe.</summary>
public sealed record SourceDisplay
{
    [JsonPropertyName("type")] public string Type { get; init; } = "image";
    [JsonPropertyName("chemin")] public string Chemin { get; init; } = "";
    [JsonPropertyName("peripherique")] public string Peripherique { get; init; } = "";
    [JsonPropertyName("fenetre")] public long Fenetre { get; init; }
    [JsonPropertyName("processus")] public int Processus { get; init; }
}

public sealed record CibleDisplay(string Id) : Cible;

public sealed class Annonce
{
    [JsonPropertyName("id")] public string Id { get; set; } = Chant.NouvelId();
    [JsonPropertyName("titre")] public string Titre { get; set; } = "";
    [JsonPropertyName("texte")] public string Texte { get; set; } = "";
    [JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SourceDisplay? Source { get; set; }
    [JsonIgnore] public string Description => Source?.Type switch { "camera" => "Périphérique de capture vidéo · " + Source.Peripherique, "fenetre" => "Capture de fenêtre · " + Titre, "image" => "Image · " + Path.GetFileName(Source.Chemin), _ => Texte };
}

/// <summary>Un agenda (comme dans VideoPsalm) : la liste préparée d'un culte.</summary>
public sealed class Agenda
{
    [JsonPropertyName("id")] public string Id { get; set; } = Chant.NouvelId();
    [JsonPropertyName("nom")] public string Nom { get; set; } = "";
    [JsonPropertyName("elements")] public List<ElementAgenda> Elements { get; set; } = new();
}

/// <summary>Une diapositive d'un texte de l'agenda (ou d'un display).</summary>
public sealed record CibleTexte(string Element, int I) : Cible;

/// <summary>Le compte à rebours d'accueil (« Le culte commence dans 05:00 »).</summary>
public sealed record CibleCompte(string Element) : Cible;

/// <summary>Une diapositive d'un élément de l'agenda.</summary>
public sealed record DiapoAgenda(string Label, string Texte, Cible Cible);

/// <summary>Un résultat de la barre de commande (Ctrl K).</summary>
public sealed record ResultatCommande(string Type, string Titre, string Sous, Ref? Ref = null, string? ChantId = null, int Minutes = 0, string? Action = null);

/// <summary>Une référence trouvée dans des notes de prédication, vérifiée dans la Bible.</summary>
public sealed record RefNotes(Ref Ref, string Libelle, string Apercu);

public sealed partial class Regie
{
    static readonly string[] Jours = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };
    static readonly string[] Mois = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };

    /// <summary>« Culte du dimanche 15 novembre » : le prochain dimanche (aujourd'hui si c'est dimanche).</summary>
    public static string NomAgendaParDefaut(DateTime? jour = null)
    {
        var j = (jour ?? DateTime.Today).Date;
        var d = j.AddDays(((int)DayOfWeek.Sunday - (int)j.DayOfWeek + 7) % 7);
        return $"Culte du {Jours[(int)d.DayOfWeek]} {d.Day} {Mois[d.Month - 1]}";
    }

    /// <summary>L'agenda change (éléments, nom, agenda choisi).</summary>
    public event Action? AgendaChange;
    /// <summary>L'élément de l'agenda en train d'être projeté (null si l'écran montre autre chose).</summary>
    public ElementAgenda? ElementCourant { get; private set; }
    bool depuisAgenda;

    public IReadOnlyList<Agenda> Agendas => donnees.Agendas;

    /// <summary>L'agenda choisi (créé au besoin).</summary>
    public Agenda Agenda
    {
        get
        {
            var a = donnees.Agendas.FirstOrDefault(x => x.Id == donnees.Reglages.Agenda) ?? donnees.Agendas.FirstOrDefault();
            if (a == null)
            {
                a = new Agenda { Nom = NomAgendaParDefaut() };
                donnees.Agendas.Add(a);
                donnees.SauverAgendas();
            }
            if (donnees.Reglages.Agenda != a.Id) { donnees.Reglages.Agenda = a.Id; donnees.SauverReglages(); }
            return a;
        }
    }

    void AgendaModifie()
    {
        donnees.SauverAgendas();
        try { AgendaChange?.Invoke(); } catch (Exception e) { Journal.Erreur("agenda", e); }
    }

    public void ChoisirAgenda(string id)
    {
        if (donnees.Agendas.All(a => a.Id != id)) return;
        donnees.Reglages.Agenda = id;
        donnees.SauverReglages();
        AgendaModifie();
    }

    public Agenda NouvelAgenda(string? nom = null)
    {
        var a = new Agenda { Nom = string.IsNullOrWhiteSpace(nom) ? NomAgendaParDefaut() : nom.Trim() };
        donnees.Agendas.Add(a);
        donnees.Reglages.Agenda = a.Id;
        donnees.SauverReglages();
        AgendaModifie();
        return a;
    }

    public void RenommerAgenda(string id, string nom)
    {
        var a = donnees.Agendas.FirstOrDefault(x => x.Id == id);
        if (a == null || string.IsNullOrWhiteSpace(nom)) return;
        a.Nom = nom.Trim();
        AgendaModifie();
    }

    public void SupprimerAgenda(string id)
    {
        var a = donnees.Agendas.FirstOrDefault(x => x.Id == id);
        if (a == null) return;
        donnees.Agendas.Remove(a);
        if (ElementCourant != null && a.Elements.Contains(ElementCourant)) ElementCourant = null;
        if (donnees.Reglages.Agenda == id) { donnees.Reglages.Agenda = donnees.Agendas.FirstOrDefault()?.Id ?? ""; donnees.SauverReglages(); }
        AgendaModifie();
    }

    ElementAgenda Ajouter(ElementAgenda e, int position = -1)
    {
        var l = Agenda.Elements;
        if (position < 0 || position > l.Count) l.Add(e); else l.Insert(position, e);
        AgendaModifie();
        return e;
    }

    public ElementAgenda? AjouterChantAgenda(string chantId, int position = -1)
    {
        var c = Trouver(chantId);
        if (c == null) return null;
        return Ajouter(new ElementAgenda { Type = "chant", ChantId = c.Id, Titre = c.Title }, position);
    }

    /// <summary>Passage de la Bible (V1 = 0 : chapitre entier).</summary>
    public ElementAgenda AjouterBibleAgenda(Ref r, int position = -1)
    {
        int v2 = r.V1 > 0 ? Math.Max(r.V1, r.V2) : 0;
        return Ajouter(new ElementAgenda { Type = "bible", Ref = new[] { r.B, r.C, r.V1, v2 }, Titre = Livres.Libelle(r.B, r.C, r.V1, v2) }, position);
    }

    public ElementAgenda AjouterTexteAgenda(string titre, string texte, int position = -1) =>
        Ajouter(new ElementAgenda { Type = "texte", Titre = string.IsNullOrWhiteSpace(titre) ? "Texte" : titre.Trim(), Texte = (texte ?? "").Replace("\r\n", "\n").Trim() }, position);

    public void ModifierTexteAgenda(string elementId, string titre, string texte)
    {
        var e = Agenda.Elements.FirstOrDefault(x => x.Id == elementId);
        if (e == null || e.Type != "texte") return;
        e.Titre = string.IsNullOrWhiteSpace(titre) ? "Texte" : titre.Trim();
        e.Texte = (texte ?? "").Replace("\r\n", "\n").Trim();
        AgendaModifie();
    }

    public void RetirerAgenda(string elementId)
    {
        var l = Agenda.Elements;
        var e = l.FirstOrDefault(x => x.Id == elementId);
        if (e == null) return;
        l.Remove(e);
        if (ElementCourant == e) ElementCourant = null;
        AgendaModifie();
    }

    public void DeplacerAgenda(string elementId, int d)
    {
        var l = Agenda.Elements;
        int i = l.FindIndex(x => x.Id == elementId), j = i + d;
        if (i < 0 || j < 0 || j >= l.Count) return;
        (l[i], l[j]) = (l[j], l[i]);
        AgendaModifie();
    }

    public void ViderAgenda()
    {
        Agenda.Elements.Clear();
        ElementCourant = null;
        AgendaModifie();
    }

    /// <summary>« CHANT », « BIBLE », « TEXTE » et de quoi il s'agit (recueil, versets…).</summary>
    public (string Type, string Sous) Description(ElementAgenda e) => e.Type switch
    {
        "display" => ("DISPLAY", e.Source?.Type == "camera" ? "Périphérique vidéo" : e.Source?.Type == "fenetre" ? "Capture de fenêtre" : "Image"),
        "chant" => ("CHANT", Trouver(e.ChantId) is { } c ? (c.Author.Length > 0 ? c.Author : "Sans recueil") + $" · {c.Sections.Count} strophe{(c.Sections.Count > 1 ? "s" : "")}" : "chant supprimé"),
        "bible" => ("BIBLE", BorneRef(e) is { } r ? $"{r.V2 - r.V1 + 1} verset{(r.V2 > r.V1 ? "s" : "")} · {Bibles.Etiquette()}" : "absent de cette Bible"),
        "compte" => ("ACCUEIL", $"Compte à rebours · {e.Minutes ?? 5} min"),
        _ => ("TEXTE", $"{BlocsTexte(e.Texte).Count} diapo{(BlocsTexte(e.Texte).Count > 1 ? "s" : "")}"),
    };

    static List<string> BlocsTexte(string? t) =>
        Regex.Split((t ?? "").Replace("\r\n", "\n"), @"\n\s*\n").Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

    /// <summary>Le passage d'un élément Bible, borné à la Bible affichée (chapitre entier : tous ses versets).</summary>
    Ref? BorneRef(ElementAgenda e)
    {
        if (e.Ref is not { Length: >= 2 } x) return null;
        var a = B.Ajuster(new Ref(x[0], x[1], x.Length > 2 ? x[2] : 0, x.Length > 3 ? x[3] : 0));
        int n = B.Versets(a.B, a.C).Count;
        if (n == 0) return null;
        int v1 = a.V1 > 0 ? Math.Min(a.V1, n) : 1;
        int v2 = a.V1 > 0 ? Math.Min(Math.Max(a.V2, v1), n) : n;
        return new Ref(a.B, a.C, v1, v2);
    }

    /// <summary>Les diapositives d'un élément : strophes, versets (et morceaux ✂), paragraphes.</summary>
    public List<DiapoAgenda> Diapos(ElementAgenda e)
    {
        var l = new List<DiapoAgenda>();
        switch (e.Type)
        {
            case "display":
                l.Add(new DiapoAgenda(e.Titre, e.Titre, new CibleDisplay(e.Id)));
                break;
            case "chant":
                if (Trouver(e.ChantId) is { } c)
                    for (int i = 0; i < c.Sections.Count; i++)
                        l.Add(new DiapoAgenda(c.Sections[i].Label.Length > 0 ? c.Sections[i].Label : $"Strophe {i + 1}", c.Sections[i].Text, new CibleChant(c.Id, i)));
                break;
            case "bible":
                if (BorneRef(e) is { } r)
                {
                    var vs = B.Versets(r.B, r.C);
                    for (int v = r.V1; v <= r.V2; v++)
                    {
                        if (string.IsNullOrWhiteSpace(vs[v - 1])) continue;
                        if (Morceaux(r.B, r.C, v) is { } m)
                            for (int k = 0; k < m.Count; k++)
                                l.Add(new DiapoAgenda($"{Livres.Libelle(r.B, r.C, v)} · {k + 1}/{m.Count}", m[k], new CibleVerset(r.B, r.C, v, v, k, m.Count)));
                        else l.Add(new DiapoAgenda(Livres.Libelle(r.B, r.C, v), vs[v - 1], new CibleVerset(r.B, r.C, v, v)));
                    }
                }
                break;
            case "compte":
                l.Add(new DiapoAgenda("Compte à rebours", $"{MessageCompte(e)}\n{e.Minutes ?? 5:00}:00", new CibleCompte(e.Id)));
                break;
            default:
                var b = BlocsTexte(e.Texte);
                for (int i = 0; i < b.Count; i++) l.Add(new DiapoAgenda(b.Count > 1 ? $"{e.Titre} · {i + 1}/{b.Count}" : e.Titre, b[i], new CibleTexte(e.Id, i)));
                break;
        }
        return l;
    }

    /// <summary>Position de la diapositive à l'écran dans cet élément (null si l'écran montre autre chose).</summary>
    public int? PositionAgenda(ElementAgenda e)
    {
        var c = Cible;
        if (c == null) return null;
        var d = Diapos(e);
        for (int i = 0; i < d.Count; i++)
        {
            bool ok = (d[i].Cible, c) switch
            {
                (CibleChant a, CibleChant b) => a.Id == b.Id && a.I == b.I,
                (CibleVerset a, CibleVerset b) => a.B == b.B && a.C == b.C && a.V1 == b.V1 && b.V1 == b.V2 && a.Part == b.Part,
                (CibleTexte a, CibleTexte b) => a.Element == b.Element && a.I == b.I,
                (CibleCompte a, CibleCompte b) => a.Element == b.Element,
                (CibleDisplay a, CibleDisplay b) => a.Id == b.Id,
                _ => false,
            };
            if (ok) return i;
        }
        return null;
    }

    /// <summary>Affiche la diapositive i d'un élément de l'agenda.</summary>
    public void AfficherElement(ElementAgenda e, int i = 0)
    {
        var d = Diapos(e);
        if (d.Count == 0) { Dire($"« {e.Titre} » : rien à afficher."); return; }
        AfficherDiapo(e, d[Math.Clamp(i, 0, d.Count - 1)]);
    }

    void AfficherDiapo(ElementAgenda e, DiapoAgenda x)
    {
        depuisAgenda = true;
        ElementCourant = e;
        try
        {
            switch (x.Cible)
            {
                case CibleChant cc: AfficherSection(cc.Id, cc.I); break;
                case CibleVerset cv:
                    Selection = new Ref(cv.B, cv.C, cv.V1, cv.V1);
                    AfficherVerset(cv.B, cv.C, cv.V1, cv.V1, cv.Part);
                    break;
                case CibleTexte ct: AfficherTexte(e, ct.I); break;
                case CibleCompte: AfficherCompte(e); break;
                case CibleDisplay: AfficherSource(e.Id, e.Titre, e.Source); break;
            }
        }
        finally { depuisAgenda = false; }
    }

    void AfficherTexte(ElementAgenda e, int i)
    {
        var b = BlocsTexte(e.Texte);
        if (i < 0 || i >= b.Count) return;
        Cible = new CibleTexte(e.Id, i);
        Oeil?.Invoke(true);
        Etat = Etat with
        {
            Mode = "chant", Media = null,
            Lignes = b[i].Split('\n').Where(l => l.Trim().Length > 0).ToArray(),
            Ref = "",
            Second = null,
        };
        Noter("e:" + e.Id, e.Titre, "t" + i);
        Signaler();
    }

    /// <summary>Suivant / précédent dans l'agenda : diapositive suivante, puis l'élément suivant. Faux hors agenda.</summary>
    bool BougerAgenda(int d)
    {
        if (ElementCourant is not { } el || PositionAgenda(el) is not int pos) return false;
        var diapos = Diapos(el);
        int j = pos + d;
        if (j >= 0 && j < diapos.Count) { AfficherDiapo(el, diapos[j]); return true; }
        var l = Agenda.Elements;
        int pas = d > 0 ? 1 : -1;
        for (int k = l.IndexOf(el) + pas; k >= 0 && k < l.Count && l.IndexOf(el) >= 0; k += pas)
        {
            var d2 = Diapos(l[k]);
            if (d2.Count > 0) { AfficherDiapo(l[k], pas > 0 ? d2[0] : d2[^1]); return true; }
        }
        return true; // début ou fin de l'agenda : on reste
    }

    /// <summary>Ce qui vient après dans l'agenda (null hors agenda ; Suivante vide à la fin).</summary>
    (bool Agenda, Suivante? S) ProchaineAgenda()
    {
        if (ElementCourant is not { } el || PositionAgenda(el) is not int pos) return (false, null);
        var diapos = Diapos(el);
        if (pos + 1 < diapos.Count) return (true, new Suivante(diapos[pos + 1].Cible, diapos[pos + 1].Label, diapos[pos + 1].Texte));
        var l = Agenda.Elements;
        int i = l.IndexOf(el);
        for (int k = i + 1; i >= 0 && k < l.Count; k++)
        {
            var d2 = Diapos(l[k]);
            if (d2.Count > 0) return (true, new Suivante(d2[0].Cible, "Agenda › " + l[k].Titre, d2[0].Texte));
        }
        return (true, null);
    }

    // ------------------------------------------------------------ notes de prédication
    /// <summary>Les références trouvées dans des notes, vérifiées dans la Bible affichée (sans doublon, dans l'ordre).</summary>
    public List<RefNotes> RefsDansNotes(string? notes)
    {
        var res = new List<RefNotes>();
        foreach (var t in Livres.Trouver(notes))
        {
            if (BorneRef(new ElementAgenda { Ref = new[] { t.Ref.B, t.Ref.C, t.Ref.V1, t.Ref.V2 } }) is not { } r) continue;
            bool entier = t.Ref.V1 == 0;
            var lib = Livres.Libelle(r.B, r.C, entier ? 0 : r.V1, entier ? 0 : r.V2);
            if (res.Any(x => x.Libelle == lib)) continue;
            var vs = B.Versets(r.B, r.C);
            res.Add(new RefNotes(entier ? new Ref(r.B, r.C, 0, 0) : r, lib, vs[r.V1 - 1]));
        }
        return res;
    }

    // ------------------------------------------------------------ barre de commande (Ctrl K)
    static readonly Regex R_MINUTEUR = new(@"^(?:minuteur|minut|chrono|timer|minute)\s*(\d{1,3})?\s*(?:min|mn|m)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex R_CHANT = new(@"^(?:chant|cantique|c|n|no|n°|#)\s*(\d{1,5})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Ce que propose la barre de commande : « jean 3 16 » (Bible), « chant 16 » ou « 16 » (numéro dans le recueil choisi d'abord),
    /// des mots du titre ou des paroles, « minuteur 35 », « noir », « retirer ».
    /// </summary>
    public List<ResultatCommande> Commande(string? saisie, string? recueil = null)
    {
        var q = (saisie ?? "").Trim();
        var res = new List<ResultatCommande>();
        if (q.Length == 0) return res;
        var n = Texte.Norm(q);

        if (R_MINUTEUR.Match(n) is { Success: true } mm)
        {
            int min = mm.Groups[1].Success ? int.Parse(mm.Groups[1].Value) : 45;
            res.Add(new ResultatCommande("ACTION", $"Minuteur de prédication : {min} min", "Démarre le minuteur", Minutes: Math.Clamp(min, 1, 300), Action: "minuteur"));
            return res;
        }
        if (n is "noir" or "ecran noir") res.Add(new ResultatCommande("ACTION", "Écran noir", "Cache le texte sans le perdre", Action: "noir"));
        if (n is "retirer" or "effacer" or "vider") res.Add(new ResultatCommande("ACTION", "Retirer de l'écran", "Échap", Action: "retirer"));

        // numéro de chant : le recueil choisi d'abord
        var mc = R_CHANT.Match(n);
        var num = mc.Success ? mc.Groups[1].Value : Regex.IsMatch(n, @"^\d{1,5}$") ? n : null;
        var vus = new HashSet<string>();
        if (num != null)
        {
            foreach (var c in (recueil != null ? ParNumero(num, recueil) : new()).Concat(ParNumero(num, null)))
                if (vus.Add(c.Id) && vus.Count <= 6)
                    res.Add(new ResultatCommande("CHANT", c.Title, c.Author.Length > 0 ? c.Author : "Sans recueil", ChantId: c.Id));
            if (mc.Success) return res;
        }

        // référence biblique (il faut un chiffre, ou le nom entier d'un livre)
        if ((q.Any(char.IsDigit) || Livres.Trouver(q + " 1").Count > 0) && LireRef(q) is { } lu)
        {
            var (r, entier) = lu;
            var vs = B.Versets(r.B, r.C);
            if (!entier && r.V1 == r.V2)
                res.Add(new ResultatCommande("BIBLE", Livres.Libelle(r.B, r.C, r.V1), vs[r.V1 - 1], Ref: r));
            else if (!entier)
                res.Add(new ResultatCommande("BIBLE", Livres.Libelle(r.B, r.C, r.V1, r.V2), $"{r.V2 - r.V1 + 1} versets · {vs[r.V1 - 1]}", Ref: r));
            else
                res.Add(new ResultatCommande("BIBLE", Livres.Libelle(r.B, r.C), $"Chapitre entier · {vs.Count} versets", Ref: new Ref(r.B, r.C, 0, 0)));
        }

        // titre ou paroles d'un chant
        if (n.Length >= 3)
            foreach (var c in Filtrer(q, null).Take(8))
                if (vus.Add(c.Id) && res.Count < 9)
                    res.Add(new ResultatCommande("CHANT", c.Title, c.Author.Length > 0 ? c.Author : "Sans recueil", ChantId: c.Id));
        return res;
    }
}
