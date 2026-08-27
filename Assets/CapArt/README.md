# Cap Art — Bottle Cap Mosaic Tool (editor version)

Design a bottle cap mosaic on a hexagonal grid and get an exact count of how
many caps of each kind you need. Everything runs inside the Unity editor — no
need to press Play.

> There is also a **standalone app** version of this tool (runs in Play mode,
> buildable for web and desktop) — see the README at the repository root. Use
> **Tools → Cap Art → Create App Scene (for builds)** to set it up. The app
> keeps its own save file; the editor tool below works with project assets.

## Workflow

1. **Import your cap photos.** No cropping needed — drag the image files
   straight from your camera/phone anywhere into Unity's Project window
   (e.g. into `Assets/CapArt/Photos`).
2. **Create cap types.** Open **Tools → Cap Art → New Cap Type…**, give the cap
   a name, assign the photo (or leave it empty and pick a color instead), frame
   the cap in the crop view, and press **Create Cap Type**. The asset is saved
   under `Assets/CapArt/Cap Types`.
3. **Paint the mosaic.** Open **Tools → Cap Art → Mosaic Painter**, create a
   mosaic with the **New** button (or the button in the middle of the window),
   pick a cap type in the left palette and paint.

## Framing uncropped photos

Wherever a cap photo is assigned you get a crop view: the full photo shown
dimmed, with a bright circle over the part that will fill the cap.

- **Drag** the circle to position it over the cap in the photo.
- **Scroll** (or use the Zoom slider) to zoom in and out.
- **Reset Crop** returns to the largest centered square.

The crop is stored on the cap type asset. To adjust it later, select the cap
asset in the Project window and use the same controls in the Inspector — every
tile already painted with that cap updates automatically. The photo is never
distorted: the crop is always a square region, so caps keep their proportions.

## Mosaic Painter controls

| Input                 | Action                                  |
| --------------------- | --------------------------------------- |
| **Left mouse button** | Place the selected cap on a tile        |
| **Right mouse button**| Empty the tile                          |
| Click + drag          | Paint / erase continuously              |
| Mouse wheel           | Zoom                                    |
| Middle mouse / Alt+drag | Pan the view                          |
| Ctrl+Z / Ctrl+Y       | Undo / redo painting                    |

## Toolbar

- **Size** — grid width × height in tiles. Existing caps are kept when you
  resize (anchored to the top-left corner).
- **Offset Rows / Offset Columns** — hex packing orientation: horizontal rows
  shifted half a cap, or vertical columns shifted half a cap.
- **Gap (mm)** — real spacing between caps, entered in millimeters. Included
  in the artwork size estimate and in all exported pin positions. The gap
  stays the same physical size if you later change the cap diameter.
- **Cap Ø (mm)** — the real diameter of your caps, used for the artwork size
  estimate in the status bar. Measure one with a ruler: a pried-off crown cap
  flares to ~29 mm across the skirt (the "26 mm" in cap specs refers to the
  bottle mouth, not the cap itself); plastic bottle caps are usually ~30 mm.

## Counting caps & your inventory

Every cap type has an **Amount owned** — how many of those caps you physically
have. Set it when creating the cap, in the cap asset's Inspector, or directly
in the painter palette (the small **own** field on each row).

Each palette row shows live numbers:

> `12 here · 27 / 30 all designs`

- **here** — caps of this type used in the mosaic you are painting.
- **all designs** — caps of this type used across *every* mosaic asset in the
  project, against how many you own. This is what tells you whether several
  mosaics can be built at the same time from one collection.

Two live warning levels:

- **Orange ●** — *at limit*: you have used exactly as many as you own. The
  row tints orange, the status bar counts the types at limit, and the brush
  readout shows "(at limit)" when that cap is selected — one more and you go
  over.
- **Red ⚠** — *over stock*: used more than you own. The row turns red, a
  warning box below the palette lists exactly which caps you are short on
  (used vs. owned), and the status bar shows how many types are over.

Warnings appear the moment you paint the relevant cap and disappear when you
erase or raise the owned amount.

## Marking the canvas for pins

Three ways to find the physical cap centers:

- **Hover readout** — hover any tile in the painter and the status bar shows
  its center as X/Y in millimeters, measured from the **bottom-left corner**
  of the artwork (X to the right, Y straight up).
- **Export… → Marking Guide (.txt)** — a text file listing every placed cap
  with its X/Y center (grouped by row, with the cap name), plus ruler
  shortcuts: the repeating distance between caps in a row, between rows, and
  the sideways shift between neighbouring rows. A **GUIDE LINES** section
  lists every horizontal line: its height (Y from the bottom), where its
  first dot starts (X from the left), and how many caps it carries — draw the
  lines first, then dot them. Rows are numbered from the bottom (Row 1 =
  bottom row, listed first — the order you'd build in).
- **Export… → Pin Template 1:1 (.svg)** — a true-to-scale vector sheet with a
  crosshair at every cap center and faint colored circles for orientation.
  Print it at **100% scale** (no "fit to page"!) and verify with the printed
  100 mm calibration bar before use; then lay it on the canvas and punch
  through the cross centers. For artworks larger than your printer's paper,
  have it printed at a copy shop (A2/A1 plotter) — it is a standard SVG file.

All coordinates assume the artwork's bottom-left corner as origin. If your
board is cut larger than the artwork, draw the artwork rectangle on the board
first and measure from its bottom-left corner.

## Arranging the palette

Drag the **≡ handle** on the left of any palette row to reorder the cap types
— for example to group them by color. The order is saved on the cap assets
themselves, so it persists across sessions. The **Sort: color** button above
the palette arranges everything automatically (grays first, then around the
color wheel, using the average color of each cap's photo); you can fine-tune
by dragging afterwards. Both are undoable with Ctrl+Z.

Totals (caps placed / empty tiles) are shown below the palette and in the
status bar, together with the estimated physical size of the artwork.

## Good to know

- Mosaics are regular project assets — you can keep several design variants and
  switch between them with the picker in the toolbar. Selecting a mosaic asset
  in the Project window shows a summary and an *Open in Mosaic Painter* button.
- Changing a cap type's photo or color later automatically updates every tile
  that uses it.
- Deleting a cap type asset empties all tiles that used it.
- Painting edits are saved into the mosaic asset automatically after each
  stroke.
