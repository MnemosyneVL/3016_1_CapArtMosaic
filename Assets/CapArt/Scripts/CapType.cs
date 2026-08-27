using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// One bottle cap design. Assign a photo of the cap (it does not need to be
    /// pre-cropped — the crop controls choose which part fills the cap circle),
    /// or leave the photo empty to represent the cap with a plain color.
    /// </summary>
    [CreateAssetMenu(fileName = "New Cap", menuName = "Cap Art/Cap Type", order = 1)]
    public class CapType : ScriptableObject
    {
        [Tooltip("Photo of the cap. No pre-cropping needed — use the crop controls below to zoom and position the cap inside the circle. Leave empty to use the color instead.")]
        public Texture2D texture;

        [Tooltip("Used when no photo is assigned.")]
        public Color color = new Color(0.85f, 0.25f, 0.2f, 1f);

        [Tooltip("How many of these caps you physically own. The painter warns when your designs (all mosaics together) use more than this.")]
        [Min(0)] public int amount = 0;

        // Which square region of the photo fills the cap circle. Edited with the
        // interactive crop control in the cap inspector / creator window.
        [HideInInspector] public float cropZoom = 1f;                          // 1 = largest square that fits the photo
        [HideInInspector] public Vector2 cropCenter = new Vector2(0.5f, 0.5f); // center of the crop square, in UV space

        // Position in the painter palette. Managed by dragging rows in the
        // Mosaic Painter (or its Sort: color button).
        [HideInInspector] public int sortOrder;

        /// <summary>The crop square in UV coordinates (clamped inside the photo).</summary>
        public Rect GetCropUVRect()
        {
            if (texture == null)
                return new Rect(0f, 0f, 1f, 1f);
            return ComputeCropUV(texture.width, texture.height, cropZoom, cropCenter);
        }

        /// <summary>
        /// Computes the crop square in UV space: at zoom 1 it is the largest
        /// square that fits the image; higher zoom shrinks it around
        /// <paramref name="center"/>, clamped so it never leaves the image.
        /// </summary>
        public static Rect ComputeCropUV(int texWidth, int texHeight, float zoom, Vector2 center)
        {
            if (texWidth <= 0 || texHeight <= 0)
                return new Rect(0f, 0f, 1f, 1f);
            zoom = Mathf.Clamp(zoom, 1f, 16f);
            float squarePx = Mathf.Min(texWidth, texHeight) / zoom;
            float uSize = squarePx / texWidth;
            float vSize = squarePx / texHeight;
            float u = uSize >= 1f ? 0.5f : Mathf.Clamp(center.x, uSize * 0.5f, 1f - uSize * 0.5f);
            float v = vSize >= 1f ? 0.5f : Mathf.Clamp(center.y, vSize * 0.5f, 1f - vSize * 0.5f);
            return new Rect(u - uSize * 0.5f, v - vSize * 0.5f, uSize, vSize);
        }
    }
}
