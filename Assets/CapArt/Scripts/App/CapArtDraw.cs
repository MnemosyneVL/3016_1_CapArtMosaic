using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// Runtime IMGUI drawing helpers (no editor dependencies). Used by the
    /// standalone app; the editor windows have their own thin equivalents.
    /// </summary>
    public static class CapArtDraw
    {
        public static void DrawRect(Rect r, Color color)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.zero, Vector4.zero);
        }

        /// <summary>Draws a cap circle: a (pre-cropped/baked) texture or a colored disc.</summary>
        public static void DrawCapRaw(Rect r, Texture texture, Color color, float alpha = 1f)
        {
            float radius = r.width * 0.5f;
            if (texture != null)
            {
                GUI.DrawTexture(r, texture, ScaleMode.StretchToFill, true, 0f,
                    new Color(1f, 1f, 1f, alpha), Vector4.zero, Vector4.one * radius);
            }
            else
            {
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                    new Color(color.r, color.g, color.b, color.a * alpha), Vector4.zero, Vector4.one * radius);
            }
            DrawRing(r, new Color(0f, 0f, 0f, 0.35f * alpha), Mathf.Max(1f, r.width * 0.03f));
        }

        public static void DrawDisc(Rect r, Color color)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.zero, Vector4.one * (r.width * 0.5f));
        }

        public static void DrawRing(Rect r, Color color, float thickness)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.one * thickness, Vector4.one * (r.width * 0.5f));
        }
    }
}
