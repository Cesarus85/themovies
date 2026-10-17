# Art-Brief: Kinopalast und Heimkino (Blender → Unity → Quest 3)

Auftrag für die Modellierung in **Blender 5.2** (installiert unter `/Applications/Blender.app`). Ergebnis sind zwei
Raum-Modelle, die die App statt ihrer eingebauten Räume lädt. Bitte diesen Brief vollständig lesen; Maße, Namen und
Export-Einstellungen sind verbindlich, weil die App die Modelle automatisch einbaut.

## 1. Was gebaut wird

| Raum | Stil | Datei |
| --- | --- | --- |
| **Kinosaal** | Klassischer **Kinopalast** der 1920er/30er Jahre | `Assets/TheMovies/Art/Cinema/Cinema.fbx` |
| **Heimkino** | Klassisches, gemütliches **Wohnzimmer-Kino** | `Assets/TheMovies/Art/Home/Home.fbx` |
| **Dachterrasse** | Open-Air über der Stadt (Abschnitt 10) | `Assets/TheMovies/Art/Rooftop/Rooftop.fbx` |
| **Weltraum-Lounge** | Aussichtsdeck einer Raumstation (Abschnitt 11) | `Assets/TheMovies/Art/Space/Space.fbx` |
| **Orient-Express** | Salonwagen der 1920er/30er | `Assets/TheMovies/Art/OrientExpress/OrientExpress.fbx` |
| **Präsidentenjet** | Konferenz-/Lounge-Kabine eines Präsidenten-Jumbos | `Assets/TheMovies/Art/Jet/Jet.fbx` |

### Kinopalast – Stilvorgaben

- **Bühnenportal (Proszenium)** mit vergoldeter Ornamentik, darin die Leinwand; davor ein schwerer **roter
  Samtvorhang** mit Goldfransen, seitlich gerafft, oben ein drapierter Querbehang (Lambrequin).
- **Seitenwände** mit Pilastern (Kapitelle), dazwischen Stoffbespannung (Damast, dunkelrot) oder Stuckfelder;
  **Logen/Ränge** mit Balustraden an den Seitenwänden, eine **Galerie (Rang)** hinten.
- **Decke**: Kassettendecke oder flache Kuppel mit zentraler Rosette und **Kronleuchter**; umlaufendes Gesims.
- **Wandleuchten** (Art déco oder klassisch, Messing mit Glas/Stoffschirm) zwischen den Pilastern.
- **Bestuhlung**: ansteigende Reihen (Stadion), rote Samtsessel mit Holz-/Messing-Armlehnen, gemusterter Teppich in
  den Gängen, Stufenleisten.
- **Farben**: Rot (Samt, Stoff), Gold (Ornament, Messing), Creme/Elfenbein (Stuck), dunkles Holz. Warm, festlich,
  nie reines Schwarz (auch dunkle Bereiche haben Farbe).
- Vorbilder (Fotos im Netz): Fox Theatre Atlanta, Paramount Theatre Oakland, Tuschinski Amsterdam, Grauman's
  Egyptian, Loew's Jersey, Delphi Filmpalast Berlin, Lichtburg Essen, Schauburg Karlsruhe.

### Heimkino – Stilvorgaben

- Wohnzimmer mit **Holzvertäfelung** und/oder **Bücherregalen**, **Kamin** mit Sims, **Sofa für vier** (Chesterfield
  o. ä., Samt oder Leder), **Perserteppich**, Stehlampen, Beistelltisch, **Fenster** mit Nachthimmel/Stadtlichtern.
- Die Leinwand hängt an der Stirnwand (Rahmen oder Rollo-Leinwand mit Kasten oben).
- Warm, abends, gedämpft; Lichtquellen: Stehlampen, Kamin, ein paar Wandleuchten.

## 2. Koordinaten, Maße, Marker (verbindlich)

Blender-Einheiten = **Meter**, **Z oben**. Die vier Standardplätze der Zuschauer sind tatsächliche
Sitzplätze der Teilnehmerreihe (im Heimkino entsprechende Sofaplätze):

- stehen **auf dem Boden bei z = 0**, in einer Reihe entlang **X**, **0,75 m** Abstand, mittig um den Ursprung:
  x = −1,125 / −0,375 / +0,375 / +1,125, y = 0;
- blicken nach **+Y**; die Leinwand liegt auf **+Y**;
- Augenhöhe sitzend: **1,25 m** über diesem Boden.

**Definition der Sitzmarker (alle Räume):** Ein Sitzmarker liegt am Bodenpunkt senkrecht unter den
Augen eines sitzenden Erwachsenen, auf der **Bodenhöhe der jeweiligen Reihe**. Die App addiert
**1,25 m Augenhöhe**. Ein Kopf mit **0,20 m Durchmesser** muss vollständig vor/frei von
Rückenpolster, Rahmen und allen anderen Oberflächen liegen, mit **mindestens 0,08 m Luft in jeder
Richtung**. Die Prüfung erfolgt an der tatsächlichen exportierten Geometrie, einschließlich aller
Polsterdetails, als Kugel mit Radius 0,10 m über **Mittenhöhen 1,10–1,35 m** relativ zum jeweiligen
Boden. Der kleinste Oberflächenabstand muss also mindestens 0,18 m betragen; bei diskreten Proben
ist deren Zwischenraum konservativ abzusichern (5-mm-Schritte: zusätzlich 2,5 mm Abstand).
Die Kopfmitte muss außerhalb der Möbel liegen. Marker und die Bodenprojektion der Kopffläche
(±0,10 m in X/Y) bleiben über dem Sitzpolster, nicht vor der Sitzvorderkante. Eine einzelne Probe
in Schulterhöhe 0,85 m und der frühere pauschale 0,12-m-Rückenabstand genügen ausdrücklich nicht.
Bestehende Räume bei einem fehlgeschlagenen Test melden; Änderungen nur im beauftragten Raum.

`Seat1` … `Seat4` bleiben exakt an den oben festgelegten Standardkoordinaten. Falls zur Korrektur nötig, wird der
**gesamte Saal einschließlich aller Sitze, Architektur, Lichtquellen und Leinwandmarker starr verschoben**.
Sitze dürfen dabei nicht relativ zum Saal versetzt werden. Bei bereits gebackenen Meshes bleiben beide UV-Sätze
und der Lichtatlas erhalten; eine reine gemeinsame Translation benötigt keinen neuen Bake.

**Marker** (Empties, Namen exakt so, ohne `.001`; Orientierung beliebig):

| Name | Lage |
| --- | --- |
| `Seat1` … `Seat4` | Bodenprojektion der angelehnten Kopfposition gemäß obiger Definition; exakte Standardkoordinaten, Seat1 links |
| `Spot_000`, `Spot_001`, … | Ein Marker pro tatsächlichem Sitzplatz, lückenlos mit drei Ziffern; gleiche Kopfpositionsdefinition und jeweilige Reihenbodenhöhe. Einschließlich aller vier Standardplätze; im Heimkino später je Sofasitzplatz. |
| `ScreenAnchor` | Unterkante Mitte der Leinwand |
| `ScreenTop` | Oberkante der größtmöglichen Leinwand (gleiches x/y wie der Anker, nur höher) |

Im Kinosaal gibt es **152 Spot-Marker**: `Spot_000`–`Spot_119` im Parkett,
`Spot_120`–`Spot_139` auf der hinteren Galerie und `Spot_140`–`Spot_151` in den Logen.
`Seat1`–`Seat4` sind deckungsgleich mit `Spot_044`–`Spot_047`.
Jeder Spot muss direkt vor einem Rückenpolster und über einem Boden liegen; Abstand zwischen zwei Spots mindestens 0,4 m.
`ScreenAnchor` und `ScreenTop` bewegen sich mit dem Saal und bleiben mittig im Portal.

Die App richtet das Modell anhand dieser Marker aus. Sie zeichnet die **Leinwand selbst** (leicht gewölbt, eigener
Shader). Bitte **keine Leinwandfläche modellieren**, nur Rahmen/Portal/Vorhänge drumherum und eine dunkle Wand dahinter.

### Kinosaal – Maße

- Leinwand: Unterkante **0,4 m** über dem Boden der Zuschauerreihe; bis **19 m breit** und **8,2 m hoch**
  (die App wählt je nach Film und Einstellung). Das Portal muss diese Öffnung frei lassen.
  Die ursprüngliche Konstruktionslage y = 11,9 m wird mit dem gesamten Saal verschoben: nach der am fertigen
  Parkettpolster gemessenen Kopfkorrektur um **+0,4575765 m in Y** liegen beide Leinwandmarker bei
  **y = 12,3575765 m**. Z bleibt 0,4 m für `ScreenAnchor` bzw. 8,6 m für `ScreenTop`.
- Ursprüngliche Konstruktionsmaße vor der gemeinsamen Kopfkorrektur: Raum etwa **22 m breit**, Rückwand
  **7,2 m** hinter der Reihe, Stirnwand ~**13,6 m** davor, Decke ~**10,5 m** hoch.
  Abweichungen sind ok, wenn es dem Raum gut tut.
- Stadion-Bestuhlung: Reihenabstand **1,65 m**, Stufenhöhe **0,45 m**; die Zuschauerreihe ist eine mittlere Reihe
  (2 Reihen davor tiefer, 3 dahinter höher), Sessel ~0,55–0,6 m breit. Vordere Reihen dürfen die Sicht aus 1,25 m
  Augenhöhe auf die Leinwand-Unterkante nicht verdecken.

- Erhöhte Plätze: Von jedem Galerie-/Logen-Spot muss bei **1,25 m Augenhöhe** die volle 19 × 8,2 m große
  Leinwand einschließlich unterer Ecken sichtbar sein. Prüfraster 39 × 17 Punkte; zusätzliche Reserveprobe
  bei 1,25 m Augenhöhe. Die Galeriefront ist **0,80 m**, die Logenfronten sind **0,75 m** über ihrem Boden hoch.
  Damit nachfolgende Logen nicht selbst die Leinwand verdecken, liegen ihre Böden von hinten zur Bühne hin
  bei **2,90 / 1,30 / 0,00 m** statt bisher einheitlich 3,62 m. Sitze und Spots folgen dem jeweiligen Boden;
  Parkett, Galerie-Sitzboden (4,825 m), Kopfmarkerdefinition und Teilnehmerkoordinaten bleiben unverändert.

### Heimkino – Maße

- Raum etwa **8,4 m × 7,8 m**, Decke **2,75 m**; das Sofa steht mit seinen vier Plätzen an den Sitzpositionen.
- Leinwand an der Stirnwand **5,2 m** vor den Plätzen, Unterkante **0,78 m**, bis **1,85 m** hoch (bis ~6 m breit).

## 3. Budget für die Quest 3 (verbindlich)

Die Quest rendert jedes Bild zweimal (ein Bild pro Auge) bei 72–90 Hz auf einer Mobil-GPU.

| | Kinosaal | Heimkino |
| --- | --- | --- |
| Dreiecke gesamt | ≤ **250 000** | ≤ **120 000** |
| Materialien | ≤ **16** | ≤ **12** |
| Basisfarb-Texturen | je ≤ 2048², besser 1024² | je ≤ 2048² |
| Licht-Textur (ein Atlas) | **4096²** | **2048²** |

- **Statische Objekte pro Material zusammenfassen** (wenige Objekte = wenige Draw Calls).
- **Unsichtbare Flächen löschen** (Rückseiten von Wänden, Unterseiten von Sitzen, Flächen in Wänden).
- Details lieber in Basisfarbe und gebackenes Licht als in Geometrie; keine Normal Maps (werden nicht verwendet).
- Viele gleiche Sessel: eine gute Low-Poly-Version (~500–1500 Dreiecke) vervielfältigen.

## 4. Materialien

- **Principled BSDF**, Basisfarbe als **Image Texture → Base Color** (oder nur eine Farbe). Nur die Basisfarbe kommt
  in Unity an; Rauheit, Metall, Normal Maps werden ignoriert.
- Texturen speichern als `Textures/<Materialname>_BaseColor.png` (sRGB, 8 Bit), Kachelung über die **erste UV-Ebene**
  „UVMap“ (Maßstab sinnvoll, z. B. 1 UV-Einheit = 1 m).
- **Stichworte im Materialnamen** steuern die Darstellung in der App:

| Stichwort im Namen | Wirkung |
| --- | --- |
| `velvet` / `samt` | Samtglanz an streifenden Kanten (Sessel, Vorhang) |
| `gold` / `brass` / `messing` / `chrome` / `metal` | Glanzlichter |
| `emit` / `glow` / `bulb` / `leucht` | leuchtet selbst, wird mit dem Saallicht gedimmt (Glühbirnen, Lampenschirme, Kronleuchter-Kristalle) |
| `unlit` | leuchtet immer gleich (Notausgang-Schild, Sternenhimmel) |
| `planet` | eigener Planeten-Shader: Tag-/Nachtseite mit fester Sonne (Marker `Sun`), `<Material>_Night.png` = Stadtlichter |
| `scroll<N>` | **vorbeiziehende Kulisse**: die Textur wandert waagrecht um N/10000 ihrer Breite pro Sekunde (z. B. `Far_unlit_scroll8`); Textur muss horizontal nahtlos kacheln. Mehrere Ebenen mit unterschiedlichem Tempo (nah schnell, fern langsam) ergeben Tiefe. |

| `scrollv<N>` | wie `scroll`, aber senkrecht (N/10000 der Texturhöhe pro Sekunde; aufsteigende Flammen, Rauch) |
| `flicker<N>` | das eigene Leuchten flackert um N % (Flammen 15–35, Glühbirnen/Lichterketten 2–6); nur zusammen mit `emit`/`glow`/`bulb`/`unlit` |
| `twinkle<N>` | helle Bildpunkte funkeln um N % (Sterne am Nachthimmel; nicht im Weltraum – dort funkeln Sterne nicht) |
| `blink<N>` | Leuchten blinkt kurz auf, Periode N/10 s (Positionslichter, Kontrollleuchten) |

Jede Lampe flackert/blinkt mit eigener Phase (abhängig von ihrer Position), auch wenn alle Lampen in einem Mesh liegen.

Bewegte Objekte: Ein Objekt/Empty mit `_rotate<Sekunden pro Umdrehung>` im Namen dreht die App um seine lokale
Hochachse (Blender lokal Z), Kinder drehen mit (z. B. `Earth_rotate5520`).

Feuerschein: Empties `FireLight_<Radius in cm>` (höchstens 4 pro Raum, z. B. `FireLight_350` vor dem Kamin) lassen
das gebackene Licht in diesem Umkreis warm mitflackern – Wände, Möbel und Boden im Schein des Feuers bewegen sich mit.

Marken und Hoheitszeichen: keine offiziellen Siegel, Wappen, Logos oder Kennzeichen realer Staaten, Behörden,
Bahn- oder Fluggesellschaften (z. B. das Siegel des US-Präsidenten, „Air Force One“-Schriftzug, Logo der Wagons-Lits)
– eigene, gleichwertige Gestaltung im passenden Stil.

## 5. Licht backen (der wichtigste Schritt für den Look)

Die App hat keine Echtzeit-Lampen. Das ganze Licht kommt aus einer **in Blender gebackenen Licht-Textur**:

1. Jedes Mesh bekommt eine **zweite UV-Ebene „Lightmap“** (Index 1): eindeutige, **nicht überlappende** Inseln für
   den ganzen Raum in **einem** Atlas, Rand ≥ 4 Pixel bei der Atlasgröße.
2. Beleuchtung im Zustand **„Saallicht an“**: Kronleuchter, Wandleuchten, indirektes Licht in Gesimsen, Bühnenlicht
   auf dem Vorhang. Leuchtende Materialien (`emit` …) auch in Blender emissiv, damit sie zur Beleuchtung beitragen.
3. **Cycles-Bake**, Typ **Diffuse**, nur **Direct + Indirect** (ohne Color), ≥ 256 Samples, Denoising; Margin 8 px.
4. Speichern mit **View Transform „Standard“, Look „None“, Exposure −1,0** (= halbe Helligkeit), PNG RGB 8 Bit, als
   `Textures/<Modellname>_Light.png` (also `Cinema_Light.png` bzw. `Home_Light.png`). Die App multipliziert mit 2.
   Hellste Stellen dürfen im gespeicherten Bild nicht großflächig bei 255 anstoßen.

Beim Film dimmt die App dieses Licht auf ~12 % und legt den farbigen Schein der Leinwand darüber. Der Raum soll also
mit Licht festlich wirken und ohne Licht nicht in Schwarz versinken (ein Grundlicht im Bake schadet nicht).

## 6. Export

FBX mit genau diesen Einstellungen (siehe Referenz `tools/blender/test_room.py`):

```python
bpy.ops.export_scene.fbx(
    filepath=".../Cinema.fbx", use_selection=False, object_types={"EMPTY", "MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    use_space_transform=True, bake_space_transform=True, mesh_smooth_type="FACE",
    use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
```

- Vor dem Export Hilfsobjekte entfernen (Bake-Lampen, Kameras); nur Meshes und die Marker-Empties exportieren.
- Ordner: `Assets/TheMovies/Art/Cinema/` mit `Cinema.fbx` und `Textures/` (bzw. `Art/Home/` mit `Home.fbx`).

## 7. Arbeitsweise

- **Reproduzierbar per Python-Skript**: `tools/blender/cinema.py` und `tools/blender/home.py` bauen den Raum
  vollständig, backen und exportieren (Aufruf wie im Referenzskript: `Blender -b --factory-startup -P … -- <Ausgabeordner>`).
  `.blend`-Dateien gehören nicht nach `Assets/` (Unity würde Blender zum Importieren brauchen); falls nötig unter
  `art/blender/`.
- **Fremde Assets** nur mit passender Lizenz (CC0, CC-BY mit Namensnennung, Unity Asset Store, Fab Standard License);
  nicht: ND, „Editorial“, „keine Weitergabe in Apps“. Jede Quelle mit Lizenz in `docs/CREDITS.md` eintragen.
  Fremde Modelle auf das Budget reduzieren und neu auf die Lightmap-UV abwickeln.
- **Vorschau zur Abnahme**: Cycles-Renderings nach `Logs/art-preview/` – aus Sicht von `Seat2` in 1,25 m Augenhöhe
  Richtung Leinwand, einmal mit „Saallicht an“, einmal gedimmt (Bake-Licht × 0,12); dazu eine Übersicht von hinten oben.
- **Bericht** am Ende: Dreiecke je Objekt und gesamt, Materialien, Texturen mit Größe, offene Punkte.

## 8. Quellen (Recherche Oktober 2026)

Ein fertiger, freier Kinopalast existiert nicht (die freien Säle sind modern, zu schwer oder kostenpflichtig).
Deshalb: **Raumhülle selbst modellieren**, Texturen und einzelne Requisiten aus freien Quellen.

**Das Repository ist öffentlich.** Daraus folgt:

- Bevorzugt **CC0** (Poly Haven, ambientCG): ohne Konto per Download/API, keine Namensnennung nötig.
- **CC BY 4.0** (viele Sketchfab-Modelle) ist erlaubt, aber mit Eintrag in `docs/CREDITS.md` (Titel, Autor, Link,
  Lizenz, „verändert“). Sketchfab-Downloads brauchen ein Konto → nur, wenn der Nutzer die Dateien selbst lädt.
- **Nicht** ins Repo: Unity-Asset-Store-, Fab- und ShareTextures-Dateien (deren Lizenzen verbieten die Weitergabe
  der Quelldateien). Nicht verwenden: CC BY-NC, CC BY-ND, „Editorial“.
- In `Assets/` nur die **fertigen, verkleinerten** Texturen und das exportierte FBX; heruntergeladene Rohdateien
  (4K/8K-Texturen, Original-Modelle) unter `art/sources/` – dieser Ordner ist von Git ausgeschlossen.

Hinweis zu Ornamenten: Die App nutzt **keine Normal Maps**. Feine Ornamentik also als (vereinfachte) Geometrie, deren
Plastizität das gebackene Licht zeigt, oder als in die Basisfarbe gebackene Detailschattierung (AO/Cavity aus einem
hochaufgelösten Scan auf eine niedrige Fläche gebacken).

### Kinopalast

| Zweck | Quelle | Lizenz |
| --- | --- | --- |
| Samt (Sessel, Vorhang, Logenbrüstung) | [Poly Haven velour_velvet](https://polyhaven.com/a/velour_velvet) | CC0 |
| Wandbespannung (Brokat) | [Poly Haven quatrefoil_jacquard_fabric](https://polyhaven.com/a/quatrefoil_jacquard_fabric), [floral_jacquard](https://polyhaven.com/a/floral_jacquard) | CC0 |
| Gold/Messing | [ambientCG Metal048A](https://ambientcg.com/view?id=Metal048A), [Metal042A](https://ambientcg.com/view?id=Metal042A) | CC0 |
| Teppich | [ambientCG Carpet015](https://ambientcg.com/view?id=Carpet015) (Muster gern selbst ergänzen) | CC0 |
| Stuck/Putz, Marmor, Holz | [Poly Haven white_plaster_02](https://polyhaven.com/a/white_plaster_02), [marble_01](https://polyhaven.com/a/marble_01), [dark_wood](https://polyhaven.com/a/dark_wood), [walnut_veneer](https://polyhaven.com/a/walnut_veneer) | CC0 |
| Zentraler Kronleuchter | [Poly Haven Chandelier_03](https://polyhaven.com/a/Chandelier_03) (29k Dreiecke → auf ~8–10k reduzieren) | CC0 |
| Kapitell-Vorlage | [Corinthian capital, Virtual Museums of Małopolska](https://sketchfab.com/models/1b61fd199e744afa9bdb8f46cf843e31) (Scan, nur als Vorlage zum Backen) | CC0 |
| Sessel | selbst modellieren, ~1–2k Dreiecke (Vorbild: [Theater Chair – Red Velvet](https://sketchfab.com/models/87815e72aa35450198e1cd98e888d68f), CC BY, nur als Optik-Referenz) | – |
| Vorhang | selbst (Blender-Stoffsimulation, als statisches Mesh) | – |
| Wandleuchte (optional) | [Art Deco Wall Light](https://sketchfab.com/models/a16f2db93e384c278153df472a5af35b), 1k Dreiecke | CC BY (Konto nötig) |

Gesimse, Profile, Rosetten, Balustraden: mit Blender-Bordmitteln per Skript (Kurven-Bevel mit eigenem Profil bzw.
den Presets „Cornice/Crown Molding“, Geometry Nodes „Curve to Mesh“, Screw/Lathe, Array/Curve-Modifier).

Vorbilder: [Fox Theatre Atlanta](https://en.wikipedia.org/wiki/Fox_Theatre_(Atlanta)) (Sternenhimmel-Decke),
[Paramount Oakland](https://en.wikipedia.org/wiki/Paramount_Theatre_(Oakland,_California)) (hinterleuchtete
Deco-Gitterdecke), [Loew's Jersey](https://en.wikipedia.org/wiki/Loew%27s_Jersey_Theatre) (vergoldetes Portal,
Farbpalette Gold/Blau/Elfenbein), [Grauman's Egyptian](https://en.wikipedia.org/wiki/Grauman%27s_Egyptian_Theatre)
(Sonnen-Medaillon an der Decke), [Radio City Music Hall](https://en.wikipedia.org/wiki/Radio_City_Music_Hall)
(konzentrische Bögen ums Portal mit verdecktem Licht – lässt sich gut prozedural bauen und backen),
[Delphi Filmpalast](https://cinematreasures.org/theaters/3235) (Rang, großer Kronleuchter),
[Lichtburg Essen](https://de.wikipedia.org/wiki/Lichtburg_(Essen)) (Kuppel mit indirekt beleuchteten Ringen).

### Heimkino

| Zweck | Quelle | Lizenz |
| --- | --- | --- |
| Sofa für vier | [Poly Haven sofa_03](https://polyhaven.com/a/sofa_03) (2,73 m, 8k Dreiecke) | CC0 |
| Sessel, Hocker | [ArmChair_01](https://polyhaven.com/a/ArmChair_01), [Ottoman_01](https://polyhaven.com/a/Ottoman_01) | CC0 |
| Tische, Kommode | [coffee_table_round_01](https://polyhaven.com/a/coffee_table_round_01), [ClassicConsole_01](https://polyhaven.com/a/ClassicConsole_01) | CC0 |
| Regal, Bücher | [wooden_bookshelf_worn](https://polyhaven.com/a/wooden_bookshelf_worn), [book_encyclopedia_set_01](https://polyhaven.com/a/book_encyclopedia_set_01) | CC0 |
| Deckenleuchte | [Chandelier_01](https://polyhaven.com/a/Chandelier_01) oder [Chandelier_02](https://polyhaven.com/a/Chandelier_02) | CC0 |
| Deko | [ornate_mirror_01](https://polyhaven.com/a/ornate_mirror_01), [fancy_picture_frame_01](https://polyhaven.com/a/fancy_picture_frame_01), [vintage_grandfather_clock_01](https://polyhaven.com/a/vintage_grandfather_clock_01) | CC0 |
| Kamin | selbst modellieren (Alternative: [Victorian Fire Place](https://sketchfab.com/models/7154a82dc7844075996c65aeb508bfd5), CC BY, Konto nötig) | – |
| Stehlampe, Fenster | selbst modellieren | – |
| Böden, Wände | [herringbone_parquet](https://polyhaven.com/a/herringbone_parquet), [dark_paneled_wood](https://polyhaven.com/a/dark_paneled_wood), [leather_red_02](https://polyhaven.com/a/leather_red_02) | CC0 |

Poly-Haven-Modelle sind teils schwer (z. B. mantel_clock_01 mit 30k): auf das Budget reduzieren (Decimate), Texturen
auf 1K/2K verkleinern und mehrere Möbel möglichst auf einen gemeinsamen Textur-Atlas legen.

## 9. Übernahme in Unity (macht die App-Seite)

Unity-Menü **The Movies → Raum-Modell aufbereiten → Kinosaal / Heimkino** (oder im Batchmode
`-executeMethod TheMovies.EditorTools.RoomModelSetup.PrepareAllBatch`) stellt Import, Texturen und Materialien ein,
prüft Marker und Budget und erzeugt `Resources/TheMovies/CustomCinema.prefab` bzw. `CustomHome.prefab`. Fehlt ein
Prefab, nutzt die App den eingebauten Raum.


## 10. Dachterrasse – dritter Raum

**Stil:** Mediterranes Freiluftkino über einer Stadt zwischen Hügeln zur blauen Stunde/frühen Nacht.
Teakdeck, gekalkte Brüstung mit Kalksteinabdeckung, zwei cremefarbene Loveseats, indigoblaue
Kissen und gestreifte Decken, gewebter Außenteppich, Terrakotta, Oliven, Lavendel und kleine
Fächerpalmen. Pflanzen bestehen aus opaker Geometrie; keine Alpha-Karten oder Glasscheiben.
Warmweiße Festons an schlanken Metallpfosten, Kerzenlaternen, Beistelltische, eine kleine Bar
mit niedrigen Rückenlehnen-Hockern und ein rollbarer Servierwagen.

**Maße und Sitze:** Deck 13,6 × 14 m (X ±6,8; Y −4,9 bis +9,1), Oberkante Z=0.
Brüstungsabdeckung maximal 0,79 m. Die vier Standardmarker bleiben exakt bei
X −1,125 / −0,375 / +0,375 / +1,125, Y=Z=0 und entsprechen Spot_000–003.
Weitere Plätze: zwei Lounge-Sessel (−3,15 / −4,35; Y=0,08), zwei Bankplätze
(X ±0,70; Y=−2,82) und zwei niedrige Barhocker (X 3,15 / 4,25; Y=−2,67),
alle auf Z=0, alle nach +Y. Spot_000–009 stehen unter den angelehnten Köpfen;
die Kopfhülle gemäß Abschnitt 2 ist frei und bleibt über dem Sitzpolster.
Die akzeptierte Szene wurde insgesamt starr um Y=−0,10 m verschoben (inklusive Panorama und
Leinwandmarker); Loveseat-Köpfe liegen dadurch 0,10 m weiter vorn relativ zum Sitz. Die übrigen
Spots wurden um 0,08 m relativ zu ihren Sitzen nach vorn gesetzt. Geometrie, UVs und Texturen
bleiben unverändert; kein neuer Bake. Bei Schulterhöhe beträgt der Rückenabstand nun 0,22 bzw. 0,20 m.
Sitzende Augenhöhe ist 1,25 m; jeder Platz muss das vollständige Projektionsraster sehen.

**Leinwand:** ScreenAnchor=(0; 8,40; 1,10), ScreenTop=(0; 8,40; 5,10).
Freie Fläche 9,5 × 4 m für die App-Bildbreiten 3,5–9,5 m. Stabiler Teakrahmen und
dunkle bauliche Nachbarwand dahinter, keine modellierte Leinwandfläche.

**Tiefe und Himmel:** Zwölf echte Nachbardächer mit Kaminen, Gesimsen, Tanks und Antennen
im Abstand von ungefähr 10–40 m. Dahinter vollständiges CC0-Panorama „Clarens Night 02“,
Dimitrios Savva (Fotografie), Jarod Guest (Verarbeitung) / Poly Haven, 4096 × 2048, ohne Winkelstreckung auf einer Kugel mit Radius 150 m.
Material und Textur heißen Rooftop_Panorama_unlit bzw. Rooftop_Panorama_unlit_BaseColor.png.
Das vollständige equirektangulare Foto bleibt erhalten, nur Belichtung und weiche HDR-Schulter
werden angepasst. Tiefblauer Himmel und begrenzte Stadtlichter lassen dem Filmbild Vorrang.

**Budget und Pipeline:** ≤120.000 Dreiecke, ≤14 Materialien und Material-Meshes,
Basisfarben 1024² (maximal 2048²), ein 2048² Lichtatlas, ein 4096×2048 Panorama.
Cycles Diffuse Direct+Indirect ohne Color, mindestens 256 Samples, Denoising, 8 px Bake-Rand,
≥4 px UV-Inselabstand. Standard/None, Exposure −1, RGB8. Vorschauen multiplizieren ausschließlich
Basisfarbe × gespeicherten Lichtatlas ×2; bei Filmlicht ×0,04. Unlit-Panorama und ferne Fenster
bleiben unverändert. Keine Echtzeitlampen im Export.

Aufruf: `Blender -b --factory-startup -P tools/blender/rooftop.py -- Assets/TheMovies/Art/Rooftop`.
Prüfung: `Blender -b --factory-startup -P tools/blender/verify_rooftop.py` (inklusive rein lesender
Kopffreiheitsprüfung von Home/Cinema; deren Befunde in `all-rooms-head-clearance.json`).
Nur Kopfkorrektur aus vorhandenem Bake-Checkpoint: gleicher Build-Aufruf mit `--head-positions-only`
(idempotent, ohne Textur- oder Bake-Änderung).
Lieferung: `Rooftop.fbx`, `Textures/`, Sitz-/Leinwandmarker; Bake-Checkpoint nur unter
`art/blender/`. Reimport-, UV-, Kopfpositions- und Sichtlinienprüfungen sowie gebackene Ansichten
unter `Logs/art-preview/`; Bericht `ROOFTOP-REPORT.md`. Keine Änderungen an Kino/Heimkino,
App-Code oder Unity-Konfiguration. Rohquellen ausschließlich CC0 von Poly Haven, unter
`art/sources/`; Herkunft und Bearbeitung in `docs/CREDITS.md` und `art/rooftop-sources.json`.

Aktueller Dachterrassen-Export: **95.056 Dreiecke, 14 Materialien/Material-Meshes, zehn Plätze**.
Die Oliventöpfe enthalten abgeschirmte warme Uplights; die Blätter sind räumliche Low-Poly-Meshes.

## 11. Weltraum-Lounge – vierter Raum

**Gestaltung:** „Orbit / 04“, eine elegante Aussichts-Lounge einer Orbitalstation. Gerundetes,
16 m breites Deck, Rückwand bei Y=−5 m, vorderer Fensterrand bei Y=11 m, Decke bei
Z=6,25 m. Porzellanweiße und graphitfarbene Verkleidung, Walnuss, cognacfarbenes Leder,
gewebter Teppich, Champagner-Metall, warme indirekte Lichtvouten und kühles blaues Erdlicht.
Das durchgehende Front-/Seitenfenster ist eine geometrische Öffnung **ohne Glasscheibe**.
Instrumente, eine kleine Hospitality-Konsole und opak modellierte Pflanzen ergänzen den Raum.

**Plätze und Leinwand:** Vier einzelne Lounge-Sessel an den unveränderten Standardkoordinaten;
Seat1–4 entsprechen Spot_000–003. Vier weitere Sessel: X=−4,1 / −3,2 / +3,2 / +4,1,
Y=−0,6, Z=0 (Spot_004–007). Alle blicken nach +Y; alle Marker liegen unter den sitzenden
Köpfen und über Polstern. Die vollständige Kopfhülle aus Abschnitt 2 gilt unverändert.
ScreenAnchor=(0; 8,3; 1,35), ScreenTop=(0; 8,3; 5,35). Der schlanke abgehängte Rahmen
lässt 9,5 × 4 m vollständig frei. Keine Leinwandfläche im Export. Die Erde bleibt seitlich
und unterhalb der maximalen Projektion sichtbar; die App kann auf 3,5 m Bildbreite verkleinern.

**Neue Konvention: planet / Planet.** Die Erde ist eine eigenständige Kugel, keine Fototapete.
Material **Earth_Planet**, Tagkarte `Earth_Planet_BaseColor.png`, Nachtkarte
`Earth_Planet_Night.png`, jeweils equirektangular 4096×2048, RGB8. „planet“ wählt den
App-Planetshader (feste Sonne, Tag/Nacht, Nachtlichter, blauer Atmosphärenrand); das große P in
„Planet“ erhält zugleich den 4096er-Texturimport. NASA Blue Marble plus NASA-Wolkenkarte bilden
die unbeleuchtete Tagesbasis; NASA Black Marble liefert die Nachtlichter. **Kein Licht auf
Erde backen**, keine Terminatorlinie in den Texturen. Rohquellen, Verarbeitung und Prüfsummen
stehen in `art/space-sources.json` und `docs/CREDITS.md`.

Die Erde hat **1.350 m Radius**, Zentrum (280; 1000; −1080), etwa 148 m Abstand der
Standardplätze zur Oberfläche. Dies konkretisiert „≈1.500 m“ unter der strengeren
Sichtweitenbedingung: Eine vollständige 1.500-m-Kugel von einem Standpunkt außerhalb der Kugel
könnte nicht vollständig innerhalb von 2.900 m liegen. Die kleinere Kugel behält den Blick
wie aus niedrigem Orbit; alle Erdvertices bleiben einschließlich aller Drehphasen unter
2.900 m von jedem Sitz. Der Sternendom hat 2.500 m Radius um (0; 0; 1,25), nach innen
weisende Flächen und Material **Space_Panorama_unlit**. Originales prozedurales 4096×2048-
Equirektangular-Panorama mit scharfen 1–2-Pixel-Sternkernen, ohne Weichzeichner und ohne Sonnenscheibe.

**Neue Konvention: Sun.** Das Empty **Sun** bei (−900; 160; −80) beschreibt mit seinem
Positionsvektor vom Raumursprung die Richtung zur Sonne. Es ist keine exportierte Lampe.
Die Tag-Nacht-Grenze soll im Fenster sichtbar sein.

**Neue Konvention: _rotate<seconds>.** Empty **Earth_rotate5520** am Erdzentrum, die Erdkugel
als Kind. Die App dreht um die lokale Hochachse (Blender Z / Unity Y), 5.520 Sekunden je
Umdrehung, entsprechend 92 Minuten. Die geneigte Achse (Blender XYZ: −10,465° / 16,7312° / 101,263°)
vermittelt einen Überflug. Translation und Parent-Hierarchie müssen im FBX erhalten bleiben;
die Stars sind kein Kind des Rotators.

**Budget und Pipeline:** ≤120.000 Dreiecke, ≤14 Materialien, statische Raumflächen nach
Material zusammengefasst; Erde und Sternendom bleiben eigene Meshes. Basisfarben ≤2048²,
Lichtatlas `Space_Light.png` 2048²; ausschließlich Erde und Panorama erhalten 4096×2048.
Alle Meshes haben UVMap und globale, überlappungsfreie Lightmap-UVs mit ≥4 px Inselabstand.
Erde und Sterne belegen reservierte UV2-Streifen, verwenden aber keinen Lichtatlas.
Cycles Diffuse Direct+Indirect ohne Color, 256 Samples, Denoising, 8 px Bake-Margin,
Standard/None, −1 EV, RGB8. Die diffusen Innenraumflächen der Vorschauen verwenden Basisfarbe × Atlas ×2;
Glow-Flächen zeigen ihre Eigenemission. Beide werden bei Filmlicht auf ×0,04 gedimmt.
Erde/Sterne bleiben ungedimmt. Der Vorschau-Planetshader mischt
Tag-/Nachtkarte nach Sun, zeigt Stadtlichter und einen blauen Rand. Er dient zur Beurteilung;
die endgültige Darstellung und Bewegung erfolgen durch die App-Shader.

Build: `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/space.py -- Assets/TheMovies/Art/Space`.
Prüfung: gleicher Blender-Aufruf mit `-P tools/blender/verify_space.py`.
Lieferung: `Assets/TheMovies/Art/Space/Space.fbx`, `Textures/`, reproduzierbare `space.py` /
`lib_space_*.py`, `verify_space.py`. Checkpoints nur in `art/blender/`, Quellen nur in
`art/sources/`; beide ignoriert. Vorschauen `Logs/art-preview/space-*.png`, Prüfprotokoll
und `SPACE-REPORT.md` ebenfalls ignoriert. Keine Änderungen an den drei akzeptierten Räumen,
App-Code oder Unity-Konfiguration. Commit auf `art/movie-palace`, kein Push.

Aktueller Weltraum-Export: **102.189 Dreiecke, zwölf Materialien/Material-Meshes, acht Plätze**.
Die Atlas-Packung trennt 23 hohle UV-Inseln in kompakte Sektoren. Reimport: 5.304 freie
Leinwandstrahlen; garantierter Kopf-Freiraum ≥14,82 cm; Erde höchstens 2.850,34 m und Sterne
2.504,14 m von den Sitzaugen entfernt. Die Rotationsgrenze gilt für sämtliche Drehphasen.

## 12. Orient-Express – fünfter Raum

**Gestaltung:** Erfundenes Art-déco-Salonabteil „Aster Vale / AV“, ohne reale Bahnlogos,
Hoheitszeichen oder Siegel. Innenbreite 2,90 m, Länge 16,10 m (Y −9,25 bis +6,85),
flach gewölbte Elfenbeindecke bis Z 2,56 m. Walnussvertäfelung mit originalen floralen
Intarsien, petrolfarbene Samtpolster, champagnerfarbene geraffte Vorhänge mit Pelmets,
Messing-Gepäckablagen, gemusterter Teppich, opake geätzte Leuchtglasfelder,
Deckenleuchten und kleine Tische mit gefransten Seidenschirmen. Türen an beiden Enden.
Die vordere Tür liegt hinter dem Projektionsportal und ist bei eingefahrener App-Leinwand sichtbar.

**Sitze und Leinwand:** Seat1–4 unverändert X −1,125 / −0,375 / +0,375 / +1,125,
Y=Z=0, deckungsgleich mit Spot_000–003; vier schmale einzelne Sessel nach +Y.
Zwei Bankplätze X ±0,38, Y −3,10; zwei Essplätze X ±0,83, Y −6,20,
Spot_004–007; zwei zusätzliche Sessel X ±0,83, Y +2,65, Spot_008–009, alle Z=0. Niedrige gepolsterte Lehnen erhalten die Sicht aus 1,25 m Augenhöhe.
Alle Marker müssen die vollständige kontinuierliche Kopfhülle aus Abschnitt 2 bestehen,
einschließlich freier Kopfprojektion über dem Polster. ScreenAnchor=(0; 6,65; 0,90),
ScreenTop=(0; 6,65; 2,40), freier Bildbereich 2,60 × 1,50 m an der Stirnwand.
Keine Leinwandfläche im FBX. Die App begrenzt die Bildhöhe anhand ScreenTop;
breitere/extreme Seitenverhältnisse müssen die App-seitige Wagenbreite respektieren.

**Vorbeiziehende Landschaft – 70 km/h:** Der Nutzer hat den Innenraum auf Quest 3
abgenommen; dessen Look, Geometrie, Normalen, beide UV-Sätze, Marker und sämtliche
Innenraumtexturen einschließlich Lichtatlas bleiben erhalten. Nur unbeleuchtete
Kulissen ändern sich; kein neuer Bake.

Drei opake, horizontal nahtlose RGB-Panoramen mit geraden horizontalen Bandkanten:
- **Nah:** Gleisbett/Gras/Zaun, dargestellt in 4,5 m Entfernung (über die Sitzbreite
  effektiv 3,49–5,51 m), Träger X ±5 m, Textur 4096×1024. Oberkante Z=0,65;
  darunter Bodenschürze. Wenige breite, kontrastarme Schwellen/Zaunpfosten;
  periodischer horizontaler Tiefpass statt flimmernder Körnung.
- **Mitte:** Felder, Hecken, Bäume, kleine Höfe mit warmen Fenstern, dargestellt in
  250 m Entfernung, Träger X ±18 m, Z −6 bis +1, Textur 4096×1024. Baum-, Dach-
  und Heckenkonturen sind vollständig Teil der scrollenden RGB-Textur, unterhalb
  einer ruhigen geraden Feldkante. Keine unbewegliche geometrische Baumsilhouette,
  keine Alpha-Karte und keine mitbewegte Himmelsattrappe.
- **Fern:** dieselbe CC0-Fotografie „Clarens Night 02“, Berge/Himmel dargestellt in
  10 km Entfernung, Träger X ±60 m. Die vorbereiteten RGB-Pixel bleiben unverändert,
  4096×2048. Langsamer UV-Scroll; horizontale Wiederholung um 8,02 % vergrößert,
  damit `scroll6` das Zieltempo ohne gröbere Integer-Rundung abbildet.

**Rechnung:** v=70/3,6=19,444444 m/s; bei seitlichem Blick Ziel-Winkelgeschwindigkeit
ω=v/d. Für eine ebene Trägerfläche X=±g mit U=Y/L gilt lokal in Fenstermitte
Θ=dφ/dU=L/g (rad pro Texturbreite). Damit exakt u/s=(v/d)/Θ und N=round(10000·u/s).
Θ ist die **lokale projizierte Winkelbreite**, nicht der endliche Winkel zwischen
zwei weit entfernten Textur-Endpunkten. Der Endpunktwinkel 2·atan(L/(2g)) ist beim
Nahband nur 2,3520 rad; ihn als lokalen Umrechnungsfaktor zu verwenden würde dort
die Bewegung um Faktor 2,04 überhöhen. Affine UVs erhalten die korrekte Bewegung
auch bei den hinteren Plätzen; beim Schrägblick sinkt ω physikalisch mit cos²(φ).

| Ebene | dargestelltes d | Träger g | Wiederholung L | Θ lokal rad/U | u/s exportiert | Material | ω rad/s |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| Nah | 4,5 m | 5 m | 24 m | 4,8 | 0,9002 | OE_Near_Panorama_unlit_scroll9002 | 4,320960 |
| Mitte | 250 m | 18 m | 80 m | 4,444444 | 0,0175 | OE_Middle_Panorama_unlit_scroll175 | 0,077778 |
| Fern | 10.000 m | 60 m | 194,444444 m | 3,240741 | 0,0006 | OE_Far_Panorama_unlit_scroll6 | 0,001944 |

Ideal Nah=0,900205761 U/s, Rundungsfehler −0,00064 %. Mitte und Fern sind exakt
auf v kalibriert. Für Fern ergibt sich L=(v/d)·g/0,0006; deshalb ist kein feineres
`scroll<N>`-Format nötig. Auf beiden Seiten wächst U nach +Y; der positive
Shader-Abtastoffset bewegt Bildmerkmale nach −Y zum Heck.

Nahtextur: höchstens 18 räumliche Zyklen pro Texturbreite nach weichem Tiefpass,
entsprechend 16,2036 Hz bei voller Fahrt; Mittentextur höchstens 400 Zyklen,
entsprechend 7 Hz. Beides unter der Nyquist-Grenze von 36 Hz bei 72 fps und 45 Hz
bei 90 fps. RGB8-Restenergie, periodische Übergänge und vier sekundenlange
Fenster-Clips beider Seiten bei 72/90 fps werden mit dem tatsächlichen FBX geprüft.
Bewegungsbericht: `Logs/art-preview/ORIENT-EXPRESS-MOTION-REPORT.md`; vier MP4-Clips
`orient-express-motion-{left,right}-{72,90}fps.mp4` daneben.
Die Clips verwenden weder Bewegungsunschärfe noch zeitliches Blending, um Fehler
nicht zu verdecken. Sie ersetzen keinen Quest-Test von Laufzeit, Komfort und Last.

**Pipeline und Lieferung:** `Assets/TheMovies/Art/OrientExpress/OrientExpress.fbx`
(Art-Unterordner `Art/OrientExpress`), Textures daneben; App-Prefabname
`CustomOrientExpress` (Erzeugung durch die App-Seite, kein Unity-Aufruf hier).
≤120.000 Dreiecke, ≤14 Materialien; Zusammenfassung nach Material.
Basisfarben 1024², ausschließlich Panorama-Dateien größer; globaler Lichtatlas
`OrientExpress_Light.png` 2048², zwei UV-Sätze, global keine Überschneidungen,
≥4 px Abstand. Cycles Diffuse Direct+Indirect ohne Color, 256 Samples,
Denoising, 8 px Bake-Margin, Standard/None/−1 EV/RGB8. Warme Innenbeleuchtung
und kühles Fensterlicht im Bake; alle drei Kulissenbänder unlit und ungedimmt.
Vorschauen Basisfarbe × Atlas ×2, dimmbar auf 4 %, Glow ebenfalls dimmbar.

Build: `Blender -b --factory-startup -P tools/blender/orient_express.py -- Assets/TheMovies/Art/OrientExpress`.
Prüfung: `Blender -b --factory-startup -P tools/blender/verify_orient_express.py`.
`--scenery-only` ersetzt nur die Kulissen im vorhandenen Bake-Checkpoint und exportiert neu;
`--no-previews` überspringt dabei die Standbilder. `verify_orient_express_motion.py`
erzeugt und prüft die 72/90-fps-Clips unter Logs/art-preview.
`--geometry-only` erstellt einen lokalen Entwurf; Prüfer `-- --draft` testet dessen Sichtlinien.
`--preview-only` wiederholt die gebackenen Vorschauen aus dem ignorierten Checkpoint.
Vorschauen und Bericht: `Logs/art-preview/orient-express-*.png`,
`Logs/art-preview/ORIENT-EXPRESS-REPORT.md`; Logs nicht versioniert.
Walnussmaserung und Samtflor aus den CC0-Diffusfotos dark_wood / velour_velvet
von Poly Haven, auf 1024² reduziert und eingefärbt. Rohdaten ausschließlich unter
ignoriertem `art/sources/`, Quellenmanifest `art/orient-express-sources.json`, Credits in `docs/CREDITS.md`.
Die vier akzeptierten Räume, App-Code, ProjectSettings und Packages bleiben unverändert.
Commit auf dem aktuellen Branch, kein Push. Innenraum auf Quest 3 abgenommen; erneuter Quest-Test des geänderten Fahrtempos offen.

Aktueller Orient-Express-Export: **55.340 Dreiecke, dreizehn Materialien/Material-Meshes, zehn Plätze**.
FBX-Reimport: 6.630 freie Leinwandstrahlen; garantierte Luft um die kontinuierlich geprüfte
Kopfhülle mindestens 17,70 cm. Globale UV-Prüfung ohne Überschneidungen, ≥4 px Inselabstand.
## 13. Präsidentenjet – sechster Raum

**Gestaltung:** Eigenständige Präsidentenjet-Konferenz-/Lounge-Kabine, inspiriert vom
Hauptdeck eines luxuriös ausgebauten Großraumflugzeugs. Keine offiziellen Siegel,
Wappen, realen Staatsflaggen, Betreiberlogos oder „Air Force One“-Beschriftung.
Walnussfarbenes Holz und buchgespiegeltes Furnier, cremefarbene Ledersessel, marineblaue Wandsofas, Messing,
fein gemusterter Teppich, warme Lichtvouten, Leselampen und Wandleuchten. Hinten
Konferenztisch, Hospitality-Sideboards und ein selbst entworfenes Kompassornament.

**Maße:** 6 m Innenbreite, 12 m Länge (Y −6 bis +6), Boden Z=0. Gerundete
Schultern/Stauraumvouten; Deckenmitte normalerweise Z=2,45 m. Ab Y=2,8 steigt die
Mitte sanft auf Z=2,80 m an der vorderen Schottwand: Diese lokale Erhöhung löst die
Maßkollision zwischen etwa 2,4 m Kabinenhöhe und 0,90+1,80=2,70 m ScreenTop.
Schottwände an beiden Enden, 28 echte unverschlossene Fensteröffnungen mit
0,28 × 0,40 m Außenöffnung und etwa 0,20 m tiefer, gerundeter Laibung. Kein Glas.

**Sitze:** Seat1–4 unverändert bei X −1,125 / −0,375 / +0,375 / +1,125,
Y=Z=0; identisch mit Spot_000–003. Vier einzelne Leder-Drehsessel nach +Y.
Spot_004–007: zwei Sofaplätze je Seite, X=±2,35, Y=1,70 / 2,85, Z=0,
nach innen orientiert. Spot_008–011: Konferenzsessel, X=±1,72,
Y=−2,70 / −4,00, Z=0, zum Tisch orientiert. Jeder Marker steht unter dem
sitzenden Kopf; die komplette 20-cm-Kopfhülle gemäß Abschnitt 2 bleibt frei und
mit ihrer Bodenprojektion über dem Polster. Alle Plätze haben aus 1,25 m
Augenhöhe freie Sicht auf das volle Leinwandraster.

**Leinwand:** ScreenAnchor=(0; 5,48; 0,90), ScreenTop=(0; 5,48; 2,70).
Freie Projektionsfläche 3,40 × 1,80 m; Messingrahmen und dunkel zurückgesetzte
Schottwand, keine modellierte Leinwandfläche. Die App begrenzt die Höhe über
ScreenTop; die bauliche Öffnung ist auf diese 3,40-m-Projektion ausgelegt.

**Vorbeiziehende Kulisse:** Originaler in Blender gerenderter Multiple-Scattering-Abendhimmel,
`Jet_Sky_Panorama_unlit_scroll2`, auf opaken seitlichen Bändern in 900 m
Entfernung. Die separat in Cycles als Volumen gerenderte, opake Wolkendecke liegt
45 m unter dem Kabinenboden: `Jet_Clouds_Panorama_unlit_scroll390`. Keine Alpha-
Karten. UV-U steigt auf **beiden** Seiten mit Welt-Y: Ein positiver U-Texturversatz
verschiebt Motive nach −Y, also zum Flugzeugheck. Beide Texturen sind horizontal
nahtlos einschließlich des Nahtgradienten; die unterschiedlichen Geschwindigkeiten
und geometrischen Entfernungen liefern Parallaxe. Die App übernimmt die Animation.

**Bewegung nach Quest-3-Abnahme des Looks (16.10.2026):** Wolken nach erneutem Quest-Feedback auf etwa 1,3°/s reduziert,
Himmel unverändert. Keine zusätzliche Wolkenebene; die akzeptierte Gestaltung,
alle Textur-Pixel und der Lichtatlas bleiben unverändert. Re-Export ohne neuen Bake.

`scroll<N>` bedeutet U-Versatz `N/10000` pro Sekunde. Die Wolkenfläche spannt
4.600 m in Y mit neun U-Wiederholungen: eine Texturbreite L=511,111 m.
Für den Vektor r vom Sitzauge zum sichtbaren Wolkenpunkt und die Bewegungsrichtung
−Y gilt die lokale Winkelbreite pro U-Einheit
`A = (180/pi) * |r × (0, −L, 0)| / |r|²` in Grad. Damit ist
`omega = A * N/10000` in Grad/s und `N = 10000 * omega/A`.
Gemessen wird an den tatsächlichen FBX-Flächen durch beide Fensterreihen, Augenhöhe
1,25 m, drei horizontale Proben pro Fenster und 40 Höhen zwischen 1,06 und 1,245 m;
nur Strahlen, die zuerst Wolken treffen, gehen ein. Die Tabelle zeigt Mediane.
Die endliche Winkelspanne zwischen r±(0,L/2,0) ist zusätzlich aufgeführt; für die
momentane Geschwindigkeit ist die lokale Winkelbreite maßgebend.

| Sitz | Lokale Winkelbreite A | Endliche Texturspanne | Wolken bei scroll390 |
| --- | ---: | ---: | ---: |
| Seat1 / Seat4 | 36,233°/U | 36,030° | 1,413°/s |
| Seat2 / Seat3 | 30,601°/U | 30,210° | 1,193°/s |

Für ungefähr 1,3°/s ergeben sich N≈359–425; **scroll390** liegt dazwischen:
0,039 U/s, eine Wiederholung in 25,641 s, Motive bewegen sich mit 19,933 m/s nach
−Y. Der Prüfbereich für die Mediane von Seat1–4 beträgt **1,17–1,43°/s**.
Die vorige Einstellung scroll600 (1,84–2,17°/s) wurde auf Quest 3 weiterhin als zu
schnell beurteilt. **390 = 600 × 0,65**: scroll390 ist exakt **35 % langsamer**.
Perspektivisch variieren einzelne Blickrichtungen: horizontnahe/schräge Strahlen
sind langsamer, die näheren Sofaplätze erreichen im Median 2,33–2,82°/s,
die Konferenzplätze 1,70–1,71°/s. Eine gemeinsam verschobene Fläche kann nicht
an jedem Sitz und in jeder Blickrichtung dieselbe Winkelgeschwindigkeit haben.

Der Himmel spannt 4.600 m mit drei U-Wiederholungen, also L=1.533,333 m.
**scroll2** bleibt bei 0,0002 U/s beziehungsweise 0,3067 m/s. Selbst am nächsten
Sofaplatz beträgt die lokale Winkelbreite höchstens
`(180/pi)*1533,333/(900−2,35) = 97,871°/U`, die Bewegung also höchstens
**0,0196°/s**; schräg betrachtete Bereiche bewegen sich noch langsamer.

Bewegungsfreundliche, weich gerenderte Wolkendetails unverändert: bei 72/90 Hz
1,109/0,887 Quelltexel Versatz pro Bild. Nur 0,008 % / 0,000 % der horizontalen
Kontrastenergie liegen oberhalb der zeitlichen Nyquist-Grenze, bereits vor
Mipmapping. Der Prüfer kontrolliert diese Werte sowie identische Texturkanten
und Nahtgradienten. Das ist ein technischer Indikator gegen Flimmern; die erneute
Quest-Abnahme der reduzierten Bewegung bleibt ein separater Sichttest.
Messdaten aller zwölf Sitze: `Logs/art-preview/jet-motion-verification.json`.

**Pipeline/Budget:** ≤120.000 Dreiecke, ≤14 Materialien/Material-Meshes.
1024² Basisfarben, `Jet_Light.png` 2048²; Himmel 4096×2048,
Wolken-Panorama 2048×1024. RGB8, keine Normal Maps oder Transparenz.
UVMap plus globale, überschneidungsfreie Lightmap mit ≥4 px Inselabstand.
Cycles Diffuse Direct+Indirect ohne Color, 256 Samples, Denoise, 8 px Bake-Rand,
Standard/None/−1 EV. Warme Kabinenbeleuchtung und Sonnenlicht durch die Fenster
werden gebacken. Vorschauen zeigen Basisfarbe × gespeicherten Atlas ×2;
Filmlicht 4 %, Glow ebenfalls dimmbar, Unlit-Kulissen unverändert.

Build: `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/jet.py -- Assets/TheMovies/Art/Jet`.
Prüfung: gleicher Aufruf mit `-P tools/blender/verify_jet.py`.
`--geometry-only` liefert einen ignorierten Geometrie-Checkpoint;
`--preview-only` rendert aus dem Bake-Checkpoint. `--motion-only` aktualisiert
nur Wolken-Namen/-Texturpfade im vorhandenen FBX mit Blenders FBX-Encoder; sämtliche
numerischen FBX-Eigenschaften sowie Geometrie/Normalen/UVs/Marker nach Reimport
werden exakt verglichen, Texturdateien per SHA-256. `--motion-preview` rendert
72 Bilder bei 24 Hz aus Seat2 zum linken Fenster unter `Logs/art-preview/jet-motion-frames/`. Scenery-PNGs werden für erneute
Bakes wiederverwendet; zum vollständigen Neurendern nur die beiden Jet-Panorama-PNGs löschen.
Lieferung: `Assets/TheMovies/Art/Jet/Jet.fbx` mit `Textures/`, für das von der
App-Seite erzeugte Prefab `CustomJet`. Checkpoints unter ignoriertem `art/blender/`;
Vorschauen `Logs/art-preview/jet-*.png`, Prüfprotokolle und `JET-REPORT.md` ignoriert.
Geometrie und Kulissen sind Originalarbeit. Einzige Fremdquelle: CC0 `dark_wood`
von Poly Haven, auf 1024² verkleinert und warm getönt; Rohquelle nur unter
`art/sources/`, Prüfsumme in `art/jet-sources.json`, Herkunft in `docs/CREDITS.md`. Keine Änderungen an den vier akzeptierten Räumen, App-Code,
Unity-Konfiguration oder Packages. Commit auf dem aktuellen Branch, kein Push.

Aktueller Jet-Export: **45.886 Dreiecke, 13 Materialien/Material-Meshes, zwölf Plätze**.
FBX-Reimport: 7.956 freie Leinwandstrahlen, kontinuierlich garantierter Kopf-Freiraum
mindestens 17,79 cm. Himmel und Wolken sind von jedem Platz sichtbar; alle Kulissen-
Vertices bleiben höchstens 2.932,27 m von den Sitzaugen entfernt. Horizontale
Textur- und Gradientennähte sind identisch, beide Seiten bewegen sich zum Heck.
Der Nutzer hat den Look auf Quest 3 abgenommen („bombastisch“). Die reduzierte
Scrollwirkung muss erneut im Headset beurteilt werden; Unity wurde für diese
Art-Lieferung nicht gestartet.

## 14. Dezente Bewegung in den sechs abgenommenen Räumen

| Raum | Neue Effekte | FireLight | Materialien / Budget |
| --- | --- | --- | --- |
| Kinopalast | `Bulb_emit_flicker2`, nur Kronleuchter-/Wandleuchtenbirnen | 0 | 11 / 16 |
| Heimkino | Glut und opake Flammen `flicker30`, Simskerzen `flicker15`, Nachthimmel `twinkle12` | 350, 65 und 70 cm | 12 / 12 |
| Dachterrasse | Lichterkette `flicker4`, Laternenkerzen `flicker20`, Panorama und Stadtfenster `twinkle10` | 140, 145, 150 und 125 cm an vier lounge-nahen Laternen | 14 / 14 |
| Weltraum | Drei kleine rot/grün/weiße Flächen auf bestehenden Stationsrippen: `unlit_blink16` (1,6 s) | 0 | 13 / 14 |
| Orient-Express | Nur Tischlampenschirme `OE_Silk_glow_flicker2` | 0 | 13 / 14 |
| Präsidentenjet | Kein zusätzlicher Effekt: kein sichtbarer modellierter Flügel | 0 | 13 / 14 |

Weltraumsterne bleiben ruhig; Erdrotation und Instrumente bleiben wie abgenommen.
Bestehende Landschafts-/Wolkenbewegung bleibt erhalten. Alle neuen Flächen sind opak.
Nur Home erhält einen neuen 2048²-Bake mit 1024 Samples, Diffuse Direct+Indirect,
Denoising und dem bisherigen Lampenaufbau; die anderen fünf Lichtatlanten bleiben
byte-identisch. Cinema/OrientExpress erhalten ausschließlich FBX-Namensänderungen.
Rooftop erhält getrennte Materialzuweisungen und einen Laub-Farbatlas; Space kleine
Materialflächen auf bestehenden Rippen, ohne neue Silhouette oder Lichtverdeckung.
Die übrige Raum- und Sitzgeometrie bleibt erhalten. Jet-FBX und sämtliche Jet-PNGs
bleiben byte-identisch. Vorhandene Textur-GUIDs werden beim Umbenennen übernommen.

Reproduzierbarer Effekt-Pass mit Blender 5.2, ohne Unity:

```sh
# Der akzeptierte Home-Export dient als exakte Oberflächen-/UV0-Vorlage.
git show cabcc7044ac8f1048e1a1e29794074e58bfd718e:Assets/TheMovies/Art/Home/Home.fbx > /tmp/Home-accepted.fbx
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P tools/blender/home.py -- Assets/TheMovies/Art/Home --accepted-fbx /tmp/Home-accepted.fbx --no-previews
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P tools/blender/room_motion.py -- Cinema Rooftop Space OrientExpress
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python-exit-code 1 -P tools/blender/preview_room_motion.py
```

Home-Vorlage SHA-256: `9468e58b4d2317a95983b7d66527a095821bb9b2d7e7ecc1282a32aded203802`.
Die sechs `verify_<room>.py` prüfen zusätzlich die Effektgrammatik, Prozentwerte,
Emissionsvoraussetzungen, Materialbudgets, opake Materialien und höchstens vier
FireLight-Empties innerhalb des begehbaren Raumbereichs. Space prüft zusätzlich,
dass alle drei Positionslichtfarben aus Seat2 sichtbar sind. Die bestehenden
UV-, Normalen-, Sichtlinien-, Textur- und Sitzprüfungen bleiben aktiv.
`verify_orient_express_motion.py` prüft weiterhin die Landschaft bei 72 und 90 fps.

`verify_room_motion_preservation.py` vergleicht mit den vor dem Effekt-Pass unter
`Logs/art-preview/motion-baseline/<Room>/` gesicherten FBX-/Texturdateien. Es prüft
vorhandene Oberflächen, Marker, die unveränderten Lichtatlanten und den vollständigen
Jet-Export. Neue Home-Oberflächen dürfen ausschließlich an Kamin und Simskerzen
liegen. Der Vergleich dokumentiert auch bereits im Ausgangsstand vorhandene
Kopffreiheitsabweichungen in Cinema und Home; diese akzeptierten Sitze werden hier
nicht geändert. Das ist keine neue Freigabe ihrer Kopffreiheit.

Detailbilder: `Logs/art-preview/home-fireplace-motion-closeup.png` und
`Logs/art-preview/rooftop-lanterns-motion-closeup.png`. Die Standbilder verwenden
exportierte Basisfarben und Lichtatlanten mit einer statischen Emissionsvorschau;
sie belegen keine zeitliche Wirkung auf Quest. Laufzeit/Headset wurden in diesem
Art-Pass nicht gestartet. Sämtliche Prüfberichte und Vorschauen bleiben unter
ignoriertem `Logs/`, lokale Blender-Dateien unter ignoriertem `art/blender/`.

## 15. Fotorealistische Räume – Pipeline v2 (zuerst Orient-Express)

Ziel: Räume, die auf der Quest 3 wie echt wirken statt „gezeichnet“. Der alte Weg (nur Basisfarbe × gebackenes
diffuses Licht, LDR, keine Normal Maps) bleibt für die abgenommenen Räume unverändert. Ein Raum nutzt Pipeline v2,
sobald seine RNM-Lichtatlanten (unten) vorliegen. Die App rendert v2-Räume mit einem eigenen physikalisch
basierten Shader:

- **Diffus:** Basisfarbe × gerichtetes gebackenes Licht (Radiosity Normal Mapping, drei Richtungen) – Normal Maps
  wirken damit auch im gebackenen Licht (Maserung, Polsterknöpfe, Teppichflor, Prägungen).
- **Glanz:** GGX-Spiegelung aus einer in Blender gerenderten, box-projizierten Umgebungskarte des Raums
  (Rauheit, Metall, Fresnel; Glanzverdeckung aus AO und gebackenem Licht). Dazu ein Glanzschein der Filmleinwand.
- **HDR:** Licht als HDR (RGBM) statt auf 0–2 begrenzt; am Ende die Tonwertkurve **Khronos PBR Neutral** mit
  Belichtung 1,0 – identisch mit Blenders View Transform „Khronos PBR Neutral“ (Look „None“, Exposure 0).
  Blender-Vorschauen mit genau dieser Einstellung entsprechen damit der Darstellung in der App.
- Saallicht dimmt gebackenes Licht und Spiegelung gemeinsam (wie bisher auf ~4 %).

### Material-Texturen (UV-Satz 0 „UVMap“, Kachelung wie bisher)

| Datei | Inhalt | Format |
| --- | --- | --- |
| `Textures/<Material>_BaseColor.png` | Albedo, bei Metall die Reflexionsfarbe | sRGB, 8 Bit |
| `Textures/<Material>_Normal.png` | Tangentenraum-Normal Map, **OpenGL-Konvention (+Y = V-Richtung)**, Tangenten aus „UVMap“ (MikkTSpace) | linear, 8 Bit RGB |
| `Textures/<Material>_ORM.png` | R = Ambient Occlusion (Materialdetail), G = Rauheit, B = Metall | linear, 8 Bit RGB |

Fehlen Normal/ORM, nimmt die App eine flache Normale und Rauheit/Metall aus Stichworten (`brass`/`gold`/`metal`:
Metall, Rauheit 0,3; sonst Rauheit 0,7). Größe je ≤ 2048²; höchstens 12 Materialien mit 2048², die übrigen 1024².
Quellen bevorzugt **Poly Haven / ambientCG (CC0)** mit vollständigen PBR-Sätzen (Diffuse, Normal GL, Rough, AO,
Metal), eingefärbt/kombiniert nach Bedarf. Nichts davon wird nachträglich „gemalt“, was physikalisch entstehen soll
(keine aufgemalten Glanzlichter oder Schatten in der Basisfarbe).

### Stichworte, zusätzlich zu Abschnitt 4

| Stichwort | Wirkung in v2 |
| --- | --- |
| `glass` | **Glasscheibe:** additive Spiegelung (Fresnel, Umgebungskarte) über dem, was dahinter liegt – Fenster, Vitrinen, Bilderglas. Eigenes dünnes Mesh, wenige Millimeter vor der Kulisse bzw. dem Inhalt. Basisfarbe = Verschmutzung/Schlieren (schwarz = sauber), ORM.G = Rauheit. |
| `velvet` | Samtglanz wie bisher, zusätzlich zur GGX-Spiegelung (Rauheit hoch halten) |
| `emit`/`glow`/`bulb`, `flicker`… | wie bisher; Leuchtstärke in HDR (Lampenschirm hell, aber nicht weiß ausgebrannt) |
| `unlit`, `scroll`… | wie bisher (Kulissen sind schon fertige Bilder, ohne Tonwertkurve) |

### Gebackenes Licht (UV-Satz 1 „Lightmap“)

- Drei Atlanten `Textures/<Modell>_RNM0.png`, `_RNM1.png`, `_RNM2.png`, je **4096²**, Inseln ohne Überschneidung,
  ≥ 4 px Abstand, Texeldichte auf Sitzhöhe ≥ 200 px/m (sichtbare Nahflächen mehr).
- Je ein Cycles-Bake **Diffuse, Direct + Indirect, ohne Color**, ≥ 1024 Samples, Denoising, Margin 8 px,
  wobei die Schattierungsnormale **aller** Materialien durch eine konstante Tangentenraum-Richtung ersetzt wird
  (Normal-Map-Knoten auf „UVMap“, Farbe = b·0,5+0,5; die eigenen Normal Maps sind beim Bake aus):
  - b0 = ( √(2/3), 0, 1/√3 )  b1 = ( −1/√6, 1/√2, 1/√3 )  b2 = ( −1/√6, −1/√2, 1/√3 )
  - (x = Tangente/U, y = Bitangente/V, z = Normale). Die App gewichtet w_i = max(0, n·b_i)², normiert auf Σ = 1.
- **Normierung auf den flachen Bake:** zusätzlich ein vierter Bake mit konstanter Normale (0, 0, 1). Die drei
  Richtungsatlanten werden pro Texel so skaliert, dass ihr Mittel (= flache Normale in der App) diesem Bake entspricht
  (Faktor über den hellsten Kanal, begrenzt auf 0,25–4). Ohne das wären direkt beleuchtete Flächen um bis zu 42 %
  zu dunkel (Licht senkrecht von oben trifft jede Basis unter 54,7°). Nur die drei RNM-Dateien werden exportiert.
- **Metalle als Nichtmetalle backen** (Metallic beim Bake 0): sonst ist ihr Lichtatlas schwarz – die App braucht das
  Licht dort für die Glanzverdeckung.
- **Flächen zeigen in den Raum:** gebacken wird die Seite, in die die Normale zeigt; Wände mit verkehrter Normale
  bleiben im Atlas dunkel, auch wenn Cycles-Vorschauen (die beide Seiten rendern) sie hell zeigen. Das Prüfskript
  vergleicht deshalb Stichproben des dekodierten Atlas mit Erwartungswerten.
- **Kodierung RGBM, Bereich 8:** lineares HDR-Licht L → M = max(L)/8, auf 1/255 aufgerundet, mindestens 1/255;
  RGB = (L / (M·8)) **sRGB-kodiert**, A = M (linear). PNG RGBA 8 Bit. Die App liest RGB als sRGB-Textur
  (Hardware linearisiert) und rechnet L = RGB · A · 8.
- Die Bake-Werte entsprechen direkt der Beleuchtungsstärke-Skala von Blender (keine Exposure-Verschiebung wie bei
  v1); die Belichtung wird so gewählt, dass die Vorschau mit Khronos PBR Neutral bei Exposure 0 stimmt.
- Meshes vor UV-Abwicklung, Bake und Export **triangulieren** (gleiche Tangenten in Blender und Unity).

- **Texeldichte pro Material** festlegen, nicht pauschal: große, nahe Flächen (Decke, Teppich, Vertäfelung) und
  Polster ≥ 150 px/m, Glas fast nichts (wird nicht gebacken). Zu grobe Texel sind blockig und rauschen sichtbar.
- **Leuchtstärken:** `Textures/<Modell>_Materials.json` – `{ "<Material>": { "emission": 2.0 } }` mit der Blender-
  Emission Strength jedes leuchtenden Materials; die App übernimmt sie 1:1.
- Material-AO aus Fotosätzen nur schwach (≈ 35 %) in ORM.R übernehmen – Cycles backt die echte Verdeckung schon ein.

### Umgebungskarte für Spiegelungen

- Empty `ReflectionProbe` an einer Stelle mit gutem Überblick (etwa Kopfhöhe in Raummitte). **Seine Skalierung
  (X/Y/Z, in Metern) ist die halbe Größe des achsparallelen Projektionsquaders** um das Empty, der den Raum
  möglichst gut umschließt (bei langen Räumen wie dem Zugwagen: der ganze Wagen). Nicht drehen.
- Panoramakamera mit **Rotation (90°, 0°, 0°)** (Blickmitte = +Y, oben = +Z); die App dreht passend dazu.
- `Textures/<Modell>_Reflection.exr`: Cycles-Panorama (equirektangular 2048 × 1024, 32-Bit- oder Half-Float-EXR,
  linear, ohne Tonwertkurve) aus der Position des Empties, Saallicht an, Kulissen sichtbar wie gebacken.

### Sonstiges

- Budget v2: ≤ **200 000 Dreiecke**, ≤ **20 Materialien**, Statisches pro Material zusammenfassen.
- Kanten fasen (Bevel 2–10 mm, wo man nah sitzt), damit sie Licht fangen; keine messerscharfen 90°-Kanten an Möbeln.
- Ein v2-Raum liefert zusätzlich die alte Ein-Atlas-Datei `<Modell>_Light.png` **nicht** mehr; die alten
  Abschnitte zu Exposure −1 und „keine Normal Maps“ gelten für v2 nicht.
- Prüfskript wie bisher (`verify_<raum>.py`), zusätzlich: Texturdateien und -formate, RNM-Atlanten vorhanden und
  gleich groß, Probe-Empty vorhanden, Dreiecke/Materialien im Budget, alle Meshes trianguliert.
- Vorschauen: Cycles-Renderings aus Seat2 (1,25 m) mit Khronos PBR Neutral, Saallicht an und gedimmt, dazu
  Nahaufnahmen (Tisch mit Lampe, Polster, Messing, Fenster) – zur Abnahme mit „sieht echt aus?“ als Maßstab.
