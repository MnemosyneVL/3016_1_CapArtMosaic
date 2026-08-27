using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Small window for creating new bottle cap types: name it, assign a photo
    /// (no pre-cropping needed — frame the cap right here), or pick a plain
    /// color, then hit Create.
    /// </summary>
    public class CapTypeCreatorWindow : EditorWindow
    {
        const string kFolder = "Assets/CapArt/Cap Types";

        string _capName = "New Cap";
        Texture2D _texture;
        Color _color = new Color(0.85f, 0.25f, 0.2f, 1f);
        int _amount = 0;
        float _cropZoom = 1f;
        Vector2 _cropCenter = new Vector2(0.5f, 0.5f);
        string _lastCreatedPath;

        [MenuItem("Tools/Cap Art/New Cap Type...", false, 2)]
        public static void Open()
        {
            var w = GetWindow<CapTypeCreatorWindow>("New Cap Type");
            w.minSize = new Vector2(340f, 560f);
            w.Show();
        }

        void OnGUI()
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Create a Bottle Cap Type", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag your cap photo into the Project window to import it, then assign it below. " +
                "No cropping needed — frame the cap with the circle: drag to move, scroll to zoom. " +
                "If no photo is assigned, the color is used.",
                MessageType.Info);
            GUILayout.Space(4f);

            _capName = EditorGUILayout.TextField("Name", _capName);
            EditorGUI.BeginChangeCheck();
            _texture = (Texture2D)EditorGUILayout.ObjectField("Photo (optional)", _texture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                // New photo — start from the default centered crop.
                _cropZoom = 1f;
                _cropCenter = new Vector2(0.5f, 0.5f);
            }
            _color = EditorGUILayout.ColorField("Color (if no photo)", _color);
            _amount = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("Amount owned", "How many of these caps you physically have."), _amount));

            GUILayout.Space(10f);
            if (_texture != null)
            {
                GUILayout.Label("Frame the cap — drag the circle, scroll to zoom", EditorStyles.miniBoldLabel);
                Rect area = GUILayoutUtility.GetRect(10f, 250f, GUILayout.ExpandWidth(true));
                if (CapCropGUI.Draw(area, _texture, GetEntityId(), ref _cropZoom, ref _cropCenter))
                    Repaint();
                _cropZoom = EditorGUILayout.Slider("Zoom", _cropZoom, 1f, 16f);
                if (GUILayout.Button("Reset Crop"))
                {
                    _cropZoom = 1f;
                    _cropCenter = new Vector2(0.5f, 0.5f);
                }
            }
            else
            {
                GUILayout.Label("Preview", EditorStyles.miniBoldLabel);
                Rect area = GUILayoutUtility.GetRect(10f, 180f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.135f, 1f));
                float size = Mathf.Min(area.width, area.height) - 16f;
                if (size > 8f)
                {
                    var square = new Rect(
                        area.x + (area.width - size) * 0.5f,
                        area.y + (area.height - size) * 0.5f,
                        size, size);
                    CapArtGUI.DrawCapRaw(square, null, _color);
                }
            }

            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_capName)))
            {
                if (GUILayout.Button("Create Cap Type", GUILayout.Height(34f)))
                    Create();
            }
            if (!string.IsNullOrEmpty(_lastCreatedPath))
                EditorGUILayout.HelpBox("Created: " + _lastCreatedPath, MessageType.None);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Mosaic Painter"))
                CapMosaicWindow.Open(null);
            GUILayout.Space(6f);
        }

        void Create()
        {
            var cap = ScriptableObject.CreateInstance<CapType>();
            cap.texture = _texture;
            cap.color = _color;
            cap.amount = _amount;
            cap.cropZoom = _cropZoom;
            cap.cropCenter = _cropCenter;

            // New caps go to the bottom of the palette.
            int maxOrder = -1;
            foreach (string guid in AssetDatabase.FindAssets("t:CapType"))
            {
                var existing = AssetDatabase.LoadAssetAtPath<CapType>(AssetDatabase.GUIDToAssetPath(guid));
                if (existing != null && existing.sortOrder > maxOrder)
                    maxOrder = existing.sortOrder;
            }
            cap.sortOrder = maxOrder + 1;

            EnsureFolder(kFolder);
            string safeName = string.Join("_", _capName.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            if (string.IsNullOrEmpty(safeName))
                safeName = "Cap";
            string path = AssetDatabase.GenerateUniqueAssetPath(kFolder + "/" + safeName + ".asset");

            AssetDatabase.CreateAsset(cap, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(cap);
            _lastCreatedPath = path;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
