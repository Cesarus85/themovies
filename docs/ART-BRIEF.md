# Art-Brief: Kinopalast und Heimkino (Blender → Unity → Quest 3)

Auftrag für die Modellierung in **Blender 5.2** (installiert unter `/Applications/Blender.app`). Ergebnis sind zwei
Raum-Modelle, die die App statt ihrer eingebauten Räume lädt. Bitte diesen Brief vollständig lesen; Maße, Namen und
Export-Einstellungen sind verbindlich, weil die App die Modelle automatisch einbaut.

## 1. Was gebaut wird

| Raum | Stil | Datei |
| --- | --- | --- |
| **Kinosaal** | Klassischer **Kinopalast** der 1920er/30er Jahre | `Assets/TheMovies/Art/Cinema/Cinema.fbx` |
| **Heimkino** | Klassisches, gemütliches **Wohnzimmer-Kino** | `Assets/TheMovies/Art/Home/Home.fbx` |

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
- Augenhöhe sitzend: **1,3 m** über diesem Boden.

**Definition der Sitzmarker (Kinosaal und später auch Heimkino-Sofa):** Ein Sitzmarker liegt am Bodenpunkt
senkrecht unter den Augen eines angelehnten, sitzenden Erwachsenen, auf der **Bodenhöhe der jeweiligen Reihe**.
Er liegt horizontal **0,12 m vor der gepolsterten Vorderfläche der Rückenlehne**, gemessen auf der Mittellinie des
Sitzplatzes in **0,85 m Höhe über dem Reihenboden** (Schulterhöhe). Die Fläche ist am tatsächlichen fertigen
Polstermesh zu messen, bei unterschiedlichen Detailstufen jeweils separat. Der Marker liegt damit unter der
sitzenden Kopfposition, nicht vor der Sitzvorderkante; die App addiert die Augenhöhe von 1,3 m.

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
  (2 Reihen davor tiefer, 3 dahinter höher), Sessel ~0,55–0,6 m breit. Vordere Reihen dürfen die Sicht aus 1,3 m
  Augenhöhe auf die Leinwand-Unterkante nicht verdecken.

- Erhöhte Plätze: Von jedem Galerie-/Logen-Spot muss bei **1,3 m Augenhöhe** die volle 19 × 8,2 m große
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
- **Vorschau zur Abnahme**: Cycles-Renderings nach `Logs/art-preview/` – aus Sicht von `Seat2` in 1,3 m Augenhöhe
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
