using System.Collections.Generic;
using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// Bakes the cropped square region of a cap photo into a cached 512px
    /// texture for fast circle drawing. The bake is a pure CPU resample
    /// (see CapArtResample): raw sRGB bytes go in and come out untouched, so
    /// the baked texture displays exactly like the photo itself — no GPU
    /// blits, render textures or color-space conversions are involved.
    /// Rebakes automatically when the source texture or crop changes.
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

        // Single-slot cache of source pixels at a given mip level — hot while
        // dragging in the crop editor (same source and mip every frame).
        static Texture2D sPixelCacheTexture;
        static int sPixelCacheMip = -1;
        static Color32[] sPixelCache;

        static readonly Color32[] sScratch = new Color32[kSize * kSize];

        public static Texture2D GetForCap(CapType cap)
        {
            if (cap == null || cap.texture == null)
                return null;
            return Get(cap, cap.texture, cap.GetCropUVRect());
        }

        static Texture2D Get(CapType owner, Texture2D source, Rect uv)
        {
            sEntries.TryGetValue(owner, out Entry entry);
            bool hasBake = entry != null && entry.baked != null && entry.source == source;
            if (hasBake && entry.uv == uv)
                return entry.baked;

            // IMGUI delivers several events per frame while dragging the crop;
            // rebake only on the Repaint pass once a bake exists.
            if (hasBake && Event.current != null && Event.current.type != EventType.Repaint)
                return entry.baked;

            if (!source.isReadable)
                return hasBake ? entry.baked : null;

            if (entry == null)
            {
                entry = new Entry();
                sEntries[owner] = entry;
            }
            if (entry.baked == null)
            {
                entry.baked = new Texture2D(kSize, kSize, TextureFormat.RGBA32, true, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear
                };
            }

            BakeCpu(source, uv, entry.baked);
            entry.source = source;
            entry.uv = uv;
            return entry.baked;
        }

        static void BakeCpu(Texture2D source, Rect uv, Texture2D dest)
        {
            // Use the smallest source mip that still covers the output size, so
            // each bilinear tap spans at most ~2 source pixels.
            float cropPx = uv.width * source.width;
            int mip = 0;
            while (mip < source.mipmapCount - 1 && cropPx / (1 << (mip + 1)) >= kSize)
                mip++;
            int mipW = Mathf.Max(1, source.width >> mip);
            int mipH = Mathf.Max(1, source.height >> mip);

            if (sPixelCacheTexture != source || sPixelCacheMip != mip || sPixelCache == null)
            {
                sPixelCache = source.GetPixels32(mip);
                sPixelCacheTexture = source;
                sPixelCacheMip = mip;
            }

            var region = new Rect(uv.x * mipW, uv.y * mipH, uv.width * mipW, uv.height * mipH);
            CapArtResample.Resample(sPixelCache, mipW, mipH, region, sScratch, kSize, kSize);
            dest.SetPixels32(sScratch);
            dest.Apply(true, false);
        }

        /// <summary>Frees the cached bake for a cap (e.g. when it is deleted).</summary>
        public static void Release(CapType cap)
        {
            if (cap == null)
                return;
            if (cap.texture != null && sPixelCacheTexture == cap.texture)
            {
                sPixelCacheTexture = null;
                sPixelCacheMip = -1;
                sPixelCache = null;
            }
            if (!sEntries.TryGetValue(cap, out Entry entry))
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
