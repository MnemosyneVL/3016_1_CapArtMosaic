using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>IMGUI helpers for drawing bottle caps as circles.</summary>
    public static class CapArtGUI
    {
        /// <summary>Draws a cap into a square rect: the cropped photo over a circle, or a colored disc.</summary>
        public static void DrawCap(Rect r, CapType cap, float alpha = 1f)
        {
            if (cap == null)
                return;
            Texture baked = cap.texture != null ? CapBake.GetForCap(cap) : null;
            DrawCapRaw(r, baked, cap.color, alpha);
        }

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
            // Subtle dark rim so neighbouring caps of the same color still read as separate objects.
            DrawRing(r, new Color(0f, 0f, 0f, 0.35f * alpha), Mathf.Max(1f, r.width * 0.03f));
        }

        /// <summary>Draws a solid filled circle.</summary>
        public static void DrawDisc(Rect r, Color color)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.zero, Vector4.one * (r.width * 0.5f));
        }

        /// <summary>Draws a circle outline.</summary>
        public static void DrawRing(Rect r, Color color, float thickness)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.one * thickness, Vector4.one * (r.width * 0.5f));
        }
    }
}
