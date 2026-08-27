using System.Collections.Generic;
using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// Runtime version of the crop baker: renders the cropped square region of
    /// a cap photo into a cached texture. Rebakes when the source texture
    /// reference or the crop parameters change.
    /// </summary>
    public static class CapCropBaker
    {
        const int kSize = 512;

        class Entry
        {
            public Texture2D baked;
            public Texture2D source;
            public Rect uv;
        }

        static readonly Dictionary<CapType, Entry> sEntries = new Dictionary<CapType, Entry>();

        public static Texture2D GetForCap(CapType cap)
        {
            if (cap == null || cap.texture == null)
                return null;
            return Get(cap, cap.texture, cap.GetCropUVRect());
        }

        static Texture2D Get(CapType owner, Texture2D source, Rect uv)
        {
            sEntries.TryGetValue(owner, out Entry entry);
            if (entry != null && entry.baked != null && entry.source == source && entry.uv == uv)
                return entry.baked;

            if (entry == null)
            {
                entry = new Entry();
                sEntries[owner] = entry;
            }
            if (entry.baked == null)
            {
                entry.baked = new Texture2D(kSize, kSize, TextureFormat.RGBA32, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            RenderTexture rt = RenderTexture.GetTemporary(kSize, kSize, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
                RenderTexture.active = rt;
                entry.baked.ReadPixels(new Rect(0f, 0f, kSize, kSize), 0, 0);
                entry.baked.Apply(true, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            entry.source = source;
            entry.uv = uv;
            return entry.baked;
        }

        /// <summary>Frees the cached bake for a cap (e.g. when it is deleted).</summary>
        public static void Release(CapType cap)
        {
            if (cap == null || !sEntries.TryGetValue(cap, out Entry entry))
                return;
            if (entry.baked != null)
                Object.Destroy(entry.baked);
            sEntries.Remove(cap);
        }

        /// <summary>Average color of the cap as shown (baked photo, or the plain color).</summary>
        public static Color AverageColor(CapType cap)
        {
            if (cap == null)
                return Color.gray;
            if (cap.texture == null)
                return cap.color;
            Texture2D baked = GetForCap(cap);
            if (baked == null)
                return cap.color;
            int mip = Mathf.Clamp(baked.mipmapCount - 5, 0, baked.mipmapCount - 1);
            Color[] pixels = baked.GetPixels(mip);
            if (pixels.Length == 0)
                return cap.color;
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                r += pixels[i].r;
                g += pixels[i].g;
                b += pixels[i].b;
            }
            return new Color(r / pixels.Length, g / pixels.Length, b / pixels.Length, 1f);
        }
    }
}
