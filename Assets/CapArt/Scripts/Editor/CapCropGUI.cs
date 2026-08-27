using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Interactive crop control: shows the full (uncropped) photo dimmed, with a
    /// bright circle over the region that will fill the cap. Drag the circle to
    /// move it, scroll to zoom.
    /// </summary>
    public static class CapCropGUI
    {
        static readonly int kControlHash = "CapArtCropEditor".GetHashCode();

        /// <summary>
        /// Draws the crop editor and updates <paramref name="zoom"/> /
        /// <paramref name="center"/> from user input. Returns true when they changed.
        /// </summary>
        public static bool Draw(Rect area, Texture2D texture, EntityId ownerId, ref float zoom, ref Vector2 center)
        {
            if (texture == null)
                return false;
            bool changed = false;

            EditorGUI.DrawRect(area, new Color(0.10f, 0.10f, 0.105f, 1f));

            float texW = texture.width;
            float texH = texture.height;
            const float pad = 6f;
            float scale = Mathf.Min((area.width - pad * 2f) / texW, (area.height - pad * 2f) / texH);
            if (scale <= 0f)
                return false;
            var fit = new Rect(
                area.x + (area.width - texW * scale) * 0.5f,
                area.y + (area.height - texH * scale) * 0.5f,
                texW * scale, texH * scale);

            // Input first, so the circle follows the cursor without a one-frame lag.
            int id = GUIUtility.GetControlID(kControlHash, FocusType.Passive, area);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (area.Contains(e.mousePosition) && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        center.x += e.delta.x / fit.width;
                        center.y -= e.delta.y / fit.height; // GUI y is down, UV v is up
                        changed = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;

                case EventType.ScrollWheel:
                    if (area.Contains(e.mousePosition))
                    {
                        zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 1f / 1.1f : 1.1f), 1f, 16f);
                        changed = true;
                        e.Use();
                    }
                    break;
            }

            Rect uv = CapType.ComputeCropUV(texture.width, texture.height, zoom, center);
            if (changed)
                center = uv.center; // snap the stored center back to the clamped position

            // Dimmed full photo.
            GUI.DrawTexture(fit, texture, ScaleMode.StretchToFill, true, 0f,
                new Color(1f, 1f, 1f, 0.30f), Vector4.zero, Vector4.zero);

            // Bright circular preview of the crop, at its real position in the photo.
            var crop = new Rect(
                fit.x + uv.x * fit.width,
                fit.y + (1f - uv.y - uv.height) * fit.height,
                uv.width * fit.width,
                uv.height * fit.height);
            Texture2D baked = CapBake.Get(ownerId, texture, uv);
            if (baked != null)
            {
                GUI.DrawTexture(crop, baked, ScaleMode.StretchToFill, true, 0f,
                    Color.white, Vector4.zero, Vector4.one * (crop.width * 0.5f));
            }
            CapArtGUI.DrawRing(crop, new Color(0.3f, 0.62f, 1f, 0.95f), Mathf.Max(1.5f, crop.width * 0.02f));

            return changed;
        }
    }
}
