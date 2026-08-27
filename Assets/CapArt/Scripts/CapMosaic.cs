using System.Collections.Generic;
using UnityEngine;

namespace CapArt
{
    /// <summary>How the hexagonal packing is oriented.</summary>
    public enum HexLayout
    {
        /// <summary>Horizontal rows; every other row is shifted half a cap to the right.</summary>
        OffsetRows,
        /// <summary>Vertical columns; every other column is shifted half a cap down.</summary>
        OffsetColumns,
    }

    /// <summary>
    /// A bottle cap mosaic design: a hexagonally packed grid of cells, each
    /// either empty or referencing a CapType.
    /// </summary>
    [CreateAssetMenu(fileName = "New Mosaic", menuName = "Cap Art/Mosaic", order = 2)]
    public class CapMosaic : ScriptableObject
    {
        public int width = 16;
        public int height = 12;
        public HexLayout layout = HexLayout.OffsetRows;

        [Tooltip("Gap between caps, stored as a fraction of the cap diameter. Entered in millimeters in the painter toolbar; included in the artwork size estimate and the pin positions.")]
        [Range(0f, 1f)]
        public float spacing = 0.04f;

        [Tooltip("Real diameter of one cap in millimeters — measure one with a ruler. A pried-off crown cap flares to ~29 mm across the skirt (the '26 mm' standard refers to the bottle mouth, not the cap). Used to estimate the physical artwork size.")]
        public float capDiameterMm = 29f;

        /// <summary>Row-major cell array of size width * height. Null entry = empty tile.</summary>
        public CapType[] cells = new CapType[16 * 12];

        /// <summary>Fixes the cells array if its length no longer matches width * height.</summary>
        public void EnsureSize()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            int expected = width * height;
            if (cells == null)
            {
                cells = new CapType[expected];
            }
            else if (cells.Length != expected)
            {
                var next = new CapType[expected];
                System.Array.Copy(cells, next, Mathf.Min(cells.Length, expected));
                cells = next;
            }
        }

        public CapType GetCell(int col, int row)
        {
            if (col < 0 || row < 0 || col >= width || row >= height || cells == null)
                return null;
            int i = row * width + col;
            return i < cells.Length ? cells[i] : null;
        }

        public void SetCell(int col, int row, CapType cap)
        {
            if (col < 0 || row < 0 || col >= width || row >= height)
                return;
            EnsureSize();
            cells[row * width + col] = cap;
        }

        /// <summary>Resizes the grid, keeping existing caps that still fit (anchored top-left).</summary>
        public void Resize(int newWidth, int newHeight)
        {
            newWidth = Mathf.Max(1, newWidth);
            newHeight = Mathf.Max(1, newHeight);
            var next = new CapType[newWidth * newHeight];
            if (cells != null)
            {
                int copyW = Mathf.Min(width, newWidth);
                int copyH = Mathf.Min(height, newHeight);
                for (int r = 0; r < copyH; r++)
                {
                    for (int c = 0; c < copyW; c++)
                    {
                        int oldIndex = r * width + c;
                        if (oldIndex < cells.Length)
                            next[r * newWidth + c] = cells[oldIndex];
                    }
                }
            }
            width = newWidth;
            height = newHeight;
            cells = next;
        }

        /// <summary>Counts placed caps per type. Deleted/missing cap types count as empty.</summary>
        public Dictionary<CapType, int> CountCaps(out int totalFilled, out int emptyCells)
        {
            var counts = new Dictionary<CapType, int>();
            totalFilled = 0;
            emptyCells = 0;
            if (cells == null)
            {
                emptyCells = width * height;
                return counts;
            }
            for (int i = 0; i < cells.Length; i++)
            {
                CapType cap = cells[i];
                if (cap == null)
                {
                    emptyCells++;
                    continue;
                }
                totalFilled++;
                counts.TryGetValue(cap, out int n);
                counts[cap] = n + 1;
            }
            return counts;
        }

        /// <summary>Physical center-to-center distance between neighbouring caps in mm (cap Ø + gap).</summary>
        public float StepMm()
        {
            return Mathf.Max(1f, capDiameterMm) * (1f + Mathf.Max(0f, spacing));
        }

        /// <summary>Physical size of the artwork in millimeters, including the gap between caps.</summary>
        public Vector2 ArtworkSizeMm()
        {
            const float ROW = 0.8660254f; // sqrt(3)/2 — center-to-center distance between offset rows
            float d = Mathf.Max(1f, capDiameterMm);
            float step = StepMm();
            if (layout == HexLayout.OffsetRows)
                return new Vector2(
                    ((width - 1) + (height > 1 ? 0.5f : 0f)) * step + d,
                    (height - 1) * ROW * step + d);
            return new Vector2(
                (width - 1) * ROW * step + d,
                ((height - 1) + (width > 1 ? 0.5f : 0f)) * step + d);
        }

        /// <summary>
        /// Physical position of a cap center in millimeters, measured from the
        /// BOTTOM-LEFT corner of the artwork: X to the right, Y straight up.
        /// This is the convention used everywhere the user sees coordinates.
        /// </summary>
        public Vector2 CellCenterFromBottomLeftMm(int col, int row)
        {
            Vector2 p = CellCenterMm(col, row);
            return new Vector2(p.x, ArtworkSizeMm().y - p.y);
        }

        /// <summary>
        /// Physical position of a cap center in millimeters, measured from the
        /// top-left corner of the artwork (used for top-down drawing, e.g. SVG).
        /// </summary>
        public Vector2 CellCenterMm(int col, int row)
        {
            const float ROW = 0.8660254f;
            float d = Mathf.Max(1f, capDiameterMm);
            float step = StepMm();
            float margin = (step - d) * 0.5f; // half-gap surrounding the virtual grid
            if (layout == HexLayout.OffsetRows)
            {
                float x = (col + 0.5f + (((row & 1) == 1) ? 0.5f : 0f)) * step - margin;
                float y = (0.5f + row * ROW) * step - margin;
                return new Vector2(x, y);
            }
            else
            {
                float x = (0.5f + col * ROW) * step - margin;
                float y = (row + 0.5f + (((col & 1) == 1) ? 0.5f : 0f)) * step - margin;
                return new Vector2(x, y);
            }
        }
    }
}
