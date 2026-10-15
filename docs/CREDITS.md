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

The Home architecture, four-place Chesterfield-inspired sofa, fireplace and embers, bookcases and books, clock,
lamps, window and curtains, furniture, speakers and decorative objects are original procedural geometry in
`tools/blender/home.py` and `lib_home_room.py`. The Persian-inspired rug, woven fabrics, book-binding/decor atlas,
landscape illustration and night-city illustration are original deterministic procedural artwork in
`lib_home_textures.py`; they do not reproduce a third-party rug, painting, photograph or book cover.
The mesh primitives and UV intersection algorithm were adapted into independent Home helpers from this project's
accepted cinema pipeline. The cinema scripts and assets were not modified.

No third-party models, normal maps, roughness maps, metallic maps or restricted marketplace files are included.
`art/home-sources.json` pins the two used CC0 diffuse inputs and their MD5 checksums. The build downloads only from
`dl.polyhaven.org` into the git-ignored `art/sources/` cache; only prepared PNGs and `Home.fbx` go into `Assets/`.
