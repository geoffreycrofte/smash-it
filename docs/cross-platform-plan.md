# Smash It sur macOS et Linux : plan

*Plan du 22 novembre 2026, à partir de `main` (52d123c). Les choix sont arrêtés : voir la section 8.*

L'objectif est de garder un seul code pour les moteurs (`SmashIt.Core`), de faire tourner la ligne de commande et
l'app sur macOS et Linux, et de ne rien changer pour les utilisateurs Windows. Les décisions prises sont réunies à
la fin ; elles priment sur les options comparées dans les sections 2 à 7.

## 1. Ce qui dépend de Windows aujourd'hui

Tout le code vise `net10.0-windows10.0.19041.0` (`Directory.Build.props`), même là où ce n'est pas nécessaire.

### Moteurs (`SmashIt.Core`)

| Code | Rôle | Dépendance | Sur macOS / Linux |
|---|---|---|---|
| `Imaging/WicCodec.cs`, `Imaging/IntermediatePng.cs`, `Imaging/JpegResampler.cs` | Décoder, redimensionner, convertir en sRGB et appliquer l'orientation EXIF ; produire le PNG intermédiaire donné à cwebp, avifenc et pngquant | WIC (`Windows.Graphics.Imaging`) | À remplacer : c'est le gros morceau |
| `Compressors/WicImageCompressor.cs` | Réencoder JPEG, PNG, TIFF, BMP, JPEG XR, HEIC ; lire HEIC, AVIF et WebP | WIC, et pour HEIC/AVIF/WebP les extensions gratuites du Microsoft Store | À remplacer ; JPEG XR peut être abandonné hors Windows |
| `Compressors/VideoCompressor.cs` | Encodeur vidéo « Windows » (option rapide) | Media Foundation | Rien à faire : l'option n'existe que sur Windows, ffmpeg est déjà le moteur par défaut |
| `IO/FileOutput.cs` | Mettre l'original à la Corbeille | `Microsoft.VisualBasic.FileIO` | Corbeille macOS (`NSFileManager.trashItem`) et spécification freedesktop sur Linux |
| `Shell/ExplorerContextMenu.cs` | Menu « Compresser avec Smash It » | Registre | Équivalents Finder et Nautilus (section 5) |
| `Updates/UpdateService.cs` | Télécharger `SmashIt-Setup-x64.exe` | Installeur Windows | Un paquet par OS (section 5) |
| `Compression/CompressionPipeline.cs` | Lit la taille et le nombre d'images via `WicCodec` | WIC | Passe par la même abstraction que le reste de l'imagerie |

Déjà portable : les métadonnées (`Metadata/*`, en .NET pur), la détection des formats, les PDF (qpdf), les
présentations (ZIP et XML), tous les moteurs ffmpeg (vidéo, GIF, audio), les préréglages, le changelog.

### Outils embarqués

Ce sont des `.exe` Windows, téléchargés et vérifiés par `tools/fetch-tools.ps1` :

| Outil | macOS | Linux | Source |
|---|---|---|---|
| oxipng 10.2.1 | arm64 + x64 officiels | x64 + arm64 officiels (musl, statiques) | Release GitHub |
| cwebp (libwebp 1.6.0) | arm64 + x64 officiels | x64 + arm64 officiels | Google |
| avifenc (libavif 1.4.2) | `macOS-artifacts.zip` officiel | `linux-artifacts.zip` officiel | Release GitHub |
| jpegtran (libjpeg-turbo 3.2.1) | `.dmg` officiel | `.deb` / `.rpm` officiels | Release GitHub |
| qpdf 12.4.2 | arm64 + x64 officiels | x64 + arm64 officiels (zip, AppImage) | Release GitHub |
| pngquant 2.17.0 | Pas de binaire publié qu'on puisse épingler | Aucun | À compiler en CI |
| gifsicle 1.97 | Aucun | Aucun | À compiler en CI (petit programme C) |
| oxvg 0.0.9 | Seuls des modules Node sont publiés | Idem | À compiler en CI (Rust), comme pour Windows |
| ffmpeg (allégé) | — | — | À compiler en CI avec `tools/ffmpeg-configure.sh` |

### App (`SmashIt.App`, WinUI 3)

Tout est lié à WinUI, ou à Win32 pour certains détails :
- **Fenêtres :** fenêtre principale, aperçu avant/après (`PreviewWindow`), écran de démarrage (`SplashScreen`), icône (`AppIcon`), curseurs de redimensionnement (`ResizeCursorGrid`), mémoire de la position des fenêtres.
- **Système :** vignettes (`Thumbnails`, via le shell Windows), taille du texte suivant le réglage de Windows (`TextScale`), instance unique (`SingleInstance`), notes de version (`ReleaseNotesView`).
- **Glisser-déposer** de fichiers et de dossiers.

Portable sans effort :
- `MainViewModel` (MVVM avec CommunityToolkit) et `UpdateViewModel`, à leurs appels WinUI près ;
- `Strings.cs` ;
- `AppSettings` : `LocalApplicationData` correspond à `~/.local/share` sur Linux et à `~/Library/Application Support` sur macOS.

### Tests

Les 268 tests tournent sous Windows. Une douzaine de fichiers dépendent de WIC ou de Media Foundation :
- **Les générateurs de fichiers de test :** `TestImages` et `TestPdf` utilisent WIC, `TestVideo` Media Foundation.
- **Les tests qui s'en servent :** métadonnées, redimensionnement, réglages fins, présentations, outils.

Pour qu'ils tournent sur macOS et Linux, ces générateurs doivent passer par le décodeur commun (section 3) ou par
ffmpeg, déjà présent partout.

## 2. Interface : quel framework

| Option | Plateformes | Ce qu'il faut réécrire | Pour | Contre |
|---|---|---|---|---|
| **Avalonia** (MIT) | Windows, macOS, Linux (X11, Wayland) | Le XAML des deux fenêtres et des contrôles : syntaxe proche, mais pas identique | Le plus mûr sur macOS et Linux pour une app de bureau ; un seul moteur de rendu partout ; grosse communauté ; glisser-déposer et fenêtres multiples bien supportés | Windows ne serait plus « natif WinUI » (le thème Fluent d'Avalonia en est très proche) |
| **Uno Platform** (Apache-2.0) | Windows (WinUI natif), macOS et Linux via Skia | Peu : Uno reprend l'API et le XAML de WinUI 3 | Windows reste en WinUI ; on garde le XAML et une bonne partie du code | Les API propres à WinUI ou à Win32 qu'on utilise (fenêtres, barre de titre, taille du texte…) restent à adapter ; Linux passe par GTK ou le framebuffer ; deux moteurs à tester |
| **.NET MAUI** | Windows, macOS ; Linux seulement via des projets tiers ou expérimentaux | Tout | — | Pas de Linux officiel : écarté |
| **WinUI sur Windows + Avalonia sur macOS et Linux** | Les trois | Le XAML des fenêtres, pour macOS et Linux seulement | Windows intact, chaque app au plus près de son système | Deux interfaces à maintenir |

**Choix retenu : la dernière ligne.** Windows garde l'app WinUI 3 actuelle ; une seconde app, en Avalonia, sert
macOS et Linux. Les deux vivent dans le même dépôt et partagent tout ce qui n'est pas de l'interface :

```
src/
  SmashIt.Core/          moteurs, pipeline, réglages              net10.0 + net10.0-windows
  SmashIt.Presentation/  view-models, textes, logique des écrans   net10.0 (nouveau, extrait de SmashIt.App)
  SmashIt.Cli/           ligne de commande                        net10.0, publiée pour chaque système
  SmashIt.App/           app WinUI 3                              Windows seulement (inchangée)
  SmashIt.Desktop/       app Avalonia                             macOS et Linux
```

- **Ce qui est fait deux fois :** le XAML des fenêtres et ce qui touche au système (fenêtres, barre de titre,
  glisser-déposer, menu contextuel). Le reste (moteurs, view-models, traductions) n'existe qu'une fois.
- **Le coût :** chaque changement d'interface se fait dans deux XAML. L'app étant petite (deux fenêtres), c'est
  acceptable, en échange d'une app Windows qui ne bouge pas.
- **Plus d'essai Avalonia contre Uno :** Uno n'avait d'intérêt que pour garder une seule interface.

## 3. Images : remplacer WIC

| Option | Licence (compatible GPL-3.0 ?) | Formats | Remarques |
|---|---|---|---|
| **libvips via NetVips** | libvips LGPL-2.1+, NetVips MIT : compatible | JPEG (libjpeg-turbo/mozjpeg), PNG, WebP, AVIF, TIFF, GIF, profils ICC, orientation EXIF, redimensionnement de qualité | Le paquet `NetVips.Native` embarque libvips pour Windows, macOS et Linux (x64 et arm64). Très rapide, peu de mémoire. HEIC : voir ci-dessous |
| SkiaSharp | MIT : compatible | JPEG, PNG, WebP | Ni AVIF ni HEIC, encodeurs limités : insuffisant seul |
| ImageSharp | Licence « Six Labors Split », pas une licence libre classique | Large | Écarté, pour la licence |
| Les outils seuls (ffmpeg, cwebp, avifenc…) | Déjà embarqués | Décodage par ffmpeg | Possible pour quelques cas, pas comme base : profils couleur et métadonnées moins bien gérés |

**Choix retenu : libvips (NetVips) derrière une interface `IImageCodec`, pour macOS et Linux seulement.** WIC reste
l'implémentation Windows : les résultats Windows ne changent pas, et le build Windows n'embarque pas libvips. Les
benchmarks comparent quand même les deux, pour repérer les écarts entre systèmes.

**HEIC (photos d'iPhone) :**
- **Le problème :** décoder du HEIC demande un décodeur HEVC (libde265 ou ffmpeg), soumis à des brevets. Les builds embarqués de libvips l'excluent souvent : c'est à vérifier dans `NetVips.Native`.
- **Pistes :**
  - macOS sait lire le HEIC nativement (ImageIO, ou la commande `sips`) ;
  - sous Linux, notre ffmpeg contient déjà le décodeur HEVC, et ffmpeg sait lire les images HEIF depuis la version 7.1 : à tester ;
  - en dernier recours, libheif avec libde265 (LGPL).
- **L'écriture en HEIC** n'est proposée aujourd'hui que via WIC : la garder réservée à Windows est acceptable.

## 4. Outils par système

Il faut un `fetch-tools` qui sache télécharger, pour chaque outil, le binaire de chaque plateforme (Windows x64,
macOS arm64, Linux x64), toujours épinglé par SHA-256. Le script est en PowerShell : `pwsh` tourne
aussi sur macOS et Linux.

- **Binaires officiels** (oxipng, cwebp, avifenc, jpegtran, qpdf) : comme aujourd'hui, avec une entrée par plateforme.
- **Compilés en CI** dans le workflow « Build tools », en pré-releases comme oxvg et ffmpeg :
  - pngquant, gifsicle, oxvg : simples à compiler (C et Rust) ;
  - **ffmpeg, le plus délicat :**
    - **Linux :** un build statique dans un conteneur Alpine (musl), avec le même `ffmpeg-configure.sh` ;
    - **macOS :** Homebrew ne fournit que des bibliothèques dynamiques. Il faudra soit compiler x264, x265, SVT-AV1, libvpx, LAME, Opus, Vorbis et dav1d en statique, soit embarquer les `.dylib` à côté de l'exécutable.
- **Apple Silicon seulement** (arm64) : un seul build macOS par outil, pas de binaire universel.
- **Linux x64 seulement** au départ ; arm64 (Raspberry Pi, certains portables) pourra suivre si on le demande.
- **Signature ad hoc :** macOS refuse de lancer un binaire arm64 sans aucune signature. Une signature « ad hoc »
  (`codesign -s -`) suffit, gratuite, sans compte Apple : la CI l'applique à chaque outil compilé et à l'app.

## 5. Distribution et intégration

| | macOS | Linux |
|---|---|---|
| App | `.app` dans un `.dmg`, et un « cask » dans notre tap Homebrew | AppImage : un seul fichier, qui tourne sur toutes les distributions, comme le zip portable sous Windows |
| Ligne de commande | `.tar.gz` et une formule dans notre tap Homebrew | `.tar.gz` |
| Clic droit | Action rapide / Service du Finder (déclaré dans l'`Info.plist`) et « Ouvrir avec » | Fichier `.desktop` avec les types MIME (« Ouvrir avec »), plus actions Nautilus, Nemo et Dolphin |
| Corbeille | `NSFileManager.trashItem` | Spécification freedesktop Trash (`~/.local/share/Trash`) |
| Mises à jour | Vérifier les releases GitHub et proposer de télécharger le `.dmg` | Idem pour l'AppImage ; Flathub se met à jour seul |

**La signature sur macOS :**
- Une app qui n'est ni signée « Developer ID » ni notarisée par Apple est bloquée par Gatekeeper.
- Depuis macOS 15 (Sequoia), on ne peut plus la lancer par clic droit → Ouvrir. Il faut l'essayer une fois, puis aller dans Réglages Système → Confidentialité et sécurité → « Ouvrir quand même », et confirmer avec son mot de passe. Certains rapportent même des cas sans ce bouton.
- La signature et la notarisation demandent l'Apple Developer Program, payant chaque année (99 USD/an à ce jour, à confirmer).
- **Tous les outils embarqués** devraient alors être signés eux aussi, avec le « hardened runtime ».
- **Choix retenu : pas d'Apple Developer Program.** L'app est distribuée non signée, comme sous Windows (où
  SmartScreen avertit de la même façon). Le README explique le passage par « Ouvrir quand même », ou la commande
  `xattr -dr com.apple.quarantine "/Applications/Smash It.app"`.
- **Homebrew, notre propre tap** (`geoffreycrofte/homebrew-smash-it`, `brew install geoffreycrofte/smash-it/smashit`) :
  - **ligne de commande (formule) :** Homebrew ne pose pas l'attribut de quarantaine sur ce qu'une formule
    installe, donc pas de blocage Gatekeeper ;
  - **app (cask) :** pratique pour installer et mettre à jour, mais Homebrew pose la quarantaine sur les casks :
    le premier lancement demande le même « Ouvrir quand même ». À vérifier lors de la phase 3.
  - Le dépôt officiel `homebrew/cask` exige désormais des apps signées et notarisées : notre tap est la seule voie.

**Linux :** pas de signature imposée. Flatpak (Flathub) reste possible plus tard si on nous le demande.

## 6. CI

- **Runners :** GitHub fournit `ubuntu-latest` et `macos-latest` (Apple Silicon), gratuits pour un dépôt public.
- **Matrice `ci.yml`** sur les trois systèmes, avec les tests du Core sur chacun.
- **Coût :** les runners macOS consomment plus de minutes, mais restent gratuits en public. Cela allonge un peu la CI.
- **Images de test :** les tests qui génèrent des images doivent passer par le décodeur commun (section 3), sinon ils ne tourneront que sous Windows.

## 7. Feuille de route

Durées indicatives, en jours de travail effectif.

**Phase 0 — Un Core multiplateforme, sans rien changer pour Windows (8 à 12 jours)**
- `SmashIt.Core` cible `net10.0` et `net10.0-windows10.0.19041.0`. Le code WIC, Media Foundation, Corbeille et menu de l'Explorateur passe derrière des interfaces : `IImageCodec`, `ITrash`, `IShellIntegration`. La version Windows garde exactement les mêmes moteurs.
- Extraction des view-models de `SmashIt.App` vers `SmashIt.Presentation` (sans type WinUI), l'app Windows s'en servant à l'identique.
- Implémentation libvips de `IImageCodec`, et HEIC selon la piste retenue (section 3).
- Générateurs de fichiers de test portables. CI sur Windows, Ubuntu et macOS, au moins pour le Core.
- Benchmarks libvips contre WIC sous Windows, consignés dans `docs/benchmarks.md`.

**Phase 1 — La ligne de commande sur macOS et Linux (5 à 8 jours)**
- `fetch-tools` multiplateforme, et compilation en CI de pngquant, gifsicle, oxvg et ffmpeg pour macOS et Linux.
- Corbeille macOS et Linux.
- Archives `smashit` en `.tar.gz` par plateforme, produites en CI (publiées seulement avec l'app, phase 3).
- Benchmarks par système dans `docs/benchmarks.md`, pour vérifier que les résultats sont les mêmes partout.

**Phase 2 — L'app Avalonia pour macOS et Linux (12 à 20 jours)**
- `SmashIt.Desktop` sur `SmashIt.Presentation` : fenêtre principale, réglages, liste, aperçu avant/après, écran de démarrage, vignettes (via le décodeur commun), glisser-déposer, instance unique, taille du texte, accessibilité (lecteurs d'écran : VoiceOver, Orca).
- L'app Windows ne change pas.

**Phase 3 — Distribution et première version macOS et Linux (5 à 8 jours)**
- `.dmg` non signé (signature ad hoc seulement), AppImage, `.tar.gz` de la ligne de commande.
- Tap Homebrew : formule pour la ligne de commande, cask pour l'app.
- Action rapide du Finder, actions Nautilus et Dolphin, « Ouvrir avec ».
- Mises à jour par système ; README (installation, premier lancement sur macOS).
- **Sortie unique :** l'app et la ligne de commande macOS et Linux sortent ensemble, dans la même version que Windows.

**Risques principaux :**
- **HEIC sous Linux**, selon ce que permettent les builds et les brevets ;
- **un ffmpeg statique sur macOS** ;
- **le premier lancement sur macOS**, sans notarisation : un frein pour les utilisateurs peu techniques ;
- **deux interfaces à garder alignées** (WinUI et Avalonia) ;
- **les écarts de résultats entre WIC et libvips**, à mesurer ;
- **le glisser-déposer sous Wayland** ;
- **le temps de CI sur trois systèmes.**

## 8. Décisions prises

1. **Interface :** une app par système, dans le même dépôt. WinUI 3 reste sur Windows ; Avalonia sert macOS et Linux. Moteurs, view-models et ligne de commande sont partagés (section 2).
2. **Pas d'Apple Developer Program :** l'app macOS est distribuée non signée, comme sous Windows, avec une signature ad hoc. Notre tap Homebrew sert la ligne de commande (formule) et l'app (cask).
3. **Mac visés :** Apple Silicon seulement.
4. **Versions minimales :** celles de .NET 10, pour ne rien maintenir de plus.
   - **macOS 15 (Sequoia)** et suivants : la plus ancienne version que .NET 10 supporte.
   - **Linux x64, glibc 2.35 ou plus**, imposé par la compilation sur le runner `ubuntu-22.04` (l'app .NET elle-même demande glibc 2.27) : Ubuntu 22.04+, Debian 12+, Fedora 36+, Linux Mint 21+. Les outils, compilés en statique, n'ajoutent pas de contrainte.
5. **Formats Linux :** AppImage pour l'app, `.tar.gz` pour la ligne de commande. Flatpak, `.deb` ou `.rpm` seulement si on nous les demande.
6. **Windows :** garde WIC et WinUI, sans changement ; libvips ne sert que sur macOS et Linux.
7. **Sortie :** l'app et la ligne de commande macOS et Linux sortent ensemble.

## Sources consultées

- Releases officielles : [oxipng](https://github.com/oxipng/oxipng/releases), [libavif](https://github.com/AOMediaCodec/libavif/releases), [qpdf](https://github.com/qpdf/qpdf/releases), [libjpeg-turbo](https://github.com/libjpeg-turbo/libjpeg-turbo/releases), [libwebp](https://storage.googleapis.com/downloads.webmproject.org/releases/webp/index.html)
- Uno Platform : [documentation](https://github.com/beto-rodriguez/uno/blob/master/doc/articles/intro.md), [tarifs (le framework reste Apache-2.0)](https://platform.uno/pricing)
- .NET MAUI et Linux : [Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/1637588/will-net-maui-include-official-support-for-linux), [Avalonia et MAUI](https://avaloniaui.net/blog/net-maui-is-coming-to-linux-and-the-browser-powered-by-avalonia)
- HEIC et libheif : [README de libheif](https://github.com/strukturag/libheif/blob/master/README.md)
- Systèmes supportés par .NET 10 : [supported-os.md](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Gatekeeper sous Sequoia : [Eclectic Light](https://eclecticlight.co/2024/10/01/living-without-notarization/), [AppleInsider](https://appleinsider.com/inside/macos-sequoia/tips/how-to-run-apps-outside-system-security-policies-in-macos-sequoia), [MacRumors (macOS 15.1)](https://forums.macrumors.com/threads/macos-15-1-completely-removes-ability-to-launch-unsigned-applications.2441792)
