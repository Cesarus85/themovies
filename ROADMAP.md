# Roadmap – The Movies

Legende: ✅ fertig und auf der Quest bestätigt · 🧪 umgesetzt, Test auf der Quest steht aus · ⚗️ experimentell · 📋 geplant · 💡 Idee

## Diese Runde umgesetzt (Test auf der Quest steht aus)

| Status | Thema | Umsetzung |
| --- | --- | --- |
| 🧪 | **Einfrieren beim Beenden eines NAS-Films** | Verbindungen beim Stoppen nur hart schließen (Socket zu), Aufräumen im Hintergrund, Video-Surface erst nach `player.release()` freigeben, kein `UnloadUnusedAssets`. |
| 🧪 | **Passthrough, eigener Weg** | Eigenes OpenXR-Feature „The Movies: Passthrough“ (`XR_FB_passthrough` direkt): Passthrough-Ebene wird in `xrEndFrame` vor das Bild gelegt, die Projektionsebene mit Alpha gemischt – unabhängig von AR Foundation. |
| 🧪 | **Protokoll in der App** | `themovies-log.txt` im App-Ordner (+ Log des vorigen Starts), *Einstellungen → Protokoll* zeigt es an. |
| 🧪 | **Ton- und Untertitelspur, Ton-Versatz** | *Einstellungen → Ton & Untertitel*: Spuren des laufenden Films wählen, Ton-Versatz ±500 ms (Bluetooth-Kopfhörer). |
| 🧪 | **Schönere Filmauswahl** | Kachelansicht mit Postern (Medienserver, `poster.jpg`/`Film.jpg`/`.tbn`, Kodi-`.nfo`), Suche/Filter, Tab „Zuletzt“, „Weiterschauen“ auf der Startseite. |
| 🧪 | **Jellyfin/Emby und Plex** | Jellyfin mit Benutzer/Passwort, Plex per Code auf plex.tv/link; Bibliotheken, Serien, Suche, Direct Play; Netzwerksuche findet beide. |
| ⚗️ | **2D → 3D (experimentell, vorerst zurückgestellt)** | Auf der Quest getestet: Effekt zu schwach, mit KI ruckelig. Offen: KI-Status/Tempo auf der Brille prüfen, Tiefenkanten an Objektränder anpassen (kantenbewusstes Hochskalieren), kleineres Modell/geringere Auflösung, Vorhersage der Bewegung zwischen Tiefenbildern. Stand: Tiefe pro Bild (KI: Depth Anything V2 Small über Unity Inference Engine, verteilt auf mehrere Frames, asynchron zurückgelesen, geglättet; ohne Modell: Schätzung aus dem Bild), Verschiebung pro Auge im Leinwand-Shader, Stärke so begrenzt, dass die Augen nie auseinanderlaufen. |
| 🧪 | **Leuchten um die Leinwand (MR)** | Weiches Ambilight aus den Randfarben des Bildes, das ins Passthrough-Zimmer ausläuft (Aus/Schwach/Stark). |
| 🧪 | **Bildeinstellungen** | Helligkeit, Kontrast, Sättigung, 3D-Tiefe (Augenbilder auseinander/zusammen) unter *Bild & 3D*. |
| 🧪 | **Untertitel-Größe/-Position** | 60–180 %, unten im Bild / etwas höher / unter dem Bild. |
| 🧪 | **Sprachchat-Komfort** | Push-to-Talk (Grip/Faust, PC: V), Film leiser während andere sprechen (Ducking). |
| 🧪 | **Zugangsdaten verschlüsselt** | AES-256-GCM mit Schlüssel im Android-Keystore; alte Einträge werden beim Start umgestellt. |
| 🧪 | **Handtracking** | OpenXR Hand Interaction Profile: Pinch = Klick, Strahl aus der Hand, Hand statt Controller-Modell. |
| 🧪 | **Heimkino** | Wohnzimmer mit Sofa (gleiche Sitzplätze wie im Kino), Kamin mit flackerndem Licht, Regal, Lampen, Fenster; Raum pro Person wählbar. |
| 🧪 | **180°/360°-Videos** | Halb-/Vollkugel um den Kopf, mono/SBS/OU, Erkennung über den Dateinamen, Saal wird ausgeblendet, für alle synchron. |

## Phase 1 – Grundlage

| Status | Thema | Notizen |
| --- | --- | --- |
| ✅ | App startet in VR auf der Quest 3 | OpenXR + Meta Quest Support, Build-Prüfung im Editor |
| ✅ | Kinosaal, Sitzhöhe, Menü | Stadionreihen, Samtsessel, Wandleuchten, Sternenhimmel, gewölbte Leinwand |
| 🧪 | Menü v2 | Augenhöhe vor dem Blick, Griffleiste zum Verschieben, Icons, Einstellungsseite |
| 🧪 | Gemeinsam schauen (Online per Raum-Code, LAN) | Uhrenabgleich, geplanter Start, Drift-Korrektur, „Warten auf alle“ |
| 🧪 | Host-Streaming | Datei unverändert über Netcode/Relay, lokaler HTTP-Server mit Range-Support |
| 🧪 | 3D (SBS/OU, Half/Full) | Automatische Erkennung über Dateiname und Bildgröße |
| 🧪 | Sprachchat (Vivox, optional) | Paket muss installiert und im Dashboard aktiviert werden |

## Phase 2 – Gemeinsames Erlebnis (diese Runde umgesetzt)

| Status | # | Thema | Umsetzung |
| --- | --- | --- | --- |
| 🧪 | 1 | **Untertitel (.srt)** | `Film.srt` oder `Film.de.srt` neben dem Film (Deutsch bevorzugt), bei Stream-URLs `…/film.srt`, beim Host-Streaming schickt der Gastgeber seine Datei mit. UTF-8 und Windows-1252 (Umlaute), Tags werden entfernt, lange Zeilen umbrochen. Anzeige im unteren Teil der Leinwand mit dunklem Hintergrund, pro Zuschauer ein-/ausschaltbar. |
| 🧪 | 2 | **Weiterschauen** | Position pro Film (Dateiname/URL) wird alle 5 s und beim Verlassen gespeichert (`resume.txt` im App-Ordner, max. 200 Filme). Wer einen Film wählt, bekommt „Zuletzt bei 1:02:33 aufgehört – Fortsetzen / Von vorne“ angeboten; Fortsetzen springt für alle. |
| 🧪 | 3 | **Reaktionen & Popcorn** | 😂 😮 ❤️ ⭐ 🍿 als Vektor-Emojis: steigen über dem Avatar auf (für alle sichtbar, eigene Reaktion vor einem selbst). Popcorn fliegt im Bogen aus der Hand Richtung Leinwand. Netzwerk-RPC mit kleinem Rate-Limit. |
| 🧪 | 4 | **Mixed Reality** | Umgebung „Mixed Reality“: Saal ausgeblendet, Passthrough zeigt das eigene Zimmer, Leinwand schwebt in TV-Größe ~4 m entfernt, Avatare bleiben. Benötigt das Paket *Unity OpenXR: Meta* – Editor-Menü *The Movies → Mixed Reality (Passthrough) einrichten* installiert es und aktiviert die Features. |

## Phase 3 – Komfort & Inhalte

| Status | # | Thema | Idee zur Umsetzung | Aufwand |
| --- | --- | --- | --- | --- |
| 🧪 | 5 | Weitere Säle | Heimkino erledigt (pro Person wählbar); offen: Autokino unter Sternen, Weltraum-Lounge | mittel |
| 🧪 | 6 | 180°/360°-Videos | Innen texturierte Kugel/Halbkugel statt Leinwand, Formate mono/SBS/OU, Erkennung über Dateiname (`_180`, `_360`, `LR`, `TB`) | mittel |
| 🧪 | 7 | Handtracking | Pinch = Klick, Strahl aus der Hand. Offen: Finger-Gelenke an den Avataren (XR Hands) | mittel |
| ✅ | – | Eigener Video-Player | ExoPlayer/Media3 über Java-Plugin, GL-Textur-Übergabe, FFmpeg-Ton, eingebettete Untertitel – auf der Quest bestätigt | groß |
| 🧪 | 12 | Spuren & Ton-Versatz | Ton- und Untertitelspur im Menü, Ton-Versatz ±500 ms, Untertitel-Größe/Position | klein |
| 🧪 | 13 | Schönere Filmauswahl | Poster, Kacheln, Suche, „Zuletzt“/„Weiterschauen“. Offen: TMDB-Poster für Ordner ohne Bilder (braucht einen eigenen API-Schlüssel) | mittel |
| 💡 | 14 | Leinwand-Erlebnis | Ambilight in MR erledigt; offen: Vorhang öffnet sich beim Start, Kino-Ton-Raumhall, Pause-Licht | mittel |
| ✅ | – | NAS / Netzlaufwerk | SMB 2/3, WebDAV, DLNA; Netzwerksuche (Bonjour/SSDP), Ordner-Navigation, Proxy mit Puffer, Untertitel vom NAS, Weiterstreamen an Mitschauer | groß |
| 🧪 | 8 | Mediathek (Jellyfin/Plex) | Jellyfin/Emby-Login, Plex per Code, Bibliothek durchsuchen, Direct Play. Offen: Transcoding für Formate, die der Player nicht kann; Gesehen-Status an den Server melden | groß |
| 🧪 | 9 | Bildeinstellungen | Helligkeit/Kontrast/Sättigung, 3D-Tiefe | klein |
| 🧪 | 10 | Sprachchat-Komfort | Ducking, Push-to-Talk. Offen: Lautstärke pro Person | klein |

## Phase 4 – Verteilung

| Status | # | Thema | Notizen |
| --- | --- | --- | --- |
| 📋 | 11 | Lobby ohne Code | Freunde über Meta-Konto einladen (Meta Platform SDK: Group Presence, Destinations) – erfordert eine registrierte App im Meta-Entwicklerportal |
| 💡 | – | Veröffentlichung | Meta Horizon Store (Release Channel), App-Icon, Splash-Screen, Signierung |
| 💡 | – | Automatische Builds | GameCI/GitHub Actions mit Unity-Lizenz, APK als Release-Artefakt |

## Qualität & technische Schulden

- **Auf der Quest messen:** Durchsatz des Host-Streamings über Relay vs. LAN, Genauigkeit und Dauer von Sprüngen im VideoPlayer, Bildrate im neuen Saal (Ziel: stabile 72/90 Hz, OVR Metrics Tool).
- **Schärfere Schrift:** Menü und Untertitel auf TextMeshPro (SDF) umstellen.
- **Netzwerk-Tests im Editor:** Multiplayer Play Mode + Unity Test Runner für Join/Leave, Host-Wechsel, Streaming.
- **Eigener Player, Ausbau:** SMB als direkte ExoPlayer-DataSource, Wiedergabe als OpenXR-Composition-Layer (Android Surface) – siehe [docs/PLAYER.md](docs/PLAYER.md).
- **NAS auf der Quest messen:** Durchsatz SMB/WebDAV über WLAN mit 4K-Remux (Ziel ≥ 15 MB/s), ggf. mehrere Lese-Anfragen parallel.
- **SMB 3.1.1 + Verschlüsselung:** Pre-Auth-Integrity und AES-GCM, damit auch „Verschlüsselung erzwingen“ funktioniert.
- **Host-Wechsel:** Wenn der Gastgeber geht, übernimmt ein anderer Teilnehmer statt das Kino zu schließen.
