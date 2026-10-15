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
