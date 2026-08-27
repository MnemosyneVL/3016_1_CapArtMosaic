using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// The mosaic painter: a hexagonally packed grid of bottle caps.
    /// LMB paints the selected cap type, RMB empties the tile.
    /// Mouse wheel zooms, middle mouse (or Alt+LMB) pans.
    /// </summary>
    public class CapMosaicWindow : EditorWindow
    {
        const float kBaseCell = 44f;                 // cap diameter in pixels at zoom 1
        const float kRowStep = 0.8660254f;           // sqrt(3)/2 вЂ” offset-row spacing factor
        const int kMaxGridSide = 250;
        const string kLastMosaicPref = "CapArt.LastMosaicGuid";
        static readonly int kCanvasHash = "CapArtCanvasControl".GetHashCode();

        [SerializeField] CapMosaic _mosaic;
        [SerializeField] CapType _selected;
        [SerializeField] bool _eraser;

        List<CapType> _palette = new List<CapType>();
        Vector2 _paletteScroll;

        float _zoom = 1f;
        Vector2 _pan;
        bool _fitPending = true;

        bool _painting;
        bool _panning;
        int _paintButton;
        int _strokeUndoGroup;

        // Tile under the cursor, for the status-bar center readout.
        bool _hasHoverInfo;
        int _hoverCol;
        int _hoverRow;

        Dictionary<CapType, int> _counts = new Dictionary<CapType, int>();
        int _filled;
        int _empty;
        bool _countsDirty = true;

        // Cap usage summed over every other CapMosaic asset in the project
        // (the current mosaic is counted live via _counts).
        Dictionary<CapType, int> _otherCounts = new Dictionary<CapType, int>();
        bool _otherCountsDirty = true;
        int _overStockCount;
        int _atLimitCount;

        // Drag-to-reorder state for the palette.
        static readonly int kPaletteDragHash = "CapArtPaletteDrag".GetHashCode();
        readonly List<Rect> _rowRects = new List<Rect>();
        int _paletteDragControlId;
        CapType _dragCap;
        int _dragInsertIndex = -1;

        static GUIStyle sRowName;
        static GUIStyle sRowInfo;
        static GUIStyle sRowInfoOver;
        static GUIStyle sRowInfoLimit;
        static GUIStyle sMiniWarn;
        static GUIStyle sMiniOver;
        static GUIStyle sCenterGray;

        [MenuItem("Tools/Cap Art/Mosaic Painter", false, 1)]
        static void OpenFromMenu()
        {
            Open(null);
        }

        public static void Open(CapMosaic mosaic)
        {
            var window = GetWindow<CapMosaicWindow>("Mosaic Painter");
            window.minSize = new Vector2(760f, 420f);
            if (mosaic != null)
                window.SetMosaic(mosaic);
            window.Show();
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged += OnProjectChanged;
            RefreshPalette();
            if (_mosaic == null)
            {
                string guid = EditorPrefs.GetString(kLastMosaicPref, "");
                if (!string.IsNullOrEmpty(guid))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                        _mosaic = AssetDatabase.LoadAssetAtPath<CapMosaic>(path);
                }
            }
            _countsDirty = true;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        void OnFocus()
        {
            RefreshPalette();
            _countsDirty = true;
            _otherCountsDirty = true;
        }

        void OnUndoRedo()
        {
            RefreshPalette(); // sort order and amounts are undoable too
            _countsDirty = true;
            _otherCountsDirty = true;
            Repaint();
        }

        void OnProjectChanged()
        {
            RefreshPalette();
            _countsDirty = true;
            _otherCountsDirty = true;
            Repaint();
        }

        void RefreshPalette()
        {
            _palette = AssetDatabase.FindAssets("t:CapType")
                .Select(guid => AssetDatabase.LoadAssetAtPath<CapType>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(cap => cap != null)
                .OrderBy(cap => cap.sortOrder)
                .ThenBy(cap => cap.name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_selected != null && !_palette.Contains(_selected))
                _selected = null;
        }

        void SetMosaic(CapMosaic mosaic)
        {
            _mosaic = mosaic;
            if (mosaic != null)
            {
                mosaic.EnsureSize();
                string path = AssetDatabase.GetAssetPath(mosaic);
                if (!string.IsNullOrEmpty(path))
                    EditorPrefs.SetString(kLastMosaicPref, AssetDatabase.AssetPathToGUID(path));
            }
            _countsDirty = true;
            _otherCountsDirty = true;
            _fitPending = true;
            Repaint();
        }

        void InitStyles()
        {
            if (sRowName != null)
                return;
            sRowName = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            sRowInfo = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            sRowInfoOver = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            sRowInfoOver.normal.textColor = new Color(1f, 0.5f, 0.45f, 1f);
            sRowInfoLimit = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            sRowInfoLimit.normal.textColor = new Color(1f, 0.72f, 0.3f, 1f);
            sMiniWarn = new GUIStyle(EditorStyles.miniBoldLabel);
            sMiniWarn.normal.textColor = new Color(1f, 0.62f, 0.25f, 1f);
            sMiniOver = new GUIStyle(EditorStyles.miniBoldLabel);
            sMiniOver.normal.textColor = new Color(1f, 0.45f, 0.4f, 1f);
            sCenterGray = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };
        }

        void OnGUI()
        {
            InitStyles();
            UpdateCountsIfNeeded();
            UpdateOtherCountsIfNeeded();

            DrawToolbar();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawPalette();
            Rect canvas = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawCanvas(canvas);
            EditorGUILayout.EndHorizontal();

            DrawStatusBar();

            if (Event.current.type == EventType.MouseMove)
                Repaint();
        }

        // ---------------------------------------------------------------- toolbar

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var picked = (CapMosaic)EditorGUILayout.ObjectField(_mosaic, typeof(CapMosaic), false, GUILayout.Width(170f));
            if (EditorGUI.EndChangeCheck())
                SetMosaic(picked);

            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                CreateMosaicAsset();

            if (_mosaic != null)
            {
                GUILayout.Space(14f);
                GUILayout.Label("Size", EditorStyles.miniLabel);
                int newW = EditorGUILayout.DelayedIntField(_mosaic.width, GUILayout.Width(42f));
                GUILayout.Label("Г—", GUILayout.Width(12f));
                int newH = EditorGUILayout.DelayedIntField(_mosaic.height, GUILayout.Width(42f));
                if (newW != _mosaic.width || newH != _mosaic.height)
                    ResizeMosaic(newW, newH);

                GUILayout.Space(14f);
                EditorGUI.BeginChangeCheck();
                var newLayout = (HexLayout)EditorGUILayout.EnumPopup(_mosaic.layout, EditorStyles.toolbarPopup, GUILayout.Width(112f));
                GUILayout.Space(10f);
                GUILayout.Label("Gap", EditorStyles.miniLabel);
                float newGapMm = EditorGUILayout.DelayedFloatField(_mosaic.spacing * _mosaic.capDiameterMm, GUILayout.Width(36f));
                GUILayout.Label("mm", EditorStyles.miniLabel);
                GUILayout.Space(10f);
                GUILayout.Label("Cap Г", EditorStyles.miniLabel);
                float newMm = EditorGUILayout.DelayedFloatField(_mosaic.capDiameterMm, GUILayout.Width(36f));
                GUILayout.Label("mm", EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_mosaic, "Mosaic Settings");
                    if (newLayout != _mosaic.layout)
                        _fitPending = true;
                    _mosaic.layout = newLayout;
                    _mosaic.capDiameterMm = Mathf.Max(1f, newMm);
                    // Gap is entered in mm but stored as a fraction of the cap
                    // diameter, so it stays the same physical size when Г changes.
                    _mosaic.spacing = Mathf.Clamp(Mathf.Max(0f, newGapMm) / _mosaic.capDiameterMm, 0f, 1f);
                    EditorUtility.SetDirty(_mosaic);
                }

                GUILayout.Space(14f);
                if (GUILayout.Button("Export...", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                    ShowExportMenu();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fit View", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                _fitPending = true;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        void CreateMosaicAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Mosaic", "My Mosaic", "asset", "Choose where to save this mosaic design");
            if (string.IsNullOrEmpty(path))
                return;
            var mosaic = ScriptableObject.CreateInstance<CapMosaic>();
            mosaic.EnsureSize();
            AssetDatabase.CreateAsset(mosaic, path);
            AssetDatabase.SaveAssets();
            SetMosaic(mosaic);
        }

        void ResizeMosaic(int newW, int newH)
        {
            if (_mosaic == null)
                return;
            newW = Mathf.Clamp(newW, 1, kMaxGridSide);
            newH = Mathf.Clamp(newH, 1, kMaxGridSide);
            if (newW == _mosaic.width && newH == _mosaic.height)
                return;
            Undo.RecordObject(_mosaic, "Resize Mosaic");
            _mosaic.Resize(newW, newH);
            EditorUtility.SetDirty(_mosaic);
            _countsDirty = true;
            _fitPending = true;
        }

        // ---------------------------------------------------------------- palette

        void DrawPalette()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(280f), GUILayout.ExpandHeight(true));
            GUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Cap Types", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Sort: color",
                    "Arrange the palette by cap color (grays first, then by hue). Fine-tune by dragging the в‰Ў handles."),
                    GUILayout.Width(78f)))
                SortPaletteByColor();
            if (GUILayout.Button("New...", GUILayout.Width(52f)))
                CapTypeCreatorWindow.Open();
            EditorGUILayout.EndHorizontal();

            _rowRects.Clear();
            _paletteDragControlId = GUIUtility.GetControlID(kPaletteDragHash, FocusType.Passive);

            _paletteScroll = EditorGUILayout.BeginScrollView(_paletteScroll, GUILayout.ExpandHeight(true));
            DrawPaletteRow(null); // eraser entry
            foreach (CapType cap in _palette)
                DrawPaletteRow(cap);
            HandlePaletteDrag();
            EditorGUILayout.EndScrollView();

            // Stock check across every mosaic in the project.
            _overStockCount = 0;
            _atLimitCount = 0;
            List<string> overNames = null;
            foreach (CapType cap in _palette)
            {
                int total = TotalUsed(cap, out _);
                if (total > cap.amount)
                {
                    _overStockCount++;
                    if (overNames == null)
                        overNames = new List<string>();
                    if (overNames.Count < 6)
                        overNames.Add(cap.name + "   " + total + " used / " + cap.amount + " owned");
                }
                else if (cap.amount > 0 && total == cap.amount)
                {
                    _atLimitCount++;
                }
            }
            if (_overStockCount > 0)
            {
                string message = "Not enough caps (all designs together):\n" + string.Join("\n", overNames);
                if (_overStockCount > overNames.Count)
                    message += "\nвЂ¦and " + (_overStockCount - overNames.Count) + " more";
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Caps placed: " + _filled + "\nEmpty tiles: " + _empty,
                MessageType.None);
            GUILayout.Space(4f);
            EditorGUILayout.EndVertical();
        }

        void DrawPaletteRow(CapType cap)
        {
            bool isEraser = cap == null;
            Rect row = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            row.x += 2f;
            row.width -= 6f;
            row.y += 1f;
            row.height -= 2f;

            if (!isEraser)
                _rowRects.Add(row);

            bool selected = isEraser ? _eraser : (!_eraser && _selected == cap);
            int usedHere = 0;
            int totalUsed = 0;
            bool overStock = false;
            bool atLimit = false;
            if (!isEraser)
            {
                totalUsed = TotalUsed(cap, out usedHere);
                overStock = totalUsed > cap.amount;
                atLimit = !overStock && cap.amount > 0 && totalUsed == cap.amount;
            }

            if (selected)
                EditorGUI.DrawRect(row, new Color(0.24f, 0.49f, 0.91f, 0.35f));
            else if (row.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.06f));
            if (overStock)
                EditorGUI.DrawRect(row, new Color(1f, 0.3f, 0.25f, 0.08f));
            else if (atLimit)
                EditorGUI.DrawRect(row, new Color(1f, 0.6f, 0.15f, 0.07f));

            Event e = Event.current;

            if (isEraser)
            {
                var preview = new Rect(row.x + 21f, row.y + (row.height - 34f) * 0.5f, 34f, 34f);
                CapArtGUI.DrawRing(preview, new Color(0.7f, 0.7f, 0.7f, 0.9f), 2f);
                GUI.Label(preview, "Г—", sCenterGray);
                var eraserName = new Rect(preview.xMax + 8f, row.y, row.xMax - 8f - (preview.xMax + 8f), row.height);
                GUI.Label(eraserName, "Eraser  (RMB anywhere)", sRowName);
            }
            else
            {
                // Drag handle for reordering.
                var handle = new Rect(row.x + 2f, row.y, 16f, row.height);
                GUI.Label(handle, "в‰Ў", sCenterGray);
                EditorGUIUtility.AddCursorRect(handle, MouseCursor.MoveArrow);
                if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition))
                {
                    _dragCap = cap;
                    _dragInsertIndex = _rowRects.Count - 1;
                    GUIUtility.hotControl = _paletteDragControlId;
                    e.Use();
                }

                var preview = new Rect(row.x + 21f, row.y + (row.height - 34f) * 0.5f, 34f, 34f);
                CapArtGUI.DrawCap(preview, cap);

                float nameX = preview.xMax + 8f;
                var nameRect = new Rect(nameX, row.y + 4f, Mathf.Max(30f, row.xMax - 92f - nameX), 18f);
                GUI.Label(nameRect, cap.name, sRowName);

                // Owned amount, editable in place.
                var ownLabel = new Rect(row.xMax - 88f, row.y + 6f, 32f, 14f);
                GUI.Label(ownLabel, "own", EditorStyles.miniLabel);
                var ownField = new Rect(row.xMax - 54f, row.y + 4f, 48f, 17f);
                EditorGUI.BeginChangeCheck();
                int newAmount = EditorGUI.DelayedIntField(ownField, cap.amount);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(cap, "Cap Amount");
                    cap.amount = Mathf.Max(0, newAmount);
                    EditorUtility.SetDirty(cap);
                    AssetDatabase.SaveAssetIfDirty(cap);
                }

                var info = new Rect(nameX, row.y + 26f, row.xMax - 8f - nameX, 18f);
                string text = usedHere + " here   В·   " + totalUsed + " / " + cap.amount + " all designs";
                GUIStyle infoStyle = sRowInfo;
                if (overStock)
                {
                    text = "вљ  " + text;
                    infoStyle = sRowInfoOver;
                }
                else if (atLimit)
                {
                    text = "в—Џ " + text;
                    infoStyle = sRowInfoLimit;
                }
                GUI.Label(info, text, infoStyle);
            }

            if (e.type == EventType.MouseDown && row.Contains(e.mousePosition))
            {
                if (e.button == 0)
                {
                    if (isEraser)
                    {
                        _eraser = true;
                        _selected = null;
                    }
                    else
                    {
                        _selected = cap;
                        _eraser = false;
                    }
                    e.Use();
                    Repaint();
                }
                else if (e.button == 1 && !isEraser)
                {
                    EditorGUIUtility.PingObject(cap);
                    e.Use();
                }
            }
        }

        // ------------------------------------------------------- palette ordering

        void HandlePaletteDrag()
        {
            if (_dragCap == null)
            {
                if (GUIUtility.hotControl == _paletteDragControlId)
                    GUIUtility.hotControl = 0;
                return;
            }

            Event e = Event.current;
            switch (e.GetTypeForControl(_paletteDragControlId))
            {
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != _paletteDragControlId)
                        break;
                    _dragInsertIndex = InsertIndexFromMouse(e.mousePosition.y);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != _paletteDragControlId)
                        break;
                    GUIUtility.hotControl = 0;
                    CommitPaletteReorder();
                    e.Use();
                    Repaint();
                    break;
            }

            if (Event.current.type == EventType.Repaint && _dragInsertIndex >= 0 && _rowRects.Count > 0)
            {
                // Insertion line.
                float y = _dragInsertIndex < _rowRects.Count
                    ? _rowRects[_dragInsertIndex].yMin
                    : _rowRects[_rowRects.Count - 1].yMax;
                EditorGUI.DrawRect(new Rect(_rowRects[0].x, y - 1.5f, _rowRects[0].width, 3f),
                    new Color(0.3f, 0.62f, 1f, 0.9f));

                // Ghost of the dragged row under the cursor.
                var ghost = new Rect(_rowRects[0].x, Event.current.mousePosition.y - 24f, _rowRects[0].width, 48f);
                EditorGUI.DrawRect(ghost, new Color(0.2f, 0.2f, 0.22f, 0.85f));
                var ghostPreview = new Rect(ghost.x + 21f, ghost.y + 7f, 34f, 34f);
                CapArtGUI.DrawCap(ghostPreview, _dragCap, 0.9f);
                GUI.Label(new Rect(ghostPreview.xMax + 8f, ghost.y, ghost.width - 70f, ghost.height),
                    _dragCap.name, sRowName);
            }
        }

        int InsertIndexFromMouse(float mouseY)
        {
            for (int i = 0; i < _rowRects.Count; i++)
            {
                if (mouseY < _rowRects[i].center.y)
                    return i;
            }
            return _rowRects.Count;
        }

        void CommitPaletteReorder()
        {
            CapType cap = _dragCap;
            int to = _dragInsertIndex;
            _dragCap = null;
            _dragInsertIndex = -1;
            if (cap == null)
                return;
            int from = _palette.IndexOf(cap);
            if (from < 0)
                return;
            to = Mathf.Clamp(to, 0, _palette.Count);
            _palette.RemoveAt(from);
            if (to > from)
                to--;
            to = Mathf.Clamp(to, 0, _palette.Count);
            _palette.Insert(to, cap);
            ApplyPaletteOrder("Reorder Cap Types");
        }

        /// <summary>Writes the current _palette order into the caps' sortOrder fields.</summary>
        void ApplyPaletteOrder(string undoName)
        {
            var changed = new List<CapType>();
            for (int i = 0; i < _palette.Count; i++)
            {
                if (_palette[i].sortOrder != i)
                    changed.Add(_palette[i]);
            }
            if (changed.Count == 0)
                return;
            Undo.RecordObjects(changed.ToArray(), undoName);
            for (int i = 0; i < _palette.Count; i++)
            {
                CapType cap = _palette[i];
                if (cap.sortOrder != i)
                {
                    cap.sortOrder = i;
                    EditorUtility.SetDirty(cap);
                }
            }
            foreach (CapType cap in changed)
                AssetDatabase.SaveAssetIfDirty(cap);
        }

        void SortPaletteByColor()
        {
            var keyed = new List<(CapType cap, float hue, float value)>();
            foreach (CapType cap in _palette)
            {
                Color avg = AverageCapColor(cap);
                Color.RGBToHSV(avg, out float h, out float s, out float v);
                // Grays (low saturation) first, dark to light; then around the hue wheel.
                keyed.Add(s < 0.12f ? (cap, -1f, v) : (cap, h, v));
            }
            _palette = keyed
                .OrderBy(k => k.hue)
                .ThenBy(k => k.value)
                .ThenBy(k => k.cap.name, System.StringComparer.OrdinalIgnoreCase)
                .Select(k => k.cap)
                .ToList();
            ApplyPaletteOrder("Sort Cap Types By Color");
        }

        // ------------------------------------------------------------------ export

        void ShowExportMenu()
        {
            if (_mosaic == null)
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Marking Guide (.txt)..."), false, ExportMarkingGuide);
            menu.AddItem(new GUIContent("Pin Template 1:1 (.svg)..."), false, ExportSvgTemplate);
            menu.ShowAsContext();
        }

        void ExportMarkingGuide()
        {
            if (_mosaic == null)
                return;
            string path = EditorUtility.SaveFilePanel(
                "Save Marking Guide", "", _mosaic.name + " - marking guide", "txt");
            if (string.IsNullOrEmpty(path))
                return;
            System.IO.File.WriteAllText(path, CapArtExports.BuildMarkingGuide(_mosaic, _mosaic.name));
            EditorUtility.RevealInFinder(path);
        }

        void ExportSvgTemplate()
        {
            if (_mosaic == null)
                return;
            string path = EditorUtility.SaveFilePanel(
                "Save 1:1 Pin Template", "", _mosaic.name + " - pin template", "svg");
            if (string.IsNullOrEmpty(path))
                return;
            System.IO.File.WriteAllText(path,
                CapArtExports.BuildSvgTemplate(_mosaic, _mosaic.name, AverageCapColor));
            EditorUtility.RevealInFinder(path);
        }

        static Color AverageCapColor(CapType cap)
        {
            if (cap.texture == null)
                return cap.color;
            Texture2D baked = CapBake.GetForCap(cap);
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

        // ---------------------------------------------------------------- counts

        void UpdateCountsIfNeeded()
        {
            if (!_countsDirty)
                return;
            _countsDirty = false;
            if (_mosaic == null)
            {
                _counts = new Dictionary<CapType, int>();
                _filled = 0;
                _empty = 0;
                return;
            }
            _mosaic.EnsureSize();
            _counts = _mosaic.CountCaps(out _filled, out _empty);
        }

        void UpdateOtherCountsIfNeeded()
        {
            if (!_otherCountsDirty)
                return;
            _otherCountsDirty = false;
            _otherCounts.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:CapMosaic"))
            {
                var mosaic = AssetDatabase.LoadAssetAtPath<CapMosaic>(AssetDatabase.GUIDToAssetPath(guid));
                if (mosaic == null || mosaic == _mosaic)
                    continue;
                Dictionary<CapType, int> counts = mosaic.CountCaps(out _, out _);
                foreach (KeyValuePair<CapType, int> kv in counts)
                {
                    _otherCounts.TryGetValue(kv.Key, out int n);
                    _otherCounts[kv.Key] = n + kv.Value;
                }
            }
        }

        /// <summary>Caps of this type used in the current mosaic plus every other mosaic asset.</summary>
        int TotalUsed(CapType cap, out int usedHere)
        {
            usedHere = _counts.TryGetValue(cap, out int here) ? here : 0;
            int other = _otherCounts.TryGetValue(cap, out int o) ? o : 0;
            return usedHere + other;
        }

        // ---------------------------------------------------------------- canvas

        float GridStep()
        {
            return kBaseCell * (1f + (_mosaic != null ? _mosaic.spacing : 0f));
        }

        Vector2 GridCenter(int col, int row)
        {
            float gs = GridStep();
            if (_mosaic.layout == HexLayout.OffsetRows)
            {
                float x = (col + 0.5f + (((row & 1) == 1) ? 0.5f : 0f)) * gs;
                float y = (0.5f + row * kRowStep) * gs;
                return new Vector2(x, y);
            }
            else
            {
                float x = (0.5f + col * kRowStep) * gs;
                float y = (row + 0.5f + (((col & 1) == 1) ? 0.5f : 0f)) * gs;
                return new Vector2(x, y);
            }
        }

        Vector2 ContentSize()
        {
            float gs = GridStep();
            int w = _mosaic.width;
            int h = _mosaic.height;
            if (_mosaic.layout == HexLayout.OffsetRows)
                return new Vector2((w + (h > 1 ? 0.5f : 0f)) * gs, ((h - 1) * kRowStep + 1f) * gs);
            return new Vector2(((w - 1) * kRowStep + 1f) * gs, (h + (w > 1 ? 0.5f : 0f)) * gs);
        }

        Vector2 ToGrid(Vector2 screen, Rect canvas)
        {
            return (screen - canvas.position - _pan) / _zoom;
        }

        void FitView(Rect canvas)
        {
            Vector2 content = ContentSize();
            if (content.x <= 0f || content.y <= 0f)
                return;
            _zoom = Mathf.Clamp(Mathf.Min(canvas.width / content.x, canvas.height / content.y) * 0.94f, 0.02f, 8f);
            _pan = new Vector2(
                (canvas.width - content.x * _zoom) * 0.5f,
                (canvas.height - content.y * _zoom) * 0.5f);
        }

        bool CellAtPoint(Vector2 screen, Rect canvas, out int col, out int row)
        {
            col = -1;
            row = -1;
            if (_mosaic == null)
                return false;
            Vector2 g = ToGrid(screen, canvas);
            float gs = GridStep();
            int guessC, guessR;
            if (_mosaic.layout == HexLayout.OffsetRows)
            {
                guessR = Mathf.RoundToInt((g.y / gs - 0.5f) / kRowStep);
                guessC = Mathf.RoundToInt(g.x / gs - 1f);
            }
            else
            {
                guessC = Mathf.RoundToInt((g.x / gs - 0.5f) / kRowStep);
                guessR = Mathf.RoundToInt(g.y / gs - 1f);
            }

            float bestDistSq = float.MaxValue;
            for (int r = guessR - 2; r <= guessR + 2; r++)
            {
                for (int c = guessC - 2; c <= guessC + 2; c++)
                {
                    if (c < 0 || r < 0 || c >= _mosaic.width || r >= _mosaic.height)
                        continue;
                    float distSq = (GridCenter(c, r) - g).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        col = c;
                        row = r;
                    }
                }
            }
            // Accept clicks anywhere inside the cell's hex area (circumradius в‰€ 0.577 * step).
            float maxDist = gs * 0.58f;
            if (col >= 0 && bestDistSq <= maxDist * maxDist)
                return true;
            col = -1;
            row = -1;
            return false;
        }

        void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.13f, 0.13f, 0.135f, 1f));

            if (_mosaic == null)
            {
                _hasHoverInfo = false;
                var msg = new Rect(canvas.x, canvas.y + canvas.height * 0.5f - 34f, canvas.width, 20f);
                GUI.Label(msg, "No mosaic selected вЂ” create one to start", sCenterGray);
                var btn = new Rect(canvas.x + canvas.width * 0.5f - 75f, msg.yMax + 8f, 150f, 28f);
                if (GUI.Button(btn, "Create Mosaic..."))
                    CreateMosaicAsset();
                return;
            }

            _mosaic.EnsureSize();

            if (_fitPending && Event.current.type == EventType.Repaint && canvas.width > 40f && canvas.height > 40f)
            {
                FitView(canvas);
                _fitPending = false;
            }

            HandleCanvasInput(canvas);

            // Hover cell, computed in window space before entering the group.
            int hoverCol = -1, hoverRow = -1;
            bool hasHover = !_panning && !_painting
                && canvas.Contains(Event.current.mousePosition)
                && CellAtPoint(Event.current.mousePosition, canvas, out hoverCol, out hoverRow);
            _hasHoverInfo = hasHover;
            _hoverCol = hoverCol;
            _hoverRow = hoverRow;

            float d = kBaseCell * _zoom;
            GUI.BeginGroup(canvas);
            for (int r = 0; r < _mosaic.height; r++)
            {
                for (int c = 0; c < _mosaic.width; c++)
                {
                    Vector2 center = _pan + GridCenter(c, r) * _zoom;
                    if (center.x < -d || center.y < -d || center.x > canvas.width + d || center.y > canvas.height + d)
                        continue;
                    var rect = new Rect(center.x - d * 0.5f, center.y - d * 0.5f, d, d);
                    CapType cap = _mosaic.GetCell(c, r);
                    if (cap != null)
                        CapArtGUI.DrawCap(rect, cap);
                    else
                        CapArtGUI.DrawRing(rect, new Color(1f, 1f, 1f, 0.10f), Mathf.Max(1f, d * 0.025f));
                }
            }

            if (hasHover)
            {
                Vector2 hoverCenter = _pan + GridCenter(hoverCol, hoverRow) * _zoom;
                var hoverRect = new Rect(hoverCenter.x - d * 0.5f, hoverCenter.y - d * 0.5f, d, d);
                if (!_eraser && _selected != null && _mosaic.GetCell(hoverCol, hoverRow) != _selected)
                    CapArtGUI.DrawCap(hoverRect, _selected, 0.45f);
                Color ringColor = _eraser
                    ? new Color(1f, 0.35f, 0.3f, 0.95f)
                    : new Color(0.3f, 0.62f, 1f, 0.95f);
                CapArtGUI.DrawRing(hoverRect, ringColor, Mathf.Max(1.5f, d * 0.05f));
            }
            GUI.EndGroup();

            if (_palette.Count == 0)
            {
                var hint = new Rect(canvas.x, canvas.y + canvas.height * 0.5f - 34f, canvas.width, 20f);
                GUI.Label(hint, "No cap types yet вЂ” create one to start painting", sCenterGray);
                var btn = new Rect(canvas.x + canvas.width * 0.5f - 80f, hint.yMax + 8f, 160f, 28f);
                if (GUI.Button(btn, "New Cap Type..."))
                    CapTypeCreatorWindow.Open();
            }
        }

        void HandleCanvasInput(Rect canvas)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(kCanvasHash, FocusType.Passive, canvas);

            switch (e.GetTypeForControl(id))
            {
                case EventType.ScrollWheel:
                    if (canvas.Contains(e.mousePosition))
                    {
                        float oldZoom = _zoom;
                        _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0f ? 1f / 1.12f : 1.12f), 0.02f, 8f);
                        Vector2 local = e.mousePosition - canvas.position;
                        _pan = local - (local - _pan) * (_zoom / oldZoom);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDown:
                    if (!canvas.Contains(e.mousePosition))
                        break;
                    GUIUtility.hotControl = id;
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        _panning = true;
                    }
                    else if (e.button == 0 || e.button == 1)
                    {
                        _painting = true;
                        _paintButton = e.button;
                        Undo.IncrementCurrentGroup();
                        _strokeUndoGroup = Undo.GetCurrentGroup();
                        PaintAt(e.mousePosition, canvas, e.button == 1 || _eraser);
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id)
                        break;
                    if (_panning)
                        _pan += e.delta;
                    else if (_painting)
                        PaintAt(e.mousePosition, canvas, _paintButton == 1 || _eraser);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id)
                        break;
                    GUIUtility.hotControl = 0;
                    if (_painting)
                    {
                        Undo.CollapseUndoOperations(_strokeUndoGroup);
                        if (_mosaic != null)
                            AssetDatabase.SaveAssetIfDirty(_mosaic);
                    }
                    _painting = false;
                    _panning = false;
                    e.Use();
                    break;

                case EventType.ContextClick:
                    if (canvas.Contains(e.mousePosition))
                        e.Use(); // RMB is the eraser вЂ” suppress the context menu
                    break;
            }
        }

        void PaintAt(Vector2 screen, Rect canvas, bool erase)
        {
            if (_mosaic == null || !canvas.Contains(screen))
                return;
            if (!erase && _selected == null)
                return;
            if (!CellAtPoint(screen, canvas, out int col, out int row))
                return;
            CapType target = erase ? null : _selected;
            if (_mosaic.GetCell(col, row) == target)
                return;
            Undo.RecordObject(_mosaic, erase ? "Erase Cap" : "Place Cap");
            _mosaic.SetCell(col, row, target);
            EditorUtility.SetDirty(_mosaic);
            _countsDirty = true;
            Repaint();
        }

        // ---------------------------------------------------------------- status bar

        void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (_mosaic != null)
            {
                Vector2 mm = _mosaic.ArtworkSizeMm();
                GUILayout.Label(
                    string.Format("Grid {0}Г—{1}   вЂў   Caps placed: {2}   вЂў   Empty: {3}   вЂў   Artwork {4:0.0} Г— {5:0.0} cm (incl. gap)",
                        _mosaic.width, _mosaic.height, _filled, _empty, mm.x / 10f, mm.y / 10f),
                    EditorStyles.miniLabel);
                if (_hasHoverInfo)
                {
                    Vector2 center = _mosaic.CellCenterFromBottomLeftMm(_hoverCol, _hoverRow);
                    GUILayout.Label(
                        string.Format("   вЂў   Center (col {0}, row {1}):  X {2:0.0}  Y {3:0.0} mm from bottom-left",
                            _hoverCol + 1, _mosaic.height - _hoverRow, center.x, center.y),
                        EditorStyles.miniBoldLabel);
                }
            }
            GUILayout.FlexibleSpace();
            if (_overStockCount > 0)
            {
                GUILayout.Label("вљ  " + _overStockCount
                    + (_overStockCount == 1 ? " cap type over stock" : " cap types over stock"), sMiniOver);
                GUILayout.Space(12f);
            }
            if (_atLimitCount > 0)
            {
                GUILayout.Label("в—Џ " + _atLimitCount
                    + (_atLimitCount == 1 ? " cap type at limit" : " cap types at limit"), sMiniWarn);
                GUILayout.Space(12f);
            }
            string brush;
            GUIStyle brushStyle = EditorStyles.miniBoldLabel;
            if (_eraser)
            {
                brush = "Brush: Eraser";
            }
            else if (_selected == null)
            {
                brush = "в†ђ Pick a cap type";
            }
            else
            {
                brush = "Brush: " + _selected.name;
                int total = TotalUsed(_selected, out _);
                if (total > _selected.amount)
                {
                    brush += "  (over stock)";
                    brushStyle = sMiniOver;
                }
                else if (_selected.amount > 0 && total == _selected.amount)
                {
                    brush += "  (at limit)";
                    brushStyle = sMiniWarn;
                }
            }
            GUILayout.Label(brush, brushStyle);
            GUILayout.Space(12f);
            GUILayout.Label("LMB paint  вЂў  RMB erase  вЂў  Wheel zoom  вЂў  MMB / Alt+drag pan", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
