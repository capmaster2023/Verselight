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

// ------------------------------------------------------------------ réglages de la 1.2 bêta (ancien mixeur local) → 3.0
Groupe("Reglages 1.2");
if (Actif())
{
    var dir = Temp();
    File.WriteAllText(Path.Combine(dir, "chants.json"), """[{"id":"c12","title":"Chant gardé","author":"Recueil 1.2","sections":[{"label":"Couplet","text":"Ligne"}]}]""");
    File.WriteAllText(Path.Combine(dir, "reglages.json"), """
    {"theme":"ciel","taille":1.3,"presentation":"bandeau","recueil":"Recueil 1.2","inclus":true,
     "bibles":{"principale":"kjv","seconde":"lsg","importees":[]},
     "audio":{"version":2,"sampleRate":48000,"monitorDeviceId":null,"monitorDb":-12,"listenZoom":true,
       "sources":[{"id":"a","name":"Micro","kind":"Capture","stereo":true,"channel":1,"sends":{"salle":{"enabled":true,"volumeDb":-3}},
                   "filters":[{"kind":"Equalizer","q":0.7},{"kind":"Gain","gainDb":3},{"kind":"Sidechain","sidechainSourceId":"b"}]},
                  {"id":"b","name":"PC","kind":"Loopback","filters":[{"kind":9}]},{"id":"c","kind":2}],
       "buses":[{"id":"salle","outputDeviceId":"x"}],"scenes":[{"name":"Culte","sources":[{"kind":"Capture"}]}],
       "profiles":[{"name":"Culte","icon":"●","format":"wav","allowedSources":["a"],"sendDb":{"a":-6},"applyToRoom":true}]}}
    """);
    var d12 = Donnees.Charger(dir, res);
    Egal(d12.Reglages.Theme, "ciel", "1.2 : thème conservé (les réglages se lisent malgré l'ancien audio)");
    Egal(d12.Reglages.Taille, 1.3, "1.2 : taille conservée");
    Egal(d12.Reglages.Bibles.Principale, "kjv", "1.2 : Bible principale conservée");
    Egal(d12.Reglages.Recueil, "Recueil 1.2", "1.2 : recueil conservé");
    Egal(d12.Chants.Count, 1, "1.2 : chants conservés");
    var a12 = d12.Reglages.Audio;
    a12.Normalize();
    Egal(a12.Sources.Count, 0, "1.2 : le mélangeur OBS repart vide (anciennes sources ignorées)");
    Egal(a12.Profiles.Count, 0, "1.2 : anciens profils d'enregistrement non repris (aucun préréglage)");
    Egal(a12.MonitorDeviceId, "default", "1.2 : écoute au casque sur le périphérique par défaut");
    Ok(a12.MicrophoneId == "default" && a12.OutputId == "default", "1.2 : micro et sortie proposés = périphériques Windows par défaut");
    Egal(a12.Version, MyVerse.Audio.AudioConfiguration.Actuelle, "1.2 : version des réglages audio mise à jour");
    // nouvelles sources : enregistrées sous « obsSources » et relues telles quelles
    a12.Sources.Add(new MyVerse.Audio.AudioSource { Name = "Chaire", Kind = MyVerse.Audio.AudioInputKind.Microphone, Tracks = 5, Monitoring = MyVerse.Audio.AudioMonitoring.Only,
        Filters = { new MyVerse.Audio.AudioFilter { Kind = MyVerse.Audio.AudioFilterKind.Compressor, Ratio = 6 } } });
    d12.SauverReglages();
    var json = File.ReadAllText(Path.Combine(dir, "reglages.json"));
    Ok(json.Contains("\"obsSources\"") && !json.Contains("\"sources\""), "3.0 : sources écrites sous « obsSources »");
    var r12 = Donnees.Charger(dir, res).Reglages.Audio;
    r12.Normalize();
    Egal(r12.Sources.Count, 1, "3.0 : source relue");
    Egal(r12.Sources.FirstOrDefault()?.Tracks, 5, "3.0 : pistes relues");
    Egal(r12.Sources.FirstOrDefault()?.Monitoring, MyVerse.Audio.AudioMonitoring.Only, "3.0 : écoute relue");
    Egal(r12.Sources.FirstOrDefault()?.Filters.FirstOrDefault()?.Ratio, 6.0, "3.0 : filtre relu");
    var neuf = new MyVerse.Audio.AudioConfiguration(); neuf.Normalize();
    Ok(neuf.Sources.Count == 0 && neuf.Profiles.Count == 0 && neuf.VirtualSounds.Count == 0, "nouveaux réglages : mélangeur vide, aucun enregistrement ni sortie (rien de préréglé)");
}

// ------------------------------------------------------------------ régie
Groupe("Transfert");
if (Actif())
{
    var dA = Donnees.Charger(Temp(), res);
    var bA = new Bibles(dA);
    var rA = new Regie(dA, bA);
    rA.AjouterChants(new[] {
        new Chant { Title = "1 - Un", Sections = { new Section { Label = "Couplet 1", Text = "Ligne une" } } },
        new Chant { Title = "2 - Deux", Sections = { new Section { Label = "Couplet 1", Text = "Ligne deux" } } } }, "Recueil T");
    var infoA = bA.Ajouter(Bible.DepuisJson("x", File.ReadAllText(Path.Combine(res, "lsg.json"))), "MAB");
    var fichier = Path.Combine(Temp(), "export" + Transfert.Extension);
    var m = Transfert.Exporter(fichier, dA, true, true, "test");
    Ok(m.Chants == dA.Chants.Count && m.Bibles.Count == 1, $"export : {m.Chants} chants, {m.Bibles.Count} Bible");
    Ok(Transfert.Lire(fichier).Recueils.Contains("Recueil T"), "contenu du fichier lisible");

    var dB = Donnees.Charger(Temp(), res);
    var bB = new Bibles(dB);
    var rB = new Regie(dB, bB);
    var bil = Transfert.Importer(fichier, rB, bB);
    Ok(bil.ChantsAjoutes == 2 && bil.ChantsMisAJour == 0 && bil.ChantsPareils == m.Chants - 2 && rB.Filtrer("", "Recueil T").Count == 2, $"import : {bil.ChantsAjoutes} ajoutés, {bil.ChantsPareils} déjà là");
    Egal(bil.BiblesAjoutees, 1, "import : Bible ajoutée");
    var bibB = bB.Toutes().FirstOrDefault(x => !x.Integree);
    Ok(bibB != null && bibB.Court == "MAB" && bB.Charger(bibB.Id)?.NombreVersets() == infoA.Versets, "Bible importée lisible");
    var bil2 = Transfert.Importer(fichier, rB, bB);
    Ok(bil2.ChantsAjoutes == 0 && bil2.ChantsMisAJour == 0 && bil2.BiblesAjoutees == 0 && bil2.BiblesPresentes == 1, "réimport : rien en double");
    rA.Filtrer("", "Recueil T")[0].Sections[0].Text = "Ligne changée";
    Transfert.Exporter(fichier, dA, true, false);
    var bil3 = Transfert.Importer(fichier, rB, bB);
    Ok(bil3.ChantsMisAJour == 1 && rB.Filtrer("changée", null).Count == 1, "chant modifié : mis à jour");
    Ok(Transfert.Lire(fichier).Bibles.Count == 0, "export des chants seuls");
    bool refuse = false;
    try { Transfert.Lire(Path.Combine(res, "lsg.json")); } catch (Exception) { refuse = true; }
    Ok(refuse, "fichier qui n'est pas un export refusé");
}

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
    Ok(rg.Prochaine()?.Label.Contains("morceau 2/2") == true, "Suivant display le morceau 2/2");
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
        var display = rg.NouveauDisplay("Caméra USB", new SourceDisplay { Type = "camera", Peripherique = "device-test" });
        await Env(ws1, new { t = "display.list", rid = 10 });
        var ds = await Rec(ws1, "displays");
        Ok(ds?["liste"]?.AsArray().Any(x => (string?)x?["id"] == display.Id && (string?)x?["type"] == "camera") == true, "APK : liste des sources Display");
        await Env(ws1, new { t = "display.show", id = display.Id });
        st = await Rec(ws1, "state");
        Ok((string?)st?["mode"] == "media" && (string?)st?["displayId"] == display.Id && (string?)st?["titre"] == "Caméra USB", "APK : périphérique Display en direct");
        await Env(ws1, new { t = "display.show", id = "inexistant" });
        Ok(((string?)(await Rec(ws1, "error"))?["message"] ?? "").Contains("introuvable"), "APK : source Display inconnue refusée");
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
        Ok((await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/")).Contains("MyChurch ✓"), "page de test http://IP:port");
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

    // page « suivre » : l'assemblée suit sur son téléphone (sans jumelage, lecture seule)
    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
        var page = await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre");
        Ok(page.Contains("MyChurch") && page.Contains("/suivre/etat"), "page « suivre »");
        var e0 = System.Text.Json.Nodes.JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre/etat?v=-1&id=test1"))!;
        int v0 = (int)e0["v"]!;
        var attente = http.GetStringAsync($"http://127.0.0.1:{tel.Port}/suivre/etat?v={v0}&id=test1");
        await Task.Delay(400);
        Ok(!attente.IsCompleted, "attente longue tant que l'écran ne change pas");
        rg.AfficherVerset(42, 3, 16, 16);
        var e1 = System.Text.Json.Nodes.JsonNode.Parse(await attente)!;
        Ok((int)e1["v"]! > v0 && ((string?)e1["ref"] ?? "").StartsWith("Jean 3:16") && ((string?)e1["principale"]) == "LSG", "l'assemblée voit le verset aussitôt");
        Ok(tel.Suiveurs() >= 1, $"téléphones qui suivent : {tel.Suiveurs()}");
        Ok(Telephone.AdressesLocales().Count == 0 || (tel.AdresseSuivre() ?? "").EndsWith($":{tel.Port}/suivre"), "adresse du code QR");
    }
}

Groupe("Agenda");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var bib = new Bibles(don);
    var rg = new Regie(don, bib);
    Egal(Regie.NomAgendaParDefaut(new DateTime(2026, 11, 9)), "Culte du dimanche 15 novembre", "nom par défaut : le prochain dimanche");
    Egal(Regie.NomAgendaParDefaut(new DateTime(2026, 11, 15)), "Culte du dimanche 15 novembre", "un dimanche : ce jour-là");
    var ag = rg.Agenda;
    Ok(rg.Agendas.Count == 1 && ag.Elements.Count == 0, "un agenda créé tout seul");
    var idChant = rg.EnregistrerChant(null, "1 - Chant de test", new List<Section> { new() { Label = "Couplet 1", Text = "Ligne A\nLigne B" }, new() { Label = "Refrain", Text = "Ligne C" } }, "Recueil A");
    var eC = rg.AjouterChantAgenda(idChant)!;
    var eB = rg.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
    var eT = rg.AjouterTexteAgenda("Annonces", "Bienvenue\n\nRepas après le culte");
    Egal(rg.Agenda.Elements.Count, 3, "chant, Bible et texte dans l'agenda");
    Egal($"{rg.Diapos(eC).Count}/{rg.Diapos(eB).Count}/{rg.Diapos(eT).Count}", "2/2/2", "diapositives de chaque élément");
    // 3.2 : vignettes de la page Culte et aperçu « Suivant » : chaque diapositive dessinée sans rien afficher
    {
        int avantChanges = 0;
        rg.Change += () => avantChanges++;
        var vC = rg.EtatCible(rg.Diapos(eC)[1].Cible, eC);
        var vB = rg.EtatCible(rg.Diapos(eB)[0].Cible, eB);
        var vT = rg.EtatCible(rg.Diapos(eT)[1].Cible);
        var eK = rg.AjouterCompteAgenda(7);
        var vK = rg.EtatCible(rg.Diapos(eK)[0].Cible);
        Ok(vC is { Mode: "chant" } && string.Join("|", vC.Lignes) == "Ligne C", "vignette : la 2e strophe du chant");
        Ok(vB is { Mode: "verset" } && vB.Ref.StartsWith("Jean 3:16") && vB.Lignes[0].Length > 20, "vignette : le verset avec sa référence");
        Ok(vT is { Mode: "chant" } && vT.Lignes[0] == "Repas après le culte", "vignette : le 2e paragraphe du texte");
        Ok(vK is { Mode: "chant" } && vK.Lignes.Count == 2 && vK.Lignes[1] == "07:00", "vignette : le compte à rebours à sa durée");
        Ok(rg.EtatCible(null) == null && rg.EtatCible(new CibleChant("absent", 0)) == null, "vignette d'une cible absente : rien");
        Ok(rg.Etat.Vide && rg.Cible == null && avantChanges == 0, "vignettes : rien ne s'affiche, aucun changement d'écran");
        rg.RetirerAgenda(eK.Id);
    }
    Egal(rg.Description(eB).Type, "BIBLE", "type affiché");
    rg.AfficherElement(eC, 0);
    Ok(rg.ElementCourant == eC && rg.Etat.Mode == "chant", "élément de l'agenda à l'écran");
    rg.Bouger(1);
    rg.Bouger(1);
    Ok(rg.ElementCourant == eB && rg.Etat.Ref.StartsWith("Jean 3:16"), "Suivant passe à l'élément suivant");
    Egal(rg.Prochaine()?.Label, "Jean 3:17", "Suivant = Jean 3:17");
    rg.Bouger(1);
    Egal(rg.Prochaine()?.Label, "Agenda › Annonces", "fin du passage : l'élément suivant est annoncé");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eT && string.Join("|", rg.Etat.Lignes) == "Bienvenue", "puis le texte");
    rg.Bouger(1); rg.Bouger(1);
    Egal(string.Join("|", rg.Etat.Lignes), "Repas après le culte", "fin de l'agenda : on reste");
    rg.Bouger(-1); rg.Bouger(-1);
    Ok(rg.ElementCourant == eB && rg.Etat.Ref.StartsWith("Jean 3:17"), "Précédent revient à l'élément d'avant");
    rg.AfficherVerset(1, 1, 1, 1);
    Ok(rg.ElementCourant == null, "autre chose à l'écran : plus d'élément en cours");
    rg.DeplacerAgenda(eT.Id, -2);
    Egal(rg.Agenda.Elements[0].Id, eT.Id, "déplacer en tête");
    var a2 = rg.NouvelAgenda("Culte de Noël");
    Ok(rg.Agenda.Id == a2.Id && rg.Agenda.Elements.Count == 0, "nouvel agenda choisi");
    rg.ChoisirAgenda(ag.Id);
    Egal(rg.Agenda.Elements.Count, 3, "retour au premier agenda");
    var don2 = Donnees.Charger(don.Dossier, res);
    Ok(don2.Agendas.Count == 2 && don2.Agendas.Any(a => a.Nom == "Culte de Noël") && don2.Agendas.First(a => a.Id == ag.Id).Elements.Count == 3, "agendas enregistrés");
    rg.SupprimerAgenda(a2.Id);
    Egal(rg.Agendas.Count, 1, "agenda supprimé");

    var notes = "La grâce qui transforme\n1. Sauvés par grâce — Éphésiens 2:8-9\n2. Rm 5.8 et 2 Co 5:17\nAppel : Jean 3:16, puis Psaume 23. Une 2 fois, Rom 8, le 3:16, culte à 10:30";
    Egal(string.Join(" | ", rg.RefsDansNotes(notes).Select(x => x.Libelle)), "Éphésiens 2:8-9 | Romains 5:8 | 2 Corinthiens 5:17 | Jean 3:16 | Psaumes 23", "versets trouvés dans les notes");

    // 5.2 (design Aube) : compte à rebours, heures, éléments faits, résumé, displays
    var t0 = new DateTime(2026, 11, 15, 9, 55, 0);
    rg.Horloge = () => t0;
    rg.OublierResume();
    var eCpt = rg.AjouterCompteAgenda(5);
    Egal(rg.Agenda.Elements[0].Id, eCpt.Id, "compte à rebours : en tête du déroulé");
    rg.AfficherElement(eCpt);
    Egal(string.Join("|", rg.Etat.Lignes), "Le culte commence dans|05:00", "compte à rebours à l'écran");
    rg.Horloge = () => t0.AddSeconds(61);
    Ok(rg.TicCompte() && rg.Etat.Lignes[1] == "03:59", "le compte à rebours avance : " + rg.Etat.Lignes[1]);
    rg.Horloge = () => t0.AddMinutes(7);
    rg.TicCompte();
    Egal(rg.Etat.Lignes[1], "00:00", "compte à rebours fini");
    Ok(rg.EstFait(eCpt) && !rg.EstFait(eC), "élément fait (pas encore le chant)");
    Egal(Regie.LireHeure("10h12"), "10:12", "heure « 10h12 »");
    Egal(Regie.LireHeure("9h") + "|" + (Regie.LireHeure("25:00") ?? "null"), "09:00|null", "heures « 9h » et « 25:00 »");
    rg.HeureAgenda(eC.Id, "10h");
    Egal(rg.Agenda.Elements.First(x => x.Id == eC.Id).Heure, "10:00", "heure prévue enregistrée");
    rg.AfficherElement(eC, 0); rg.Bouger(1);
    Egal(rg.Resume(), "Accueil · 1 - Chant de test (2 couplets)", "résumé du culte");
    Ok(rg.ResumePartage().Contains("• 1 - Chant de test (2 couplets)") && rg.DeroulePartage().Contains("10:00 — 1 - Chant de test"), "résumé et déroulé à partager");
    var an = rg.NouvelleAnnonce("Bienvenue", "Bienvenue à tous !\n\nRepas après le culte");
    rg.AfficherAnnonce(an);
    Ok(rg.Etat.Lignes[0] == "Bienvenue à tous !" && rg.ElementCourant == null, "display à l'écran");
    Egal(rg.Prochaine()?.Texte, "Repas après le culte", "display : diapositive suivante");
    rg.Bouger(1);
    Egal(rg.Etat.Lignes[0], "Repas après le culte", "display : Suivant");
    var eAn = rg.AjouterAnnonceAgenda(an.Id)!;
    Ok(eAn.Type == "texte" && rg.Diapos(eAn).Count == 2, "display ajoutée au déroulé");
    rg.ModifierAnnonce(an.Id, "Bienvenue !", "Bonjour");
    Ok(Donnees.Charger(don.Dossier, res).Annonces.Any(a => a.Titre == "Bienvenue !"), "displays enregistrées");
    rg.SupprimerAnnonce(an.Id);
    Egal(rg.Annonces.Count, 0, "display supprimée");
    rg.Horloge = () => DateTime.Now;

    var c1 = rg.Commande("jean 3 16");
    Ok(c1.Count > 0 && c1[0].Type == "BIBLE" && c1[0].Titre == "Jean 3:16", "commande « jean 3 16 »");
    var c2 = rg.Commande("chant 1", "Recueil A");
    Ok(c2.Count > 0 && c2[0].ChantId == idChant, "commande « chant 1 » : le recueil choisi d'abord");
    Ok(!rg.Commande("minuteur 35").Any(x => x.Action == "minuteur"), "3.0 : plus de minuteur de prédication");
    Ok(rg.Commande("Chant de test").Any(x => x.ChantId == idChant), "commande : titre d'un chant");
    Ok(rg.Commande("romains").Any(x => x.Type == "BIBLE" && x.Titre == "Romains 1"), "commande : nom d'un livre");
}

Groupe("Ecran par defaut 3.3");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var idChant = rg.EnregistrerChant(null, "Chant de reprise", new List<Section> { new() { Label = "1", Text = "A" }, new() { Label = "2", Text = "B" }, new() { Label = "3", Text = "C" } }, "Recueil R");
    var logo = rg.NouveauDisplay("Logo MEPC", new SourceDisplay { Type = "image", Chemin = "logo.png" });
    var bienvenue = rg.NouvelleAnnonce("Bienvenue", "Bienvenue\nMEPC Montréal\n\nDeuxième diapositive");
    var vide = rg.NouvelleAnnonce("Vide", "");
    var eP = rg.AjouterPrincipalAgenda(logo.Id)!;
    var eC = rg.AjouterChantAgenda(idChant)!;
    var eT = rg.AjouterPrincipalAgenda(bienvenue.Id)!;
    var eF = rg.AjouterPrincipalAgenda(null)!;
    Ok(eP.Type == "principal" && eP.Source?.Chemin == "logo.png" && eT.Texte!.StartsWith("Bienvenue") && eF.Titre == "Fond seul", "écrans principaux ajoutés au déroulé (image, texte, fond seul)");
    Ok(rg.AjouterPrincipalAgenda(vide.Id) == null && rg.AjouterPrincipalAgenda("absent") == null, "un texte vide ou une source absente : rien n'est ajouté");
    Egal(rg.Description(eP).Type, "ÉCRAN PRINCIPAL", "type affiché dans le déroulé");
    Egal(rg.Description(eT).Sous, "Texte · reste à l'écran", "détail affiché dans le déroulé");
    Egal(rg.Diapos(eP).Count, 1, "un écran principal = une diapositive");
    Egal(string.Join("|", rg.EcransPrincipaux.Select(e => e.Titre)), "Logo MEPC|Bienvenue|Fond seul", "écrans principaux du déroulé, dans l'ordre");
    Egal(rg.EcranDefaut, "noir", "écran par défaut au départ : noir (comme avant)");

    rg.AfficherElement(eC, 1);
    rg.Effacer();
    Ok(rg.Etat.Vide && rg.Cible == null && rg.ElementCourant == null, "écran par défaut noir : Retirer vide l'écran comme avant");

    int oeil = -1;
    rg.Oeil += v => oeil = v ? 1 : 0;
    rg.AfficherElement(eP, 0);
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media?.Chemin == "logo.png" && rg.ElementCourant == eP && rg.Cible is CibleDefaut, "écran principal à l'écran (déroulé)");
    Egal(rg.EcranDefaut, eP.Id, "il devient l'écran par défaut");
    Egal(rg.LibelleEcranDefaut, "Logo MEPC", "libellé de l'écran par défaut");
    Egal(oeil, 0, "OBS : la source reste cachée pour un écran principal");
    Ok(rg.EtatDiffusion(rg.Etat).Vide && !rg.Etat.Vide, "OBS, caméra virtuelle et page publique : rien, la salle voit l'écran principal");
    Egal(rg.PositionAgenda(eP), 0, "l'écran principal est l'élément en direct");

    rg.Bouger(1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "A", "Suivant : le chant après l'écran principal");
    rg.Bouger(1);
    rg.Effacer();
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media?.Chemin == "logo.png" && rg.Cible is CibleDefaut { } cd1 && cd1.Id == eP.Id && rg.ElementCourant == null,
        "Retirer : retour direct sur l'écran par défaut");
    Ok(rg.EtatDiffusion(rg.Etat).Vide, "Retirer : OBS reste vide comme avant");
    Egal(rg.Prochaine()?.Label, "3", "Suivant annoncé : la strophe après celle retirée");
    Ok(rg.SuivanteDeroule() is { } sd && sd.E == eC && sd.I == 2, "vignette SUIVANT : la strophe après celle retirée");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut && rg.Prochaine()?.Label == "3", "Retirer deux fois : on reste sur l'écran par défaut, la reprise est gardée");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "C", "Suivant reprend après la strophe retirée");
    rg.Effacer();
    rg.Bouger(-1);
    Ok(rg.ElementCourant == eC && rg.Etat.Lignes[0] == "C", "Précédent remet la strophe retirée");

    rg.Bouger(1);
    Ok(rg.ElementCourant == eT && rg.Etat.Mode == "chant" && string.Join("|", rg.Etat.Lignes) == "Bienvenue|MEPC Montréal" && rg.EcranDefaut == eT.Id,
        "écran principal texte : sa première diapositive, et il devient l'écran par défaut");
    rg.Effacer();
    Ok(rg.Cible is CibleDefaut { } cd2 && cd2.Id == eT.Id && rg.ElementCourant == null, "Retirer sur un écran principal : il reste");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond", "Suivant après Retirer sur un écran principal : l'élément d'après");
    rg.Bouger(1);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond", "fin du déroulé : on reste");

    rg.ChoisirEcranDefaut(eT.Id);
    Ok(rg.ElementCourant == eF && rg.Etat.Mode == "fond" && rg.EcranDefaut == eT.Id, "choisir l'écran par défaut ne remplace pas un écran principal affiché depuis le déroulé");
    rg.AfficherElement(eC, 1);
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.Etat.Lignes.FirstOrDefault() == "B" && rg.EcranDefaut == "fond", "choisir l'écran par défaut ne touche pas aux paroles à l'écran");
    rg.Effacer();
    Ok(rg.Etat.Mode == "fond" && !rg.Etat.Vide && rg.Etat.Lignes.Count == 0 && rg.Cible is CibleDefaut { Id: "fond" }, "fond seul : l'habillage sans texte");
    rg.ChoisirEcranDefaut(eP.Id);
    Ok(rg.Etat.Mode == "media" && rg.Cible is CibleDefaut { } cd3 && cd3.Id == eP.Id, "changer d'écran par défaut pendant qu'il est à l'écran : il est remplacé tout de suite");
    Egal(rg.Prochaine()?.Label, "3", "… et Suivant reprend toujours après les paroles retirées");
    rg.ChoisirEcranDefaut("absent");
    Egal(rg.EcranDefaut, eP.Id, "choix inconnu : ignoré");
    Egal(Donnees.Charger(don.Dossier, res).Reglages.EcranDefaut, eP.Id, "écran par défaut enregistré");

    rg.ChoisirEcranDefaut("noir");
    Ok(rg.Etat.Vide && rg.Cible == null, "écran par défaut noir pendant qu'il est à l'écran : l'écran se vide");
    rg.ChoisirEcranDefaut("fond");
    Ok(rg.Etat.Mode == "fond" && rg.Cible is CibleDefaut, "écran vide : le nouvel écran par défaut s'affiche tout de suite");

    Ok(rg.EtatCible(rg.Diapos(eT)[0].Cible, eT) is { Mode: "chant" } vt && vt.Lignes[0] == "Bienvenue", "vignette d'un écran principal texte");
    Ok(rg.EtatCible(rg.Diapos(eP)[0].Cible) is { Mode: "media" } vp && vp.Media?.Chemin == "logo.png", "vignette d'un écran principal image");
    Ok(rg.EtatCible(rg.Diapos(eF)[0].Cible, eF) is { Mode: "fond" } && rg.EtatCible(new CibleDefaut("fond")) is { Mode: "fond" }, "vignette du fond seul");

    rg.AfficherElement(eC, 0);
    rg.Epingler();
    Ok(rg.Etat.Vide && rg.Etat.Duo != null && rg.Cible == null, "Épingler libère l'écran sans écran par défaut (comme avant)");
    rg.Effacer();
    Ok(rg.Etat.Mode == "fond" && rg.Etat.Duo != null, "Retirer avec un contenu épinglé : le fond par défaut, l'épinglé reste");
    Ok(rg.EtatDiffusion(rg.Etat) is { Mode: "vide" } dd && dd.Duo != null, "OBS : seulement le contenu épinglé");
    rg.Bouger(1);
    Ok(rg.Cible is CibleDefaut, "rien n'a été retiré : Suivant ne reprend rien");
    rg.Desepingler();

    rg.AfficherVerset(42, 3, 16, 16);
    rg.Effacer();
    rg.Bouger(1);
    Ok(rg.Etat.Ref.StartsWith("Jean 3:17") && rg.ElementCourant == null, "Bible : Suivant après Retirer passe au verset suivant");
    rg.Effacer();
    rg.Bouger(-1);
    Ok(rg.Etat.Ref.StartsWith("Jean 3:17"), "Bible : Précédent remet le verset retiré");

    rg.AfficherSection(idChant, 0);
    rg.Effacer();
    rg.RetirerAgenda(eF.Id);
    rg.ChoisirAgenda(rg.NouvelAgenda("Autre culte").Id);
    Egal(rg.EcranDefaut, "fond", "autre culte : fond seul garde son sens");
    rg.ChoisirEcranDefaut(eP.Id);
    Egal(rg.EcranDefaut, "fond", "un écran principal d'un autre culte : refusé");
    don.Reglages.EcranDefaut = eP.Id;
    Egal(rg.EcranDefaut, "noir", "écran principal absent du culte choisi : noir");
}

Groupe("Display 6.0");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var texte = rg.NouvelleAnnonce("Ancien texte", "Ancienne diapositive");
    var image = rg.NouveauDisplay("Image", new SourceDisplay { Type = "image", Chemin = "/test/image.png" });
    var camera = rg.NouveauDisplay("Carte HDMI", new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:test" });
    var fenetre = rg.NouveauDisplay("Fenêtre", new SourceDisplay { Type = "fenetre", Fenetre = 1234, Processus = 42 });
    var relu = Donnees.Charger(don.Dossier, res);
    Ok(relu.Annonces.Count == 4 && relu.Annonces.First().Source == null, "compatibilité des anciens textes après réouverture");
    Ok(relu.Annonces.Last().Source == fenetre.Source, "identité de fenêtre et processus conservée");
    Ok(relu.Annonces.First(a => a.Id == camera.Id).Source == camera.Source, "identité stable du périphérique vidéo conservée");
    rg.AfficherAnnonce(camera);
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media == camera.Source && rg.Etat.Lignes.Count == 0, "caméra en direct sans texte résiduel");
    rg.AfficherAnnonce(texte);
    Ok(rg.Etat.Mode == "chant" && rg.Etat.Media == null, "retour caméra vers texte : capture arrêtée");
    rg.AfficherAnnonce(image); rg.Effacer();
    Ok(rg.Etat.Vide && rg.Etat.Media == null, "retirer une source coupe la capture");
    var el = rg.AjouterAnnonceAgenda(camera.Id)!;
    var suivant = rg.AjouterTexteAgenda("Après caméra", "Bonjour");
    rg.AfficherElement(el);
    Ok(rg.ElementCourant == el && rg.PositionAgenda(el) == 0 && rg.Diapos(el).Count == 1, "source dans le déroulé du culte");
    rg.Bouger(1);
    Ok(rg.ElementCourant == suivant && rg.Etat.Media == null && rg.Etat.Lignes[0] == "Bonjour", "Suivant quitte la caméra pour la diapositive suivante");
    rg.Bouger(-1);
    Ok(rg.ElementCourant == el && rg.Etat.Media == camera.Source, "Précédent revient à la caméra");
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.Etat.Media == null, "afficher un verset coupe la capture");
    Ok(MiseAJour.PlusRecente("6.0.0", "6.0.0-beta") && !MiseAJour.PlusRecente("6.0.0-beta", "6.0.0"), "passage bêta vers version finale");
}

Groupe("Dossiers Duo");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    int changes = 0; string dit = "";
    rg.Change += () => changes++;
    rg.Message += m => dit = m;
    var d = rg.NouveauDuo("Chant et verset");
    Ok(rg.Duos.Count == 1 && !rg.AfficherDuo(d) && dit.Contains("deux éléments"), "dossier vide : refusé, avec un message");
    var ch = don.Chants.First(c => c.Sections.Count > 1);
    d.A = new PartDuo { Type = "chant", Id = ch.Id, Titre = ch.Title, X = 0, Y = 0, L = 1 };
    d.B = new PartDuo { Type = "bible", Ref = new[] { 42, 3, 16, 16 }, Titre = "Jean 3:16", X = 0.9, Y = 0.03, L = 0.35 };
    rg.EnregistrerDuo(d);
    Ok(d.B.X <= 0.65 + 1e-9, "mesures bornées : le cadre reste dans l'écran");
    changes = 0;
    Ok(rg.AfficherDuo(d), "afficher un dossier : chant + verset");
    Egal(changes, 1, "un seul changement d'écran pour les deux éléments");
    Ok(rg.Etat.Mode == "chant" && rg.Etat.Duo is { Place: "libre" } e1 && e1.Etat.Mode == "verset" && e1.Dossier == d.Id && e1.Cadres is { Length: 6 }, "écran : le chant, avec le verset dans le Duo (place libre, 6 mesures)");
    rg.Bouger(1);
    Ok(rg.Cible is CibleChant { I: 1 } && rg.Etat.Duo != null, "Suivant : la strophe suivante, le dossier reste");
    d.B.L = 0.2; rg.EnregistrerDuo(d, false);
    Ok(rg.Etat.Duo?.Cadres?[5] == 0.2, "mesure changée pendant l'affichage : appliquée tout de suite");
    rg.AfficherVerset(42, 3, 17, 17);
    Ok(rg.Etat.Duo == null && rg.Etat.Mode == "verset", "autre chose à l'écran : le dossier s'en va");
    rg.AfficherDuo(d);
    rg.AfficherSection(ch.Id, 0);
    Ok(rg.Etat.Duo == null && rg.Etat.Mode == "chant", "clic sur l'élément 1 seul : il s'affiche seul, le dossier s'en va");
    var an = rg.AjouterDuoDisplay(d);
    Ok(an.Grille && an.Source?.Type == "duo" && rg.AjouterDuoDisplay(d).Id == an.Id && rg.Annonces.Count(x => x.Source?.Type == "duo") == 1, "+ Display : une seule tuile, dans la grille");
    rg.AfficherAnnonce(an);
    Ok(rg.Etat.Duo is { Place: "libre" } && rg.Etat.Mode == "chant", "clic sur la tuile Display : le dossier part en direct");
    rg.Effacer();
    Ok(rg.Etat.Vide && rg.Etat.Duo == null, "Retirer de l'écran : tout le dossier part");
    changes = 0;
    var apercu = rg.EtatAnnonce(an);
    Ok(apercu is { Mode: "chant", Duo: { Place: "libre" } ad } && ad.Etat.Mode == "verset" && ad.Cadres is { Length: 6 } && ad.Dossier == d.Id, "aperçu du dossier (vignette, vidéo) : le chant, avec le verset par-dessus");
    Ok(rg.Etat.Vide && changes == 0 && rg.Cible == null, "aperçu : rien ne s'affiche, aucun changement d'écran");
    var texteAn = rg.NouvelleAnnonce("Annonce", "Bienvenue\nà tous\n\nDeuxième");
    Ok(rg.EtatAnnonce(texteAn) is { Mode: "chant", Lignes.Count: 2 } && rg.EtatAnnonce(texteAn, 1)?.Lignes[0] == "Deuxième", "aperçu d'un texte Display : ses diapositives");
    Ok(rg.EtatVerset(42, 3, 16, 16) is { Mode: "verset" } ev && ev.Ref.StartsWith("Jean 3"), "aperçu d'un verset");
    Ok(rg.EtatDuo(new DossierDuo()) == null, "aperçu d'un dossier incomplet : rien");
    var relu = Donnees.Charger(don.Dossier, res);
    Ok(relu.Duos.Count == 1 && relu.Duos[0].A.Type == "chant" && relu.Duos[0].B.Ref?[2] == 16, "dossiers relus du disque");
    rg.SupprimerDuo(d.Id);
    Ok(rg.Duos.Count == 0 && rg.Annonces.All(x => x.Source?.Type != "duo"), "supprimer le dossier : sa tuile Display part aussi");
}

Groupe("Duo");
if (Actif())
{
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    int changes = 0; bool? oeil = null; string dit = "";
    rg.Change += () => changes++;
    rg.Oeil += v => oeil = v;
    rg.Message += m => dit = m;
    Egal(don.Reglages.DuoPlace, "hd", "place par défaut : haut droite");
    Egal(don.Reglages.DuoPart, 0.35, "taille par défaut : 35 %");
    // rien à l'écran : rien à épingler
    Ok(!rg.Epingler() && rg.Etat.Duo == null && dit.Contains("Affichez d'abord"), "épingler un écran vide : refusé, avec un message");
    Ok(!System.Text.Json.JsonSerializer.Serialize(rg.Etat, Donnees.Json).Contains("\"duo\""), "etat.json sans Duo : pas de clé « duo »");
    // un verset épinglé, puis un chant à l'écran
    rg.AfficherVerset(42, 3, 16, 16);
    changes = 0;
    Ok(rg.Epingler(), "épingler un verset");
    Ok(rg.Etat.Vide && rg.Cible == null && rg.Etat.Lignes.Count == 0, "épingler libère l'écran principal");
    Ok(rg.Etat.Duo is { Place: "hd", Part: 0.35 } d1 && d1.Libelle == "Jean 3:16 (LSG)" && d1.Etat.Mode == "verset" && d1.Etat.Lignes[0].StartsWith("Car Dieu"), "le verset est dans le Duo (libellé, texte, place, taille)");
    Ok(changes > 0 && oeil == true, "épingler : l'écran est prévenu et la source OBS reste visible");
    var idDuo = rg.EnregistrerChant(null, "Chant du duo", new List<Section> { new() { Label = "Couplet 1", Text = "Ligne une\nLigne deux" }, new() { Label = "Refrain", Text = "Gloire à toi" } });
    rg.AfficherSection(idDuo, 0);
    Ok(rg.Etat.Mode == "chant" && rg.Etat.Lignes[0] == "Ligne une" && rg.Etat.Duo?.Libelle == "Jean 3:16 (LSG)", "un chant + un verset épinglé");
    rg.Bouger(1);
    Ok(rg.Etat.Lignes[0] == "Gloire à toi" && rg.Etat.Duo != null, "Suivant garde le Duo");
    rg.Theme("aube"); rg.Taille(0.9); rg.Presentation("bandeau");
    Ok(rg.Etat.Duo != null && rg.Etat.Theme == "aube", "habillage de l'écran : le Duo reste");
    rg.Presentation("plein");
    var json = System.Text.Json.JsonSerializer.Serialize(rg.Etat, Donnees.Json);
    Ok(json.Contains("\"duo\"") && json.Contains("\"place\"") && json.Contains("\"libelle\"") && json.Contains("\"part\""), "etat.json contient le Duo (pour la page OBS)");
    var relu = System.Text.Json.JsonSerializer.Deserialize<EtatEcran>(json, Donnees.Json)!;
    Ok(relu.Duo is { Place: "hd" } dr && dr.Etat.Ref == "Jean 3:16 (LSG)" && dr.Etat.Duo == null, "etat.json relu : même Duo");
    // Effacer garde le Duo
    oeil = null;
    rg.Effacer();
    Ok(rg.Etat.Vide && rg.Etat.Duo?.Libelle == "Jean 3:16 (LSG)", "Retirer de l'écran garde le Duo");
    Ok(oeil == true, "Retirer avec un Duo : la source OBS reste visible");
    // place et taille, bornées et gardées
    rg.PlaceDuo("cote", 0.9);
    Ok(rg.Etat.Duo is { Place: "cote", Part: 0.5 }, "côte à côte, taille bornée à 50 %");
    rg.PlaceDuo("bg", 0.01);
    Ok(rg.Etat.Duo is { Place: "bg", Part: 0.2 }, "bas gauche, taille bornée à 20 %");
    rg.PlaceDuo("n'importe quoi", 0.4);
    Ok(rg.Etat.Duo is { Place: "hd", Part: 0.4 }, "place inconnue : haut droite");
    rg.PlaceDuo("hg", double.NaN);
    Ok(rg.Etat.Duo is { Place: "hg", Part: 0.35 }, "taille illisible : 35 %");
    rg.PlaceDuo("bd", 0.3);
    var regRelu = Donnees.Charger(don.Dossier, res).Reglages;
    Ok(regRelu.DuoPlace == "bd" && regRelu.DuoPart == 0.3, "place et taille gardées pour la prochaine fois");
    // épingler autre chose remplace le Duo (jamais de Duo dans un Duo)
    rg.AfficherSection(idDuo, 1);
    Ok(rg.Epingler() && rg.Etat.Duo is { Place: "bd", Part: 0.3 } d2 && d2.Libelle == "Gloire à toi" && d2.Etat.Mode == "chant" && d2.Etat.Duo == null, "épingler un chant remplace le verset épinglé");
    rg.AfficherVerset(18, 23, 1, 1);
    Ok(rg.Etat.Mode == "verset" && rg.Etat.Duo?.Etat.Mode == "chant", "un verset + un chant épinglé");
    // désépingler
    changes = 0;
    rg.Desepingler();
    Ok(rg.Etat.Duo == null && rg.Etat.Mode == "verset" && changes == 1, "Retirer le duo : l'écran principal ne change pas");
    rg.Desepingler();
    Ok(changes == 1, "Retirer le duo sans Duo : rien ne se passe");
    // deux sources Display en même temps
    var camera = rg.NouveauDisplay("Caméra salle", new SourceDisplay { Type = "camera", Peripherique = "@device:pnp:duo" });
    var image = rg.NouveauDisplay("Logo", new SourceDisplay { Type = "image", Chemin = "/test/logo.png" });
    rg.AfficherAnnonce(camera);
    Ok(rg.Epingler() && rg.Etat.Duo is { Libelle: "Caméra salle" } d3 && d3.Etat.Mode == "media" && d3.Etat.Media == camera.Source && rg.Etat.Media == null, "une source Display épinglée (caméra)");
    rg.AfficherAnnonce(image);
    Ok(rg.Etat.Mode == "media" && rg.Etat.Media == image.Source && rg.Etat.Duo?.Etat.Media == camera.Source, "deux sources Display en même temps");
    rg.AfficherVerset(42, 3, 16, 16);
    Ok(rg.Etat.Media == null && rg.Etat.Duo?.Etat.Media == camera.Source, "une source épinglée + un verset");
    Ok(rg.Epingler("  Mon libellé  ") && rg.Etat.Duo?.Libelle == "Mon libellé", "libellé choisi (menu « Afficher puis épingler »)");
    // tout retirer : la source OBS se cache
    oeil = null;
    rg.Desepingler();
    Ok(rg.Etat.Vide && rg.Etat.Duo == null && oeil == false, "plus rien à l'écran : la source OBS se cache");
    // un Duo n'est pas gardé d'une séance à l'autre
    var rg2 = new Regie(Donnees.Charger(don.Dossier, res), new Bibles(don));
    Ok(rg2.Etat.Duo == null && rg2.Etat.Vide, "au démarrage : pas de Duo");
}

Groupe("Enregistrements et sorties");
if (Actif())
{
    // dispositions de la vidéo
    var c1 = MyVerse.Audio.RecordingProfile.Cadres(1, "incrustation");
    Ok(c1.Count == 1 && c1[0] == (0, 0, 1, 1), "une source Display : plein écran");
    var c3 = MyVerse.Audio.RecordingProfile.Cadres(3, "incrustation");
    Ok(c3.Count == 3 && c3[0] == (0, 0, 1, 1) && c3.Skip(1).All(x => x.W == 0.3 && x.X >= 0 && x.Y >= 0 && x.X + x.W <= 1 && x.Y + x.H <= 1) && c3[1].X > c3[2].X, "incrustation : la 1re en grand, les autres en petit en bas à droite");
    var c2 = MyVerse.Audio.RecordingProfile.Cadres(2, "cote");
    Ok(c2.Count == 2 && c2[0] == (0, 0.25, 0.5, 0.5) && c2[1] == (0.5, 0.25, 0.5, 0.5), "côte à côte : deux moitiés à la forme de l'écran, centrées en hauteur");
    var c5 = MyVerse.Audio.RecordingProfile.Cadres(5, "cote");
    Ok(c5.Count == 5 && c5.All(x => Math.Abs(x.W - 1 / 3d) < 1e-9 && x.W == x.H && x.Y >= 0 && x.Y + x.H <= 1.000001), "côte à côte : 5 sources en grille 3 × 2, sans déformation");
    var c4 = MyVerse.Audio.RecordingProfile.Cadres(4, "cote");
    Ok(c4.Count == 4 && c4.All(x => x.W == 0.5 && x.H == 0.5) && c4[3] == (0.5, 0.5, 0.5, 0.5), "côte à côte : 4 sources en grille 2 × 2");

    // 3.1 : une bande par entrée d'une carte son (entrée 0 = toutes ensemble)
    var ent = new MyVerse.Audio.AudioConfiguration();
    ent.Sources.Add(new MyVerse.Audio.AudioSource { Id = "e3", Kind = MyVerse.Audio.AudioInputKind.Microphone, Channel = 3 });
    ent.Sources.Add(new MyVerse.Audio.AudioSource { Id = "trop", Kind = MyVerse.Audio.AudioInputKind.Microphone, Channel = 99 });
    ent.Sources.Add(new MyVerse.Audio.AudioSource { Id = "neg", Kind = MyVerse.Audio.AudioInputKind.Microphone, Channel = -2 });
    ent.Sources.Add(new MyVerse.Audio.AudioSource { Id = "media", Kind = MyVerse.Audio.AudioInputKind.Media, Channel = 2 });
    ent.Normalize();
    Ok(ent.Sources.Select(x => x.Channel).SequenceEqual(new[] { 3, MyVerse.Audio.AudioConfiguration.MaxEntrees, 0, 0 }), "entrée d'un micro : gardée, bornée, et 0 hors micro", string.Join(",", ent.Sources.Select(x => x.Channel)));
    var entJson = System.Text.Json.JsonSerializer.Serialize(ent, Donnees.Json);
    var entRelu = System.Text.Json.JsonSerializer.Deserialize<MyVerse.Audio.AudioConfiguration>(entJson, Donnees.Json)!;
    Ok(entRelu.Sources[0].Channel == 3, "l'entrée d'une bande est enregistrée dans les réglages");
    var ancien = System.Text.Json.JsonSerializer.Deserialize<MyVerse.Audio.AudioConfiguration>("{\"obsSources\":[{\"Id\":\"m\",\"Name\":\"Micro\",\"Kind\":0}]}", Donnees.Json)!;
    ancien.Normalize();
    Ok(ancien.Sources.Count == 1 && ancien.Sources[0].Channel == 0, "réglages de la 3.0 : un micro garde toutes ses entrées ensemble");
    var cfg = new MyVerse.Audio.AudioConfiguration();
    foreach (var n in new[] { "guitare", "piano", "chanteur", "pasteur" }) cfg.Sources.Add(new MyVerse.Audio.AudioSource { Id = n, Name = n });
    var louange = new MyVerse.Audio.RecordingProfile { Name = "Louange", SourceIds = { "guitare", "piano", "chanteur" } };
    var predication = new MyVerse.Audio.RecordingProfile { Name = "Prédication", SourceIds = { "pasteur" }, MuteOthers = true, DisplayIds = { "projection", "camera1" }, Layout = "cote" };
    cfg.Profiles.Add(louange); cfg.Profiles.Add(predication);
    var zoom = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", Enabled = true, SourceIds = { "pasteur", "inconnue" } };
    var flux = new MyVerse.Audio.VirtualSoundSettings { Name = "Direct YouTube", SourceIds = { "guitare", "piano", "chanteur" } };
    cfg.VirtualSounds.Add(zoom); cfg.VirtualSounds.Add(flux);
    cfg.Normalize();
    Ok(louange.AudioOnly && louange.Format == "wav" && !predication.AudioOnly && predication.Format == "mkv", "sans source Display : son seulement (WAV) ; avec : vidéo MKV");
    Ok(zoom.SourceIds.SequenceEqual(new[] { "pasteur" }), "sources inconnues retirées");
    Ok(louange.Directory.EndsWith("Louange"), "dossier propre à chaque enregistrement : Vidéos\\MyChurch\\Louange");
    var vis = predication.VisuelsDisplay();
    Ok(vis.Count == 2 && vis[0].Kind == "projection" && vis[1].Kind == "annonce" && vis[1].Annonce == "camera1" && vis[1].X == 0.5, "vidéo : la projection et la caméra côte à côte");
    // pistes d'OBS : une par mélange différent en cours
    Ok(cfg.AttribuerPistes(Array.Empty<string>()) && zoom.Track > 0 && louange.Track == 0 && flux.Track == 0, "rien n'enregistre : seule la sortie Zoom activée a une piste");
    var z = zoom.Track;
    Ok(cfg.Sources.First(x => x.Id == "pasteur").Tracks == 1 << (z - 1) && cfg.Sources.First(x => x.Id == "guitare").Tracks == 0, "le pasteur va dans la piste de Zoom, la guitare nulle part");
    Ok(cfg.AttribuerPistes(new[] { louange.Id }) && louange.Track > 0 && louange.Track != z && zoom.Track == z, "Louange démarre : une deuxième piste, Zoom garde la sienne");
    var l = louange.Track;
    flux.Enabled = true;
    Ok(cfg.AttribuerPistes(new[] { louange.Id }) && flux.Track == l && cfg.Melanges(new[] { louange.Id }) == 2, "mêmes sources que Louange : la sortie YouTube partage sa piste");
    Ok(cfg.AttribuerPistes(new[] { louange.Id, predication.Id }) && predication.Track == z && louange.Track == l, "Prédication (pasteur seul) partage la piste de Zoom ; Louange ne change pas de piste");
    Ok(cfg.Sources.First(x => x.Id == "piano").Tracks == 1 << (l - 1), "le piano va seulement dans la piste de Louange");
    // couper les autres sources
    var coupees = cfg.SourcesCoupees(new[] { predication.Id });
    Ok(coupees.SetEquals(new[] { "guitare", "piano", "chanteur" }), "Prédication en cours : guitare, piano et chanteur coupés partout");
    Ok(cfg.SourcesCoupees(new[] { louange.Id }).Count == 0, "Louange (sans l'option) ne coupe rien");
    var obs = cfg.AvecSourdines(coupees);
    Ok(obs.Sources.Count(x => x.Muted) == 3 && cfg.Sources.All(x => !x.Muted), "OBS reçoit les sourdines ; les réglages de l'utilisateur ne changent pas");
    // plus de 6 mélanges différents
    var trop = new MyVerse.Audio.AudioConfiguration();
    for (int i = 0; i < 9; i++) trop.Sources.Add(new MyVerse.Audio.AudioSource { Id = "s" + i });
    for (int i = 0; i < 7; i++) trop.Profiles.Add(new MyVerse.Audio.RecordingProfile { SourceIds = { "s" + i } });
    for (int i = 0; i < 6; i++) trop.VirtualSounds.Add(new MyVerse.Audio.VirtualSoundSettings { SourceIds = { "s" + i, "s8" } });
    trop.Normalize();
    Ok(trop.Profiles.Count == 5 && trop.VirtualSounds.Count == 4, "5 enregistrements et 4 sorties au plus");
    trop.VirtualSounds.ForEach(v => v.Enabled = true);
    Ok(!trop.AttribuerPistes(trop.Profiles.Select(p => p.Id)) && trop.Profiles.Concat<MyVerse.Audio.IDestinationAudio>(trop.VirtualSounds).Count(d => d.Track > 0) == 6, "9 mélanges différents : 6 pistes données, les autres refusées");
    // réglages de la 3.0.0 : une seule sortie MyVirtualSound sur une piste choisie à la main
    var json30 = """{"version":3,"virtualSound":{"enabled":true,"deviceId":"","track":2},"obsSources":[{"id":"a","name":"Micro","tracks":2},{"id":"b","name":"Ordi","tracks":1}],"profiles":[{"name":"Enregistrement","track":1}]}""";
    var a30 = System.Text.Json.JsonSerializer.Deserialize<MyVerse.Audio.AudioConfiguration>(json30, Donnees.Json)!;
    a30.Normalize();
    Ok(a30.VirtualSounds.Count == 1 && a30.VirtualSounds[0].Name == "Zoom" && a30.VirtualSounds[0].Enabled && a30.VirtualSounds[0].SourceIds.SequenceEqual(new[] { "a" }), "3.0.0 : la sortie MyVirtualSound devient « Zoom » avec les sources de son ancienne piste");
    Ok(a30.Profiles.Count == 0 && a30.AncienneSortie == null && a30.Version == MyVerse.Audio.AudioConfiguration.Actuelle, "3.0.0 : le profil automatique n'est pas repris");
    var relu30 = System.Text.Json.JsonSerializer.Serialize(a30, Donnees.Json);
    Ok(relu30.Contains("\"sortiesVirtuelles\"") && relu30.Contains("\"enregistrements\"") && !relu30.Contains("\"virtualSound\""), "enregistré sous les nouveaux noms");
}

Groupe("MyCam H.264 (4.1)");
if (Actif())
{
    // en-tête version 2 : une unité d'accès H.264 (Annex B), bit 3 = image clé
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 1 | 8, 0, 0, 0, 1, 0x67, 0x42 }) is { H264: true, Cle: true, Rotation: 1, Miroir: false, Debut: 4 }, "en-tête MC 2 : H.264, image clé, quart de tour");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 4, 0, 0, 1, 0x41 }) is { H264: true, Cle: false, Miroir: true }, "en-tête MC 2 : code de début court (00 00 01), miroir");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 0, 0xFF, 0xD8, 0, 0 }) == null, "en-tête MC 2 : pas du H.264, refusé");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 3, 0, 0, 0, 0, 1 }) == null, "en-tête : version inconnue refusée");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 1, 0, 0xFF, 0xD8, 0, 0 }) is { H264: false, Cle: false }, "en-tête MC 1 : toujours le JPEG");

    // FFmpeg : celui d'OBS (MYCHURCH_OBS) sous Windows, celui du système sous Linux
    bool ffmpegAttendu = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("MYCHURCH_OBS") != null
        : new[] { "/usr/lib/x86_64-linux-gnu", "/usr/lib64", "/usr/lib" }.Any(d => Directory.Exists(d) && Directory.GetFiles(d, "libavcodec.so.*").Length > 0);
    Console.WriteLine($"  décodeur : {DecodeurH264.Etat}");
    if (!ffmpegAttendu && !DecodeurH264.Disponible) Console.WriteLine("  (FFmpeg absent : essais du décodage H.264 non faits)");
    else
    {
        Ok(DecodeurH264.Disponible, "FFmpeg chargé, la mire H.264 se décode (couleurs vérifiées)", DecodeurH264.Etat);
        using var dec = DecodeurH264.Creer();
        Ok(dec != null, "décodeur H.264 créé");
        if (dec != null)
        {
            var m = DecodeurH264.Mire;
            var c = DecodeurH264.CouleursMire;
            for (int k = 0; k < m.Length; k++)
            {
                var px = dec.Decoder(m[k], 0, m[k].Length, out var l, out var h);
                Ok(px != null && l == 64 && h == 48 && px.Length == 64 * 48 * 4 && DecodeurH264.Proche(px, l, 32, 24, c[k]) && DecodeurH264.Proche(px, l, 2, 2, c[k]),
                    $"image {k} ({(k == 0 ? "clé" : "P")}) : 64 × 48 BGRA, couleurs plein écart BT.601 comme le JPEG",
                    px == null ? "rien" : $"{l} × {h}, centre {px[(24 * 64 + 32) * 4]},{px[(24 * 64 + 32) * 4 + 1]},{px[(24 * 64 + 32) * 4 + 2]}");
            }
            var bidon = new byte[] { 0, 0, 0, 1, 0x65, 0x88, 0x84, 0, 0x33, 0xFF, 1, 2, 3, 4 };
            Ok(dec.Decoder(bidon, 0, bidon.Length, out _, out _) == null, "données invalides : aucune image, pas de plantage");
            Ok(dec.Decoder(bidon, 0, 0, out _, out _) == null && dec.Decoder(bidon, 10, 9, out _, out _) == null, "longueurs incohérentes refusées");
        }

        // MyCams : P avant la clé → rien, image clé demandée ; clé → BGRA tourné ; P → image suivante ; arrêt → décodeur libéré
        byte[] Au(byte[] au, bool cle, int rotation = 0)
        {
            var b = new byte[au.Length + 4];
            b[0] = (byte)'M'; b[1] = (byte)'C'; b[2] = 2; b[3] = (byte)(rotation | (cle ? 8 : 0));
            au.CopyTo(b, 4);
            return b;
        }
        var camsH = new MyCams();
        var demandes = new List<string>();
        camsH.Demande += (id, j) => { lock (demandes) demandes.Add(id + ":" + (string?)j["t"]); };
        var p1 = Au(DecodeurH264.Mire[1], false);
        Ok(camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length), "H.264 : message MyCam reconnu");
        Ok(camsH.Dernier("tel-h264") == null && demandes.Contains("tel-h264:cam.cle"), "H.264 sans image clé : rien d'affiché, image clé demandée au téléphone", string.Join(",", demandes));
        camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length);
        Egal(demandes.Count(x => x == "tel-h264:cam.cle"), 1, "image clé demandée au plus une fois par seconde");
        var k0 = Au(DecodeurH264.Mire[0], true, 1);
        camsH.Recevoir("tel-h264", "Pixel", k0, k0.Length);
        var ch = camsH.Dernier("tel-h264");
        Ok(ch is { Pixels: not null, Largeur: 64, Hauteur: 48, Rotation: 1, Jpeg.Length: 0 } && DecodeurH264.Proche(ch.Pixels, 64, 32, 24, DecodeurH264.CouleursMire[0]),
            "H.264 : image clé décodée en BGRA, quart de tour gardé");
        Ok(camsH.InfoDe("tel-h264") is { EnDirect: true, Codec: "H.264" } i && i.Resume.Contains("H.264"), "H.264 : le téléphone diffuse, format affiché", camsH.InfoDe("tel-h264")?.Resume);
        long n0 = ch?.Numero ?? 0;
        var p2 = Au(DecodeurH264.Mire[2], false, 1);
        camsH.Recevoir("tel-h264", "Pixel", p1, p1.Length);
        camsH.Recevoir("tel-h264", "Pixel", p2, p2.Length);
        var ch2 = camsH.Dernier("tel-h264");
        Ok(ch2 is { Pixels: not null } && ch2.Numero == n0 + 2 && DecodeurH264.Proche(ch2.Pixels, 64, 32, 24, DecodeurH264.CouleursMire[2]), "H.264 : images P décodées à la suite");
        camsH.Infos("tel-h264", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","diffuse":false}""")!.AsObject());
        Ok(camsH.Dernier("tel-h264") == null, "diffusion arrêtée : plus d'image");
        demandes.Clear();
        camsH.Recevoir("tel-h264", "Pixel", p2, p2.Length);
        Ok(camsH.Dernier("tel-h264") == null && demandes.Contains("tel-h264:cam.cle"), "reprise : le nouveau décodeur attend une image clé", string.Join(",", demandes));
        camsH.Recevoir("tel-h264", "Pixel", k0, k0.Length);
        Ok(camsH.Dernier("tel-h264") is { Pixels: not null }, "reprise : l'image clé repart");
        camsH.Deconnecte("tel-h264");
        Ok(camsH.Dernier("tel-h264") == null && camsH.InfoDe("tel-h264") is { EnDirect: false }, "déconnecté : décodeur libéré, plus d'image");
    }
}

Groupe("MyCam 4.0");
if (Actif())
{
    // images : en-tête « MC », version 1, quarts de tour et miroir, puis le JPEG
    var img = new byte[] { (byte)'M', (byte)'C', 1, 5, 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
    Ok(MyCams.LireEntete(img) is { Rotation: 1, Miroir: true, Debut: 4 }, "en-tête MC : quart de tour et miroir");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 2, 0, 0xFF, 0xD8, 0, 0 }) == null, "en-tête : autre version refusée");
    Ok(MyCams.LireEntete(new byte[] { (byte)'M', (byte)'C', 1, 0, 0x89, 0x50, 0, 0 }) == null, "en-tête : pas un JPEG, refusé");
    var cams = new MyCams();
    int nCadres = 0;
    cams.NouveauCadre += _ => nCadres++;
    Ok(cams.Recevoir("tel-cam", "Pixel", img, img.Length), "image reçue");
    var c1 = cams.Dernier("tel-cam");
    Ok(c1 != null && c1.Jpeg.Length == img.Length - 4 && c1.Jpeg[0] == 0xFF && c1.Rotation == 1 && c1.Miroir, "dernière image : le JPEG sans l'en-tête");
    Ok(nCadres == 1 && cams.InfoDe("tel-cam") is { EnDirect: true }, "le téléphone diffuse");
    cams.Infos("tel-cam", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","nom":"Caméra · Chaire","taille":"1280 × 720","fps":30,"batterie":78,"charge":false,"temperature":34.5,"lien":"wifi","diffuse":true,"depuis":1700000000000}""")!.AsObject());
    var inf = cams.InfoDe("tel-cam");
    Ok(inf is { Nom: "Caméra · Chaire", Batterie: 78, Lien: "wifi", Temperature: 34.5, Depuis: 1700000000000 } && inf.Resume.Contains("1280 × 720") && inf.Resume.Contains("78 %"), "cam.info : nom, image, batterie", inf?.Resume);
    cams.Infos("tel-cam", "Pixel", System.Text.Json.Nodes.JsonNode.Parse("""{"t":"cam.info","diffuse":false}""")!.AsObject());
    Ok(cams.Dernier("tel-cam") == null && cams.InfoDe("tel-cam") is { EnDirect: false, Nom: "Caméra · Chaire" }, "diffusion arrêtée : plus d'image, le nom reste");
    Ok(cams.Recevoir("tel-cam", "Pixel", new byte[] { 1, 2, 3 }, 3) == false, "message binaire inconnu ignoré");
    cams.Deconnecte("tel-cam");
    Ok(cams.Liste().Count == 1 && !cams.Liste()[0].EnDirect, "déconnecté : le téléphone reste connu");
    Egal(MyCams.Duree(0), "", "durée vide");

    // source Display MyCam et témoin rouge / orange
    var don = Donnees.Charger(Temp(), res);
    var rg = new Regie(don, new Bibles(don));
    var cam = rg.NouveauDisplay("Caméra · Chaire", new SourceDisplay { Type = "mycam", Peripherique = "tel-cam" });
    Ok(cam.Description == "Téléphone MyCam", "source Display MyCam");
    rg.AfficherAnnonce(cam);
    Ok(rg.Etat is { Mode: "media", Media.Type: "mycam" }, "MyCam à l'écran");
    Egal(rg.TemoinMyCam("tel-cam"), "live", "témoin rouge : la caméra est à l'écran");
    Egal(rg.TemoinMyCam("autre"), "off", "témoin éteint pour un autre téléphone");
    bool noir = true;
    rg.EstNoir = () => noir;
    Egal(rg.TemoinMyCam("tel-cam"), "off", "écran noir : témoin éteint");
    noir = false;
    rg.Effacer();
    Egal(rg.TemoinMyCam("tel-cam"), "off", "retirée : témoin éteint");
    rg.ViderAgenda();
    var eV = rg.AjouterBibleAgenda(new Ref(43, 3, 16, 16));
    var eC = rg.AjouterAnnonceAgenda(cam.Id)!;
    Ok(eC.Type == "display" && rg.Description(eC).Sous == "Téléphone MyCam", "MyCam dans le déroulé");
    rg.AfficherElement(eV);
    Egal(rg.TemoinMyCam("tel-cam"), "next", "témoin orange : la caméra vient avec Suivant");
    rg.Bouger(1);
    Egal(rg.TemoinMyCam("tel-cam"), "live", "Suivant : la caméra passe à l'écran");
    var eP = rg.AjouterPrincipalAgenda(cam.Id)!;
    Ok(eP.Source is { Type: "mycam" } && rg.Description(eP).Sous.StartsWith("MyCam"), "écran principal MyCam");
    rg.AfficherElement(eP);
    Ok(rg.Etat.Mode == "media" && rg.TemoinMyCam("tel-cam") == "live", "écran principal MyCam : témoin rouge");
    rg.Effacer();
    Egal(rg.TemoinMyCam("tel-cam"), "live", "Retirer revient sur l'écran principal MyCam : témoin rouge");
    rg.ChoisirEcranDefaut("noir");
    Ok(rg.Etat.Vide && rg.TemoinMyCam("tel-cam") == "off", "écran par défaut noir : l'écran se vide, témoin éteint");

    // protocole du téléphone : bienvenue 2, images binaires, témoin, messages, déroulé, écran par défaut, noir
    using var tel = new Telephone(don.Dossier, rg, f => f(), "PC-TEST");
    tel.Demarrer();
    bool noirPc = false;
    rg.EstNoir = () => noirPc;
    rg.BasculerNoir = () => { noirPc = !noirPc; tel.Actualiser(); };
    var url = tel.OuvrirJumelage();
    var q = System.Web.HttpUtility.ParseQueryString(new Uri(url.Replace("verselight://pair", "http://x/pair")).Query);
    var cle = Telephone.DeriverCle(Telephone.B64Dec(q["secret"]!), tel.PcId, "tel-cam");
    var ws = new System.Net.WebSockets.ClientWebSocket();
    await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{tel.Port}/"), CancellationToken.None);
    async Task Env(object o)
    {
        if (ws.State != System.Net.WebSockets.WebSocketState.Open) return;
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(o)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    }
    async Task<System.Text.Json.Nodes.JsonObject?> Rec(string? type = null, int ms = 3000, Func<System.Text.Json.Nodes.JsonObject, bool>? si = null)
    {
        var fin = DateTime.Now.AddMilliseconds(ms);
        var buf = new byte[1 << 16];
        while (DateTime.Now < fin && ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(fin - DateTime.Now);
            using var mem = new MemoryStream();
            try
            {
                System.Net.WebSockets.WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buf, cts.Token); mem.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.WebSockets.WebSocketException) { return null; }
            var o = System.Text.Json.Nodes.JsonNode.Parse(mem.ToArray())!.AsObject();
            if ((type == null || (string?)o["t"] == type) && (si == null || si(o))) return o;
        }
        return null;
    }
    await Env(new { t = "hello", v = 1, pcId = tel.PcId, deviceId = "tel-cam", deviceName = "Pixel 8", pair = true, cnonce = "cn" });
    var ch = await Rec("challenge");
    await Env(new { t = "auth", mac = Telephone.MacClient(cle, (string)ch!["nonce"]!, "tel-cam") });
    var w = await Rec("welcome");
    Egal((int?)w?["proto"], 2, "bienvenue : fonctions 4.0 annoncées");
    var codecsPc = w?["codecs"]?.AsArray().Select(x => (string?)x).ToList() ?? new();
    Ok(codecsPc.Contains("jpeg") && codecsPc.Contains("h264") == MyCams.H264, "bienvenue : formats MyCam annoncés (H.264 si FFmpeg est là)", string.Join(",", codecsPc));
    await Env(new { t = "state.get" });
    var st = await Rec("state");
    Ok(st?["defauts"]?.AsArray().Count >= 2 && (string?)st?["defaut"]?["id"] == "noir" && st?["noir"] is not null && st?["agendaV"] is not null, "état : écran par défaut, noir, déroulé");

    await Env(new { t = "cam.info", nom = "Caméra · Chaire", taille = "1280 × 720", fps = 30, batterie = 81, diffuse = true, lien = "wifi" });
    var cadre = new byte[] { (byte)'M', (byte)'C', 1, 0, 0xFF, 0xD8, 0xFF, 0xD9 };
    if (ws.State == System.Net.WebSockets.WebSocketState.Open) await ws.SendAsync(cadre, System.Net.WebSockets.WebSocketMessageType.Binary, true, CancellationToken.None);
    for (int k = 0; k < 40 && tel.Cameras.Dernier("tel-cam") == null; k++) await Task.Delay(25);
    Ok(tel.Cameras.Dernier("tel-cam") is { Jpeg.Length: 4 }, "image MyCam reçue par le PC");
    Ok(tel.Cameras.InfoDe("tel-cam") is { Nom: "Caméra · Chaire", Batterie: 81 }, "infos MyCam reçues par le PC");
    var t0 = await Rec("cam.tally");
    Egal((string?)t0?["etat"], "off", "témoin envoyé dès la première image");
    rg.AfficherAnnonce(cam);
    var t1 = await Rec("cam.tally");
    Egal((string?)t1?["etat"], "live", "témoin rouge envoyé au téléphone");
    st = await Rec("state", si: o => (string?)o["mode"] == "media");
    Ok((string?)st?["sous"] != null && ((string?)st?["sous"] ?? "").StartsWith("MyCam"), "état : sous-titre MyCam", st?["sous"]?.ToJsonString());
    await Env(new { t = "noir" });
    var t2 = await Rec("cam.tally");
    Ok(noirPc && (string?)t2?["etat"] == "off", "Noir depuis le téléphone : écran noir et témoin éteint");
    st = await Rec("state", si: o => (bool?)o["noir"] == true);
    Ok(st != null, "état : noir");
    await Env(new { t = "noir" });
    Egal((string?)(await Rec("cam.tally"))?["etat"], "live", "Noir de nouveau : la caméra revient");

    string? vu = null;
    tel.MessageVu += (_, texte) => vu = texte;
    Ok(tel.EnvoyerMessageCam("tel-cam", "Plan serré sur le pasteur"), "message au caméraman envoyé");
    Ok(!tel.EnvoyerMessageCam("inconnu", "x"), "message à un téléphone absent : refusé");
    var msg = await Rec("cam.msg");
    Egal((string?)msg?["texte"], "Plan serré sur le pasteur", "message reçu par le téléphone");
    await Env(new { t = "cam.ack", id = (string?)msg?["id"] });
    for (int k = 0; k < 40 && vu == null; k++) await Task.Delay(25);
    Egal(vu, "Plan serré sur le pasteur", "« Vu » du caméraman reçu par la régie");

    await Env(new { t = "agenda.add", type = "bible", @ref = "ps 23 1", rid = 1 });
    await Env(new { t = "agenda.get", rid = 2 });
    var ag = await Rec("agenda", si: o => o["elements"]?.AsArray().Any(x => ((string?)x?["titre"] ?? "").StartsWith("Psaume")) == true);
    Ok(ag != null && ag["elements"]!.AsArray().Any(x => (string?)x?["source"] == "mycam"), "déroulé envoyé au téléphone (avec la source MyCam)", ag?.ToJsonString());
    var idPs = (string?)ag?["elements"]?.AsArray().FirstOrDefault(x => ((string?)x?["titre"] ?? "").StartsWith("Psaume"))?["id"];
    await Env(new { t = "agenda.show", id = idPs });
    st = await Rec("state", si: o => (string?)o["element"] == idPs);
    Ok(st != null && (string?)st["mode"] == "verset", "élément du déroulé affiché depuis le téléphone");
    await Env(new { t = "agenda.show", id = "absent" });
    Ok(((string?)(await Rec("error"))?["message"] ?? "").Contains("déroulé"), "élément absent : refusé");
    await Env(new { t = "default.set", id = "fond" });
    st = await Rec("state", si: o => (string?)o["defaut"]?["id"] == "fond");
    Ok(st != null, "écran par défaut choisi depuis le téléphone");
    await Env(new { t = "clear" });
    st = await Rec("state", si: o => (bool?)o["surDefaut"] == true);
    Ok(st != null && (string?)st["ecran"] == "fond" && (string?)st["mode"] == "vide", "Retirer : le fond seul (« vide » pour les anciennes apps)");
    if (ws.State == System.Net.WebSockets.WebSocketState.Open) await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
    else ws.Abort();
    for (int k = 0; k < 40 && tel.Cameras.InfoDe("tel-cam")?.Diffuse == true; k++) await Task.Delay(25);
    Ok(tel.Cameras.InfoDe("tel-cam") is { Diffuse: false }, "téléphone parti : MyCam ne diffuse plus");
}

Groupe("Audio comme OBS 32 (4.0)");
if (Actif())
{
    var a = new MyVerse.Audio.AudioConfiguration();
    a.Normalize();
    Ok(a.SampleRate == 48000 && a.Channels == "stereo" && a.HautParleurs == 2 && a.MeterDecay == "rapide" && a.PeakMeter == "echantillon",
        "réglages d'OBS par défaut : 48 kHz, stéréo, retombée rapide, crête d'échantillon");
    Ok(a.AutoAddInputs && a.VerticalLayout && !a.ShowHidden && !a.ShowInactive && !a.LowLatency && !a.DisableDucking,
        "par défaut : entrées branchées ajoutées toutes seules, mélangeur vertical, masquées et inactives cachées");
    Ok(Math.Abs(a.RetombeeDbParSeconde - 23.53) < 1e-9, "retombée rapide d'OBS : 23,53 dB/s");
    a.SampleRate = 96000; a.Channels = "9.1"; a.MeterDecay = "?"; a.PeakMeter = "?"; a.Normalize();
    Ok(a.SampleRate == 48000 && a.Channels == "stereo" && a.MeterDecay == "rapide" && a.PeakMeter == "echantillon", "valeurs inconnues ramenées à celles d'OBS par défaut");
    a.SampleRate = 44100; a.Channels = "5.1"; a.MeterDecay = "lente"; a.PeakMeter = "reelle"; a.Normalize();
    Ok(a.SampleRate == 44100 && a.HautParleurs == 6 && Math.Abs(a.RetombeeDbParSeconde - 8.57) < 1e-9 && a.TypeCrete == 1, "44,1 kHz, 5.1 (speaker_layout 6), retombée lente, crête réelle gardés");
    a.Channels = "7.1"; a.Normalize();
    Egal(a.HautParleurs, 8, "7.1 : speaker_layout 8 de libobs");
    var vieux = new MyVerse.Audio.AudioConfiguration { Version = 2, SampleRate = 44100, Channels = "5.1" };
    vieux.Normalize();
    Ok(vieux.SampleRate == 48000 && vieux.Channels == "stereo" && vieux.Version == MyVerse.Audio.AudioConfiguration.Actuelle, "réglages d'avant la 4.0 : la fréquence de la 1.2 bêta n'est pas reprise");

    // périphériques globaux
    var g = new MyVerse.Audio.AudioConfiguration();
    Ok(g.InitialiserGlobaux() && g.GlobalsInitialized, "premier démarrage : périphériques globaux créés, comme OBS");
    var bureau = g.Globale("bureau1"); var micro = g.Globale("micro1");
    Ok(bureau is { Kind: MyVerse.Audio.AudioInputKind.Sortie, DeviceId: "default", Name: "Audio du bureau" } && micro is { Kind: MyVerse.Audio.AudioInputKind.Microphone, DeviceId: "default", Name: "Micro/Aux" },
        "« Audio du bureau » (sortie) et « Micro/Aux » (micro) sur les périphériques par défaut");
    Ok(!g.InitialiserGlobaux() && g.Sources.Count == 2, "une seule fois : pas de doublon au démarrage suivant");
    var plein = new MyVerse.Audio.AudioConfiguration { Sources = { new MyVerse.Audio.AudioSource { Name = "Pasteur" } } };
    Ok(!plein.InitialiserGlobaux() && plein.Sources.Count == 1 && plein.GlobalsInitialized, "mélangeur déjà rempli (mise à jour) : rien n'est ajouté");
    var autre = new MyVerse.Audio.AudioSource { Name = "Chorale" };
    g.Sources.Add(autre);
    var m2 = g.RegleGlobal("micro2", "{usb}", "Microphone (Blue Yeti)");
    Ok(m2 != null && g.Sources.IndexOf(m2) == 2 && g.Sources[3] == autre && m2.DeviceName == "Microphone (Blue Yeti)", "Micro/Aux 2 réglé : placé avec les globaux, avant les autres sources");
    var zoomG = new MyVerse.Audio.VirtualSoundSettings { Name = "Zoom", SourceIds = { m2!.Id, autre.Id } };
    g.VirtualSounds.Add(zoomG);
    Ok(g.RegleGlobal("micro2", null) == null && g.Globale("micro2") == null && !zoomG.SourceIds.Contains(m2.Id) && zoomG.SourceIds.Contains(autre.Id), "« Désactivé » : la source quitte le mélangeur et ses sorties");
    var re = g.RegleGlobal("bureau1", "{hdmi}", "Haut-parleurs (HDMI)");
    Ok(re == bureau && g.Sources.Count(x => x.Global == "bureau1") == 1 && bureau!.DeviceId == "{hdmi}", "changer le périphérique d'un global garde sa bande (volume, filtres, destinations)");
    g.RegleGlobal("bureau1", "default");
    Ok(bureau!.DeviceId == "default" && bureau.DeviceName == "", "retour à « Par défaut »");
    var faux = new MyVerse.Audio.AudioSource { Name = "Double", Global = "micro1", Kind = MyVerse.Audio.AudioInputKind.Sortie, Channel = 3, Hidden = true };
    var inconnu = new MyVerse.Audio.AudioSource { Name = "X", Global = "micro9" };
    g.Sources.Add(faux); g.Sources.Add(inconnu); g.Normalize();
    Ok(faux.Global == "" && inconnu.Global == "" && micro!.Kind == MyVerse.Audio.AudioInputKind.Microphone && micro.Channel == 0, "un seul global par emplacement ; emplacement inconnu ramené à une source ordinaire");
    var tordu = g.RegleGlobal("micro3", "default")!;
    tordu.Kind = MyVerse.Audio.AudioInputKind.Media; tordu.Channel = 2; tordu.Hidden = true; tordu.Pinned = true; g.Normalize();
    Ok(tordu.Kind == MyVerse.Audio.AudioInputKind.Microphone && tordu.Channel == 0 && !tordu.Hidden, "un global reste un micro entier, jamais masqué");
    var bornes = new MyVerse.Audio.AudioSource { Balance = 3, SyncOffsetMs = 50000, PushToTalkDelayMs = -5, PushToMuteDelayMs = 99999, PushToTalkKey = new string('x', 80) };
    var bornes2 = new MyVerse.Audio.AudioSource { Balance = double.NaN, SyncOffsetMs = -5000 };
    g.Sources.Add(bornes); g.Sources.Add(bornes2); g.Normalize();
    Ok(bornes.Balance == 1 && bornes2.Balance == 0.5 && bornes.SyncOffsetMs == 20000 && bornes2.SyncOffsetMs == -950, "balance (0 à 1) et décalage de synchro (−950 à 20 000 ms) dans les bornes d'OBS");
    Ok(bornes.PushToTalkDelayMs == 0 && bornes.PushToMuteDelayMs == 10000 && bornes.PushToTalkKey == "", "délais des raccourcis bornés, touche illisible oubliée");

    // le mélangeur d'OBS 32.1 : globales, épinglées, masquées, inactives
    var mx = new MyVerse.Audio.AudioConfiguration();
    var sa = new MyVerse.Audio.AudioSource { Name = "A" };
    var sh = new MyVerse.Audio.AudioSource { Name = "Masquée", Hidden = true };
    var si = new MyVerse.Audio.AudioSource { Name = "Débranchée", DeviceId = "{absent}" };
    var sp = new MyVerse.Audio.AudioSource { Name = "Épinglée", Pinned = true };
    var sz = new MyVerse.Audio.AudioSource { Name = "Z" };
    mx.Sources.AddRange(new[] { sa, sh, si, sp, sz });
    mx.RegleGlobal("micro1", "default"); mx.RegleGlobal("bureau1", "default");
    mx.Normalize();
    bool Inact(MyVerse.Audio.AudioSource x) => x.DeviceId == "{absent}";
    string Ordre() => string.Join(",", mx.Affichage(Inact).Select(x => x.Name));
    Egal(Ordre(), "Audio du bureau,Micro/Aux,Épinglée,A,Z", "OBS 32.1 : globales puis épinglées, masquées et inactives cachées");
    mx.ShowHidden = true;
    Egal(Ordre(), "Audio du bureau,Micro/Aux,Épinglée,A,Masquée,Z", "« Afficher les sources masquées » : à sa place");
    mx.HiddenRight = true;
    Egal(Ordre(), "Audio du bureau,Micro/Aux,Épinglée,A,Z,Masquée", "« Garder les sources masquées à droite »");
    mx.ShowInactive = true; mx.InactiveRight = true;
    Egal(Ordre(), "Audio du bureau,Micro/Aux,Épinglée,A,Z,Masquée,Débranchée", "« Afficher les sources inactives » et à droite");
    mx.InactiveRight = false;
    Egal(Ordre(), "Audio du bureau,Micro/Aux,Épinglée,A,Débranchée,Z,Masquée", "inactive à sa place sans « garder à droite »");
    si.Pinned = true; mx.ShowInactive = false;
    Ok(mx.Affichage(Inact).Contains(si), "une source épinglée reste affichée même débranchée");
    Ok(MyVerse.Audio.AudioConfiguration.Etat(mx.Globale("micro1")!, true) == MyVerse.Audio.EtatMelangeur.Globale
        && MyVerse.Audio.AudioConfiguration.Etat(si, true) == MyVerse.Audio.EtatMelangeur.Epinglee
        && MyVerse.Audio.AudioConfiguration.Etat(sh, false) == MyVerse.Audio.EtatMelangeur.Masquee
        && MyVerse.Audio.AudioConfiguration.Etat(sz, true) == MyVerse.Audio.EtatMelangeur.Inactive
        && MyVerse.Audio.AudioConfiguration.Etat(sa, false) == MyVerse.Audio.EtatMelangeur.Active, "étiquettes d'OBS : Globale, Épinglée, Masquée, Inactive, Active");

    // une entrée branchée s'ajoute toute seule
    var hp = new MyVerse.Audio.AudioConfiguration();
    var yeti = new MyVerse.Audio.AudioDevice("{yeti}", "Microphone (Blue Yeti)");
    var webcam = new MyVerse.Audio.AudioDevice("{cam}", "Microphone (Webcam C920)");
    var usb = new MyVerse.Audio.AudioDevice("{usb}", "Line In (Behringer UMC404HD)");
    Ok(hp.NouvellesEntrees(new[] { yeti, webcam }).Count == 0 && hp.KnownInputsReady && hp.KnownInputs.Count == 2, "premier relevé : ce qui est déjà branché est noté, rien n'est ajouté");
    var nv = hp.NouvellesEntrees(new[] { yeti, webcam, usb });
    Ok(nv.Count == 1 && nv[0] == usb, "carte son branchée ensuite : reconnue comme nouvelle");
    Ok(hp.NouvellesEntrees(new[] { yeti, usb }).Count == 0 && hp.NouvellesEntrees(new[] { yeti, webcam, usb }).Count == 0, "débranchée puis rebranchée : pas une nouvelle entrée");
    var ajoutee = hp.AjouterEntree(usb);
    Ok(ajoutee is { Kind: MyVerse.Audio.AudioInputKind.Microphone, DeviceId: "{usb}", Channel: 0, Name: "Behringer UMC404HD" }, "ajoutée au mélangeur : une bande stéréo au nom court du périphérique");
    Ok(hp.AjouterEntree(usb) == null, "déjà dans le mélangeur : pas de deuxième bande");
    hp.Sources.Add(new MyVerse.Audio.AudioSource { Name = "Blue Yeti" });
    Egal(hp.AjouterEntree(yeti)?.Name, "Blue Yeti 2", "nom déjà pris : « Blue Yeti 2 »");
    while (hp.Sources.Count < MyVerse.Audio.AudioConfiguration.MaxSources) hp.Sources.Add(new MyVerse.Audio.AudioSource());
    Ok(hp.AjouterEntree(webcam) == null, "mélangeur plein (32 bandes) : rien n'est ajouté");

    // appuyer pour parler / appuyer pour couper le son (règle de libobs)
    var ptt = new MyVerse.Audio.AudioSource { PushToTalk = true, PushToTalkDelayMs = 300 };
    var et = new MyVerse.Audio.EtatRaccourcis();
    Ok(et.Coupee(ptt, false, false, 0), "appuyer pour parler : coupée tant que la touche n'est pas enfoncée");
    Ok(!et.Coupee(ptt, true, false, 10), "touche enfoncée : le son passe");
    Ok(!et.Coupee(ptt, false, false, 20) && !et.Coupee(ptt, false, false, 319), "touche relâchée : le son passe encore pendant le délai (300 ms)");
    Ok(et.Coupee(ptt, false, false, 320), "délai écoulé : coupée");
    var ptm = new MyVerse.Audio.AudioSource { PushToMute = true };
    var et2 = new MyVerse.Audio.EtatRaccourcis();
    Ok(!et2.Coupee(ptm, false, false, 0) && et2.Coupee(ptm, false, true, 5) && !et2.Coupee(ptm, false, false, 6), "appuyer pour couper le son : coupée seulement touche enfoncée (délai 0)");
    Ok(!new MyVerse.Audio.EtatRaccourcis().Coupee(new MyVerse.Audio.AudioSource(), false, false, 0), "sans raccourci : jamais coupée");

    // fréquence du moteur : les blocs de 10 ms suivent
    MyVerse.Audio.AudioBlock.Configurer(44100);
    Ok(MyVerse.Audio.AudioBlock.Rate == 44100 && MyVerse.Audio.AudioBlock.Frames == 441 && MyVerse.Audio.AudioBlock.Samples == 882, "44,1 kHz : blocs de 441 trames stéréo");
    MyVerse.Audio.AudioBlock.Configurer(12345);
    Egal(MyVerse.Audio.AudioBlock.Rate, 48000, "fréquence inconnue : 48 kHz");

    // tout est enregistré
    var dirA = Temp();
    var dA4 = Donnees.Charger(dirA, res);
    var c4 = dA4.Reglages.Audio;
    c4.SampleRate = 44100; c4.Channels = "5.1"; c4.MeterDecay = "moyenne"; c4.PeakMeter = "reelle"; c4.DisableDucking = true; c4.LowLatency = true;
    c4.ShowHidden = true; c4.InactiveRight = true; c4.VerticalLayout = false; c4.AutoAddInputs = false;
    c4.NouvellesEntrees(new[] { yeti });
    c4.InitialiserGlobaux();
    var pa = new MyVerse.Audio.AudioSource { Name = "Pasteur", Pinned = true, Balance = 0.25, SyncOffsetMs = 120, PushToTalk = true, PushToTalkDelayMs = 250, PushToTalkKey = "Ctrl+F13" };
    c4.Sources.Add(pa); c4.Normalize();
    dA4.SauverReglages();
    var json4 = File.ReadAllText(Path.Combine(dirA, "reglages.json"));
    var r4 = Donnees.Charger(dirA, res).Reglages.Audio; r4.Normalize();
    var pr4 = r4.Sources.FirstOrDefault(x => x.Name == "Pasteur");
    Ok(r4.SampleRate == 44100 && r4.Channels == "5.1" && r4.MeterDecay == "moyenne" && r4.PeakMeter == "reelle" && r4.DisableDucking && r4.LowLatency,
        "Réglages › Audio relus (fréquence, canaux, vumètres, avancé)");
    Ok(r4.ShowHidden && r4.InactiveRight && !r4.VerticalLayout && !r4.AutoAddInputs && r4.KnownInputsReady && r4.KnownInputs.Contains("{yeti}") && r4.GlobalsInitialized,
        "options du mélangeur, entrées connues et globaux relus");
    Ok(r4.Globale("bureau1") != null && r4.Globale("micro1") != null, "périphériques globaux relus");
    Ok(pr4 is { Pinned: true, Balance: 0.25, SyncOffsetMs: 120, PushToTalk: true, PushToTalkDelayMs: 250, PushToTalkKey: "Ctrl+F13" }, "épinglée, balance, synchro et appuyer pour parler relus");
    Ok(!json4.Contains("\"sources\""), "toujours aucune liste « sources » dans le fichier (réservée à la 1.2 bêta)");
}

Groupe("Audio OBS");
if (Actif()) MyVerse.Tests.ObsTests.Run(Ok);
Groupe("Enregistrement");
if (Actif()) MyVerse.Tests.RecordingIntegrationTests.RunAsync(Ok).GetAwaiter().GetResult();

Console.WriteLine($"\n{total - echecs}/{total} vérifications réussies" + (echecs > 0 ? $" — {echecs} ÉCHEC(S)" : " — tout est bon"));
return echecs == 0 ? 0 : 1;
