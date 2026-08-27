using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Inspector for CapType assets: default fields, the interactive crop
    /// control for uncropped photos, and a live circular preview.
    /// </summary>
    [CustomEditor(typeof(CapType))]
    [CanEditMultipleObjects]
    public class CapTypeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1)
                return;
            var cap = (CapType)target;

            if (cap.texture != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label("Frame the cap — drag the circle, scroll to zoom", EditorStyles.miniBoldLabel);
                Rect area = GUILayoutUtility.GetRect(10f, 250f, GUILayout.ExpandWidth(true));
                float zoom = cap.cropZoom;
                Vector2 center = cap.cropCenter;
                if (CapCropGUI.Draw(area, cap.texture, cap.GetEntityId(), ref zoom, ref center))
                {
                    Undo.RecordObject(cap, "Edit Cap Crop");
                    cap.cropZoom = zoom;
                    cap.cropCenter = center;
                    EditorUtility.SetDirty(cap);
                    Repaint();
                }

                EditorGUI.BeginChangeCheck();
                float sliderZoom = EditorGUILayout.Slider("Zoom", cap.cropZoom, 1f, 16f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(cap, "Edit Cap Crop");
                    cap.cropZoom = sliderZoom;
                    EditorUtility.SetDirty(cap);
                }
                if (GUILayout.Button("Reset Crop"))
                {
                    Undo.RecordObject(cap, "Reset Cap Crop");
                    cap.cropZoom = 1f;
                    cap.cropCenter = new Vector2(0.5f, 0.5f);
                    EditorUtility.SetDirty(cap);
                }
            }

            GUILayout.Space(10f);
            Rect previewArea = GUILayoutUtility.GetRect(10f, 150f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewArea, new Color(0.13f, 0.13f, 0.135f, 1f));
            float size = Mathf.Min(previewArea.width, previewArea.height) - 16f;
            if (size > 8f)
            {
                var square = new Rect(
                    previewArea.x + (previewArea.width - size) * 0.5f,
                    previewArea.y + (previewArea.height - size) * 0.5f,
                    size, size);
                CapArtGUI.DrawCap(square, cap);
            }
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var cap = target as CapType;
            if (cap == null)
                return null;
            try
            {
                return CapThumbnail.Render(cap, width, height);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Builds circular thumbnail textures for CapType assets (Project window icons).</summary>
    static class CapThumbnail
    {
        public static Texture2D Render(CapType cap, int width, int height)
        {
            Color[] source = null;
            if (cap.texture != null)
            {
                // Copy the cropped region through a temporary RenderTexture so
                // non-readable textures work too.
                Rect uv = cap.GetCropUVRect();
                RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(cap.texture, rt, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
                    RenderTexture.active = rt;
                    var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    readable.Apply();
                    source = readable.GetPixels();
                    Object.DestroyImmediate(readable);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            float cx = (width - 1) * 0.5f;
            float cy = (height - 1) * 0.5f;
            float radius = Mathf.Min(width, height) * 0.5f - 1f;
            float radiusSq = radius * radius;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    bool inside = dx * dx + dy * dy <= radiusSq;
                    Color c = source != null ? source[y * width + x] : cap.color;
                    pixels[y * width + x] = inside ? new Color(c.r, c.g, c.b, 1f) : Color.clear;
                }
            }
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }
    }
}
