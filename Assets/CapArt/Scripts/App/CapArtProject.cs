using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// The standalone app's data layer: all cap types and all mosaics of one
    /// project, serialized to a single JSON file with photos embedded (base64
    /// JPEG). Auto-saved to Application.persistentDataPath; the same format is
    /// used for the export/import "project file" so designs can be shared.
    /// </summary>
    public class CapArtProject
    {
        const int kMaxPhotoSize = 768; // imported photos are downscaled to this

        [Serializable]
        public class CapTypeDto
        {
            public string id;
            public string name;
            public Color color;
            public int amount;
            public float cropZoom = 1f;
            public Vector2 cropCenter = new Vector2(0.5f, 0.5f);
            public int sortOrder;
            public string photoB64; // JPEG bytes as base64, empty = no photo
        }

        [Serializable]
        public class MosaicDto
        {
            public string name;
            public int width;
            public int height;
            public int layout;
            public float spacing;
            public float capDiameterMm;
            public string[] cellIds; // cap id per cell, "" = empty
        }

        [Serializable]
        public class ProjectDto
        {
            public int version = 1;
            public List<CapTypeDto> caps = new List<CapTypeDto>();
            public List<MosaicDto> mosaics = new List<MosaicDto>();
        }

        public readonly List<CapType> caps = new List<CapType>();
        public readonly List<CapMosaic> mosaics = new List<CapMosaic>();
        public readonly List<string> mosaicNames = new List<string>();

        readonly Dictionary<CapType, string> _capIds = new Dictionary<CapType, string>();
        readonly Dictionary<CapType, string> _photoB64 = new Dictionary<CapType, string>();

        public static string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, "capart-project.json"); }
        }

        // ------------------------------------------------------------- caps

        public string GetCapId(CapType cap)
        {
            if (!_capIds.TryGetValue(cap, out string id))
            {
                id = Guid.NewGuid().ToString("N");
                _capIds[cap] = id;
            }
            return id;
        }

        public CapType NewCap(string capName)
        {
            var cap = ScriptableObject.CreateInstance<CapType>();
            cap.name = string.IsNullOrEmpty(capName) ? "New Cap" : capName;
            int maxOrder = -1;
            foreach (CapType c in caps)
                maxOrder = Mathf.Max(maxOrder, c.sortOrder);
            cap.sortOrder = maxOrder + 1;
            caps.Add(cap);
            GetCapId(cap);
            return cap;
        }

        public void DeleteCap(CapType cap)
        {
            foreach (CapMosaic m in mosaics)
            {
                m.EnsureSize();
                for (int i = 0; i < m.cells.Length; i++)
                {
                    if (m.cells[i] == cap)
                        m.cells[i] = null;
                }
            }
            caps.Remove(cap);
            _capIds.Remove(cap);
            _photoB64.Remove(cap);
            CapCropBaker.Release(cap);
            if (cap.texture != null)
                UnityEngine.Object.Destroy(cap.texture);
            UnityEngine.Object.Destroy(cap);
        }

        /// <summary>
        /// Imports raw image bytes (PNG/JPEG) as a cap photo: decodes,
        /// downscales to a manageable size, re-encodes as JPEG for storage and
        /// returns the texture (not yet assigned to any cap).
        /// </summary>
        public static bool TryDecodePhoto(byte[] rawBytes, out Texture2D texture, out string storedB64)
        {
            texture = null;
            storedB64 = null;
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!loaded.LoadImage(rawBytes))
            {
                UnityEngine.Object.Destroy(loaded);
                return false;
            }

            int w = loaded.width;
            int h = loaded.height;
            int max = Mathf.Max(w, h);
            if (max > kMaxPhotoSize)
            {
                float scale = (float)kMaxPhotoSize / max;
                int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
                int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                RenderTexture rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(loaded, rt);
                    RenderTexture.active = rt;
                    var small = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
                    small.ReadPixels(new Rect(0f, 0f, nw, nh), 0, 0);
                    small.Apply();
                    UnityEngine.Object.Destroy(loaded);
                    loaded = small;
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            byte[] jpg = loaded.EncodeToJPG(88);
            storedB64 = Convert.ToBase64String(jpg);
            // Rebuild the texture from the stored bytes so what you see is what is saved.
            UnityEngine.Object.Destroy(loaded);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(jpg);
            return true;
        }

        public void SetCapPhoto(CapType cap, Texture2D texture, string storedB64)
        {
            if (cap.texture != null && cap.texture != texture)
                UnityEngine.Object.Destroy(cap.texture);
            cap.texture = texture;
            if (string.IsNullOrEmpty(storedB64))
                _photoB64.Remove(cap);
            else
                _photoB64[cap] = storedB64;
        }

        public string GetCapPhotoB64(CapType cap)
        {
            return _photoB64.TryGetValue(cap, out string b64) ? b64 : null;
        }

        // ------------------------------------------------------------- mosaics

        public CapMosaic NewMosaic(string mosaicName)
        {
            var mosaic = ScriptableObject.CreateInstance<CapMosaic>();
            mosaic.EnsureSize();
            mosaics.Add(mosaic);
            mosaicNames.Add(string.IsNullOrEmpty(mosaicName) ? "Mosaic " + mosaics.Count : mosaicName);
            return mosaic;
        }

        public CapMosaic DuplicateMosaic(int index)
        {
            CapMosaic src = mosaics[index];
            src.EnsureSize();
            CapMosaic copy = NewMosaic(mosaicNames[index] + " copy");
            copy.width = src.width;
            copy.height = src.height;
            copy.layout = src.layout;
            copy.spacing = src.spacing;
            copy.capDiameterMm = src.capDiameterMm;
            copy.cells = (CapType[])src.cells.Clone();
            return copy;
        }

        public void DeleteMosaic(int index)
        {
            UnityEngine.Object.Destroy(mosaics[index]);
            mosaics.RemoveAt(index);
            mosaicNames.RemoveAt(index);
        }

        // ------------------------------------------------------------- counting

        /// <summary>Caps of this type used across all mosaics in the project.</summary>
        public int TotalUsed(CapType cap)
        {
            int total = 0;
            foreach (CapMosaic m in mosaics)
            {
                if (m == null || m.cells == null)
                    continue;
                for (int i = 0; i < m.cells.Length; i++)
                {
                    if (m.cells[i] == cap)
                        total++;
                }
            }
            return total;
        }

        // ------------------------------------------------------------- serialization

        public string ToJson()
        {
            var dto = new ProjectDto();
            for (int i = 0; i < caps.Count; i++)
            {
                CapType cap = caps[i];
                cap.sortOrder = i;
                dto.caps.Add(new CapTypeDto
                {
                    id = GetCapId(cap),
                    name = cap.name,
                    color = cap.color,
                    amount = cap.amount,
                    cropZoom = cap.cropZoom,
                    cropCenter = cap.cropCenter,
                    sortOrder = cap.sortOrder,
                    photoB64 = GetCapPhotoB64(cap) ?? ""
                });
            }
            for (int i = 0; i < mosaics.Count; i++)
            {
                CapMosaic m = mosaics[i];
                m.EnsureSize();
                var cellIds = new string[m.cells.Length];
                for (int c = 0; c < m.cells.Length; c++)
                    cellIds[c] = m.cells[c] != null ? GetCapId(m.cells[c]) : "";
                dto.mosaics.Add(new MosaicDto
                {
                    name = mosaicNames[i],
                    width = m.width,
                    height = m.height,
                    layout = (int)m.layout,
                    spacing = m.spacing,
                    capDiameterMm = m.capDiameterMm,
                    cellIds = cellIds
                });
            }
            return JsonUtility.ToJson(dto);
        }

        /// <summary>Replaces the current content with the given project JSON. Returns false if unreadable.</summary>
        public bool FromJson(string json)
        {
            ProjectDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProjectDto>(json);
            }
            catch (Exception)
            {
                return false;
            }
            if (dto == null)
                return false;

            Clear();

            var byId = new Dictionary<string, CapType>();
            dto.caps.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            foreach (CapTypeDto capDto in dto.caps)
            {
                var cap = ScriptableObject.CreateInstance<CapType>();
                cap.name = capDto.name;
                cap.color = capDto.color;
                cap.amount = capDto.amount;
                cap.cropZoom = capDto.cropZoom;
                cap.cropCenter = capDto.cropCenter;
                cap.sortOrder = capDto.sortOrder;
                if (!string.IsNullOrEmpty(capDto.photoB64))
                {
                    try
                    {
                        byte[] jpg = Convert.FromBase64String(capDto.photoB64);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(jpg))
                        {
                            cap.texture = tex;
                            _photoB64[cap] = capDto.photoB64;
                        }
                        else
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }
                    catch (Exception) { }
                }
                caps.Add(cap);
                string id = string.IsNullOrEmpty(capDto.id) ? Guid.NewGuid().ToString("N") : capDto.id;
                _capIds[cap] = id;
                byId[id] = cap;
            }

            foreach (MosaicDto mosaicDto in dto.mosaics)
            {
                var m = ScriptableObject.CreateInstance<CapMosaic>();
                m.width = Mathf.Max(1, mosaicDto.width);
                m.height = Mathf.Max(1, mosaicDto.height);
                m.layout = (HexLayout)mosaicDto.layout;
                m.spacing = mosaicDto.spacing;
                m.capDiameterMm = mosaicDto.capDiameterMm;
                m.cells = new CapType[m.width * m.height];
                if (mosaicDto.cellIds != null)
                {
                    int n = Mathf.Min(m.cells.Length, mosaicDto.cellIds.Length);
                    for (int c = 0; c < n; c++)
                    {
                        if (!string.IsNullOrEmpty(mosaicDto.cellIds[c])
                            && byId.TryGetValue(mosaicDto.cellIds[c], out CapType cap))
                            m.cells[c] = cap;
                    }
                }
                mosaics.Add(m);
                mosaicNames.Add(string.IsNullOrEmpty(mosaicDto.name) ? "Mosaic " + mosaics.Count : mosaicDto.name);
            }
            return true;
        }

        public void Clear()
        {
            foreach (CapMosaic m in mosaics)
                UnityEngine.Object.Destroy(m);
            foreach (CapType cap in caps)
            {
                CapCropBaker.Release(cap);
                if (cap.texture != null)
                    UnityEngine.Object.Destroy(cap.texture);
                UnityEngine.Object.Destroy(cap);
            }
            mosaics.Clear();
            mosaicNames.Clear();
            caps.Clear();
            _capIds.Clear();
            _photoB64.Clear();
        }

        // ------------------------------------------------------------- disk

        public void SaveToDisk()
        {
            try
            {
                File.WriteAllText(SavePath, ToJson());
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cap Art: could not save project: " + e.Message);
            }
        }

        /// <summary>Loads the auto-saved project, or creates a fresh default one.</summary>
        public void LoadFromDiskOrCreateDefault()
        {
            try
            {
                if (File.Exists(SavePath) && FromJson(File.ReadAllText(SavePath)))
                    return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cap Art: could not load project: " + e.Message);
            }
            Clear();
            NewMosaic("My Mosaic");
        }
    }
}
