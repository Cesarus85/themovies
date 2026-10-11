# Roadmap – The Movies

Legende: ✅ fertig und auf der Quest bestätigt · 🧪 umgesetzt, Test auf der Quest steht aus · 📋 geplant · 💡 Idee

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
| 📋 | 5 | Weitere Säle | Heimkino (Sofa, Kamin), Autokino unter Sternen, Weltraum-Lounge; Auswahl in den Einstellungen, für alle synchron oder pro Person | mittel |
| 📋 | 6 | 180°/360°-Videos | Innen texturierte Kugel/Halbkugel statt Leinwand, Formate mono/SBS/OU, Erkennung über Dateiname (`_180`, `_360`, `LR`, `TB`) | mittel |
| 📋 | 7 | Handtracking | OpenXR Hand Interaction Profile, Pinch = Trigger, Strahl aus der Hand; Avatare mit Hand-Posen | mittel |
| ✅ | – | Eigener Video-Player | ExoPlayer/Media3 über Java-Plugin, GL-Textur-Übergabe, FFmpeg-Ton, eingebettete Untertitel | groß |
| ✅ | – | NAS / Netzlaufwerk | SMB 2/3, WebDAV, DLNA; Netzwerksuche (Bonjour/SSDP), Ordner-Navigation, Proxy mit Puffer, Untertitel vom NAS, Weiterstreamen an Mitschauer | groß |
| 📋 | 8 | Mediathek (Jellyfin/Plex) | Login per Code, Bibliothek durchsuchen, Direct-Play-URL an alle verteilen | groß |
| 📋 | 9 | Bildeinstellungen | Helligkeit/Kontrast/Sättigung im Leinwand-Shader, 3D-Tiefe über horizontalen Augen-Versatz | klein |
| 📋 | 10 | Sprachchat-Komfort | Mitschauer leiser während der Film läuft (Ducking), Push-to-Talk auf Grip-Taste, Lautstärke pro Person | klein |

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
- **Logs:** Fehlerprotokoll im App-Ordner, damit Probleme von Mitschauern leichter nachvollziehbar sind.
- **Eigener Player, Ausbau:** Ton-/Untertitelspur im Menü wählbar, SMB als direkte ExoPlayer-DataSource, Wiedergabe als OpenXR-Composition-Layer (Android Surface) – siehe [docs/PLAYER.md](docs/PLAYER.md).
- **NAS auf der Quest messen:** Durchsatz SMB/WebDAV über WLAN mit 4K-Remux (Ziel ≥ 15 MB/s), ggf. mehrere Lese-Anfragen parallel.
- **NAS-Zugangsdaten verschlüsseln:** Android Keystore statt Klartext in den App-Einstellungen.
- **SMB 3.1.1 + Verschlüsselung:** Pre-Auth-Integrity und AES-GCM, damit auch „Verschlüsselung erzwingen“ funktioniert.
- **Host-Wechsel:** Wenn der Gastgeber geht, übernimmt ein anderer Teilnehmer statt das Kino zu schließen.
