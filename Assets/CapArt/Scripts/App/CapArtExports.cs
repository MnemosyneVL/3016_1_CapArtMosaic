using System;
using System.Globalization;
using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// Builds the text of the exportable files (marking guide, 1:1 SVG pin
    /// template). Pure string building — used by both the editor tool and the
    /// standalone app.
    /// </summary>
    public static class CapArtExports
    {
        /// <summary>Invariant-culture number formatting for exported files.</summary>
        public static string F(float value)
        {
            return value.ToString("0.0#", CultureInfo.InvariantCulture);
        }

        public static string BuildMarkingGuide(CapMosaic mosaic, string mosaicName)
        {
            mosaic.EnsureSize();
            Vector2 size = mosaic.ArtworkSizeMm();
            float step = mosaic.StepMm();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("CAP ART MARKING GUIDE — " + mosaicName);
            sb.AppendLine("========================================");
            sb.AppendLine();
            sb.AppendLine("Artwork size: " + F(size.x) + " × " + F(size.y) + " mm  ("
                + F(size.x / 10f) + " × " + F(size.y / 10f) + " cm), gaps included");
            sb.AppendLine("Cap Ø " + F(mosaic.capDiameterMm) + " mm  |  gap "
                + F(mosaic.spacing * mosaic.capDiameterMm) + " mm  |  grid "
                + mosaic.width + "×" + mosaic.height + "  |  "
                + (mosaic.layout == HexLayout.OffsetRows ? "offset rows" : "offset columns"));
            sb.AppendLine();
            sb.AppendLine("HOW TO USE");
            sb.AppendLine("  All numbers are millimeters, measured from the BOTTOM-LEFT corner");
            sb.AppendLine("  of the artwork:");
            sb.AppendLine("    X = distance from the left edge, going right");
            sb.AppendLine("    Y = distance from the bottom edge, going up");
            sb.AppendLine("  Each line below is one cap. Mark a point at (X, Y) — that point is");
            sb.AppendLine("  the CENTER of the cap, where the pin goes.");
            sb.AppendLine("  Rows are numbered from the bottom: Row 1 is the bottom row.");
            sb.AppendLine();
            sb.AppendLine("RULER SHORTCUTS");
            if (mosaic.layout == HexLayout.OffsetRows)
            {
                sb.AppendLine("  caps within a row:  every " + F(step) + " mm");
                sb.AppendLine("  rows:               every " + F(step * 0.8660254f) + " mm");
                sb.AppendLine("  neighbouring rows are shifted " + F(step * 0.5f)
                    + " mm sideways from each other");
                sb.AppendLine("  (the X values below show which way)");
            }
            else
            {
                sb.AppendLine("  caps within a column:  every " + F(step) + " mm");
                sb.AppendLine("  columns:               every " + F(step * 0.8660254f) + " mm");
                sb.AppendLine("  neighbouring columns are shifted " + F(step * 0.5f)
                    + " mm vertically from each other");
                sb.AppendLine("  (the Y values below show which way)");
            }
            sb.AppendLine();

            float lineStep = step * 0.8660254f;
            if (mosaic.layout == HexLayout.OffsetRows)
            {
                sb.AppendLine("GUIDE LINES (draw the horizontal lines first, then put dots on them)");
                sb.AppendLine("  Lines are " + F(lineStep) + " mm apart. Dots on a line are every "
                    + F(step) + " mm.");
                sb.AppendLine("  For exact dot positions use the row tables further below.");
                for (int r = mosaic.height - 1; r >= 0; r--)
                {
                    int rowNumber = mosaic.height - r;
                    int capsInRow = 0;
                    float fromX = 0f, toX = 0f;
                    for (int c = 0; c < mosaic.width; c++)
                    {
                        if (mosaic.GetCell(c, r) == null)
                            continue;
                        float x = mosaic.CellCenterFromBottomLeftMm(c, r).x;
                        if (capsInRow == 0)
                            fromX = x;
                        toX = x;
                        capsInRow++;
                    }
                    Vector2 rowPos = mosaic.CellCenterFromBottomLeftMm(0, r);
                    string line = "  Line " + rowNumber.ToString().PadLeft(2)
                        + ":  Y = " + F(rowPos.y).PadLeft(7) + " mm";
                    if (capsInRow > 0)
                        line += "    from X " + F(fromX).PadLeft(6) + " to X " + F(toX).PadLeft(7)
                            + "  (length " + F(toX - fromX) + " mm, " + capsInRow + " caps)";
                    else
                        line += "    empty row — no line needed";
                    if (rowNumber == 1)
                        line += "    (bottom row)";
                    sb.AppendLine(line);
                }
            }
            else
            {
                sb.AppendLine("GUIDE LINES (draw the vertical lines first, then put dots on them)");
                sb.AppendLine("  Lines are " + F(lineStep) + " mm apart. Dots on a line are every "
                    + F(step) + " mm.");
                sb.AppendLine("  For exact dot positions use the row tables further below.");
                for (int c = 0; c < mosaic.width; c++)
                {
                    int capsInCol = 0;
                    float fromY = 0f, toY = 0f;
                    for (int r = mosaic.height - 1; r >= 0; r--)
                    {
                        if (mosaic.GetCell(c, r) == null)
                            continue;
                        float y = mosaic.CellCenterFromBottomLeftMm(c, r).y;
                        if (capsInCol == 0)
                            fromY = y;
                        toY = y;
                        capsInCol++;
                    }
                    Vector2 colPos = mosaic.CellCenterFromBottomLeftMm(c, mosaic.height - 1);
                    string line = "  Line " + (c + 1).ToString().PadLeft(2)
                        + ":  X = " + F(colPos.x).PadLeft(7) + " mm";
                    if (capsInCol > 0)
                        line += "    from Y " + F(fromY).PadLeft(6) + " to Y " + F(toY).PadLeft(7)
                            + "  (length " + F(toY - fromY) + " mm, " + capsInCol + " caps)";
                    else
                        line += "    empty column — no line needed";
                    if (c == 0)
                        line += "    (left column)";
                    sb.AppendLine(line);
                }
            }
            sb.AppendLine();

            int marks = 0;
            for (int r = mosaic.height - 1; r >= 0; r--)
            {
                bool rowHasCaps = false;
                for (int c = 0; c < mosaic.width; c++)
                {
                    if (mosaic.GetCell(c, r) != null)
                    {
                        rowHasCaps = true;
                        break;
                    }
                }
                if (!rowHasCaps)
                    continue;
                int rowNumber = mosaic.height - r;
                sb.AppendLine("Row " + rowNumber + (rowNumber == 1 ? "  (bottom row)" : "") + ":");
                for (int c = 0; c < mosaic.width; c++)
                {
                    CapType cap = mosaic.GetCell(c, r);
                    if (cap == null)
                        continue;
                    Vector2 p = mosaic.CellCenterFromBottomLeftMm(c, r);
                    sb.AppendLine("  col " + (c + 1).ToString().PadLeft(3)
                        + ":   X " + F(p.x).PadLeft(7)
                        + "   Y " + F(p.y).PadLeft(7)
                        + "   " + cap.name);
                    marks++;
                }
                sb.AppendLine();
            }
            sb.AppendLine("Total pins: " + marks);
            return sb.ToString();
        }

        public static string BuildSvgTemplate(CapMosaic mosaic, string mosaicName, Func<CapType, Color> averageColor)
        {
            mosaic.EnsureSize();
            Vector2 size = mosaic.ArtworkSizeMm();
            const float margin = 15f;
            float totalW = size.x + margin * 2f;
            float totalH = size.y + margin * 2f;
            float capRadius = Mathf.Max(1f, mosaic.capDiameterMm) * 0.5f;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + F(totalW) + "mm\" height=\""
                + F(totalH) + "mm\" viewBox=\"0 0 " + F(totalW) + " " + F(totalH) + "\">");
            sb.AppendLine("  <text x=\"" + F(margin) + "\" y=\"" + F(margin - 5f)
                + "\" font-size=\"4\" font-family=\"sans-serif\">" + EscapeXml(mosaicName)
                + " — " + F(size.x) + " × " + F(size.y)
                + " mm — PRINT AT 100% SCALE, verify with the 100 mm bar</text>");
            sb.AppendLine("  <path d=\"M " + F(margin) + " " + F(totalH - 6f)
                + " h 100\" stroke=\"#000\" stroke-width=\"0.5\"/>");
            sb.AppendLine("  <text x=\"" + F(margin + 102f) + "\" y=\"" + F(totalH - 4.5f)
                + "\" font-size=\"4\" font-family=\"sans-serif\">= 100 mm</text>");
            sb.AppendLine("  <rect x=\"" + F(margin) + "\" y=\"" + F(margin) + "\" width=\"" + F(size.x)
                + "\" height=\"" + F(size.y)
                + "\" fill=\"none\" stroke=\"#888\" stroke-width=\"0.3\" stroke-dasharray=\"3 2\"/>");

            for (int r = 0; r < mosaic.height; r++)
            {
                for (int c = 0; c < mosaic.width; c++)
                {
                    CapType cap = mosaic.GetCell(c, r);
                    if (cap == null)
                        continue;
                    Vector2 p = mosaic.CellCenterMm(c, r);
                    float cx = margin + p.x;
                    float cy = margin + p.y;
                    string hex = ColorUtility.ToHtmlStringRGB(averageColor != null ? averageColor(cap) : cap.color);
                    sb.AppendLine("  <circle cx=\"" + F(cx) + "\" cy=\"" + F(cy) + "\" r=\"" + F(capRadius)
                        + "\" fill=\"#" + hex + "\" fill-opacity=\"0.22\" stroke=\"#999\" stroke-width=\"0.2\"/>");
                    sb.AppendLine("  <path d=\"M " + F(cx - 3f) + " " + F(cy) + " h 6 M " + F(cx) + " "
                        + F(cy - 3f) + " v 6\" stroke=\"#000\" stroke-width=\"0.3\"/>");
                }
            }
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        static string EscapeXml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
