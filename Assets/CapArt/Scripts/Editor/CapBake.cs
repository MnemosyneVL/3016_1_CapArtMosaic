using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Bakes the cropped square region of a cap photo into a cached square
    /// texture, so the painter can keep drawing caps with the fast
    /// GUI.DrawTexture circle path. Rebakes automatically when the source
    /// photo or the crop parameters change.
    /// </summary>
    public static class CapBake
    {
        const int kSize = 512;

        class Entry
        {
            public Texture2D baked;
            public Texture2D source;
            public Hash128 sourceHash;
            public Rect uv;
        }

        static readonly Dictionary<EntityId, Entry> sEntries = new Dictionary<EntityId, Entry>();

        /// <summary>Baked crop texture for a cap type asset (null if it has no photo).</summary>
        public static Texture2D GetForCap(CapType cap)
        {
            if (cap == null || cap.texture == null)
                return null;
            return Get(cap.GetEntityId(), cap.texture, cap.GetCropUVRect());
        }

        /// <summary>
        /// Baked crop texture for arbitrary parameters. <paramref name="ownerId"/>
        /// identifies who is asking (cap instance id, window instance id, ...) so
        /// each editing context keeps its own cache slot.
        /// </summary>
        public static Texture2D Get(EntityId ownerId, Texture2D source, Rect uv)
        {
            if (source == null)
                return null;

            sEntries.TryGetValue(ownerId, out Entry entry);
            Hash128 hash = source.imageContentsHash;
            if (entry != null && entry.baked != null && entry.source == source
                && entry.sourceHash == hash && entry.uv == uv)
                return entry.baked;

            if (entry == null)
            {
                entry = new Entry();
                sEntries[ownerId] = entry;
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
            entry.sourceHash = hash;
            entry.uv = uv;
            return entry.baked;
        }
    }
}
