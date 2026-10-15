# Kinopalast – Art-Abnahme

Stand: 2026-10-15, Blender 5.2.0 LTS. Nur Kinosaal; keine App-/Shader-/Projektänderungen, Unity wurde nicht gestartet.

## Ergebnis

Warmer Kinopalast mit drei vergoldeten Portalbögen und Sonnenfächer, gefalteten roten Samtvorhängen,
fünf Swags mit Goldfransen und Quasten, kannelierten Pilastern mit Kapitellen, sechs Seitenlogen,
einer möblierten Rückgalerie, Kassettendecke/Rosette, CC0-Kronleuchter, Wandleuchten und Art-déco-Teppich.
152 gepolsterte Sessel: 120 im Parkett (6 × 20), 20 in der Galerie und 12 in den Logen.
Die Vorhangfalten sind deterministisch parametrisch modelliert; kein simulationsabhängiger Cloth-Cache.

Der Saal ist 24 m breit und 11,1 m hoch (bewusste Erweiterung gegenüber ca. 22 / 10,5 m für Portal und Logen).
Rückwand y = −7,2 m, Stirnwand y = 13,6 m. Parkettreihen: Abstand 1,65 m, Höhen −0,90 / −0,45 / 0 / 0,45 / 0,90 / 1,35 m.
Die Zuschauerreihe bleibt exakt bei z = 0. Seat1…Seat4 liegen bei x = −1,125 / −0,375 / 0,375 / 1,125, y = 0.
ScreenAnchor = (0; 11,9; 0,4), ScreenTop = (0; 11,9; 8,6). Das Portal hält 19 × 8,2 m frei.
Es gibt **keine Leinwandfläche**: Die sichtbare dunkle Fläche ist die zurückgesetzte Stirnwand bei y = 13,6.

## Budget / Export

Ein statisches Mesh pro Material, neun Meshes und sechs Marker-Empties. Keine Kameras oder Lichter im FBX.
Die großen Hüllflächen sind einseitig; Wandrückseiten, Bodenunterseiten und verdeckte Polsterunterseiten wurden ausgelassen.
Die Geometrie ist vor UV-Erzeugung trianguliert. Exportparameter entsprechen unverändert `test_room.py` / Art-Brief.

| Exportobjekt / Material | Dreiecke |
| --- | ---: |
| Cinema_Bulb_emit | 2,952 |
| Cinema_Carpet_Deco | 76 |
| Cinema_Ceiling_Petrol | 2 |
| Cinema_Damask_Oxblood | 6 |
| Cinema_Gold_Ornament | 60,651 |
| Cinema_Ivory_Stucco | 33,704 |
| Cinema_Stage_WarmShadow | 2 |
| Cinema_Velvet_Crimson | 33,444 |
| Cinema_Walnut_Panelling | 48,468 |
| **Gesamt** | **179,305 / 250.000** |

Neun Materialien von maximal 16. Alle Materialien haben genau eine sRGB-Basisfarbtextur; keine Normal Maps.
Kronleuchter: Poly Haven Chandelier 03, 29.301 → 8.789 Dreiecke; bereits im Gold-Mesh enthalten.
Die zusätzlichen Lampen und Leuchtspitzen sind prozedural.

| Textur | Auflösung | Dateigröße |
| --- | --- | ---: |
| Bulb_emit_BaseColor.png | 1024 × 1024 | 1298 KiB |
| Carpet_Deco_BaseColor.png | 1024 × 1024 | 906 KiB |
| Ceiling_Petrol_BaseColor.png | 1024 × 1024 | 900 KiB |
| Cinema_Light.png | 4096 × 4096 | 14984 KiB |
| Damask_Oxblood_BaseColor.png | 1024 × 1024 | 1142 KiB |
| Gold_Ornament_BaseColor.png | 1024 × 1024 | 1231 KiB |
| Ivory_Stucco_BaseColor.png | 1024 × 1024 | 1307 KiB |
| Stage_WarmShadow_BaseColor.png | 1024 × 1024 | 605 KiB |
| Velvet_Crimson_BaseColor.png | 1024 × 1024 | 1315 KiB |
| Walnut_Panelling_BaseColor.png | 1024 × 1024 | 1195 KiB |

## Bake und Prüfung

Ein global gepacktes UV-Set `Lightmap` (Index 1), daneben `UVMap` (Index 0) für Basisfarben.
Pack-Abstand 6 Pixel bei 4096²; Bake-Dilation 8 Pixel. Cycles auf Metal, 256 Samples,
DIFFUSE mit ausschließlich DIRECT + INDIRECT, ohne COLOR. Anschließend explizites OpenImageDenoise
im Compositor auf dem linearen Atlas. PNG RGB 8 Bit mit Standard / None / Exposure −1,0 / Gamma 1,0.
Die Vorschauen laden die **gespeicherte** PNG neu und berechnen Basisfarbe × Lichtatlas × 2 × Dimmfaktor.
Alle Oberflächen verwenden dafür reine Emission: keine erneute Oberflächenbeleuchtung und keine zusätzlichen Glanzlichter.

Der unabhängige Prüflauf importiert die ausgelieferte FBX-Datei in eine frische Blender-Szene:

- Budget, ein Material pro Mesh, beide UV-Kanäle und alle Texturabmessungen: bestanden.
- Exakt sechs Marker, Positionen nach FBX-Rückimport innerhalb 0,1 mm: bestanden.
- 36 Sichtstrahlen (9 Leinwandpunkte einschließlich Ecken je Zuschauerplatz bei 1,2 m Augenhöhe): frei.
- UV-Dreieckstest mit SAT auf positive Schnittfläche: 0 Überlappungen (Toleranz 10⁻⁷ UV).
- Zusätzliche Rasterprüfung auf tatsächlichen 4096²-Texelzentren: 0 überlappende Texel.
- Bei 0.666 % der belegten Atlas-Texel erreicht mindestens ein Kanal 255; kleine lokale Lichtspitzen, keine großflächige Sättigung.
- Letzter Build: ca. 42 s bis FBX/Checkpoint, ca. 67 s einschließlich vier Vorschauen auf Apple M4 / Metal.

Maschinenlesbare Messwerte: `metrics.json`, `verification.json`; vollständige Ausführungsprotokolle: `build.log`, `verification.log`.

## Vorschauen und Iteration

- [Seat2, Saallicht an](Seat2-house-on.png)
- [Seat2, exakt 12 %](Seat2-house-12pct.png)
- [Übersicht von hinten oben](Overview-back-top.png)
- [Zusatzansicht: Galerie und Sessel](Gallery-and-seating.png)

Alle PNGs: 1600 × 1000, Cycles mit 32 Samples für die reine Texturwiedergabe.
Beide Seat2-Bilder: Kamera (−0,375; 0; 1,2), Ziel (0; 11,9; 4,25), 17 mm Brennweite, gleiche Belichtung.
Die 12-%-Ansicht enthält keinen erfundenen Film-/Leinwandschein.
Visuell geprüft und iteriert: Normalen der Ornamente und Swags korrigiert, Rohrprofile ohne Drehung stabilisiert,
Sesselrückseiten gepolstert, Rückgalerie mit Feldern/Ornamenten/Sesseln ergänzt, Lichtspitzen reduziert.

## Herkunft / Reproduktion

Architektur, Stühle, Vorhänge, Teppichmuster und Dekoration: eigener prozeduraler Aufbau.
CC0-Eingaben: Chandelier_03, velour_velvet, quatrefoil_jacquard_fabric und dark_wood von Poly Haven.
Urheber und Änderungen: `docs/CREDITS.md`; feste Download-URLs, Größen und MD5: `art/cinema-sources.json`.
Nur Blender-Geometrie des Kronleuchters und drei 2K-Diffuse-Maps wurden heruntergeladen.
Rohdateien bleiben git-ignoriert unter `art/sources/`; der lokale Checkpoint unter `art/blender/` ist ebenfalls ignoriert.

```sh
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/cinema.py -- Assets/TheMovies/Art/Cinema
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P tools/blender/verify_cinema.py
```

## Offene Abnahme

Unity-Import und physische Quest-3-Abnahme sind noch offen und waren nicht Teil dieses Laufs.
Dabei insbesondere Material-Stichwörter, tatsächliche Filmleinwand/Screen-Spill, Stereo-Eindruck, Texturkompression,
Mipmap-Verhalten der kleinen Ornamente und GPU-Frametime prüfen. Die Vorschauen bilden den statischen
Basisfarbe-mal-Lichtatlas-Anteil ab; zusätzliche laufzeitabhängige Samt-/Metall-Effekte bleiben der App vorbehalten.
Die sechs Marker und das freie Portal sind geprüft; es besteht kein bekannter Export- oder Budgetfehler.
