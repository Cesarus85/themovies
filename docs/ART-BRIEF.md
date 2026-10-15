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

Blender-Einheiten = **Meter**, **Z oben**. Die vier Plätze der Zuschauer:

- stehen **auf dem Boden bei z = 0**, in einer Reihe entlang **X**, **0,75 m** Abstand, mittig um den Ursprung:
  x = −1,125 / −0,375 / +0,375 / +1,125, y = 0;
- blicken nach **+Y**; die Leinwand liegt auf **+Y**;
- Augenhöhe sitzend: **1,2 m** über diesem Boden.

**Marker** (Empties, Namen exakt so, ohne `.001`):

| Name | Lage |
| --- | --- |
| `Seat1` … `Seat4` | auf dem Boden vor dem jeweiligen Sitz (Positionen oben, Seat1 links) |
| `ScreenAnchor` | Unterkante Mitte der Leinwand |
| `ScreenTop` | Oberkante der größtmöglichen Leinwand (gleiches x/y wie der Anker, nur höher) |

Die App richtet das Modell anhand dieser Marker aus. Sie zeichnet die **Leinwand selbst** (leicht gewölbt, eigener
Shader). Bitte **keine Leinwandfläche modellieren**, nur Rahmen/Portal/Vorhänge drumherum und eine dunkle Wand dahinter.

### Kinosaal – Maße

- Leinwand: Unterkante **0,4 m** über dem Boden der Zuschauerreihe, **11,9 m** vor ihr (y = 11,9); bis **19 m breit**
  und **8,2 m hoch** (die App wählt je nach Film und Einstellung). Das Portal muss diese Öffnung frei lassen.
- Raum etwa **22 m breit**, Rückwand **7,2 m** hinter der Reihe, Stirnwand ~**13,6 m** davor, Decke ~**10,5 m** hoch.
  Abweichungen sind ok, wenn es dem Raum gut tut.
- Stadion-Bestuhlung: Reihenabstand **1,65 m**, Stufenhöhe **0,45 m**; die Zuschauerreihe ist eine mittlere Reihe
  (2 Reihen davor tiefer, 3 dahinter höher), Sessel ~0,55–0,6 m breit. Vordere Reihen dürfen die Sicht aus 1,2 m
  Augenhöhe auf die Leinwand-Unterkante nicht verdecken.

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
- **Vorschau zur Abnahme**: Cycles-Renderings nach `Logs/art-preview/` – aus Sicht von `Seat2` in 1,2 m Augenhöhe
  Richtung Leinwand, einmal mit „Saallicht an“, einmal gedimmt (Bake-Licht × 0,12); dazu eine Übersicht von hinten oben.
- **Bericht** am Ende: Dreiecke je Objekt und gesamt, Materialien, Texturen mit Größe, offene Punkte.

## 8. Übernahme in Unity (macht die App-Seite)

Unity-Menü **The Movies → Raum-Modell aufbereiten → Kinosaal / Heimkino** (oder im Batchmode
`-executeMethod TheMovies.EditorTools.RoomModelSetup.PrepareAllBatch`) stellt Import, Texturen und Materialien ein,
prüft Marker und Budget und erzeugt `Resources/TheMovies/CustomCinema.prefab` bzw. `CustomHome.prefab`. Fehlt ein
Prefab, nutzt die App den eingebauten Raum.
