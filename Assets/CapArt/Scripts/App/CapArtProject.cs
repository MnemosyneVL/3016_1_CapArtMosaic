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
        /// <summary>
        /// Imported photos up to this size (longest side, px) are stored
        /// byte-identical; only larger ones are downscaled to it.
        /// </summary>
        public const int kMaxPhotoSize = 2560;

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
            public string photoB64; // image file bytes as base64, empty = no photo
            public bool photoFromBundle; // photo came from the bundled samples and was never replaced by the user
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
        // Caps whose photo still is the bundled sample photo (safe to auto-update).
        readonly HashSet<CapType> _bundlePhotoCaps = new HashSet<CapType>();

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
            _bundlePhotoCaps.Remove(cap);
            CapCropBaker.Release(cap);
            if (cap.texture != null)
                UnityEngine.Object.Destroy(cap.texture);
            UnityEngine.Object.Destroy(cap);
        }

        /// <summary>
        /// Decodes PNG/JPEG bytes into a texture guaranteed to have a full
        /// mipmap chain (required for clean crop baking and small previews),
        /// trilinear filtering and clamped edges. Null if not a readable image.
        ///
        /// Photo textures keep the sRGB flag (linear:false), like imported
        /// texture assets. All cropping/downscaling in the app is done on the
        /// CPU (CapArtResample) so pixel bytes are never run through GPU
        /// color-space conversions.
        /// </summary>
        public static Texture2D DecodePhotoTexture(byte[] bytes)
        {
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!loaded.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(loaded);
                return null;
            }
            var result = new Texture2D(loaded.width, loaded.height, TextureFormat.RGBA32, true, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            result.SetPixels32(loaded.GetPixels32());
            result.Apply(true, false);
            UnityEngine.Object.Destroy(loaded);
            return result;
        }

        /// <summary>
        /// Imports raw image bytes (PNG/JPEG) as a cap photo. Images up to
        /// kMaxPhotoSize are stored byte-identical — no recompression, no
        /// resizing. Larger ones are downscaled once using mipmapped GPU
        /// filtering and re-encoded at near-lossless quality.
        /// </summary>
        public static bool TryDecodePhoto(byte[] rawBytes, out Texture2D texture, out string storedB64)
        {
            texture = null;
            storedB64 = null;
            Texture2D loaded = DecodePhotoTexture(rawBytes);
            if (loaded == null)
                return false;

            int w = loaded.width;
            int h = loaded.height;
            int max = Mathf.Max(w, h);
            if (max <= kMaxPhotoSize)
            {
                // Keep the user's file exactly as it is.
                storedB64 = Convert.ToBase64String(rawBytes);
                texture = loaded;
                return true;
            }

            float scale = (float)kMaxPhotoSize / max;
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            // CPU downscale from the closest mip — no GPU, no color conversions.
            int mip = 0;
            while (mip < loaded.mipmapCount - 1 && (max >> (mip + 1)) >= kMaxPhotoSize)
                mip++;
            int mipW = Mathf.Max(1, w >> mip);
            int mipH = Mathf.Max(1, h >> mip);
            Color32[] srcPixels = loaded.GetPixels32(mip);
            var dstPixels = new Color32[nw * nh];
            CapArtResample.Resample(srcPixels, mipW, mipH, new Rect(0f, 0f, mipW, mipH), dstPixels, nw, nh);
            byte[] jpg;
            {
                var small = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
                small.SetPixels32(dstPixels);
                small.Apply(false, false);
                jpg = small.EncodeToJPG(95);
                UnityEngine.Object.Destroy(small);
            }
            UnityEngine.Object.Destroy(loaded);
            storedB64 = Convert.ToBase64String(jpg);
            texture = DecodePhotoTexture(jpg);
            return texture != null;
        }

        public void SetCapPhoto(CapType cap, Texture2D texture, string storedB64)
        {
            if (cap.texture != null && cap.texture != texture)
                UnityEngine.Object.Destroy(cap.texture);
            cap.texture = texture;
            _bundlePhotoCaps.Remove(cap); // the photo is user-chosen from now on
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
                    photoB64 = GetCapPhotoB64(cap) ?? "",
                    photoFromBundle = _bundlePhotoCaps.Contains(cap)
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
                CapType cap = InstantiateCap(capDto);
                byId[_capIds[cap]] = cap;
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

        /// <summary>Creates a cap instance from a DTO and registers it (list, id, photo).</summary>
        CapType InstantiateCap(CapTypeDto capDto)
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
                    byte[] bytes = Convert.FromBase64String(capDto.photoB64);
                    Texture2D tex = DecodePhotoTexture(bytes);
                    if (tex != null)
                    {
                        cap.texture = tex;
                        _photoB64[cap] = capDto.photoB64;
                        if (capDto.photoFromBundle)
                            _bundlePhotoCaps.Add(cap);
                    }
                }
                catch (Exception) { }
            }
            caps.Add(cap);
            string id = string.IsNullOrEmpty(capDto.id) ? Guid.NewGuid().ToString("N") : capDto.id;
            if (_capIds.ContainsValue(id))
                id = Guid.NewGuid().ToString("N");
            _capIds[cap] = id;
            return cap;
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
            _bundlePhotoCaps.Clear();
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

        /// <summary>Name of the bundled sample project inside a Resources folder.</summary>
        public const string kDefaultResourceName = "capart-default-project";

        /// <summary>
        /// Replaces the current content with the sample project bundled into the
        /// build (baked from the repo's cap assets). Returns false if none is
        /// bundled; the current content is only replaced on success.
        /// </summary>
        public bool LoadBundledDefault()
        {
            var bundled = Resources.Load<TextAsset>(kDefaultResourceName);
            if (bundled == null)
                return false;
            string json = bundled.text;
            Resources.UnloadAsset(bundled);
            var probe = new CapArtProject();
            bool ok = probe.FromJson(json);
            probe.Clear();
            if (!ok)
                return false;
            FromJson(json);
            if (mosaics.Count == 0)
                NewMosaic("My Mosaic");
            return true;
        }

        /// <summary>
        /// Appends the bundled sample cap types to the current project without
        /// touching its mosaics. Used for saves that have no caps at all (e.g.
        /// saves made before the samples existed). Returns true if caps were added.
        /// </summary>
        public bool AddBundledSampleCaps()
        {
            var bundled = Resources.Load<TextAsset>(kDefaultResourceName);
            if (bundled == null)
                return false;
            string json = bundled.text;
            Resources.UnloadAsset(bundled);
            ProjectDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProjectDto>(json);
            }
            catch (Exception)
            {
                return false;
            }
            if (dto == null || dto.caps == null || dto.caps.Count == 0)
                return false;
            dto.caps.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            int nextOrder = 0;
            foreach (CapType existing in caps)
                nextOrder = Mathf.Max(nextOrder, existing.sortOrder + 1);
            foreach (CapTypeDto capDto in dto.caps)
            {
                CapType cap = InstantiateCap(capDto);
                cap.sortOrder = nextOrder++;
            }
            return true;
        }

        /// <summary>
        /// Updates the photos of caps that came from the bundled samples — and
        /// were never given a different photo by the user — when the bundle
        /// carries different bytes. This lets improved sample photos reach
        /// existing saves. Returns how many caps were updated.
        /// </summary>
        public int SyncBundledSamplePhotos()
        {
            if (_bundlePhotoCaps.Count == 0)
                return 0;
            var bundled = Resources.Load<TextAsset>(kDefaultResourceName);
            if (bundled == null)
                return 0;
            string json = bundled.text;
            Resources.UnloadAsset(bundled);
            ProjectDto dto;
            try
            {
                dto = JsonUtility.FromJson<ProjectDto>(json);
            }
            catch (Exception)
            {
                return 0;
            }
            if (dto == null || dto.caps == null)
                return 0;

            var photoById = new Dictionary<string, string>();
            foreach (CapTypeDto capDto in dto.caps)
            {
                if (!string.IsNullOrEmpty(capDto.id) && !string.IsNullOrEmpty(capDto.photoB64))
                    photoById[capDto.id] = capDto.photoB64;
            }

            int updated = 0;
            foreach (CapType cap in caps)
            {
                if (!_bundlePhotoCaps.Contains(cap))
                    continue;
                if (!_capIds.TryGetValue(cap, out string id) || !photoById.TryGetValue(id, out string freshB64))
                    continue;
                if (GetCapPhotoB64(cap) == freshB64)
                    continue;
                try
                {
                    Texture2D tex = DecodePhotoTexture(Convert.FromBase64String(freshB64));
                    if (tex == null)
                        continue;
                    if (cap.texture != null)
                        UnityEngine.Object.Destroy(cap.texture);
                    cap.texture = tex;
                    _photoB64[cap] = freshB64;
                    updated++;
                }
                catch (Exception) { }
            }
            return updated;
        }

        /// <summary>
        /// Loads the auto-saved project; falls back to the bundled sample
        /// project on first run, then to a fresh empty one. A save without a
        /// single cap type gets the bundled sample caps added (nothing can be
        /// painted without caps, so nothing is lost). Returns true when the
        /// resulting state differs from what is on disk and should be saved.
        /// </summary>
        public bool LoadFromDiskOrCreateDefault()
        {
            try
            {
                if (File.Exists(SavePath) && FromJson(File.ReadAllText(SavePath)))
                {
                    bool changed = false;
                    if (mosaics.Count == 0)
                    {
                        NewMosaic("My Mosaic");
                        changed = true;
                    }
                    if (caps.Count == 0 && AddBundledSampleCaps())
                        changed = true;
                    if (SyncBundledSamplePhotos() > 0)
                        changed = true;
                    return changed;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Cap Art: could not load project: " + e.Message);
            }
            if (LoadBundledDefault())
                return true;
            Clear();
            NewMosaic("My Mosaic");
            return true;
        }
    }
}
