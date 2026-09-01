# Cap Art — Bottle Cap Mosaic Planner

Design a bottle cap mosaic on a hexagonal grid, count exactly how many caps of
each kind you need, and export everything required to build the real thing —
pin coordinates, guide-line measurements and a true-to-scale printable
template.

Made by an artist planning a real mosaic from a bottle cap collection; shared
so you can plan yours.

**▶ Try it in your browser:** `https://mnemosynevl.github.io/3016_1_CapArtMosaic/`
*(link goes live once GitHub Pages is set up — see below)*

**⬇ Desktop downloads:** see the [Releases](../../releases) page.

<!-- screenshot: add docs/screenshot.png and reference it here -->

## What it does

- **Hexagonal tilemap painter** — pick a cap type, left-click to place,
  right-click to erase, drag to paint. Zoom with the wheel, pan with the
  middle mouse button. Undo with Ctrl+Z. Grid size is adjustable, with two
  hex packings (offset rows / offset columns).
- **Cap types from photos** — photograph your caps, import the image
  *uncropped*, and frame the cap by dragging a circle over the photo and
  zooming. No image editing software needed. Caps without a photo use a plain
  color.
- **Inventory tracking** — enter how many caps of each kind you own. Usage is
  counted per mosaic *and across all your designs together*, with live
  warnings: orange when you've used exactly what you own, red when you've
  gone over.
- **Real-world planning** — enter your real cap diameter and gap; the app
  shows the physical artwork size and exports:
  - a **marking guide** (.txt): every cap center as X/Y millimeters from the
    bottom-left corner, plus guide-line heights, lengths and spacing for
    marking the canvas with a ruler;
  - a **1:1 pin template** (.svg): crosshairs at every cap center, with a
    100 mm calibration bar — print at 100% scale and punch through the marks.
- **Multiple mosaics** in one project, and a single-file **project export**
  (photos embedded) for backup or sharing.
- **Sample caps included** — the app starts with 16 real bottle cap photos
  from the author's collection, so you can try everything before importing
  your own. Get them back anytime via *Export / Import → Load sample project*.

Designs auto-save locally (in the browser's storage for the web version — use
*Export project file* for a safe backup).

## Run from source

1. Install **Unity 6000.5** (any 6000.5.x should work).
2. Clone this repo and open the project folder in Unity.
3. Menu **Tools → Cap Art → Create App Scene (for builds)**, then press
   **Play** to use the app inside the editor.

The repo also contains the original editor-integrated version of the tool
(**Tools → Cap Art → Mosaic Painter** / **New Cap Type…**), which works with
project assets instead of the app's save file. The app and the editor tool
share the same core code. See
[Assets/CapArt/README.md](Assets/CapArt/README.md) for its manual.

## Building

### Web version (GitHub Pages)

1. **File → Build Profiles**, switch platform to **WebGL**.
2. **Edit → Project Settings → Player → WebGL → Publishing Settings**: set
   **Compression Format** to **Disabled** (or Gzip with *Decompression
   Fallback* enabled). GitHub Pages doesn't serve Unity's compressed builds
   with the right headers otherwise.
3. Build into a folder named **`docs`** at the repo root.
4. Commit and push, then on GitHub: **Settings → Pages → Deploy from a
   branch**, branch `main`, folder `/docs`.
5. Your app is live at `https://mnemosynevl.github.io/3016_1_CapArtMosaic/`.

### Desktop version

1. **File → Build Profiles**, pick **Windows** (or **macOS**) and build.
2. Zip the build folder and attach it to a GitHub Release.

### Refreshing the bundled sample caps

The sample caps live in `Assets/CapArt/Resources/capart-default-project.json`.
After changing the CapType assets in the project, run
**Tools → Cap Art → Bake Default Caps for App** to regenerate it, then rebuild.

## License

[MIT](LICENSE) — use it, modify it, share it.
