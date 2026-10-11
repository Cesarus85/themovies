# The Movies – gemeinsam Kino schauen auf der Meta Quest 3

Eine Unity-App für die **Meta Quest 3**, mit der du mit **bis zu 3 weiteren Personen** übers Internet
gemeinsam Filme schaust – auch in **3D** (Side-by-Side / Over-Under). Alle sitzen nebeneinander in
einem virtuellen Kinosaal, sehen sich als Avatare und hören sich (optional) per Sprachchat.

Am besten spielt jede Person **ihre eigene Kopie** des Films ab (Datei auf der Quest oder Stream-URL) –
die App synchronisiert dann nur Start, Pause und Position: volle Bildqualität bei minimaler Bandbreite.
Wer die Datei nicht hat, bekommt sie automatisch **vom Gastgeber gestreamt**.

## Funktionen

- **Kinosaal** mit leicht gewölbter Leinwand (4 Größen), ansteigenden Sitzreihen mit Samtsesseln,
  gemustertem Teppich, Stoffwänden mit Holzpilastern und Wandleuchten, Sternenhimmel, Vorhängen und Bühne.
  Das Saallicht dimmt beim Abspielen, Wände und Sitze nehmen das Licht der Leinwand auf. Alles wird zur
  Laufzeit erzeugt (keine externen Assets) – oder du bindest ein eigenes Kino-Modell ein (siehe unten).
- **Sitzhöhe**: Beim Ausrichten sitzt du automatisch auf Kino-Augenhöhe, egal wie du zu Hause sitzt;
  im Menü mit „Sitz höher/tiefer“ feinjustierbar
- **3D-Filme**: Side-by-Side und Over-Under, jeweils Half oder Full, „Augen tauschen“; das Format wird
  aus dem Dateinamen (`3D.HSBS`, `Half-OU`, `SBS`, …) und der Bildgröße erkannt
- **Gemeinsam schauen** mit bis zu 4 Personen:
  - **Online** über einen kurzen **Raum-Code** (Unity Multiplayer Services/Relay, keine Portfreigabe nötig)
  - **LAN** direkt per IP-Adresse (ohne Unity-Konto)
- **Synchronisation**: Uhrenabgleich zum Gastgeber, geplanter gemeinsamer Start, sanfte
  Geschwindigkeitskorrektur bei kleinen Abweichungen, Sprung bei großen
- **Host-Streaming**: Hat jemand die Datei nicht, streamt der Gastgeber sie über die bestehende Verbindung
  (auch per Raum-Code/Relay, ohne Portfreigabe) – ohne Neukodierung, mit Spulen
- **Warten auf alle**: Lädt oder puffert jemand, pausiert das Kino automatisch und läuft weiter, sobald alle bereit sind
- Alle dürfen Play/Pause/Spulen/Film wählen; die Teilnehmerliste zeigt, wer bereit ist, wer puffert und
  wem die Datei fehlt
- **Avatare** (Kopf, Hände, Name, Mundbewegung beim Sprechen) auf festen Plätzen nebeneinander
- **Sprachchat** über Unity Vivox (optional)
- **NAS & Netzlaufwerke**: Filme direkt von Windows-/Samba-Freigaben (**SMB 2/3**), **WebDAV** oder
  **DLNA/UPnP-Medienservern** abspielen – mit Netzwerksuche, Ordner-Navigation und Spulen; der Gastgeber
  kann NAS-Filme auch an Mitschauer weiterstreamen
- **Untertitel** aus `.srt`-Dateien (neben dem Film, bei Streams auch vom Gastgeber), pro Person ein-/ausschaltbar
- **Weiterschauen**: Die App merkt sich, wo ihr aufgehört habt, und bietet beim nächsten Mal „Fortsetzen“ an
- **Reaktionen**: 😂 😮 ❤️ ⭐ über dem eigenen Avatar und **Popcorn werfen** 🍿
- **Mixed Reality**: Leinwand im eigenen Wohnzimmer per Passthrough, die anderen sitzen als Avatare daneben
- **Allein schauen** geht auch – und wer danach ein Kino eröffnet, schaut einfach mit den anderen weiter
- Am PC ohne Brille testbar (Maus/Tastatur), auch mit Quest Link

## Was du brauchst

- **Unity 6.3 LTS** (6000.3.x) mit dem Modul **Android Build Support** (inkl. OpenJDK und Android SDK & NDK),
  installiert über den Unity Hub
- Eine Meta Quest 3 im **Entwicklermodus** (Meta-Horizon-App auf dem Handy → Gerät → Entwicklermodus)
- Für den Online-Modus: ein kostenloses **Unity-Konto** (Unity Cloud). Für LAN nicht nötig.
- Für Mitschauer: die fertige APK (du kannst sie weitergeben) und dieselbe Filmdatei

## Einrichtung (einmalig)

1. **Projekt öffnen**: Repository klonen, im Unity Hub *Add → Add project from disk* und diesen Ordner wählen.
   Mit Unity 6.3 LTS öffnen (eine abweichende 6.3-Patchversion ist in Ordnung).
2. Fragt Unity, ob die **neuen Input-System-Backends** aktiviert werden sollen: **Ja** (Unity startet neu).
3. Menü **The Movies → 1. Projekt einrichten** ausführen. Das erzeugt Szene und Netzwerk-Prefabs,
   stellt die Quest-Einstellungen ein (Android, ARM64, IL2CPP, Vulkan, Linear, 4× MSAA) und aktiviert
   **OpenXR mit „Meta Quest Support“**. Den Wechsel der Plattform auf **Android** bestätigen.
   - Falls die XR-Einrichtung meldet, dass etwas manuell nötig ist: *Edit → Project Settings →
     XR Plug-in Management* → Reiter Android → **OpenXR** anhaken; unter *OpenXR* das Feature
     **Meta Quest Support** und das Interaction Profile **Oculus Touch Controller Profile** aktivieren.
4. **Online-Modus** (Raum-Code) aktivieren:
   *Edit → Project Settings → Services* → mit einem Unity-Cloud-Projekt verknüpfen (neu anlegen ist ok).
   Im [Unity Cloud Dashboard](https://cloud.unity.com) beim Projekt unter *Multiplayer* ggf. **Relay**
   aktivieren. Das kostenlose Kontingent reicht für private Kinoabende locker.
5. **Sprachchat (optional)**: *Window → Package Manager → Unity Registry* → **Vivox** installieren und im
   Dashboard Vivox für das Projekt aktivieren. Der Code erkennt das Paket automatisch.
   Ohne Vivox funktioniert alles andere genauso – dann einfach parallel per Discord o. Ä. telefonieren.

## Bauen und auf die Quest bringen

1. **The Movies → 2. APK für Quest bauen** → ergibt `Builds/TheMovies.apk`.
2. Quest per USB-C anschließen, in der Brille „USB-Debugging zulassen“ bestätigen.
3. Installieren mit dem **Meta Quest Developer Hub** (APK ins Fenster ziehen), **SideQuest**, oder:
   ```
   adb install -r Builds/TheMovies.apk
   ```
4. In der Quest: *App-Bibliothek → Filter → Unbekannte Quellen* → **The Movies**. Beim ersten Start den
   Zugriff auf Dateien (und fürs Mikrofon) erlauben.

Die Mitschauer installieren dieselbe APK auf ihrer Quest.

## Problemlösung

**Die App öffnet sich auf der Quest als flaches Fenster** – dann war VR beim Bauen nicht aktiv:

1. *Edit → Project Settings → XR Plug-in Management*, Reiter **Android** (Robotersymbol): **OpenXR** anhaken.
2. Darunter *OpenXR*, Reiter Android: Feature **Meta Quest Support** anhaken; bei *Enabled Interaction
   Profiles* mit **+** das **Oculus Touch Controller Profile** hinzufügen.
3. *XR Plug-in Management → Project Validation*, Reiter Android: **Fix All** klicken.
4. Neu bauen und installieren. (*The Movies → VR-Einrichtung prüfen* zeigt, was gefunden wurde; beim Android-Build
   erscheint eine Warnung in der Console, falls etwas zu fehlen scheint. Maßgeblich sind die Haken in den Project Settings.)

## Filme auf die Quest kopieren

Quest per USB anschließen und Videos in **`Movies`** oder **`Download`** ablegen
(alternativ in den App-Ordner `Android/data/com.themovies.cinema/files/Movies`).
**Jede Person braucht dieselbe Datei**, am besten mit gleichem Dateinamen. Die App findet sie
automatisch (über Name und Größe); falls nicht, gibt es im Menü den Knopf **„Datei zuordnen“**.

- Empfohlen: **MP4 oder MKV mit H.264/H.265 (HEVC) und AAC-Ton**. Bis 4K läuft auf der Quest 3 flüssig.
- **Kein Ton?** Viele 3D-Dateien haben AC3/DTS-Ton, den Android oft nicht abspielen kann. Ton ohne
  Neukodierung des Bildes umwandeln:
  ```
  ffmpeg -i film.mkv -map 0:v:0 -map 0:a:0 -c:v copy -c:a aac -b:a 384k film.mp4
  ```
- **3D-Erkennung** über den Dateinamen: `HSBS`/`Half-SBS` → SBS Half, `SBS` + doppelt breites Bild →
  SBS Full, `HOU`/`TAB`/`OU` → Over-Under. Sonst einfach im Menü auf **Format** tippen.

### Untertitel

Lege die Untertitel als `.srt` mit **gleichem Namen** neben den Film, z. B. `Avatar.mkv` + `Avatar.srt`
oder `Avatar.de.srt` (deutsche Dateien werden bevorzugt). Bei Stream-Adressen sucht die App
`…/film.srt` neben der Video-URL. Wer den Film vom Gastgeber gestreamt bekommt, erhält dessen Untertitel
automatisch mit. Ein-/Ausschalten: *Einstellungen → Untertitel*.

### Vom Gastgeber streamen

Wählt der **Gastgeber** eine eigene Datei, bekommen alle, die sie nicht selbst haben, den Film
automatisch von seiner Quest gestreamt (Anzeige im Menü: „Stream vom Gastgeber … MB/s“).
Die Datei wird unverändert übertragen, daher gilt:

- Die **Upload-Geschwindigkeit des Gastgebers** muss für alle Zuschauer ohne Datei reichen
  (z. B. 1080p-Film mit 8 Mbit/s × 2 Zuschauer = 16 Mbit/s Upload).
- Über den Raum-Code (Relay) sind pro Zuschauer grob **10–15 Mbit/s** möglich, im LAN deutlich mehr.
  Für 4K-Dateien mit hoher Bitrate besser die Datei vorher verteilen.
- Reicht es nicht, pausiert das Kino automatisch, bis wieder genug gepuffert ist.
- Unity Relay ist bis zu einem Freikontingent kostenlos; ganze Filme über Relay zu streamen kann dieses
  Kontingent aufbrauchen (Übersicht im Unity Cloud Dashboard).

### Vom NAS / Netzlaufwerk

Im Menü *Film wählen → Netzwerk*. Beim ersten Öffnen sucht die App im WLAN nach NAS-Geräten
(Bonjour „_smb._tcp“, z. B. Synology/QNAP) und DLNA-Medienservern. Gefundenes erscheint mit „gefunden“;
antippen, Benutzer und Passwort eingeben (leer = Gast) – klappt die Anmeldung, wird die Quelle gespeichert.
Nicht gefunden? **„NAS hinzufügen“** und die Adresse eintippen:

| Protokoll | Beispiele | Hinweise |
| --- | --- | --- |
| **SMB** (Windows-Freigabe, Samba, NAS) | `smb://192.168.1.10/filme`, `smb://diskstation`, `smb://nas:4455/video/3D` | SMB 2.0 bis 3.0.2, NTLMv2-Anmeldung, Signierung. Ohne Freigabename zeigt die App die Freigaben des Servers. Domänen-Konten als `DOMÄNE\name`. |
| **WebDAV** | `https://nas:5006/filme`, `davs://cloud.example.de/remote.php/dav/files/ich/` | Basic/Digest-Anmeldung. Selbstsignierte Zertifikate werden nur bei Adressen im Heimnetz akzeptiert. |
| **DLNA/UPnP** | wird automatisch gefunden | Ohne Anmeldung; die Wiedergabe läuft direkt vom Medienserver. |

- Spulen funktioniert überall; die App liest bei SMB/WebDAV über einen kleinen Zwischenspeicher
  (ca. 12 MB Vorlauf) und meldet „puffert“, damit die anderen warten, falls das WLAN mal nicht reicht.
- **Untertitel** (`Film.srt`, `Film.de.srt` …) werden auch im NAS-Ordner gefunden.
- **Mitschauer**: Für die anderen ist ein NAS-Film wie eine lokale Datei – wer denselben Film hat, nimmt
  seinen eigenen; alle anderen bekommen ihn vom Gastgeber gestreamt (wie oben).
- Klappt ein Gerätename wie `smb://diskstation` nicht, die **IP-Adresse** des NAS verwenden
  (steht in der NAS-Oberfläche oder im Router).
- **Nicht unterstützt**: SMB-Verschlüsselung („Verschlüsselung erzwingen“ – am NAS auf „optional“ stellen),
  SMB 1, Kerberos-only-Domänen, NFS/FTP.
- Zugangsdaten werden nur auf der Brille gespeichert (in den App-Einstellungen, nicht im
  per USB sichtbaren Ordner), aber nicht verschlüsselt – am NAS am besten einen eigenen
  Nur-Lese-Benutzer für die Filme anlegen. Quelle löschen: Quelle öffnen → „Quelle entfernen“.

### Streams aus dem Netz

Statt Dateien gehen auch **http(s)-Adressen** (z. B. direkter Link von deinem NAS/Medienserver),
solange alle Teilnehmer darauf zugreifen können:

- im Menü *Film wählen → Adresse eingeben*, oder
- in `Android/data/com.themovies.cinema/files/Movies/urls.txt` (wird beim ersten Start angelegt):
  ```
  https://server/film.mp4
  Mein 3D-Film | https://server/film-3d.mp4 | sbs
  ```

Kopiergeschützte Dienste (Netflix, Disney+ …) lassen sich technisch nicht einbinden. Bitte nur Inhalte
verwenden, die du besitzt bzw. abspielen darfst.

## Bedienung

| Aktion | Quest-Controller | PC (ohne Brille) |
| --- | --- | --- |
| Menü zeigen / ausblenden | **B**, **Y** oder ☰; Trigger ins Leere zeigt es | **M** oder **Tab** |
| Klicken | Laserstrahl + **Trigger** | linke Maustaste |
| Play / Pause | **A** oder **X** | **Leertaste** |
| 10 s zurück / vor | Stick nach links / rechts | Pfeiltasten |
| Umschauen | Kopf | rechte Maustaste halten + ziehen |
| Platz/Blickrichtung neu ausrichten | „Ausrichten“ im Menü oder Meta-Taste lang drücken | – |
| Sitzhöhe anpassen | *Einstellungen → Sitzhöhe* | dito |
| Menü verschieben | Leiste unter dem Menü mit dem Trigger greifen; Stick vor/zurück ändert den Abstand | Leiste mit der Maus ziehen |

**Ablauf:** Eine Person tippt **„Kino eröffnen“** und sagt den anderen den **Raum-Code** (steht oben im
Menü). Die anderen tippen **„Mit Code beitreten“**. Dann *Film wählen* → *Abspielen*. Das Menü blendet
sich beim Start aus. Rechts oben im Menü siehst du, ob alle **bereit** sind.

Im **LAN** (alle im selben WLAN): „LAN-Kino eröffnen“, die anderen geben die angezeigte IP ein.

## Wie die Synchronisation funktioniert

- Der Gastgeber führt eine gemeinsame **Zeitleiste**: „Film läuft ab Position *P* zur Gastgeber-Zeit *T*“.
- Jede Quest misst per Ping laufend den **Uhrenversatz** zum Gastgeber (NTP-Prinzip, schnellste Messungen zählen).
- Damit weiß jedes Gerät jederzeit, wo der Film gerade sein sollte. Start und Sprünge werden
  **0,45 s im Voraus geplant**, damit alle gleichzeitig loslegen.
- Kleine Abweichungen (> 60 ms) werden durch **±5 % Tempo** unmerklich aufgeholt, große (> 1 s) durch einen
  Sprung – mit Vorhalt für die gemessene Spulzeit des Geräts.
- Wer neu dazukommt oder kurz die Brille absetzt, springt automatisch an die richtige Stelle.

Die Logik steckt in `Assets/TheMovies/Scripts/Core` (ohne Unity-Abhängigkeit) und ist mit Unit-Tests abgedeckt.

## Am PC testen

- **Play** im Editor: Szene `Assets/Scenes/Cinema.unity` (oder *The Movies → Szene öffnen*). Ohne Brille
  steuerst du mit Maus und Tastatur; mit **Quest Link/Air Link** läuft es direkt in der Brille.
- Mehrere Teilnehmer: Paket **Multiplayer Play Mode** installieren (*Window → Multiplayer Play Mode*),
  oder eine Windows-Version bauen und parallel starten. Zum Ausprobieren ohne Unity-Konto den LAN-Modus
  mit IP `127.0.0.1` nutzen.
- Filme sucht die PC-Version in *Videos*, *Movies* und *Downloads* deines Benutzerordners.

## Tests und Prüfungen ohne Unity

```
dotnet test tools/CoreTests            # Unit-Tests (Sync, Streaming, SMB/WebDAV/DLNA-Parser; auch im Unity Test Runner)
dotnet build tools/CompileCheck        # Kompiliert alle Skripte gegen UnityEngine-Referenzen + API-Stubs
dotnet build tools/CompileCheck -p:Android=true -p:AllPackages=true
```

Integrationstests gegen echte Server (werden sonst übersprungen): Samba-Freigabe `filme` (Benutzer
`tester`/`geheim`, mit `film.mp4`, `film.srt` und Ordner `Ordner mit Ä`), Gast-Freigabe `gast`, und ein
WebDAV-Ordner mit `Filme/film.mp4` + `film.srt`:

```
THEMOVIES_SMB=127.0.0.1:4455 THEMOVIES_DAV=http://127.0.0.1:8089/dav/ dotnet test tools/CoreTests --filter Category=Integration
```

## Projektaufbau

```
Assets/TheMovies/
  Scripts/Core/        Logik ohne Unity: Uhrenabgleich, Zeitleiste, Drift-Korrektur, 3D-Formate, Dateisuche
    Streaming/         Block-Cache, lokaler HTTP-Server mit Range-Support, Quellen-Streamer
    Network/           SMB2/3-Client (NTLMv2, Signierung, Freigabeliste), WebDAV, DLNA/SSDP, mDNS
  Scripts/Runtime/
    App/               AppController (Einstiegspunkt), Einstellungen
    Net/               SessionManager (Online/LAN), CinemaSync (gemeinsame Zeitleiste), NetworkPlayer, Sprachchat
    Video/             MoviePlayer (Unity VideoPlayer), MediaCoordinator (Sync anwenden), MediaLibrary,
                       NetworkLibrary (NAS-Quellen, Suche, Proxy-Wiedergabe)
    World/             Kinosaal, Leinwand mit 3D-Shader, Avatare
    XR/                Kamera-Rig, Controller, Laserpointer, PC-Steuerung
    UI/                3D-Menü (Hauptmenü, Tastatur, Steuerung, Filmauswahl)
  Resources/Shaders/   Stereo-Video-, Unlit-, Text- und Menü-Shader (Single Pass Instanced)
  Editor/              Menü „The Movies“: Projekt einrichten, APK bauen
  Tests/Editor/        NUnit-Tests
tools/                 dotnet-Projekte für Tests (inkl. NAS-Integrationstests) und Compile-Check
```

Szene und Prefabs werden vom Setup-Menü erzeugt (nicht im Repository). Die `.meta`-Dateien, die Unity
beim ersten Öffnen anlegt, solltest du mit einchecken.

## Mixed Reality (Passthrough)

In *Einstellungen → Umgebung* lässt sich zwischen **Kino** und **Mixed Reality** wählen (pro Person).
Im Mixed-Reality-Modus siehst du dein echtes Zimmer, die Leinwand schwebt in TV-Größe etwa 4 m vor dir.

Einmalig im Unity-Projekt: **The Movies → Mixed Reality (Passthrough) einrichten**. Das installiert das
Paket *Unity OpenXR: Meta* in der zur Unity-Version passenden Version (Unity 6.3 → 2.3.0, wie in Unitys
AR-Foundation-Beispielen) und – beim zweiten Aufruf – aktiviert die OpenXR-Features „Meta Quest: Session“
und „Meta Quest: Camera (Passthrough)“. Danach neu bauen. Ohne das Paket ist der Knopf ausgegraut.

Manuell geht es auch: *Window → Package Manager → + → Add package by name* →
`com.unity.xr.meta-openxr`, Version `2.3.0` (für Unity 6.3).

## Eigenes Kino-Modell (Asset Store o. Ä.)

Statt des eingebauten Saals kannst du ein beliebiges 3D-Kino verwenden (z. B. ein Kino-Paket aus dem
Unity Asset Store):

1. Modell in die Szene ziehen und so ausrichten, dass die Leinwand Richtung **+Z** zeigt.
2. Vier leere GameObjects **`Seat1`** bis **`Seat4`** anlegen: jeweils auf den **Boden** vor dem Sitz,
   Blickrichtung (blaue Achse) zur Leinwand.
3. Ein leeres GameObject **`ScreenAnchor`** an die **Unterkante Mitte** der Leinwand setzen, ebenfalls zur
   Leinwand ausgerichtet (die App setzt ihre eigene, gewölbte Leinwand dorthin).
4. Alles als Prefab unter **`Assets/TheMovies/Resources/TheMovies/CustomCinema.prefab`** speichern.

Die App nutzt das Prefab automatisch. Tipp für die Quest: Modelle mit wenigen Draw Calls und
gebackenem Licht (Lightmaps im Prefab bzw. „Unlit“-Materialien) laufen am flüssigsten.

## Bekannte Grenzen / Ideen

- Bedienung mit **Controllern** (Handtracking wird nicht unterstützt).
- Verlässt der **Gastgeber** das Kino, endet die Sitzung für alle.
- Videodecoding über Unitys VideoPlayer (Android MediaCodec): AC3/DTS-Ton und exotische Container
  werden ggf. nicht unterstützt (siehe Tipp oben).
- Was als Nächstes kommt, steht in der [ROADMAP.md](ROADMAP.md).
