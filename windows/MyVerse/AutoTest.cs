using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVerse.Ui;

/// <summary>
/// Test automatique (« MyChurch.exe --autotest dossier », lancé par la compilation sur GitHub) :
/// ouvre la régie, affiche des versets et des chants, ouvre la projection et chaque fenêtre,
/// enregistre des captures d'écran et un compte rendu (autotest.txt), puis ferme MyChurch (code 0 = tout va bien).
/// </summary>
public static class AutoTest
{
    static readonly List<string> echecs = new();
    static readonly List<string> log = new();
    static string dossier = "";

    public static void Echec(string m)
    {
        lock (echecs) echecs.Add(m);
        lock (log) log.Add("✗ " + m);
    }

    static void Ok(bool c, string quoi)
    {
        if (c) lock (log) log.Add("✓ " + quoi);
        else Echec(quoi);
    }

    static void Note(string m) { lock (log) log.Add("  " + m); }

    static Task Attendre(int ms) => Task.Delay(ms);

    /// <summary>Image de la fenêtre (fond compris), sans dépendre de l'écran du PC.</summary>
    static void Capture(Window? w, string nom)
    {
        if (w == null) { Echec("capture impossible (fenêtre absente) : " + nom); return; }
        try
        {
            w.UpdateLayout();
            // la racine visuelle de la fenêtre : tout l'intérieur, fond et marges compris
            if ((VisualTreeHelper.GetChildrenCount(w) > 0 ? VisualTreeHelper.GetChild(w, 0) : w.Content) is not FrameworkElement el) { Echec("capture sans contenu : " + nom); return; }
            int wd = (int)Math.Ceiling(el.ActualWidth), h = (int)Math.Ceiling(el.ActualHeight);
            if (wd <= 0 || h <= 0) { Echec("capture vide : " + nom); return; }
            var rtb = new RenderTargetBitmap(wd, h, 96, 96, PixelFormats.Pbgra32);
            var fond = new DrawingVisual();
            using (var dc = fond.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.Black, null, new Rect(0, 0, wd, h));
            rtb.Render(fond);
            rtb.Render(el);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var f = File.Create(Path.Combine(dossier, nom));
            enc.Save(f);
            Note($"capture {nom} ({wd}×{h})");
        }
        catch (Exception x) { Echec($"capture {nom} : {x.Message}"); }
    }

    static async Task Fenetre(Window owner, Func<Window> creer, string nom, Action<Window>? avant = null)
    {
        Window? d = null;
        try
        {
            d = creer();
            d.Owner = owner;
            d.ShowActivated = false;
            d.Show();
            avant?.Invoke(d);
            await Attendre(700);
            Capture(d, nom);
            Ok(true, "fenêtre " + d.Title);
        }
        catch (Exception x) { Echec($"fenêtre {nom} : {x}"); }
        finally { try { d?.Close(); } catch (Exception) { } }
    }

    static string Police(string cle)
    {
        try
        {
            var f = (FontFamily)Application.Current.FindResource(cle);
            var t = new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            return t.TryGetGlyphTypeface(out var g) ? string.Join("/", g.FamilyNames.Values) : "aucune police";
        }
        catch (Exception x) { return "erreur " + x.Message; }
    }

    public static async void Lancer(MainWindow w, string sortie)
    {
        dossier = sortie;
        Directory.CreateDirectory(sortie);
        var R = App.Regie;
        try
        {
            Note($"MyChurch {App.Version} · données : {App.Donnees.Dossier} · {App.Donnees.Chants.Count} chants");
            Note("police Serif : " + Police("Serif") + " · Sans : " + Police("Sans") + " · Titre : " + Police("Titre"));
            Ok(Police("Serif").Contains("Literata"), "police Literata incluse");
            Ok(Police("Sans").Contains("Atkinson"), "police Atkinson incluse");
            await Attendre(1500);
            Capture(w, "00-accueil-culte.png");
            w.TestOnglet(true);
            await Attendre(400);
            Capture(w, "01-bible.png");

            w.TestRecherche("jn 3 16");
            await Attendre(300);
            w.TestAfficherSelection();
            await Attendre(500);
            Ok(R.Etat.Ref == "Jean 3:16 (LSG)", "Jean 3:16 à l'écran : " + R.Etat.Ref);
            Capture(w, "02-verset.png");

            var ecrans = Natif.Ecrans();
            Note("écrans : " + string.Join(" | ", ecrans.Select(e => $"{e.Nom} {e.W}×{e.H} @ {e.X},{e.Y}{(e.Principal ? " principal" : "")}")));
            w.OuvrirSur(ecrans[0], true);
            await Attendre(1500);
            Ok(w.Ecran != null, "fenêtre de projection ouverte");
            Capture(w.Ecran, "03-ecran.png");
            R.Bouger(1);
            await Attendre(1300);
            Ok(R.Etat.Ref == "Jean 3:17 (LSG)", "Suivant → " + R.Etat.Ref);
            Capture(w.Ecran, "04-ecran-suivant.png");
            R.Presentation("bandeau");
            R.Theme("vert");
            await Attendre(1300);
            Capture(w.Ecran, "05-ecran-bandeau-vert.png");
            R.Presentation("plein");
            R.Theme("aube");
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(1300);
            Ok(R.Etat.Second != null && R.Etat.Ref.Contains("LSG · KJV"), "versets bilingues : " + R.Etat.Ref);
            Capture(w, "06-bilingue.png");
            Capture(w.Ecran, "07-ecran-bilingue.png");
            App.Bibles.BasculerSeconde("kjv");
            R.Theme("nuit");

            w.TestRecherche("charite pasiente");
            await Attendre(3000);
            Capture(w, "08-recherche.png");

            w.TestOnglet(false);
            await Attendre(400);
            var c = w.TestPremierChant();
            Ok(c != null, "liste des chants : " + (c?.Title ?? "vide"));
            if (c != null)
            {
                w.OuvrirChant(c.Id);
                R.AfficherSection(c.Id, 0);
            }
            await Attendre(1300);
            Ok(R.Etat.Mode == "chant", "chant à l'écran");
            Capture(w, "09-chants.png");
            Capture(w.Ecran, "10-ecran-chant.png");

            // recherche par numéro dans le recueil choisi (un numéro à 2 chiffres : pas de 116 ni de 160)
            var nums = w.TestNumero("").Select(x => Regie.Numero(x.Title)).ToList();
            var cible = nums.FirstOrDefault(x => x >= 10 && x <= 99) ?? nums.FirstOrDefault(x => x != null);
            if (cible is int nn)
            {
                var l = w.TestNumero(nn.ToString());
                Ok(l.Count >= 1 && l.All(x => Regie.Numero(x.Title) == nn), $"recherche par numéro {nn} : " + string.Join(" | ", l.Select(x => x.Title)));
                await Attendre(500);
                Capture(w, "09b-numero.png");
                var vide = w.TestNumero("99999");
                Ok(vide.Count == 0, "numéro absent : liste vide");
                w.TestNumero("");
            }

            // 5.0.1 : boutons « + » devant les chants (liste) et les versets (Bible)
            var agPlus = R.NouvelAgenda("Test des boutons +");
            int ajC = w.TestPlusPremierChant(), ajV = w.TestPlusPremierVerset();
            Ok(ajC == 1 && ajV == 1 && R.Agenda.Elements.Count == 2, $"boutons + devant les chants et les versets : {ajC} / {ajV}");
            R.SupprimerAgenda(agPlus.Id);

            // 5.2 (design « Aube ») : déroulé du culte, recherche Ctrl K, compte à rebours d'accueil, résumé, displays, écran de scène
            w.TestOngletAgenda();
            var agTest = R.NouvelAgenda("Culte de test");
            var compte = R.AjouterCompteAgenda(5);
            if (c != null) R.AjouterChantAgenda(c.Id);
            var lecture = R.AjouterBibleAgenda(new Ref(42, 3, 16, 17));
            R.HeureAgenda(lecture.Id, "10h30");
            R.AjouterTexteAgenda("Annonces", "Bienvenue à tous !\n\nRepas fraternel après le culte");
            Ok(R.Agenda.Elements.Count == (c != null ? 4 : 3) && R.Agenda.Elements[0].Type == "compte", $"déroulé : {R.Agenda.Elements.Count} éléments, le compte à rebours en premier");
            Ok(lecture.Heure == "10:30", "heure prévue : " + lecture.Heure);
            R.OublierResume();
            R.AfficherElement(compte, 0);
            await Attendre(1300);
            var lc = R.Etat.Lignes;
            Ok(lc.Count == 2 && lc[0].StartsWith("Le culte commence") && (lc[1] == "05:00" || lc[1].StartsWith("04:5")), "compte à rebours : " + string.Join(" / ", lc));
            await Attendre(1200);
            Ok(R.Etat.Lignes.Count == 2 && R.Etat.Lignes[1].StartsWith("04:5"), "le compte à rebours descend : " + string.Join(" / ", R.Etat.Lignes));
            if (w.Ecran != null) Capture(w.Ecran, "19b-ecran-compte.png");
            var cm = w.TestCommande("jean 3 16");
            Ok(cm.Count > 0 && cm[0].Titre == "Jean 3:16", "recherche du culte (Ctrl K) : " + string.Join(" | ", cm.Select(x => x.Titre)));
            w.TestCommande("");
            for (int i = 0; i < 15 && R.ElementCourant?.Type != "bible"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "bible" && R.Etat.Ref.StartsWith("Jean 3:16"), "déroulé : Suivant passe à la lecture · " + R.Etat.Ref);
            App.Bibles.BasculerSeconde("kjv");
            await Attendre(900);
            Capture(w, "19-culte.png");
            App.Bibles.BasculerSeconde("kjv");
            Ok(R.EstFait(compte) && w.TestResume.Contains("Accueil") && w.TestResume.Contains("Jean 3"), "résumé du culte : " + w.TestResume);
            Ok(R.DeroulePartage().Contains("10:30") && R.ResumePartage().Length > 0, "textes à partager sur WhatsApp");
            for (int i = 0; i < 5 && R.ElementCourant?.Type != "texte"; i++) R.Bouger(1);
            Ok(R.ElementCourant?.Type == "texte" && R.Etat.Lignes.FirstOrDefault() == "Bienvenue à tous !", "déroulé : puis les displays");
            await Attendre(500);
            Capture(w, "20-culte-displays.png");
            if (w.Ecran != null) Capture(w.Ecran, "21-ecran-displays.png");
            w.TestNoir();
            await Attendre(600);
            Ok(w.TestEstNoir, "écran noir (touche B)");
            R.Bouger(1);
            await Attendre(300);
            Ok(!w.TestEstNoir, "la diapositive suivante rallume l'écran");

            // fonds clairs Lin et Ciel
            R.AfficherElement(lecture, 0);
            R.Theme("lin");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22-ecran-lin.png");
            R.Theme("ciel");
            await Attendre(1300);
            if (w.Ecran != null) Capture(w.Ecran, "22b-ecran-ciel.png");
            R.Theme("aube");

            // écran de scène (minuteur de prédication)
            var scene = w.TestScene();
            await Attendre(1200);
            Ok(scene != null, "écran de scène ouvert");
            Capture(scene, "23-scene.png");
            try { scene?.Close(); } catch (Exception) { }

            // bibliothèque d'displays
            var an = R.NouvelleAnnonce("Repas fraternel", "Repas fraternel après le culte\nTout le monde est invité");
            w.TestOngletAnnonces();
            w.TestChoisirAnnonce(an);
            await Attendre(600);
            Capture(w, "24-displays.png");
            int avantAn = R.Agenda.Elements.Count;
            var elAn = R.AjouterAnnonceAgenda(an.Id);
            Ok(elAn != null && R.Agenda.Elements.Count == avantAn + 1 && elAn.Type == "texte", "display ajoutée au déroulé");
            R.AfficherAnnonce(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "display à l'écran : " + string.Join(" / ", R.Etat.Lignes));
            // MyChurch 6: the same double-click handler sends the selected title live.
            R.Effacer();
            w.TestDisplayDoubleClick(an);
            await Attendre(300);
            Ok(R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "double-clic titre Display : en direct");
            var imagePath = Path.Combine(App.Donnees.Dossier, "test-display.png");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(200, 30, 40)), null, new Rect(0, 0, 1280, 720));
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var f = File.Create(imagePath)) png.Save(f);
            var img = R.NouveauDisplay("Image de test", new SourceDisplay { Type = "image", Chemin = imagePath });
            w.TestChoisirAnnonce(img); w.TestDisplayDoubleClick(img);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "image Display chargée et projetée");
            Capture(w, "25-display-image.png");
            // 1.0.1 : grille de sources (un clic sur une tuile = en direct)
            R.GrilleAnnonce(an.Id, true);
            R.GrilleAnnonce(img.Id, true);
            await Attendre(400);
            Ok(w.TestTuiles == 2, "grille Display : " + w.TestTuiles + " tuiles (un texte, une image)");
            Ok(w.TestCliquerTuile(0) && R.Cible is CibleTexte { I: 0 } ct && ct.Element == an.Id && R.Etat.Lignes.FirstOrDefault() == "Repas fraternel après le culte", "clic sur une tuile de la grille : en direct");
            await Attendre(500);
            Capture(w, "27-display-grille.png");
            w.TestCliquerTuile(1);
            await Attendre(1300);
            Ok(R.Etat.Mode == "media" && App.Sources.Image != null, "clic sur la tuile image : en direct");
            if (w.Ecran != null) Capture(w.Ecran, "26-ecran-image.png");
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "display.jpg")), "image Display publiée pour la source navigateur OBS");
            if (App.CameraVirtuelle != null)
            {
                using var mm = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(@"Local\MyChurch.MyVirtualCam.frames.v1");
                using var view = mm.CreateViewAccessor();
                Ok(view.ReadInt32(0) == 0x4D43414D && view.ReadInt32(16) == 1280 && view.ReadInt64(8) > 0, "MyVirtualCam publie automatiquement en 720p");
                var pixel = 64 + (360 * 1280 + 640) * 3;
                Ok(view.ReadByte(pixel) == 40 && view.ReadByte(pixel + 1) == 30 && view.ReadByte(pixel + 2) == 200, "MyVirtualCam reçoit les pixels réels de la projection");
            }
            // Closed/unavailable windows report a failure and clear the picture rather than retaining a stale image.
            var absent = R.NouveauDisplay("Fenêtre fermée", new SourceDisplay { Type = "fenetre", Fenetre = 1, Processus = -1 });
            R.AfficherAnnonce(absent); await Attendre(500);
            Ok(App.Sources.Image == null && App.Sources.Statut.Contains("fermée"), "fenêtre fermée : image effacée et message utile");
            R.Effacer();
            R.SupprimerAnnonce(img.Id); R.SupprimerAnnonce(absent.Id);
            R.SupprimerAnnonce(an.Id);
            R.SupprimerAgenda(agTest.Id);
            w.TestOnglet(false);
            R.Effacer();
            await Attendre(900);
            Capture(w, "11-vide.png");

            await Fenetre(w, () => new EditeurWindow(null), "12-editeur.png", d =>
                ((EditeurWindow)d).TestColler("Mon chant collé\n\nC G Am F\nLigne une du chant\nLigne deux du chant\nLigne trois du chant\nLigne quatre du chant\nLigne cinq du chant\nLigne six du chant\nLigne sept\n\nRefrain :\nGloire à toi\nGloire à toi"));
            await Fenetre(w, () => new DecoupeWindow(new CibleVerset(42, 3, 16, 16)), "13-decoupe.png");
            await Fenetre(w, () => new ImportWindow(), "14-import.png");
            await Fenetre(w, () => new BiblesWindow(), "15-bibles.png");
            await Fenetre(w, () => new ObsWindow(), "16-obs.png");
            await Fenetre(w, () => new TelephoneWindow(), "17-telephone.png");
            await Fenetre(w, () => new OutilsWindow(false), "18-outils.png");

            // recueil PDF (lecture du PDF par PdfPig)
            var pdf = Path.Combine(Environment.CurrentDirectory, "windows", "MyVerse.Tests", "donnees", "recueil-test.pdf");
            if (File.Exists(pdf))
            {
                try
                {
                    var lignes = await Task.Run(() => ImportWindow.LignesPdf(pdf, new Progress<string>(_ => { })));
                    var chants = Recueils.ChantsDepuisLignes(lignes);
                    Note("PDF : " + string.Join(" | ", chants.Select(x => x.Title + " [" + string.Join(", ", x.Sections.Select(s => s.Label)) + "]")));
                    Ok(chants.Count == 3 && chants[0].Title == "1. GRACE INFINIE" && chants[0].Sections.Any(s => s.Label == "Refrain") && chants[2].Title == "3. YERA YERA",
                        "recueil PDF : 3 chants numérotés avec refrain");
                }
                catch (Exception x) { Echec("recueil PDF : " + x); }
            }
            else Note("PDF de test absent : " + pdf);

            // le serveur des téléphones répond
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var page = await http.GetStringAsync($"http://127.0.0.1:{App.Telephone.Port}/");
                Ok(page.Contains("MyChurch ✓"), $"serveur des téléphones (port {App.Telephone.Port})");
            }
            catch (Exception x) { Echec("serveur des téléphones : " + x.Message); }
            Ok(File.Exists(Path.Combine(App.SortieObs.Dossier, "etat.json")), "sortie OBS (etat.json)");
            Ok(File.Exists(App.SortieObs.Page), "page OBS (ecran.html)");
        }
        catch (Exception x) { Echec("exception : " + x); }
        lock (log)
        {
            log.Add("");
            log.Add(echecs.Count == 0 ? "TOUT EST BON" : echecs.Count + " ÉCHEC(S)");
            log.Add("");
            log.Add("===== journal =====");
            log.AddRange(Journal.Lignes());
            try { File.WriteAllLines(Path.Combine(dossier, "autotest.txt"), log); } catch (Exception) { }
        }
        try { w.Ecran?.Close(); } catch (Exception) { }
        Application.Current.Shutdown(echecs.Count == 0 ? 0 : 1);
    }
}
