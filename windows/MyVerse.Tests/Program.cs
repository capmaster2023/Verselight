using System.Diagnostics;
using MyVerse;

// Tests du cœur de MyVerse 4.0 (sans interface) : dotnet run --project MyVerse.Tests -- <dossier Ressources>
var res = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MyVerse", "Ressources");
int echecs = 0, total = 0;
string? groupe = null;
var filtre = args.Length > 1 ? args[1] : null;

void Groupe(string nom) { groupe = nom; Console.WriteLine($"\n== {nom}"); }
bool Actif() => filtre == null || (groupe ?? "").Contains(filtre, StringComparison.OrdinalIgnoreCase);
void Ok(bool cond, string quoi, string? detail = null)
{
    total++;
    if (!cond) echecs++;
    if (!cond || Environment.GetEnvironmentVariable("TOUT") == "1")
        Console.WriteLine($"  {(cond ? "✓" : "✗")} {quoi}{(cond || detail == null ? "" : "  → " + detail)}");
}
void Egal<T>(T obtenu, T attendu, string quoi) => Ok(EqualityComparer<T>.Default.Equals(obtenu, attendu), quoi, $"obtenu « {obtenu} », attendu « {attendu} »");

var sw = Stopwatch.StartNew();
var lsg = Bible.DepuisJson("lsg", File.ReadAllText(Path.Combine(res, "lsg.json")));
var kjv = Bible.DepuisJson("kjv", File.ReadAllText(Path.Combine(res, "kjv.json")));
Console.WriteLine($"chargement LSG + KJV : {sw.ElapsedMilliseconds} ms ({lsg.NombreVersets()} + {kjv.NombreVersets()} versets)");

// ------------------------------------------------------------------ références
Groupe("Livres");
if (Actif())
{
    string L(string s) { var r = Livres.Lire(s); return r is { } x ? Livres.Libelle(x.B, x.C, x.V1, x.V2) : "∅"; }
    Egal(L("jn 3 16"), "Jean 3:16", "jn 3 16");
    Egal(L("Jean 3:16-18"), "Jean 3:16-18", "Jean 3:16-18");
    Egal(L("1 co 13"), "1 Corinthiens 13", "1 co 13");
    Egal(L("ps 23"), "Psaumes 23", "ps 23");
    Egal(L("premier jean 1,9"), "1 Jean 1:9", "premier jean 1,9");
    Egal(L("Rm 8.28"), "Romains 8:28", "Rm 8.28");
    Egal(L("genese 1 1"), "Genèse 1:1", "genese 1 1");
    Egal(L("Gen 1"), "Genèse 1", "Gen 1");
    Egal(L("ap 22 21"), "Apocalypse 22:21", "ap 22 21");
    Egal(L("2 rois 2"), "2 Rois 2", "2 rois 2");
    Egal(L("jude 3"), "Jude 3", "jude 3 (livre d'un seul chapitre)");
    Egal(L("bonjour"), "∅", "texte qui n'est pas une référence");
    Ok(Livres.Suggestions("co").Count > 0, "suggestions « co »");
    Egal(lsg.Texte(42, 3, 16, 16)[..20], "Car Dieu a tant aimé", "texte Jean 3:16");
    Egal(lsg.Borner(new Ref(42, 3, 16, 99)), new Ref(42, 3, 16, 36), "borner Jean 3:16-99");
    Egal(lsg.Borner(new Ref(42, 99, 1, 1)), (Ref?)null, "chapitre inexistant");
}

// ------------------------------------------------------------------ recherche floue
Groupe("Recherche");
if (Actif())
{
    var versions = new List<Bible> { lsg };
    sw.Restart();
    Recherche.Preparer(lsg);
    Console.WriteLine($"  index LSG : {sw.ElapsedMilliseconds} ms");
    void Cas(string q, string attendu, int n = 1)
    {
        sw.Restart();
        var r = Recherche.Chercher(versions, q, 50, out _);
        var ms = sw.ElapsedMilliseconds;
        int pos = r.FindIndex(x => Livres.Libelle(x.B, x.C, x.V) == attendu);
        Ok(pos >= 0 && pos < n, $"« {q} » → {attendu} (rang {pos + 1}/{r.Count}, {ms} ms)",
            string.Join(" | ", r.Take(6).Select(x => Livres.Libelle(x.B, x.C, x.V) + (x.Principale ? "" : " " + x.Version) + " " + x.Note.ToString("0.00"))));
    }
    Cas("charité patiente", "1 Corinthiens 13:4");
    Cas("charite pasiente", "1 Corinthiens 13:4");
    Cas("car dieu a tant aimé le monde", "Jean 3:16");
    Cas("kar dieu a tan aimer le monde", "Jean 3:16");
    Cas("dieu a tellement aimé le monde", "Jean 3:16", 3);
    Cas("jesu pleura", "Jean 11:35");
    Cas("léternel est mon berger", "Psaumes 23:1");
    Cas("le seigneur est mon berger", "Psaumes 23:1", 3);
    Cas("je suis le chemin la verite et la vie", "Jean 14:6");
    Cas("je suis le chemain la veriter et la vi", "Jean 14:6");
    Cas("au commencement etait la parole", "Jean 1:1");
    Cas("o comencement était la parolle", "Jean 1:1");
    Cas("tout concourt au bien de ceux qui aiment dieu", "Romains 8:28");
    Cas("toute choses concoure au bien", "Romains 8:28", 3);
    Cas("je puis tout par celui qui me fortifie", "Philippiens 4:13");
    Cas("je peux tout par celui qui me fortifit", "Philippiens 4:13");
    Cas("lumiere du monde", "Jean 8:12", 5);
    Cas("heureux les pauvres en esprit", "Matthieu 5:3");
    Cas("heureu les povres en esprit", "Matthieu 5:3");
    Cas("le salaire du peche cest la mort", "Romains 6:23");
    Cas("le salère du péché c la mort", "Romains 6:23");
    Cas("batème", "Matthieu 3:7", 40);
    var k = Recherche.Chercher(versions, "keur", 5, out var tk);
    Ok(k.Count > 0 && tk.Contains("coeur"), "« keur » trouve « coeur »", string.Join(",", tk.Take(10)));

    // fausse Bible kirundi
    var kir = new Bible { Id = "kir", Nom = "Bibiliya Yera", Court = "KIR" };
    for (int b = 0; b < 66; b++) kir.Livres.Add(new Bible.Livre { Nom = Livres.Nom(b) });
    void Put(int b, int c, int v, string t)
    {
        var ch = kir.Livres[b].Chapitres;
        while (ch.Count < c) ch.Add(new List<string>());
        while (ch[c - 1].Count < v) ch[c - 1].Add("");
        ch[c - 1][v - 1] = t;
    }
    Put(42, 3, 16, "Kuko Imana yakunze abari mw'isi cane, gushika n'aho yatanze Umwana wayo w'ikinege, kugira ngo uwumwizera wese ntazopfe, ariko aronke ubugingo budashira.");
    Put(42, 3, 17, "Kuko Imana itarungikiye Umwana wayo mw'isi ngo aciré isi urubanza, ariko ni ukugira ngo isi ikizwe na we.");
    Put(18, 23, 1, "Uhoraho ni we mwungere wanje, sinzokena.");
    for (int b = 0; b < 66; b++) for (int i = 1; i <= 30; i++) Put(b, 1, i, $"Amajambo {b} n'ivyanditswe vyose {i} hamwe n'abantu benshi");
    versions = new List<Bible> { lsg, kjv, kir };
    sw.Restart();
    Recherche.Preparer(kjv); Recherche.Preparer(kir);
    Console.WriteLine($"  index KJV + KIR : {sw.ElapsedMilliseconds} ms");
    Cas("for god so loved the world", "Jean 3:16");
    Cas("for god so lovd the wrld", "Jean 3:16");
    Cas("Imana yakunze isi", "Jean 3:16");
    Cas("imana yakunse abari mw isi", "Jean 3:16");
    Cas("mwungere wanje", "Psaumes 23:1");
    Cas("uhoraho ni mwungeri", "Psaumes 23:1");
    Cas("car dieu a tant aimé le monde", "Jean 3:16");
    Cas("charité patiente", "1 Corinthiens 13:4");
    var m = Recherche.Morceaux("L’Éternel est mon berger: je ne manquerai de rien.", new HashSet<string> { "eternel", "berger" });
    Egal(string.Concat(m.Select(x => x.Mot ? "[" + x.T + "]" : x.T)), "L’[Éternel] est mon [berger]: je ne manquerai de rien.", "surlignage");
    sw.Restart();
    for (int i = 0; i < 20; i++) Recherche.Chercher(versions, "dieu amour paix joie patience bonte", 50, out _);
    Console.WriteLine($"  20 recherches de 6 mots dans 3 Bibles : {sw.ElapsedMilliseconds} ms");
    Egal(Recherche.Son("jeunesse"), Recherche.Son("genese"), "son(jeunesse) = son(genèse)");
    Egal(Recherche.Son("keur"), Recherche.Son("coeur"), "son(keur) = son(coeur)");
}

// ------------------------------------------------------------------ import de Bibles
Groupe("Import Bible");
if (Actif())
{
    var zef = """
        <?xml version="1.0" encoding="utf-8"?>
        <XMLBIBLE biblename="Bibiliya Yera">
          <BIBLEBOOK bnumber="43" bname="Yohana">
            <CHAPTER cnumber="3">
              <VERS vnumber="16">Kuko Imana yakunze abari mw'isi cane</VERS>
              <VERS vnumber="17">Kuko Imana itarungikiye Umwana wayo</VERS>
            </CHAPTER>
          </BIBLEBOOK>
          <BIBLEBOOK bnumber="1" bname="Itanguriro">
            <CHAPTER cnumber="1"><VERS vnumber="1">Mu ntango Imana yarema ijuru n'isi.</VERS></CHAPTER>
            <CHAPTER cnumber="2">REMPLIR</CHAPTER>
          </BIBLEBOOK>
        </XMLBIBLE>
        """.Replace("REMPLIR", string.Concat(Enumerable.Range(1, 60).Select(i => $"<VERS vnumber=\"{i}\">Umurongo {i}</VERS>")));
    var b = BibleImport.Analyser("kirundi.xml", zef);
    Egal(b.Nom, "Bibiliya Yera", "Zefania : nom");
    Egal(b.Versets(42, 3).Count >= 17 ? b.Versets(42, 3)[15] : "", "Kuko Imana yakunze abari mw’isi cane", "Zefania : Jean 3:16 (apostrophe typographique)");
    Egal(b.Versets(0, 1).Count > 0 ? b.Versets(0, 1)[0] : "", "Mu ntango Imana yarema ijuru n’isi.", "Zefania : Genèse 1:1");
    Egal(b.Nom, "Bibiliya Yera", "Zefania : nom (biblename)");

    var osis = """
        <osis><osisText osisIDWork="KIR"><header><work osisWork="KIR"><title>Bibiliya</title></work></header>
        <div type="book" osisID="John"><chapter osisID="John.3"><verse osisID="John.3.16">Kuko Imana <w>yakunze</w> isi<note>note retirée</note></verse></chapter></div>
        REMPLIR</osisText></osis>
        """.Replace("REMPLIR", string.Concat(Enumerable.Range(1, 60).Select(i => $"<verse osisID=\"Gen.2.{i}\">Umurongo {i}</verse>")));
    var o = BibleImport.Analyser("x.osis.xml", osis);
    Egal(o.Versets(42, 3).Count >= 16 ? o.Versets(42, 3)[15] : "", "Kuko Imana yakunze isi", "OSIS : Jean 3:16");

    var txt = "Jean 3:16 Car Dieu a tant aimé le monde\nJean 3:17 Dieu, en effet, n'a pas envoyé\nGenèse 1:1 Au commencement\n" + string.Join("\n", Enumerable.Range(1, 60).Select(i => $"Exode 2:{i} verset {i}"));
    var t = BibleImport.Analyser("ma-bible.txt", txt);
    Egal(t.Versets(42, 3).Count >= 17 ? t.Versets(42, 3)[16] : "", "Dieu, en effet, n'a pas envoyé", "texte : Jean 3:17");

    var json = """{"name":"Test","short":"TST","books":[{"name":"Genèse","ch":[["Au commencement"],REMPLIR]}]}""".Replace("REMPLIR", "[" + string.Join(",", Enumerable.Range(1, 60).Select(i => $"\"v{i}\"")) + "]");
    var j = BibleImport.Analyser("t.json", json);
    Egal(j.Versets(0, 1).Count > 0 ? j.Versets(0, 1)[0] : "", "Au commencement", "JSON MyVerse");
    var aller = Bible.DepuisJson("x", j.VersJson());
    Egal(aller.Texte(0, 1, 1, 1), "Au commencement", "JSON aller-retour");
}

// ------------------------------------------------------------------ fichiers de chants
Groupe("Chants");
if (Actif())
{
    var vp = "{Songs:[{Text:\"Grâce infinie\",Author:\"J. Newton\",Verses:[{Text:\"Grâce infinie\nquel doux son\"},{Tag:2,Text:\"Refrain ici\nencore\"},{Text:\"Deuxième\"}]}]}";
    var c = ChantsFormats.ParseSongFile("recueil.json", vp);
    Egal(c.Count, 1, "VideoPsalm : 1 chant");
    Egal(c[0].Title, "Grâce infinie", "VideoPsalm : titre");
    Egal(string.Join("|", c[0].Sections.Select(s => s.Label)), "Couplet 1|Refrain|Couplet 2", "VideoPsalm : étiquettes");
    Egal(c[0].Sections[0].Text, "Grâce infinie\nquel doux son", "VideoPsalm : texte multiligne");

    var ol = """
        <?xml version="1.0" encoding="UTF-8"?>
        <song xmlns="http://openlyrics.info/namespace/2009/song" version="0.8">
          <properties><titles><title>Amazing Grace</title></titles><authors><author>John Newton</author></authors>
          <verseOrder>v1 c v2 c</verseOrder></properties>
          <lyrics>
            <verse name="v1"><lines>Amazing grace how sweet<br/>the sound</lines></verse>
            <verse name="c"><lines>Chorus line</lines></verse>
            <verse name="v2"><lines>Second verse</lines></verse>
          </lyrics>
        </song>
        """;
    var o = ChantsFormats.ParseSongFile("a.xml", ol);
    Egal(o[0].Title, "Amazing Grace", "OpenLyrics : titre");
    Egal(string.Join("|", o[0].Sections.Select(s => s.Label)), "Couplet 1|Refrain|Couplet 2|Refrain", "OpenLyrics : ordre");
    Egal(o[0].Sections[0].Text, "Amazing grace how sweet\nthe sound", "OpenLyrics : <br/>");
    Egal(o[0].Author, "John Newton", "OpenLyrics : auteur");

    var os = """
        <song><title>Ma chanson</title><author>Moi</author><presentation>V1 C V2</presentation>
        <lyrics>[V]
        .G       D
        1 Premier couplet
        2 Deuxième couplet
        [C]
         Le refrain|en deux lignes</lyrics></song>
        """;
    var s = ChantsFormats.ParseSongFile("ma.xml", os);
    Egal(string.Join("|", s[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2", "OpenSong : couplets numérotés");
    Egal(s[0].Sections[1].Text, "Le refrain\nen deux lignes", "OpenSong : | = retour à la ligne");

    var cp = "{title: Reckless Love}\n{artist: Cory Asbury}\n\n{start_of_verse: Verse 1}\n[G]Before I spoke a [D]word\nYou were singing\n{end_of_verse}\n\n{soc}\nOh the over[C]whelming\n{eoc}\n\n{chorus}\n";
    var p = ChantsFormats.ParseSongFile("r.cho", cp);
    Egal(p[0].Title, "Reckless Love", "ChordPro : titre");
    Egal(string.Join("|", p[0].Sections.Select(x => x.Label)), "Couplet|Refrain|Refrain", "ChordPro : sections + {chorus}");
    Egal(p[0].Sections[0].Text, "Before I spoke a word\nYou were singing", "ChordPro : accords retirés");

    var ss = "Way Maker\n\nVerse 1\nYou are here moving in our midst\nI worship You\n\nChorus\nWay maker miracle worker\n\nOsinachi Kalu Okoro Egbu\nCCLI Song # 7115744\n© 2016 Integrity Music\nFor use solely with the SongSelect® Terms of Use.\nCCLI License # 1234";
    var w = ChantsFormats.ParseSongFile("waymaker.txt", ss);
    Egal(w[0].Title, "Way Maker", "SongSelect : titre");
    Egal(w[0].Ccli, "7115744", "SongSelect : numéro CCLI");
    Egal(w[0].Author, "Osinachi Kalu Okoro Egbu", "SongSelect : auteur seul");
    Egal(string.Join("|", w[0].Sections.Select(x => x.Label)), "Couplet|Refrain", "SongSelect : sections");

    var tx = "Mon Dieu est bon\n\nIl est bon\ntout le temps\n\n[Refrain]\nAlléluia\n\nCouplet 2 : Deuxième\nstrophe";
    var t = ChantsFormats.ParseSongFile("bon.txt", tx);
    Egal(t[0].Title, "Mon Dieu est bon", "texte : titre");
    Egal(string.Join("|", t[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2", "texte : étiquettes");
    Egal(t[0].Sections[2].Text, "Deuxième\nstrophe", "texte : « Couplet 2 : » retiré");
    Egal(ChantsFormats.FrLabel("chorus 2"), "Refrain 2", "étiquette chorus 2");
    Egal(ChantsFormats.FrLabel("Bridge"), "Pont", "étiquette Bridge");
    Egal(ChantsFormats.RtfToText(@"{\rtf1\ansi{\fonttbl\f0 Arial;}\f0 Gr\'e2ce infinie\par quel \u233?l\'e9gant}"), "Grâce infinie\nquel élégant", "RTF");
    try { ChantsFormats.ParseSongFile("x.pro", "\u0001\u0002binaire"); Ok(false, "ProPresenter 7 refusé"); }
    catch (InvalidDataException) { Ok(true, "ProPresenter 7 refusé"); }

    var inclus = System.Text.Json.JsonSerializer.Deserialize<List<Chant>>(File.ReadAllText(Path.Combine(res, "chants-inclus.json")))!;
    Ok(inclus.Count > 800, $"chants inclus : {inclus.Count}");
}

// ------------------------------------------------------------------ rangement automatique
Groupe("Ranger");
if (Actif())
{
    var r = Ranger.Ranger_("Mon chant collé\n\nC G Am F\nLigne une du chant\nLigne deux du chant\nLigne trois du chant\nLigne quatre du chant\nLigne cinq du chant\nLigne six du chant\nLigne sept\n\nRefrain :\nGloire à toi\nGloire à toi\n");
    Egal(r.Titre, "Mon chant collé", "titre trouvé");
    Ok(!r.Sections.Any(x => x.Text.Contains("C G Am F")), "ligne d'accords retirée");
    Egal(r.Sections.Count, 3, "3 diapositives (4+3 lignes + refrain)");
    Egal(string.Join("|", r.Sections.Select(x => x.Label)), "Couplet 1 (1/2)|Couplet 1 (2/2)|Refrain", "étiquettes");
    var t = ChantsFormats.VersTexte(r.Sections);
    int i = t.IndexOf("Ligne deux");
    t = t[..i] + "\n" + t[i..];
    Egal(ChantsFormats.SplitSections(t).Count, 4, "Entrée devant « Ligne deux » : 4 diapositives");
    var r2 = Ranger.Ranger_("Titre\n\n1. Premier couplet ligne\nsuite\n\nRefrain\nGloire\n\n2. Deuxième couplet\nencore\n\nRefrain");
    Egal(string.Join("|", r2.Sections.Select(x => x.Label)), "Couplet 1|Refrain|Couplet 2|Refrain", "couplets numérotés + refrain repris");
    Egal(r2.Sections[3].Text, "Gloire", "« Refrain » seul = copie du refrain");
    var r3 = Ranger.Ranger_("Gloire à toi\nSeigneur\n\nTu es bon\nToujours\n\nGloire à toi\nSeigneur", 4);
    Egal(string.Join("|", r3.Sections.Select(x => x.Label)), "Refrain|Couplet 1|Refrain", "bloc répété = refrain");
    Egal(string.Join("|", Ranger.CouperLigne("Une très longue ligne de chant, qui ne tient pas sur une ligne", 40)), "Une très longue ligne de chant,|qui ne tient pas sur une ligne", "ligne trop longue coupée à la virgule");
    Ok(Ranger.LigneAccords("Do Ré Mim Sol") && !Ranger.LigneAccords("La"), "accords en français");
    Egal(string.Join(",", Ranger.Tailles(7, 5)), "4,3", "découpe équilibrée 7 → 4+3");
}

// ------------------------------------------------------------------ recueils
Groupe("Recueils");
if (Actif())
{
    var c = Recueils.ChantsDepuisTexte("1. GRÂCE INFINIE\nLigne a\nLigne b\n\n2. MON DIEU\nLigne c\n---\nAutre chant\nLigne d");
    Egal(string.Join("|", c.Select(x => x.Title)), "1. GRÂCE INFINIE|2. MON DIEU|Autre chant", "texte : 3 chants");
    var pdf = new List<Recueils.LignePdf>
    {
        new("1. GRÂCE INFINIE", 14, 1), new("1. Grâce infinie, ô quel doux son", 10, 1), new("Qui sauva un pécheur", 10, 1), new("", 0, 1),
        new("Refrain : Gloire", 10, 1), new("à Dieu", 10, 1), new("", 0, 1), new("2 DEUXIÈME CHANT", 14, 2), new("Paroles du chant", 10, 2), new("", 0, 2),
    };
    var cp = Recueils.ChantsDepuisLignes(pdf);
    Egal(cp.Count, 2, "PDF : 2 chants");
    Egal(cp[0].Title, "1. GRÂCE INFINIE", "PDF : titre");
    Egal(string.Join("|", cp[0].Sections.Select(x => x.Label)), "Couplet 1|Refrain", "PDF : strophes");
    Egal(cp[0].Sections[1].Text, "Gloire\nà Dieu", "PDF : refrain");
    var lignes = Recueils.LignesDepuisPages(new[]
    {
        (IReadOnlyList<Recueils.MorceauPdf>)new[] { new Recueils.MorceauPdf("doux", 120, 700, 10), new Recueils.MorceauPdf("Grâce", 50, 700.4, 10), new Recueils.MorceauPdf("son", 160, 699.8, 10), new Recueils.MorceauPdf("Refrain", 50, 640, 10) },
    });
    Egal(string.Join("|", lignes.Select(l => l.T)), "Grâce doux son||Refrain|", "PDF : lignes regroupées, écart = ligne vide");
    var html = """
        <!DOCTYPE html><html><head><title>Site</title><script>var a = 1 < 2;</script></head><body>
        <header><nav><a href="/">Accueil</a><h1>Mon site</h1></nav></header>
        <article><h1 class="entry-title">Quel ami fidèle</h1>
        <div class="entry-content"><p>1. Quel ami fidèle et tendre<br>Nous avons en Jésus-Christ</p><p>Refrain<br>Gloire &agrave; Dieu</p>
        <div class="sharedaddy">Partager sur Facebook</div></div></article><footer>© 2024</footer></body></html>
        """;
    var w = Recueils.ChantDepuisPage(html, "https://site.org/chants/ami");
    Egal(w?.Title, "Quel ami fidèle", "web : titre");
    Egal(string.Join("|", w?.Sections.Select(x => x.Label) ?? Array.Empty<string>()), "Couplet 1|Refrain", "web : strophes");
    Egal(w?.Sections[1].Text, "Gloire à Dieu", "web : entités décodées");
    var liste = "<a href='/chants/ami'>A</a><a href=\"chants/bon#x\">B</a><a href='https://site.org/chants/cri'>C</a><a href='/autre'>X</a><a href='https://autre.org/chants/dur'>D</a><a href='/chants/ami'>A</a><a href='/chants/x'>court</a>";
    Egal(string.Join(" ", Recueils.LiensDeChants(liste, "https://site.org/chants")), "https://site.org/chants/ami https://site.org/chants/bon https://site.org/chants/cri", "web : liens de chants");
}

// ------------------------------------------------------------------ données et reprise de la 3.x
string Temp() { var d = Path.Combine(Path.GetTempPath(), "myverse-test-" + Guid.NewGuid().ToString("n")[..8]); Directory.CreateDirectory(d); return d; }
Groupe("Donnees");
if (Actif())
{
    var d0 = Donnees.Charger(Temp(), res);
    Ok(d0.Chants.Count > 800, $"premier démarrage : chants de l'église ajoutés ({d0.Chants.Count})");
    Egal(d0.Reglages.Theme, "nuit", "thème par défaut");
    var dir = Temp();
    Directory.CreateDirectory(Path.Combine(dir, "bibles"));
    File.WriteAllText(Path.Combine(dir, "bibles", "bk1.json"), """{"name":"Bibiliya","short":"KIR","books":[]}""");
    var cles = new Dictionary<string, string>
    {
        ["verselight-chants"] = """[{"id":"a1","title":"Mon chant","author":"Recueil 3x","sections":[{"label":"Couplet","text":"Ligne"}]}]""",
        ["verselight-theme"] = "\"aube\"",
        ["verselight-taille"] = "1.2",
        ["verselight-presentation"] = "\"bandeau\"",
        ["verselight-inclus-v1"] = "true",
        ["myverse-decoupes"] = """{"42.3.16":["Car Dieu","afin que"]}""",
        ["myverse-bibles"] = """{"principale":"bk1","seconde":"kjv","importees":[{"id":"bk1","name":"Bibiliya","short":"KIR","versets":3},{"id":"bk2","name":"Perdue","short":"X","versets":3}]}""",
        ["verselight-obs"] = """{"actif":true,"port":4456,"mdp":"secret","source":"BIBLE"}""",
        ["verselight-ecran-choisi"] = """{"p":false,"x":1920,"y":0,"w":1920,"h":1080,"nom":"Projecteur","mode":"plein"}""",
    };
    File.WriteAllText(Path.Combine(dir, "myverse-donnees.json"), System.Text.Json.JsonSerializer.Serialize(new { version = "3.0.4", date = "2026-11-09", donnees = cles }));
    var d = Donnees.Charger(dir, res);
    Egal(d.Chants.Count, 1, "3.x : chants repris (sans ajouter une 2e fois ceux de l'église)");
    Egal(d.Chants[0].Author, "Recueil 3x", "3.x : recueil du chant");
    Egal(d.Reglages.Theme, "aube", "3.x : thème");
    Egal(d.Reglages.Taille, 1.2, "3.x : taille");
    Egal(d.Reglages.Presentation, "bandeau", "3.x : présentation");
    Egal(d.Decoupes.TryGetValue("42.3.16", out var dec) ? dec.Count : 0, 2, "3.x : verset découpé");
    Egal(d.Reglages.Bibles.Principale, "bk1", "3.x : Bible principale importée");
    Egal(d.Reglages.Bibles.Importees.Count, 1, "3.x : Bible dont le fichier manque retirée");
    Egal(d.Reglages.Obs.Port, 4456, "3.x : réglages OBS");
    Egal(d.Reglages.Ecran?.X, 1920, "3.x : écran choisi");
    Ok(d.Migration.Contains("1 chants"), "résumé de la reprise : " + d.Migration);
    var d2 = Donnees.Charger(dir, res);
    Egal(d2.Chants.Count, 1, "2e démarrage : rien n'est repris une 2e fois");

    var ld = Ancien3x.LireDossier(Path.Combine(AppContext.BaseDirectory, "donnees", "leveldb-3x"));
    // le dossier du profil est trouvé même dans une arborescence WebView2
    var faux = Path.Combine(Temp(), "MyVerse.exe", "EBWebView", "Default", "Local Storage", "leveldb");
    Directory.CreateDirectory(faux);
    Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(faux)!)!, "Cache"));
    var trouve = typeof(Ancien3x).GetMethod("Chercher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .Invoke(null, new object[] { Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(faux)!)!)!)!, 5 }) as IEnumerable<string>;
    Egal(trouve?.FirstOrDefault(), faux, "profil WebView2 de la 3.x trouvé");
    Ok(ld.TryGetValue("verselight-chants", out var ch3) && ch3.StartsWith("[{") && ch3.Contains("Grâce infinie’"), "LevelDB : chants (UTF-16, table compressée)");
    Egal(ld.TryGetValue("verselight-theme", out var th) ? th : "∅", "\"vert\"", "LevelDB : valeur la plus récente (journal)");
    Egal(ld.TryGetValue("verselight-taille", out var ta) ? ta : "∅", "1.2", "LevelDB : valeur du journal");
    Ok(!ld.ContainsKey("verselight-presentation"), "LevelDB : clé supprimée ignorée");
    Ok(!ld.ContainsKey("myverse-journal"), "LevelDB : journal ignoré");
    Ok(ld.ContainsKey("myverse-decoupes"), "LevelDB : découpes");
    var d3 = Donnees.Charger(Temp(), res);
    d3.AppliquerCles(ld);
    Egal(d3.Chants.Count, 2000, "LevelDB : 2000 chants repris");
}

// ------------------------------------------------------------------ régie
Groupe("Regie");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    File.Copy(Path.Combine(res, "lsg.json"), Path.Combine(don.Dossier, "lsg-copie.json"));
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    int changes = 0; bool? oeil = null;
    rg.Change += () => changes++;
    rg.Oeil += v => oeil = v;
    rg.AfficherVerset(42, 3, 16, 16);
    Egal(rg.Etat.Ref, "Jean 3:16 (LSG)", "Jean 3:16 à l'écran");
    Ok(oeil == true && changes > 0, "OBS : afficher");
    Egal(rg.Prochaine()?.Label, "Jean 3:17", "Suivant = Jean 3:17");
    var tx = rg.TexteADecouper(rg.Cible!);
    int k = tx.IndexOf("afin que");
    var msg = rg.Decouper(rg.Cible!, tx[..k] + "\n" + tx[k..]);
    Ok(msg.Contains("2 diapositives"), "découpe : " + msg);
    Ok(rg.Etat.Lignes[0].StartsWith("Car Dieu") && !rg.Etat.Lignes[0].Contains("afin que"), "morceau 1 de Jean 3:16 à l'écran");
    Ok(rg.Prochaine()?.Label.Contains("morceau 2/2") == true, "Suivant annonce le morceau 2/2");
    rg.Bouger(1);
    Ok(rg.Etat.Lignes[0].StartsWith("afin que"), "Suivant → morceau 2");
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Jean 3:17 (LSG)", "Suivant → Jean 3:17");
    rg.Bouger(-1);
    Ok(rg.Etat.Lignes[0].StartsWith("afin que"), "Précédent → dernier morceau de 3:16");
    rg.AfficherVerset(42, 3, 36, 36);
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Jean 4:1 (LSG)", "fin du chapitre → chapitre suivant");
    rg.Bouger(-1);
    Egal(rg.Etat.Ref, "Jean 3:36 (LSG)", "Précédent → fin du chapitre précédent");
    bib.BasculerSeconde("kjv");
    rg.AfficherVerset(42, 3, 17, 17);
    Ok(rg.Etat.Second?.Texte.Contains("For God sent not his Son") == true, "2e version sous le verset");
    Egal(rg.Etat.Ref, "Jean 3:17 (LSG · KJV)", "référence bilingue");
    var res3 = Recherche.Chercher(bib.Versions(), "god so loved the world", 5, out _);
    Egal(res3.Count > 0 ? Livres.Libelle(res3[0].B, res3[0].C, res3[0].V) : "∅", "Jean 3:16", "recherche en anglais (2e version)");
    bib.BasculerSeconde("kjv");
    Ok(rg.Etat.Second == null && rg.Etat.Ref == "Jean 3:17 (LSG)", "retour à la Louis Segond seule (verset réaffiché)");
    rg.Effacer();
    Ok(rg.Etat.Vide && oeil == false && rg.Cible == null, "Retirer");
    rg.Selection = new Ref(18, 23, 1, 1);
    rg.Bouger(1);
    Egal(rg.Etat.Ref, "Psaumes 23:1 (LSG)", "Suivant sans rien à l'écran → versets choisis");
    Ok(rg.AfficherRef("jude 3") && rg.Etat.Ref == "Jude 1:3 (LSG)", "« jude 3 » → Jude 1:3 : " + rg.Etat.Ref);
    Ok(rg.AfficherRef("ps 23") && rg.Etat.Ref == "Psaumes 23:1 (LSG)", "chapitre entier → premier verset");
    Egal(rg.ApercuRef("ps 23")?.Versets.Count, 6, "aperçu du chapitre entier");
    Ok(!rg.AfficherRef("bonjour"), "référence inconnue refusée");

    var r = Ranger.Ranger_("Mon chant\n\nLigne une\nLigne deux\nLigne trois\n\nRefrain\nGloire");
    var id = rg.EnregistrerChant(null, r.Titre, r.Sections, "Mes chants");
    rg.AfficherSection(id, 0);
    Egal(string.Join("/", rg.Etat.Lignes), "Ligne une/Ligne deux/Ligne trois", "strophe à l'écran");
    Egal(rg.Prochaine()?.Label, "Refrain", "Suivant = Refrain");
    msg = rg.Decouper(new CibleChant(id, 0), "Ligne une\n\nLigne deux\nLigne trois");
    Egal(string.Join("|", rg.Trouver(id)!.Sections.Select(x => x.Label)), "Couplet 1 (1/2)|Couplet 1 (2/2)|Refrain", "strophe coupée en 2");
    Egal(string.Join("/", rg.Etat.Lignes), "Ligne une", "strophe affichée mise à jour");
    rg.Bouger(1); rg.Bouger(1);
    Egal(string.Join("/", rg.Etat.Lignes), "Gloire", "Suivant → Refrain");
    Ok(rg.Recueils().Any(x => x.Nom == "Mes chants"), "recueil « Mes chants »");
    Egal(rg.Filtrer("gloire", "Mes chants").Count, 1, "filtre par paroles et recueil");
    Ok(rg.ChercherChants("1").All(c => c.Title.TrimStart().StartsWith("1")), "chant par numéro");
    rg.AjouterChants(new[] { new Chant { Title = "6 - Six" }, new Chant { Title = "16 - Seize" }, new Chant { Title = "106. Cent six" }, new Chant { Title = "N° 60 Soixante" }, new Chant { Title = "Numva Yesu" } }, "Num A");
    rg.AjouterChants(new[] { new Chant { Title = "6 - Autre six" } }, "Num B");
    Egal(string.Join("|", rg.ParNumero("6", "Num A").Select(c => c.Title)), "6 - Six", "numéro exact dans le recueil (pas 16 ni 106)");
    Egal(rg.ParNumero("06", null).Count(c => c.Author.StartsWith("Num ")), 2, "numéro « 06 » dans tous les recueils");
    Egal(string.Join("|", rg.ParNumero("60", "Num A").Select(c => c.Title)), "N° 60 Soixante", "numéro « N° 60 »");
    Egal(rg.ParNumero("7", "Num A").Count, 0, "numéro absent");
    Ok(Regie.Numero("Numva Yesu") == null && Regie.Numero("106. Cent six") == 106, "numéro lu dans le titre");
    Ok(rg.Filtrer("6", null).Take(2).All(c => Regie.Numero(c.Title) == 6), "recherche « 6 » : les n° 6 d'abord");
    Ok(rg.ChercherChants("99999999999999").Count == 0, "numéro trop long sans erreur");
    rg.SupprimerRecueil("Num A"); rg.SupprimerRecueil("Num B");
    Ok(Regie.Comparer("2. B", "10. A") < 0 && Regie.Comparer("élan", "Etoile") < 0, "tri naturel et sans accents");
    rg.SupprimerChant(id);
    Ok(rg.Trouver(id) == null, "chant supprimé");
    var don2 = Donnees.Charger(don.Dossier, res);
    Ok(don2.Decoupes.ContainsKey("42.3.16") && don2.Reglages.Bibles.Seconde == "", "réglages et découpes enregistrés");
}

// ------------------------------------------------------------------ téléphone
Groupe("Telephone");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    using var tel = new Telephone(don.Dossier, rg, f => f(), "PC-TEST");
    tel.Demarrer();
    Ok(tel.Port >= 47900 && tel.Port <= 47909, $"écoute sur le port {tel.Port}");
    var url = tel.OuvrirJumelage();
    var q = System.Web.HttpUtility.ParseQueryString(new Uri(url.Replace("verselight://pair", "http://x/pair")).Query);
    Egal(q["pcId"], tel.PcId, "code QR : pcId");
    Ok((q["ips"] ?? "").Length >= 0 && q["port"] == tel.Port.ToString(), "code QR : port");
    var secret = Telephone.B64Dec(q["secret"]!);

    async Task<System.Net.WebSockets.ClientWebSocket> Ouvrir()
    {
        var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{tel.Port}/"), CancellationToken.None);
        return ws;
    }
    async Task Env(System.Net.WebSockets.ClientWebSocket ws, object o) =>
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(o)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    async Task<System.Text.Json.Nodes.JsonObject?> Rec(System.Net.WebSockets.ClientWebSocket ws, string? type = null, int ms = 3000)
    {
        var fin = DateTime.Now.AddMilliseconds(ms);
        var buf = new byte[1 << 16];
        while (DateTime.Now < fin)
        {
            using var cts = new CancellationTokenSource(fin - DateTime.Now);
            using var mem = new MemoryStream();
            try
            {
                System.Net.WebSockets.WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buf, cts.Token); mem.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
            }
            catch (OperationCanceledException) { return null; }
            var o = System.Text.Json.Nodes.JsonNode.Parse(mem.ToArray())!.AsObject();
            if (type == null || (string?)o["t"] == type) return o;
        }
        return null;
    }
    async Task<System.Net.WebSockets.ClientWebSocket?> Connecter(string device, byte[] cle, bool pair)
    {
        var ws = await Ouvrir();
        await Env(ws, new { t = "hello", v = 1, pcId = tel.PcId, deviceId = device, deviceName = "Pixel test", pair, cnonce = "cn1" });
        var ch = await Rec(ws);
        if ((string?)ch?["t"] != "challenge") { Ok(false, "défi attendu", ch?.ToJsonString()); return null; }
        var nonce = (string)ch!["nonce"]!;
        Egal((string?)ch["proof"], Telephone.PreuveServeur(cle, "cn1", nonce), "le PC prouve qu'il connaît la clé");
        await Env(ws, new { t = "auth", mac = Telephone.MacClient(cle, nonce, device) });
        var w = await Rec(ws);
        Egal((string?)w?["t"], "welcome", "bienvenue (" + (pair ? "jumelage" : "reconnexion") + ")");
        Egal((bool?)w?["paired"], pair, "paired");
        return ws;
    }
    var cle = Telephone.DeriverCle(secret, tel.PcId, "tel-1");
    var ws1 = await Connecter("tel-1", cle, true);
    Ok(tel.Store.Liste().Any(a => a.DeviceId == "tel-1" && a.Nom == "Pixel test"), "téléphone jumelé enregistré");
    Ok(tel.Connectes().Contains("tel-1"), "téléphone connecté");
    if (ws1 != null)
    {
        await Env(ws1, new { t = "bible.show", @ref = "jn 3 16", rid = 1 });
        var st = await Rec(ws1, "state");
        Egal((string?)st?["ref"], "Jean 3:16 (LSG)", "bible.show → état reçu");
        Egal((string?)st?["suivantLabel"], "Jean 3:17", "état : suivant");
        await Env(ws1, new { t = "bible.preview", @ref = "ps 23 1-2", rid = 2 });
        var pv = await Rec(ws1, "preview");
        Egal(pv?["versets"]?.AsArray().Count, 2, "aperçu : 2 versets");
        Egal((int?)pv?["rid"], 2, "réponse avec rid");
        await Env(ws1, new { t = "bible.search", q = "charite pasiente", rid = "3" });
        var se = await Rec(ws1, "search", 8000);
        Egal((string?)se?["liste"]?[0]?["ref"], "1 Corinthiens 13:4", "recherche floue depuis le téléphone");
        Egal((string?)se?["rid"], "3", "rid texte conservé");
        await Env(ws1, new { t = "song.search", q = "1", rid = 4 });
        var so = await Rec(ws1, "songs");
        Ok((so?["liste"]?.AsArray().Count ?? 0) > 0, "chants par numéro");
        var sid = (string?)so?["liste"]?[0]?["id"];
        await Env(ws1, new { t = "song.open", id = sid, rid = 5 });
        var sg = await Rec(ws1, "song");
        Ok((sg?["sections"]?.AsArray().Count ?? 0) > 0, "chant ouvert");
        await Env(ws1, new { t = "song.show", id = sid, i = 0, rid = 6 });
        st = await Rec(ws1, "state");
        Egal((string?)st?["mode"], "chant", "chant à l'écran");
        Egal((string?)st?["chant"]?["id"], sid, "état : chant");
        await Env(ws1, new { t = "clear" });
        st = await Rec(ws1, "state");
        Egal((string?)st?["mode"], "vide", "Retirer depuis le téléphone");
        await Env(ws1, new { t = "sync.get", rid = 7 });
        var sy = await Rec(ws1, "sync");
        Ok(sy?["bibles"]?.AsArray().Any(b => (string?)b?["id"] == "kjv") == true && (string?)sy?["reglages"]?["principale"] == "lsg", "synchronisation : Bibles et réglages");
        await Env(ws1, new { t = "bible.get", id = "kjv", rid = 8 });
        var bg = await Rec(ws1, "bible", 15000);
        if (bg != null)
        {
            using var gz = new System.IO.Compression.GZipStream(new MemoryStream(Convert.FromBase64String((string)bg["gz"]!)), System.IO.Compression.CompressionMode.Decompress);
            using var lu = new StreamReader(gz);
            var kj = Bible.DepuisJson("kjv", lu.ReadToEnd());
            Ok(kj.Texte(42, 3, 16, 16).StartsWith("For God so loved"), $"Bible KJV envoyée ({((string)bg["gz"]!).Length / 1024} Ko)");
        }
        else Ok(false, "Bible KJV envoyée");
        await Env(ws1, new { t = "inconnue", rid = 9 });
        Egal((string?)(await Rec(ws1, "error"))?["message"], "Commande inconnue : inconnue", "commande inconnue");
        await ws1.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    }
    await Task.Delay(200);
    Ok(!tel.Connectes().Contains("tel-1"), "déconnexion vue");
    var ws2 = await Connecter("tel-1", cle, false);
    ws2?.Abort();
    // téléphone inconnu, mauvais PC, QR fermé
    async Task<string?> Refus(object hello)
    {
        var ws = await Ouvrir();
        await Env(ws, hello);
        var m = await Rec(ws);
        ws.Abort();
        return (string?)m?["code"];
    }
    Egal(await Refus(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "inconnu", deviceName = "X", pair = false, cnonce = "c" }), "revoked", "téléphone inconnu refusé");
    Egal(await Refus(new { t = "hello", v = 1, pcId = "autre", deviceId = "tel-1", deviceName = "X", pair = false, cnonce = "c" }), "wrong_pc", "mauvais PC");
    tel.FermerJumelage();
    Egal(await Refus(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-2", deviceName = "X", pair = true, cnonce = "c" }), "pairing_closed", "code QR fermé");
    var ws3 = await Ouvrir();
    await Env(ws3, new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-1", deviceName = "X", pair = false, cnonce = "c" });
    var ch3 = await Rec(ws3);
    await Env(ws3, new { t = "auth", mac = "faux" });
    Egal((string?)(await Rec(ws3))?["code"], "revoked", "mauvaise clé refusée");
    ws3.Abort();
    tel.Revoquer("tel-1");
    Ok(!tel.Store.Liste().Any(a => a.DeviceId == "tel-1"), "téléphone révoqué");
    using (var http = new HttpClient())
        Ok((await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/")).Contains("MyVerse ✓"), "page de test http://IP:port");
    using (var u = new System.Net.Sockets.UdpClient())
    {
        var b = System.Text.Encoding.UTF8.GetBytes("VERSELIGHT_DISCOVER " + tel.PcId);
        await u.SendAsync(b, b.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, Telephone.PortDecouverte));
        var cts = new CancellationTokenSource(2000);
        try
        {
            var r = await u.ReceiveAsync(cts.Token);
            var o = System.Text.Json.Nodes.JsonNode.Parse(r.Buffer)!;
            Ok((string?)o["pcId"] == tel.PcId && (int?)o["port"] == tel.Port, "découverte UDP");
        }
        catch (OperationCanceledException) { Ok(false, "découverte UDP (pas de réponse)"); }
    }
    var dev = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(don.Dossier, "devices.json")))!;
    Ok((string?)dev["pcId"] == tel.PcId && dev["devices"] is System.Text.Json.Nodes.JsonArray, "devices.json au format de la 3.x");
}

Console.WriteLine($"\n{total - echecs}/{total} vérifications réussies" + (echecs > 0 ? $" — {echecs} ÉCHEC(S)" : " — tout est bon"));
return echecs == 0 ? 0 : 1;
