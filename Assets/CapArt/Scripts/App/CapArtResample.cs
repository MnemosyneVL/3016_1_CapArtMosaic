using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// CPU bilinear resampling of raw pixel data. The app's whole image
    /// pipeline runs through this instead of GPU blits on purpose: byte
    /// values pass through untouched (no color-space conversions can occur),
    /// so results are identical in Linear and Gamma projects, in the editor,
    /// in Play mode and in every build target.
    /// </summary>
    public static class CapArtResample
    {
        /// <summary>
        /// Bilinearly samples <paramref name="regionPx"/> (in source pixel
        /// coordinates, bottom-up like GetPixels32) of the source pixel array
        /// into the destination array. Taps are clamped to the source bounds.
        /// </summary>
        public static void Resample(Color32[] src, int srcW, int srcH, Rect regionPx,
            Color32[] dst, int dstW, int dstH)
        {
            float xStep = regionPx.width / dstW;
            float yStep = regionPx.height / dstH;
            for (int dy = 0; dy < dstH; dy++)
            {
                float sy = regionPx.y + (dy + 0.5f) * yStep - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(sy), 0, srcH - 1);
                int y1 = Mathf.Min(y0 + 1, srcH - 1);
                float fy = Mathf.Clamp01(sy - y0);
                int row0 = y0 * srcW;
                int row1 = y1 * srcW;
                int dRow = dy * dstW;
                for (int dx = 0; dx < dstW; dx++)
                {
                    float sx = regionPx.x + (dx + 0.5f) * xStep - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(sx), 0, srcW - 1);
                    int x1 = Mathf.Min(x0 + 1, srcW - 1);
                    float fx = Mathf.Clamp01(sx - x0);

                    Color32 c00 = src[row0 + x0];
                    Color32 c10 = src[row0 + x1];
                    Color32 c01 = src[row1 + x0];
                    Color32 c11 = src[row1 + x1];

                    float top, bottom;
                    top = c00.r + (c10.r - c00.r) * fx;
                    bottom = c01.r + (c11.r - c01.r) * fx;
                    byte r = (byte)(top + (bottom - top) * fy + 0.5f);
                    top = c00.g + (c10.g - c00.g) * fx;
                    bottom = c01.g + (c11.g - c01.g) * fx;
                    byte g = (byte)(top + (bottom - top) * fy + 0.5f);
                    top = c00.b + (c10.b - c00.b) * fx;
                    bottom = c01.b + (c11.b - c01.b) * fx;
                    byte b = (byte)(top + (bottom - top) * fy + 0.5f);

                    dst[dRow + dx] = new Color32(r, g, b, 255);
                }
            }
        }
    }
}
