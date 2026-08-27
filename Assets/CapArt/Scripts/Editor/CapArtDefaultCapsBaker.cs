using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Bakes all CapType assets in the project (photos included) into the
    /// sample-project JSON that ships inside the app build. The app loads it
    /// on first run and via "Load sample project" in its Export panel.
    /// Re-run this whenever the cap assets change.
    /// </summary>
    public static class CapArtDefaultCapsBaker
    {
        const string kResourcesFolder = "Assets/CapArt/Resources";
        const string kOutputPath = kResourcesFolder + "/" + CapArtProject.kDefaultResourceName + ".json";

        [MenuItem("Tools/Cap Art/Bake Default Caps for App", false, 21)]
        public static void Bake()
        {
            var caps = AssetDatabase.FindAssets("t:CapType")
                .Select(guid => AssetDatabase.LoadAssetAtPath<CapType>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(cap => cap != null)
                .OrderBy(cap => cap.sortOrder)
                .ThenBy(cap => cap.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (caps.Count == 0)
            {
                EditorUtility.DisplayDialog("Cap Art", "No CapType assets found — nothing to bake.", "OK");
                return;
            }

            var dto = new CapArtProject.ProjectDto();
            int photos = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                CapType cap = caps[i];
                string b64 = BakePhotoB64(cap.texture);
                if (!string.IsNullOrEmpty(b64))
                    photos++;
                dto.caps.Add(new CapArtProject.CapTypeDto
                {
                    id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cap)),
                    name = cap.name,
                    color = cap.color,
                    amount = cap.amount,
                    cropZoom = cap.cropZoom,
                    cropCenter = cap.cropCenter,
                    sortOrder = i,
                    photoB64 = b64,
                    photoFromBundle = !string.IsNullOrEmpty(b64)
                });
            }

            // One empty starter mosaic so first-run users land on a paintable grid.
            var cells = new string[16 * 12];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = "";
            dto.mosaics.Add(new CapArtProject.MosaicDto
            {
                name = "My Mosaic",
                width = 16,
                height = 12,
                layout = 0,
                spacing = 0.04f,
                capDiameterMm = 29f,
                cellIds = cells
            });

            if (!AssetDatabase.IsValidFolder(kResourcesFolder))
                AssetDatabase.CreateFolder("Assets/CapArt", "Resources");
            File.WriteAllText(kOutputPath, JsonUtility.ToJson(dto));
            AssetDatabase.ImportAsset(kOutputPath);

            long sizeKb = new FileInfo(kOutputPath).Length / 1024;
            EditorUtility.DisplayDialog("Cap Art",
                "Baked " + caps.Count + " cap types (" + photos + " with photos) into\n"
                + kOutputPath + "  (" + sizeKb + " KB).\n\n"
                + "The app now ships these as its sample project.", "OK");
        }

        /// <summary>
        /// Photo as base64 for the sample bundle, same rules as app imports:
        /// the original image file is shipped byte-identical when it is within
        /// the app's size cap; larger images get one high-quality downscale.
        /// </summary>
        static string BakePhotoB64(Texture2D texture)
        {
            if (texture == null)
                return "";

            string path = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png")
                {
                    byte[] rawBytes = File.ReadAllBytes(path);
                    var full = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    bool loaded = full.LoadImage(rawBytes);
                    if (loaded && Mathf.Max(full.width, full.height) <= CapArtProject.kMaxPhotoSize)
                    {
                        UnityEngine.Object.DestroyImmediate(full);
                        return Convert.ToBase64String(rawBytes); // untouched original
                    }
                    if (loaded)
                    {
                        string b64 = DownscaleToB64(full);
                        UnityEngine.Object.DestroyImmediate(full);
                        return b64;
                    }
                    UnityEngine.Object.DestroyImmediate(full);
                }
            }
            // Fallback: bake from the imported texture (non-file or unreadable sources).
            return DownscaleToB64(texture);
        }

        static string DownscaleToB64(Texture2D source)
        {
            int w = source.width;
            int h = source.height;
            int max = Mathf.Max(w, h);
            float scale = max > CapArtProject.kMaxPhotoSize ? (float)CapArtProject.kMaxPhotoSize / max : 1f;
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            RenderTexture rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, nw, nh), 0, 0);
                readable.Apply();
                byte[] jpg = readable.EncodeToJPG(95);
                UnityEngine.Object.DestroyImmediate(readable);
                return Convert.ToBase64String(jpg);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
