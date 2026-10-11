# The Movies – gemeinsam Kino schauen auf der Meta Quest 3

Eine Unity-App für die **Meta Quest 3**, mit der du mit **bis zu 3 weiteren Personen** übers Internet
gemeinsam Filme schaust – auch in **3D** (Side-by-Side / Over-Under). Alle sitzen nebeneinander in
einem virtuellen Kinosaal, sehen sich als Avatare und hören sich (optional) per Sprachchat.

Jede Person spielt **ihre eigene Kopie** des Films ab (Datei auf der Quest oder Stream-URL). Die App
synchronisiert nur Start, Pause und Position – dadurch volle Bildqualität bei minimaler Bandbreite.

## Funktionen

- **Kinosaal** mit großer Leinwand (4 Größen), Sitzreihen, Vorhängen, Saallicht, das beim Abspielen
  dunkler wird, und Wänden, die das Licht der Leinwand aufnehmen
- **3D-Filme**: Side-by-Side und Over-Under, jeweils Half oder Full, „Augen tauschen“; das Format wird
  aus dem Dateinamen (`3D.HSBS`, `Half-OU`, `SBS`, …) und der Bildgröße erkannt
- **Gemeinsam schauen** mit bis zu 4 Personen:
  - **Online** über einen kurzen **Raum-Code** (Unity Multiplayer Services/Relay, keine Portfreigabe nötig)
  - **LAN** direkt per IP-Adresse (ohne Unity-Konto)
- **Synchronisation**: Uhrenabgleich zum Gastgeber, geplanter gemeinsamer Start, sanfte
  Geschwindigkeitskorrektur bei kleinen Abweichungen, Sprung bei großen
- Alle dürfen Play/Pause/Spulen/Film wählen; die Teilnehmerliste zeigt, wer bereit ist, wer puffert und
  wem die Datei fehlt
- **Avatare** (Kopf, Hände, Name, Mundbewegung beim Sprechen) auf festen Plätzen nebeneinander
- **Sprachchat** über Unity Vivox (optional)
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

### Streams

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
dotnet test tools/CoreTests            # Unit-Tests der Sync-Logik (laufen auch im Unity Test Runner)
dotnet build tools/CompileCheck        # Kompiliert alle Skripte gegen UnityEngine-Referenzen + API-Stubs
dotnet build tools/CompileCheck -p:Android=true -p:AllPackages=true
```

## Projektaufbau

```
Assets/TheMovies/
  Scripts/Core/        Sync-Logik ohne Unity: Uhrenabgleich, Zeitleiste, Drift-Korrektur, 3D-Formate, Dateisuche
  Scripts/Runtime/
    App/               AppController (Einstiegspunkt), Einstellungen
    Net/               SessionManager (Online/LAN), CinemaSync (gemeinsame Zeitleiste), NetworkPlayer, Sprachchat
    Video/             MoviePlayer (Unity VideoPlayer), MediaCoordinator (Sync anwenden), MediaLibrary
    World/             Kinosaal, Leinwand mit 3D-Shader, Avatare
    XR/                Kamera-Rig, Controller, Laserpointer, PC-Steuerung
    UI/                3D-Menü (Hauptmenü, Tastatur, Steuerung, Filmauswahl)
  Resources/Shaders/   Stereo-Video-, Unlit-, Text- und Menü-Shader (Single Pass Instanced)
  Editor/              Menü „The Movies“: Projekt einrichten, APK bauen
  Tests/Editor/        NUnit-Tests
tools/                 dotnet-Projekte für Tests und Compile-Check
```

Szene und Prefabs werden vom Setup-Menü erzeugt (nicht im Repository). Die `.meta`-Dateien, die Unity
beim ersten Öffnen anlegt, solltest du mit einchecken.

## Bekannte Grenzen / Ideen

- Bedienung mit **Controllern** (Handtracking wird nicht unterstützt).
- Verlässt der **Gastgeber** das Kino, endet die Sitzung für alle.
- Videodecoding über Unitys VideoPlayer (Android MediaCodec): AC3/DTS-Ton und exotische Container
  werden ggf. nicht unterstützt (siehe Tipp oben).
- Ideen für später: 180°/360°-Videos, Untertitel (SRT), Emojis/Reaktionen, Popcorn 🍿
