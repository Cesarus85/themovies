# Credits

Third-party assets used in the app. CC0 assets need no attribution and are listed as a courtesy; CC BY assets must be
listed with title, author, source, license and whether they were modified.

| Asset | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| Cinema: Chandelier 03 | Kirill Sannikov | [Poly Haven](https://polyhaven.com/a/Chandelier_03); [asset metadata](https://api.polyhaven.com/info/Chandelier_03) | CC0-1.0 | Imported original 2K-package Blender geometry; reduced to 30% with Decimate, enlarged and recolored in gold; original maps not used. Procedural candle tips added. |
| Cinema: velour_velvet | colormass (photography), Rico Cilliers (processing) | [Poly Haven](https://polyhaven.com/a/velour_velvet); [asset metadata](https://api.polyhaven.com/info/velour_velvet) | CC0-1.0 | Only 2K diffuse downloaded; luminance weave tinted crimson and reduced to 1024² for seats, curtains and balcony panels; dedicated Velvet_Seat derivative adds a subtle crimson welt/stitch border without changing the hall fabric. |
| Cinema: quatrefoil_jacquard_fabric | colormass (photography), Rico Cilliers (processing) | [Poly Haven](https://polyhaven.com/a/quatrefoil_jacquard_fabric); [asset metadata](https://api.polyhaven.com/info/quatrefoil_jacquard_fabric) | CC0-1.0 | Only 2K diffuse downloaded; tinted oxblood and reduced to 1024² for wall fabric. |
| Cinema: dark_wood | Dario Barresi (baking), Dimitrios Savva (photography), Rico Cilliers (tiling) | [Poly Haven](https://polyhaven.com/a/dark_wood); [asset metadata](https://api.polyhaven.com/info/dark_wood) | CC0-1.0 | Only 2K diffuse downloaded; tinted walnut and reduced to 1024² for paneling, stage and chair frames. |

The cinema architecture, seating, curtains, Art Deco carpet pattern, molding, capitals, balustrades, sconces and
ceiling ornament were created procedurally for this project in `tools/blender/cinema.py`, `lib_cinema.py` and `lib_cinema_chairs.py`.
The revised theatre chairs use original subdivision-shaped cushions, finished velvet backs, capsule-shaped wooden
armrests on shared solid cast-bronze standards, connected hinge/back bearers and richer aisle ends.
The bronze fan/relief base-color atlas is an original procedural Art Deco pattern. Loge chairs now form
two-place rows; the approved upholstery geometry and fabric remain unchanged. No additional third-party asset was introduced.
No third-party normal, roughness, metallic or opacity maps are used. Raw CC0 inputs are cached exclusively in
`art/sources/` (git-ignored). The script retrieves assets through `api.polyhaven.com` and `dl.polyhaven.org` only,
and verifies the downloaded files against the API checksums.

## Home cinema / Heimkino

| Asset | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| Home: Herringbone Parquet | Sergej Majboroda (photography), Jenelle van Heerden (processing) | [Poly Haven](https://polyhaven.com/a/herringbone_parquet); [asset metadata](https://api.polyhaven.com/info/herringbone_parquet) | CC0-1.0 | Only the 2K diffuse downloaded; reduced to 1024² and subtly tinted warm. UV scale retains the source's 3.4 m repeat. |
| Home: dark_wood | Dario Barresi (baking), Dimitrios Savva (photography), Rico Cilliers (tiling) | [Poly Haven](https://polyhaven.com/a/dark_wood); [asset metadata](https://api.polyhaven.com/info/dark_wood) | CC0-1.0 | Only the 2K diffuse used; luminance normalized, tinted walnut and reduced to 1024² for paneling, library, frames and tables. |

| Home: Solitude Night | Andreas Mischok | [Poly Haven asset and CC0 label](https://polyhaven.com/a/solitude_night); [license](https://polyhaven.com/license) | CC0-1.0, checked 2026-10-15 | Original 4K EXR; angular crop (160° × 94°), photographic HDR shoulder and sRGB conversion to RGB8 1742×1024. Mapped without stretching onto a 50 m spherical segment, with an unlit material. |

The Home architecture, four-place Chesterfield-inspired sofa, fireplace and embers, bookcases and books, clock,
lamps, window and curtains, furniture, speakers, garden railing, branching trees and decorative objects are original procedural geometry in
`tools/blender/home.py`, `lib_home_room.py` and `lib_home_window.py`. The Persian-inspired rug, woven fabrics, book-binding/decor atlas,
landscape illustration are original deterministic procedural artwork in
`lib_home_textures.py`; they do not reproduce a third-party rug, painting, photograph or book cover.
The mesh primitives and UV intersection algorithm were adapted into independent Home helpers from this project's
accepted cinema pipeline. The cinema scripts and assets were not modified.

No third-party models, normal maps, roughness maps, metallic maps or restricted marketplace files are included.
`art/home-sources.json` pins the two used CC0 diffuse inputs and the night HDRI and their MD5 checksums. The build downloads only from
`dl.polyhaven.org` into the git-ignored `art/sources/` cache; only prepared PNGs and `Home.fbx` go into `Assets/`.


## Rooftop terrace / Dachterrasse

| Asset | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| Clarens Night 02 | Dimitrios Savva (photography), Jarod Guest (processing) | [Poly Haven asset](https://polyhaven.com/a/clarens_night_02); [metadata](https://api.polyhaven.com/info/clarens_night_02); [license](https://polyhaven.com/license) | CC0-1.0, checked 2026-10-15 | 4K EXR, full 360° equirectangular photograph preserved at 4096×2048. Linear exposure factor 0.025 and soft per-channel shoulder 0.68*x/(1+x), then sRGB RGB8. No synthetic city or sky substitution. |

The terrace, all furniture and cushions, draped throws, lamps, opaque modeled olive/lavender/palm
foliage, neighboring buildings, chimneys, tanks and antennae are original procedural geometry.
Teak grain, plaster, fabric weave, stripes and outdoor rug are deterministic original surface
textures in `lib_rooftop_textures.py`. No third-party models or AI-generated photographs are used.
Rooftop primitives, UV audit and bake/export structure are independent adaptations of this project's
accepted Home pipeline; the accepted Home and Cinema files remain unchanged.

`art/rooftop-sources.json` pins the downloaded Poly Haven EXR by MD5. Raw files and API metadata
remain under git-ignored `art/sources/`. Only the prepared RGB PNGs and exported FBX are distributed.
No normal maps, opacity maps or restricted marketplace assets are included.

## Space lounge / Weltraum-Lounge

| Asset | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| The Blue Marble: Land Surface, Ocean Color and Sea Ice (2002) | NASA Goddard Space Flight Center; Reto Stöckli (land/shallow water); Robert Simmon (ocean color/compositing); MODIS teams; USGS/NOAA supporting data | [NASA Visible Earth](https://visibleearth.nasa.gov/images/57730/the-blue-marble-land-surface-ocean-color-and-sea-ice); [original 8192×4096 PNG](https://eoimages.gsfc.nasa.gov/images/imagerecords/57000/57730/land_ocean_ice_8192.png); [NASA background and credits](https://science.nasa.gov/earth/earth-observatory/the-blue-marble-2181/) | NASA public domain; [NASA media usage guidelines](https://www.nasa.gov/nasa-brand-center/images-and-media/) | Reduced to 4096×2048; combined with the NASA cloud composite below into Earth_Planet_BaseColor.png. No directional light, terminator, or atmosphere baked into albedo. |
| Blue Marble: Clouds (2002) | NASA Goddard Space Flight Center; Reto Stöckli; MODIS Atmosphere Group | [NASA Visible Earth](https://visibleearth.nasa.gov/images/57747/blue-marble-clouds); [original 8192×4096 TIFF](https://eoimages.gsfc.nasa.gov/images/imagerecords/57000/57747/cloud_combined_8192.tif) | NASA public domain | Reduced to 4096×2048 and composited over the day map; full longitude/latitude extent preserved. |
| Night Lights 2012 Map / Black Marble | NASA Earth Observatory image by Robert Simmon, using Suomi NPP VIIRS data courtesy of Chris Elvidge, NOAA National Geophysical Data Center; Suomi NPP is a NASA/NOAA/Department of Defense partnership | [NASA Earth Observatory](https://science.nasa.gov/earth/earth-observatory/night-lights-2012-map-79765/); [original 13500×6750 JPEG](https://eoimages.gsfc.nasa.gov/images/imagerecords/79000/79765/dnb_land_ocean_ice.2012.13500x6750.jpg) | NASA public domain | Reduced to 4096×2048; blue presentation basemap suppressed and light values warmed for Earth_Planet_Night.png. Geographic light distribution preserved. |

All lounge architecture, furniture, plants, instrument artwork, wood/leather/fabric textures and
starfield are original deterministic procedural work in `tools/blender/space.py` and `lib_space_*.py`.
The star panorama contains small discrete stellar cores, without a borrowed nebula or Milky Way image.
No ESA/Hubble, marketplace, AI-generated, normal-map, or transparent-glass assets are used.
The NASA images are scientific composites from different dates, used as decorative Earth maps;
they are not a live or contemporaneous Earth observation. NASA names identify the sources only;
no NASA logo or endorsement is implied.

`art/space-sources.json` pins all three downloaded NASA inputs by SHA-256. Raw images remain
under ignored `art/sources/`; only prepared RGB8 PNGs and Space.fbx are distributed. Mesh
primitives, UV checks and baking structure were adapted from this repository's accepted pipeline;
the accepted Cinema, Home and Rooftop scripts and assets remain unchanged.

## Orient-Express salon / Aster Vale

| Asset | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| Clarens Night 02 | Dimitrios Savva (photography), Jarod Guest (processing) | [Poly Haven asset](https://polyhaven.com/a/clarens_night_02); [metadata](https://api.polyhaven.com/info/clarens_night_02); [license](https://polyhaven.com/license) | CC0-1.0, checked 2026-10-16 | Checksum-pinned 4K EXR. Full 360° longitude retained; cylindrical elevation reprojection for the 60 m scenery band, exposure/soft HDR shoulder, restrained blue-hour grade, 32-column periodic edge treatment. RGB8 4096×2048. |

The salon architecture, furniture, floral marquetry, etched opaque glass artwork, carpet,
silk texture, recolored velvet/wood surfaces, curtain folds and tie-backs, fringed lamps, luggage racks, cups,
and AV monogram are original procedural work in `tools/blender/orient_express.py` and
`lib_orient_express_*.py`. “Aster Vale / AV” is an invented salon identity; no real railway
company logo, official seal, insignia or third-party ornament is reproduced.
The wood grain and velvet nap derive from the CC0 photographs listed below; the botanical inlay drawing is original.
The near ground panorama (ballast, grass, hedge, fence) is original deterministic opaque RGB
artwork; no alpha, normal maps, marketplace assets or third-party models are used.

Mesh primitives, global UV packing/auditing, baking and preview construction are independent
adaptations of the accepted room pipeline. The shared read-only seated-head auditor is reused.
Accepted Cinema, Home, Rooftop and Space scripts/assets are unchanged.
`art/orient-express-sources.json` pins the CC0 HDRI and two diffuse inputs by their Poly Haven MD5; raw files remain
in ignored `art/sources/`. Only prepared RGB8 PNGs and the FBX are distributed.

| Surface | Author | Source | License | Modified |
| --- | --- | --- | --- | --- |
| Dark Wood | Dario Barresi (baking), Dimitrios Savva (photography), Rico Cilliers (tiling) | [Poly Haven](https://polyhaven.com/a/dark_wood) | CC0-1.0, checked 2026-10-16 | 2K diffuse reduced to 1024², luminance-normalized and walnut/ebony tinted. Original floral inlays composited over the prepared grain. |
| Velour Velvet | colormass (photography), Rico Cilliers (processing) | [Poly Haven](https://polyhaven.com/a/velour_velvet) | CC0-1.0, checked 2026-10-16 | 2K diffuse reduced to 1024², normalized nap luminance tinted petrol for upholstery and champagne for curtains. No other maps used. |
