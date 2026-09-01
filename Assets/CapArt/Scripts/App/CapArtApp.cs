using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace CapArt
{
    /// <summary>
    /// The standalone Cap Art app: the mosaic painter, cap palette and cap
    /// editor as a runtime IMGUI application. Works in desktop builds and
    /// WebGL. All data auto-saves locally and can be exported/imported as a
    /// single project file.
    /// </summary>
    public class CapArtApp : MonoBehaviour
    {
        const float kBaseCell = 44f;
        const float kRowStep = 0.8660254f;
        const int kMaxGridSide = 250;
        const float kToolbarH = 30f;
        const float kStatusH = 24f;
        const float kPaletteW = 300f;
        static readonly int kCanvasHash = "CapArtAppCanvas".GetHashCode();
        static readonly int kPaletteDragHash = "CapArtAppPaletteDrag".GetHashCode();

        enum Overlay { None, CapEditor, Mosaics, Export, Confirm }
        enum WebPick { None, Photo, Project }

        CapArtProject _project;
        int _mosaicIndex;
        CapType _selected;
        bool _eraser;

        float _zoom = 1f;
        Vector2 _pan;
        bool _fitPending = true;
        bool _painting;
        bool _panning;
        int _paintButton;
        bool _hasHoverInfo;
        int _hoverCol, _hoverRow;

        // Counts.
        bool _countsDirty = true;
        readonly Dictionary<CapType, int> _hereCounts = new Dictionary<CapType, int>();
        readonly Dictionary<CapType, int> _totalCounts = new Dictionary<CapType, int>();
        int _filled, _empty, _overStockCount, _atLimitCount;

        // Undo (painting only).
        class Stroke
        {
            public CapMosaic mosaic;
            public readonly List<int> indices = new List<int>();
            public readonly List<CapType> before = new List<CapType>();
            public readonly List<CapType> after = new List<CapType>();
        }
        readonly List<Stroke> _undo = new List<Stroke>();
        readonly List<Stroke> _redo = new List<Stroke>();
        Stroke _activeStroke;

        // Palette drag-reorder.
        readonly List<Rect> _rowRects = new List<Rect>();
        int _paletteDragControlId;
        CapType _dragCap;
        int _dragInsertIndex = -1;
        Vector2 _paletteScroll;

        // Overlays.
        Overlay _overlay = Overlay.None;
        readonly SimpleFileBrowser _browser = new SimpleFileBrowser();
        string _confirmMessage;
        Action _confirmAction;
        Overlay _confirmReturnTo = Overlay.None;

        // Cap editor state.
        CapType _editCap;
        bool _editIsNew;
        string _editSnapshotName;
        Texture2D _editSnapshotTexture;
        Color _editSnapshotColor;
        int _editSnapshotAmount;
        float _editSnapshotZoom;
        Vector2 _editSnapshotCenter;
        string _editPendingB64;
        bool _editPhotoChanged;
        Vector2 _capEditorScroll;

        // Saving / misc UI.
        bool _dirty;
        bool _statusSaving; // _dirty latched at Layout, for a stable status-bar control count
        float _lastChangeTime;
        string _toast;
        float _toastUntil;
        WebPick _webPick = WebPick.None;
        readonly Dictionary<string, string> _fieldEdits = new Dictionary<string, string>();

        GUIStyle _sMini, _sMiniBold, _sMiniWarn, _sMiniOver, _sRowInfo, _sRowInfoOver, _sRowInfoLimit, _sCenterGray, _sTitle;
        bool _stylesReady;

        CapMosaic Cur
        {
            get { return (_project != null && _project.mosaics.Count > 0) ? _project.mosaics[_mosaicIndex] : null; }
        }

        string CurName
        {
            get { return (_project != null && _project.mosaicNames.Count > 0) ? _project.mosaicNames[_mosaicIndex] : "Mosaic"; }
        }

        // ------------------------------------------------------------ lifecycle

        void Awake()
        {
            gameObject.name = "CapArtApp";
            _project = new CapArtProject();
            if (_project.LoadFromDiskOrCreateDefault())
                MarkDirty(); // persist injected samples / first-run defaults
            _mosaicIndex = 0;
        }

        void Update()
        {
            if (_dirty && Time.realtimeSinceStartup - _lastChangeTime > 1.25f)
                SaveNow();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _dirty)
                SaveNow();
        }

        void OnApplicationQuit()
        {
            if (_dirty)
                SaveNow();
        }

        void MarkDirty()
        {
            _dirty = true;
            _lastChangeTime = Time.realtimeSinceStartup;
            _countsDirty = true;
        }

        void SaveNow()
        {
            _project.SaveToDisk();
            SyncFileSystem();
            _dirty = false;
        }

        void SetToast(string message, float seconds = 3.5f)
        {
            _toast = message;
            _toastUntil = Time.realtimeSinceStartup + seconds;
        }

        // ------------------------------------------------------------ platform

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CapArtDownload(string name, string content, string mime);
        [DllImport("__Internal")] static extern void CapArtPickFile(string accept, string objName, string method);
        [DllImport("__Internal")] static extern void CapArtSyncFS();
#endif

        void SyncFileSystem()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CapArtSyncFS();
#endif
        }

        void SaveTextFile(string fileName, string content, string mime)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            CapArtDownload(fileName, content, mime);
            SetToast("Downloaded " + fileName);
#else
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "Exports");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, fileName);
                File.WriteAllText(path, content);
                SetToast("Saved to " + path, 6f);
                Application.OpenURL("file:///" + dir.Replace("\\", "/"));
            }
            catch (Exception e)
            {
                SetToast("Export failed: " + e.Message, 6f);
            }
#endif
        }

        void PickPhotoFile()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _webPick = WebPick.Photo;
            CapArtPickFile(".png,.jpg,.jpeg", gameObject.name, "OnWebFilePicked");
#else
            _browser.Open("Choose a cap photo (PNG / JPG)", new[] { ".png", ".jpg", ".jpeg" }, path =>
            {
                try { HandlePhotoBytes(File.ReadAllBytes(path)); }
                catch (Exception e) { SetToast("Could not read file: " + e.Message, 5f); }
            });
#endif
        }

        void PickProjectFile()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _webPick = WebPick.Project;
            CapArtPickFile(".json", gameObject.name, "OnWebFilePicked");
#else
            _browser.Open("Choose a Cap Art project file (.json)", new[] { ".json" }, path =>
            {
                try { HandleProjectJson(File.ReadAllText(path)); }
                catch (Exception e) { SetToast("Could not read file: " + e.Message, 5f); }
            });
#endif
        }

        /// <summary>Called from JavaScript on WebGL with "filename|base64", or "" on cancel.</summary>
        public void OnWebFilePicked(string payload)
        {
            WebPick kind = _webPick;
            _webPick = WebPick.None;
            if (string.IsNullOrEmpty(payload))
                return;
            int sep = payload.IndexOf('|');
            if (sep < 0)
                return;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(payload.Substring(sep + 1)); }
            catch (Exception) { return; }
            if (kind == WebPick.Photo)
                HandlePhotoBytes(bytes);
            else if (kind == WebPick.Project)
                HandleProjectJson(System.Text.Encoding.UTF8.GetString(bytes));
        }

        void HandlePhotoBytes(byte[] bytes)
        {
            if (_editCap == null)
                return;
            if (!CapArtProject.TryDecodePhoto(bytes, out Texture2D texture, out string b64))
            {
                SetToast("That file is not a readable PNG/JPG image.", 5f);
                return;
            }
            if (_editCap.texture != null && _editCap.texture != _editSnapshotTexture)
                Destroy(_editCap.texture);
            _editCap.texture = texture;
            _editPendingB64 = b64;
            _editPhotoChanged = true;
            _editCap.cropZoom = 1f;
            _editCap.cropCenter = new Vector2(0.5f, 0.5f);
        }

        void HandleProjectJson(string json)
        {
            var probe = new CapArtProject();
            if (!probe.FromJson(json))
            {
                probe.Clear();
                SetToast("That file is not a valid Cap Art project.", 5f);
                return;
            }
            probe.Clear();
            _project.FromJson(json);
            AfterProjectReplaced("Project imported.");
        }

        void AfterProjectReplaced(string toastMessage)
        {
            if (_project.mosaics.Count == 0)
                _project.NewMosaic("My Mosaic");
            _mosaicIndex = 0;
            _selected = null;
            _eraser = false;
            _undo.Clear();
            _redo.Clear();
            _fieldEdits.Clear();
            _fitPending = true;
            MarkDirty();
            SetToast(toastMessage);
        }

        // ------------------------------------------------------------ counts

        void UpdateCountsIfNeeded()
        {
            if (!_countsDirty)
                return;
            _countsDirty = false;
            _hereCounts.Clear();
            _totalCounts.Clear();
            _filled = 0;
            _empty = 0;
            CapMosaic cur = Cur;
            foreach (CapMosaic m in _project.mosaics)
            {
                if (m == null)
                    continue;
                m.EnsureSize();
                for (int i = 0; i < m.cells.Length; i++)
                {
                    CapType cap = m.cells[i];
                    if (m == cur)
                    {
                        if (cap == null) _empty++;
                        else _filled++;
                    }
                    if (cap == null)
                        continue;
                    _totalCounts.TryGetValue(cap, out int t);
                    _totalCounts[cap] = t + 1;
                    if (m == cur)
                    {
                        _hereCounts.TryGetValue(cap, out int hc);
                        _hereCounts[cap] = hc + 1;
                    }
                }
            }
            _overStockCount = 0;
            _atLimitCount = 0;
            foreach (CapType cap in _project.caps)
            {
                int total = _totalCounts.TryGetValue(cap, out int t) ? t : 0;
                if (total > cap.amount) _overStockCount++;
                else if (cap.amount > 0 && total == cap.amount) _atLimitCount++;
            }
        }

        int TotalUsed(CapType cap, out int usedHere)
        {
            usedHere = _hereCounts.TryGetValue(cap, out int h) ? h : 0;
            return _totalCounts.TryGetValue(cap, out int t) ? t : 0;
        }

        // ------------------------------------------------------------ undo

        void ApplyStroke(Stroke stroke, bool undo)
        {
            for (int i = stroke.indices.Count - 1; i >= 0; i--)
                stroke.mosaic.cells[stroke.indices[i]] = undo ? stroke.before[i] : stroke.after[i];
            MarkDirty();
        }

        void DoUndo()
        {
            if (_undo.Count == 0)
                return;
            Stroke s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            ApplyStroke(s, true);
            _redo.Add(s);
        }

        void DoRedo()
        {
            if (_redo.Count == 0)
                return;
            Stroke s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            ApplyStroke(s, false);
            _undo.Add(s);
        }

        // ------------------------------------------------------------ GUI root

        void InitStyles()
        {
            if (_stylesReady)
                return;
            _stylesReady = true;
            _sMini = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            _sMiniBold = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
            _sMiniWarn = new GUIStyle(_sMiniBold);
            _sMiniWarn.normal.textColor = CapArtDraw.Ui(new Color(1f, 0.62f, 0.25f, 1f));
            _sMiniOver = new GUIStyle(_sMiniBold);
            _sMiniOver.normal.textColor = CapArtDraw.Ui(new Color(1f, 0.45f, 0.4f, 1f));
            _sRowInfo = new GUIStyle(_sMini) { alignment = TextAnchor.MiddleLeft };
            _sRowInfoOver = new GUIStyle(_sMiniBold) { alignment = TextAnchor.MiddleLeft };
            _sRowInfoOver.normal.textColor = CapArtDraw.Ui(new Color(1f, 0.5f, 0.45f, 1f));
            _sRowInfoLimit = new GUIStyle(_sMiniBold) { alignment = TextAnchor.MiddleLeft };
            _sRowInfoLimit.normal.textColor = CapArtDraw.Ui(new Color(1f, 0.72f, 0.3f, 1f));
            _sCenterGray = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _sCenterGray.normal.textColor = CapArtDraw.Ui(new Color(0.62f, 0.62f, 0.62f, 1f));
            _sTitle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
        }

        void OnGUI()
        {
            InitStyles();
            UpdateCountsIfNeeded();
            if (Event.current.type == EventType.Layout)
                _statusSaving = _dirty;
            HandleGlobalKeys();

            float w = Screen.width;
            float h = Screen.height;
            var toolbarRect = new Rect(0f, 0f, w, kToolbarH);
            var statusRect = new Rect(0f, h - kStatusH, w, kStatusH);
            var paletteRect = new Rect(0f, kToolbarH, kPaletteW, h - kToolbarH - kStatusH);
            var canvasRect = new Rect(kPaletteW, kToolbarH, w - kPaletteW, h - kToolbarH - kStatusH);

            bool modal = _overlay != Overlay.None || _browser.IsOpen;
            GUI.enabled = !modal;
            DrawToolbar(toolbarRect);
            DrawPalette(paletteRect);
            DrawCanvas(canvasRect, !modal);
            DrawStatusBar(statusRect);
            GUI.enabled = true;

            if (modal)
                CapArtDraw.DrawRect(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.55f));

            bool confirmOrBrowser = _browser.IsOpen || _overlay == Overlay.Confirm;
            if (_overlay == Overlay.CapEditor)
            {
                GUI.enabled = !confirmOrBrowser;
                DrawCapEditor(CenterRect(460f, Mathf.Min(680f, h - 40f)));
                GUI.enabled = true;
            }
            else if (_overlay == Overlay.Mosaics)
            {
                GUI.enabled = !confirmOrBrowser;
                DrawMosaicManager(CenterRect(420f, Mathf.Min(520f, h - 40f)));
                GUI.enabled = true;
            }
            else if (_overlay == Overlay.Export)
            {
                GUI.enabled = !confirmOrBrowser;
                DrawExportMenu(CenterRect(420f, 320f));
                GUI.enabled = true;
            }

            if (_overlay == Overlay.Confirm)
                DrawConfirm(CenterRect(420f, 150f));

            if (_browser.IsOpen)
                _browser.Draw(CenterRect(Mathf.Min(640f, w - 40f), Mathf.Min(520f, h - 40f)));

            DrawToast();
        }

        Rect CenterRect(float width, float height)
        {
            return new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        }

        void HandleGlobalKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || _overlay != Overlay.None || _browser.IsOpen)
                return;
            if (GUIUtility.keyboardControl != 0)
                return; // typing in a text field
            bool ctrl = e.control || e.command;
            if (!ctrl)
                return;
            if (e.keyCode == KeyCode.Z && !e.shift) { DoUndo(); e.Use(); }
            else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift)) { DoRedo(); e.Use(); }
        }

        // ------------------------------------------------------------ number fields

        int DelayedIntField(string key, int value, float width)
        {
            return Mathf.RoundToInt(DelayedFloatField(key, value, width));
        }

        float DelayedFloatField(string key, float value, float width)
        {
            string shown = _fieldEdits.TryGetValue(key, out string editing)
                ? editing
                : value.ToString("0.##", CultureInfo.InvariantCulture);
            Event e = Event.current;
            bool enterPressed = e.type == EventType.KeyDown
                && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == key;
            GUI.SetNextControlName(key);
            string next = GUILayout.TextField(shown, GUILayout.Width(width));
            bool focused = GUI.GetNameOfFocusedControl() == key;
            if (focused && !enterPressed)
            {
                _fieldEdits[key] = next;
                return value;
            }
            if (!_fieldEdits.ContainsKey(key))
                return value;
            string committed = enterPressed ? next : _fieldEdits[key];
            _fieldEdits.Remove(key);
            if (enterPressed)
                GUI.FocusControl(null);
            committed = committed.Replace(',', '.');
            if (float.TryParse(committed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return parsed;
            return value;
        }

        // ------------------------------------------------------------ toolbar

        void DrawToolbar(Rect rect)
        {
            CapArtDraw.DrawRect(rect, new Color(0.17f, 0.17f, 0.18f, 1f));
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, rect.height - 8f));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(CurName + "  ▾", GUILayout.Width(150f)))
                _overlay = Overlay.Mosaics;

            CapMosaic m = Cur;
            if (m != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label("Size", _sMini);
                int newW = DelayedIntField("gridW", m.width, 40f);
                GUILayout.Label("×", GUILayout.Width(12f));
                int newH = DelayedIntField("gridH", m.height, 40f);
                if (newW != m.width || newH != m.height)
                {
                    m.Resize(Mathf.Clamp(newW, 1, kMaxGridSide), Mathf.Clamp(newH, 1, kMaxGridSide));
                    _undo.Clear();
                    _redo.Clear();
                    _fitPending = true;
                    MarkDirty();
                }

                GUILayout.Space(12f);
                if (GUILayout.Button(m.layout == HexLayout.OffsetRows ? "Offset Rows" : "Offset Columns", GUILayout.Width(110f)))
                {
                    m.layout = m.layout == HexLayout.OffsetRows ? HexLayout.OffsetColumns : HexLayout.OffsetRows;
                    _fitPending = true;
                    MarkDirty();
                }

                GUILayout.Space(12f);
                GUILayout.Label("Gap", _sMini);
                float gapMm = m.spacing * m.capDiameterMm;
                float newGap = DelayedFloatField("gapMm", gapMm, 40f);
                GUILayout.Label("mm", _sMini);
                GUILayout.Space(8f);
                GUILayout.Label("Cap Ø", _sMini);
                float newD = DelayedFloatField("capMm", m.capDiameterMm, 40f);
                GUILayout.Label("mm", _sMini);
                if (!Mathf.Approximately(newGap, gapMm) || !Mathf.Approximately(newD, m.capDiameterMm))
                {
                    m.capDiameterMm = Mathf.Max(1f, newD);
                    m.spacing = Mathf.Clamp(Mathf.Max(0f, newGap) / m.capDiameterMm, 0f, 1f);
                    MarkDirty();
                }

                GUILayout.Space(12f);
                if (GUILayout.Button("Export / Import...", GUILayout.Width(120f)))
                    _overlay = Overlay.Export;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fit View", GUILayout.Width(70f)))
                _fitPending = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ palette

        void DrawPalette(Rect rect)
        {
            CapArtDraw.DrawRect(rect, new Color(0.19f, 0.19f, 0.20f, 1f));
            GUILayout.BeginArea(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cap Types", _sTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Sort: color", GUILayout.Width(80f)))
                SortPaletteByColor();
            if (GUILayout.Button("New Cap...", GUILayout.Width(80f)))
                OpenCapEditor(null);
            GUILayout.EndHorizontal();

            _rowRects.Clear();
            _paletteDragControlId = GUIUtility.GetControlID(kPaletteDragHash, FocusType.Passive);

            _paletteScroll = GUILayout.BeginScrollView(_paletteScroll);
            DrawPaletteRow(null);
            foreach (CapType cap in _project.caps.ToArray())
                DrawPaletteRow(cap);
            HandlePaletteDrag();
            GUILayout.EndScrollView();

            // Over-stock summary.
            var overNames = new List<string>();
            foreach (CapType cap in _project.caps)
            {
                int total = TotalUsed(cap, out _);
                if (total > cap.amount && overNames.Count < 5)
                    overNames.Add(cap.name + "   " + total + " used / " + cap.amount + " owned");
            }
            if (_overStockCount > 0)
            {
                string message = "Not enough caps (all designs together):\n" + string.Join("\n", overNames);
                if (_overStockCount > overNames.Count)
                    message += "\n…and " + (_overStockCount - overNames.Count) + " more";
                GUILayout.Label(message, _sMiniOver);
            }
            GUILayout.Label("Caps placed: " + _filled + "      Empty tiles: " + _empty, _sMini);
            GUILayout.EndArea();
        }

        void DrawPaletteRow(CapType cap)
        {
            bool isEraser = cap == null;
            Rect row = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            row.y += 1f;
            row.height -= 2f;

            if (!isEraser)
                _rowRects.Add(row);

            bool selected = isEraser ? _eraser : (!_eraser && _selected == cap);
            int usedHere = 0, totalUsed = 0;
            bool overStock = false, atLimit = false;
            if (!isEraser)
            {
                totalUsed = TotalUsed(cap, out usedHere);
                overStock = totalUsed > cap.amount;
                atLimit = !overStock && cap.amount > 0 && totalUsed == cap.amount;
            }

            if (selected)
                CapArtDraw.DrawRect(row, new Color(0.24f, 0.49f, 0.91f, 0.35f));
            else if (row.Contains(Event.current.mousePosition))
                CapArtDraw.DrawRect(row, new Color(1f, 1f, 1f, 0.05f));
            if (overStock)
                CapArtDraw.DrawRect(row, new Color(1f, 0.3f, 0.25f, 0.08f));
            else if (atLimit)
                CapArtDraw.DrawRect(row, new Color(1f, 0.6f, 0.15f, 0.07f));

            Event e = Event.current;

            if (isEraser)
            {
                var preview = new Rect(row.x + 21f, row.y + (row.height - 34f) * 0.5f, 34f, 34f);
                CapArtDraw.DrawRing(preview, new Color(0.7f, 0.7f, 0.7f, 0.9f), 2f);
                GUI.Label(preview, "×", _sCenterGray);
                GUI.Label(new Rect(preview.xMax + 8f, row.y, row.xMax - preview.xMax - 16f, row.height),
                    "Eraser  (or use RMB)", GUI.skin.label);
            }
            else
            {
                var handle = new Rect(row.x + 2f, row.y, 16f, row.height);
                GUI.Label(handle, "≡", _sCenterGray);
                if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition) && GUI.enabled)
                {
                    _dragCap = cap;
                    _dragInsertIndex = _rowRects.Count - 1;
                    GUIUtility.hotControl = _paletteDragControlId;
                    e.Use();
                }

                var preview = new Rect(row.x + 21f, row.y + (row.height - 34f) * 0.5f, 34f, 34f);
                CapArtDraw.DrawCapRaw(preview, CapCropBaker.GetForCap(cap), cap.color);

                var editBtn = new Rect(row.xMax - 26f, row.y + 4f, 24f, 18f);
                if (GUI.Button(editBtn, "✎"))
                    OpenCapEditor(cap);

                var ownField = new Rect(row.xMax - 76f, row.y + 4f, 46f, 18f);
                string ownText = GUI.TextField(ownField, cap.amount.ToString());
                if (int.TryParse(ownText, out int ownParsed) && ownParsed != cap.amount)
                {
                    cap.amount = Mathf.Max(0, ownParsed);
                    MarkDirty();
                }
                GUI.Label(new Rect(ownField.x - 30f, row.y + 6f, 28f, 14f), "own", _sMini);

                float nameX = preview.xMax + 8f;
                GUI.Label(new Rect(nameX, row.y + 4f, Mathf.Max(30f, ownField.x - 34f - nameX), 18f),
                    cap.name, GUI.skin.label);

                var info = new Rect(nameX, row.y + 26f, row.xMax - 8f - nameX, 18f);
                string text = usedHere + " here   ·   " + totalUsed + " / " + cap.amount + " all designs";
                GUIStyle infoStyle = _sRowInfo;
                if (overStock) { text = "⚠ " + text; infoStyle = _sRowInfoOver; }
                else if (atLimit) { text = "● " + text; infoStyle = _sRowInfoLimit; }
                GUI.Label(info, text, infoStyle);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition) && GUI.enabled)
            {
                if (isEraser) { _eraser = true; _selected = null; }
                else { _selected = cap; _eraser = false; }
                e.Use();
            }
        }

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
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != _paletteDragControlId)
                        break;
                    GUIUtility.hotControl = 0;
                    CommitPaletteReorder();
                    e.Use();
                    break;
            }
            if (Event.current.type == EventType.Repaint && _dragInsertIndex >= 0 && _rowRects.Count > 0)
            {
                float y = _dragInsertIndex < _rowRects.Count
                    ? _rowRects[_dragInsertIndex].yMin
                    : _rowRects[_rowRects.Count - 1].yMax;
                CapArtDraw.DrawRect(new Rect(_rowRects[0].x, y - 1.5f, _rowRects[0].width, 3f),
                    new Color(0.3f, 0.62f, 1f, 0.9f));
                var ghost = new Rect(_rowRects[0].x, Event.current.mousePosition.y - 24f, _rowRects[0].width, 48f);
                CapArtDraw.DrawRect(ghost, new Color(0.2f, 0.2f, 0.22f, 0.85f));
                var gp = new Rect(ghost.x + 21f, ghost.y + 7f, 34f, 34f);
                CapArtDraw.DrawCapRaw(gp, CapCropBaker.GetForCap(_dragCap), _dragCap.color, 0.9f);
                GUI.Label(new Rect(gp.xMax + 8f, ghost.y + 14f, ghost.width - 70f, 20f), _dragCap.name);
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
            int from = _project.caps.IndexOf(cap);
            if (from < 0)
                return;
            to = Mathf.Clamp(to, 0, _project.caps.Count);
            _project.caps.RemoveAt(from);
            if (to > from)
                to--;
            _project.caps.Insert(Mathf.Clamp(to, 0, _project.caps.Count), cap);
            MarkDirty();
        }

        void SortPaletteByColor()
        {
            var keyed = new List<(CapType cap, float hue, float value)>();
            foreach (CapType cap in _project.caps)
            {
                Color avg = CapCropBaker.AverageColor(cap);
                Color.RGBToHSV(avg, out float hh, out float s, out float v);
                keyed.Add(s < 0.12f ? (cap, -1f, v) : (cap, hh, v));
            }
            var sorted = keyed.OrderBy(k => k.hue).ThenBy(k => k.value)
                .ThenBy(k => k.cap.name, StringComparer.OrdinalIgnoreCase)
                .Select(k => k.cap).ToList();
            _project.caps.Clear();
            _project.caps.AddRange(sorted);
            MarkDirty();
        }

        // ------------------------------------------------------------ canvas

        float GridStep()
        {
            return kBaseCell * (1f + (Cur != null ? Cur.spacing : 0f));
        }

        Vector2 GridCenter(int col, int row)
        {
            float gs = GridStep();
            if (Cur.layout == HexLayout.OffsetRows)
                return new Vector2((col + 0.5f + (((row & 1) == 1) ? 0.5f : 0f)) * gs, (0.5f + row * kRowStep) * gs);
            return new Vector2((0.5f + col * kRowStep) * gs, (row + 0.5f + (((col & 1) == 1) ? 0.5f : 0f)) * gs);
        }

        Vector2 ContentSize()
        {
            float gs = GridStep();
            int w = Cur.width, h = Cur.height;
            if (Cur.layout == HexLayout.OffsetRows)
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
            _pan = new Vector2((canvas.width - content.x * _zoom) * 0.5f, (canvas.height - content.y * _zoom) * 0.5f);
        }

        bool CellAtPoint(Vector2 screen, Rect canvas, out int col, out int row)
        {
            col = -1;
            row = -1;
            if (Cur == null)
                return false;
            Vector2 g = ToGrid(screen, canvas);
            float gs = GridStep();
            int guessC, guessR;
            if (Cur.layout == HexLayout.OffsetRows)
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
                    if (c < 0 || r < 0 || c >= Cur.width || r >= Cur.height)
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
            float maxDist = gs * 0.58f;
            if (col >= 0 && bestDistSq <= maxDist * maxDist)
                return true;
            col = -1;
            row = -1;
            return false;
        }

        void DrawCanvas(Rect canvas, bool interactive)
        {
            CapArtDraw.DrawRect(canvas, new Color(0.13f, 0.13f, 0.135f, 1f));
            if (Cur == null)
            {
                _hasHoverInfo = false;
                return;
            }
            Cur.EnsureSize();

            if (_fitPending && Event.current.type == EventType.Repaint && canvas.width > 40f && canvas.height > 40f)
            {
                FitView(canvas);
                _fitPending = false;
            }

            if (interactive)
                HandleCanvasInput(canvas);

            int hoverCol = -1, hoverRow = -1;
            bool hasHover = interactive && !_panning && !_painting
                && canvas.Contains(Event.current.mousePosition)
                && CellAtPoint(Event.current.mousePosition, canvas, out hoverCol, out hoverRow);
            // The status bar adds a label while a tile is hovered, and IMGUI
            // requires identical control counts in the Layout and Repaint
            // passes of a frame — so the fields it reads are latched during
            // Layout only. Drawing below uses the fresh local values.
            if (Event.current.type == EventType.Layout)
            {
                _hasHoverInfo = hasHover;
                _hoverCol = hoverCol;
                _hoverRow = hoverRow;
            }

            float d = kBaseCell * _zoom;
            GUI.BeginGroup(canvas);
            for (int r = 0; r < Cur.height; r++)
            {
                for (int c = 0; c < Cur.width; c++)
                {
                    Vector2 center = _pan + GridCenter(c, r) * _zoom;
                    if (center.x < -d || center.y < -d || center.x > canvas.width + d || center.y > canvas.height + d)
                        continue;
                    var cellRect = new Rect(center.x - d * 0.5f, center.y - d * 0.5f, d, d);
                    CapType cap = Cur.GetCell(c, r);
                    if (cap != null)
                        CapArtDraw.DrawCapRaw(cellRect, CapCropBaker.GetForCap(cap), cap.color);
                    else
                        CapArtDraw.DrawRing(cellRect, new Color(1f, 1f, 1f, 0.10f), Mathf.Max(1f, d * 0.025f));
                }
            }
            if (hasHover)
            {
                Vector2 hoverCenter = _pan + GridCenter(hoverCol, hoverRow) * _zoom;
                var hoverRect = new Rect(hoverCenter.x - d * 0.5f, hoverCenter.y - d * 0.5f, d, d);
                if (!_eraser && _selected != null && Cur.GetCell(hoverCol, hoverRow) != _selected)
                    CapArtDraw.DrawCapRaw(hoverRect, CapCropBaker.GetForCap(_selected), _selected.color, 0.45f);
                Color ringColor = _eraser ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(0.3f, 0.62f, 1f, 0.95f);
                CapArtDraw.DrawRing(hoverRect, ringColor, Mathf.Max(1.5f, d * 0.05f));
            }
            GUI.EndGroup();

            if (_project.caps.Count == 0)
            {
                var hint = new Rect(canvas.x, canvas.y + canvas.height * 0.5f - 34f, canvas.width, 20f);
                GUI.Label(hint, "No cap types yet — create one to start painting", _sCenterGray);
                var btn = new Rect(canvas.x + canvas.width * 0.5f - 80f, hint.yMax + 8f, 160f, 26f);
                if (GUI.Button(btn, "New Cap Type..."))
                    OpenCapEditor(null);
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
                    }
                    break;

                case EventType.MouseDown:
                    if (!canvas.Contains(e.mousePosition))
                        break;
                    GUIUtility.hotControl = id;
                    GUI.FocusControl(null);
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        _panning = true;
                    }
                    else if (e.button == 0 || e.button == 1)
                    {
                        _painting = true;
                        _paintButton = e.button;
                        _activeStroke = new Stroke { mosaic = Cur };
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
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id)
                        break;
                    GUIUtility.hotControl = 0;
                    if (_painting && _activeStroke != null && _activeStroke.indices.Count > 0)
                    {
                        _undo.Add(_activeStroke);
                        if (_undo.Count > 64)
                            _undo.RemoveAt(0);
                        _redo.Clear();
                    }
                    _activeStroke = null;
                    _painting = false;
                    _panning = false;
                    e.Use();
                    break;

                case EventType.ContextClick:
                    if (canvas.Contains(e.mousePosition))
                        e.Use();
                    break;
            }
        }

        void PaintAt(Vector2 screen, Rect canvas, bool erase)
        {
            if (Cur == null || !canvas.Contains(screen))
                return;
            if (!erase && _selected == null)
                return;
            if (!CellAtPoint(screen, canvas, out int col, out int row))
                return;
            CapType target = erase ? null : _selected;
            CapType current = Cur.GetCell(col, row);
            if (current == target)
                return;
            Cur.SetCell(col, row, target);
            if (_activeStroke != null)
            {
                _activeStroke.indices.Add(row * Cur.width + col);
                _activeStroke.before.Add(current);
                _activeStroke.after.Add(target);
            }
            MarkDirty();
        }

        // ------------------------------------------------------------ status bar

        void DrawStatusBar(Rect rect)
        {
            CapArtDraw.DrawRect(rect, new Color(0.17f, 0.17f, 0.18f, 1f));
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 16f, rect.height - 6f));
            GUILayout.BeginHorizontal();
            CapMosaic m = Cur;
            if (m != null)
            {
                Vector2 mm = m.ArtworkSizeMm();
                GUILayout.Label(string.Format("Grid {0}×{1}   •   Caps: {2}   •   Empty: {3}   •   Artwork {4:0.0} × {5:0.0} cm",
                    m.width, m.height, _filled, _empty, mm.x / 10f, mm.y / 10f), _sMini);
                if (_hasHoverInfo)
                {
                    Vector2 center = m.CellCenterFromBottomLeftMm(_hoverCol, _hoverRow);
                    GUILayout.Label(string.Format("   •   Center (col {0}, row {1}):  X {2:0.0}  Y {3:0.0} mm from bottom-left",
                        _hoverCol + 1, m.height - _hoverRow, center.x, center.y), _sMiniBold);
                }
            }
            GUILayout.FlexibleSpace();
            if (_overStockCount > 0)
            {
                GUILayout.Label("⚠ " + _overStockCount + " over stock", _sMiniOver);
                GUILayout.Space(10f);
            }
            if (_atLimitCount > 0)
            {
                GUILayout.Label("● " + _atLimitCount + " at limit", _sMiniWarn);
                GUILayout.Space(10f);
            }
            string brush;
            GUIStyle brushStyle = _sMiniBold;
            if (_eraser) brush = "Brush: Eraser";
            else if (_selected == null) brush = "← Pick a cap type";
            else
            {
                brush = "Brush: " + _selected.name;
                int total = TotalUsed(_selected, out _);
                if (total > _selected.amount) { brush += "  (over stock)"; brushStyle = _sMiniOver; }
                else if (_selected.amount > 0 && total == _selected.amount) { brush += "  (at limit)"; brushStyle = _sMiniWarn; }
            }
            GUILayout.Label(brush, brushStyle);
            GUILayout.Space(10f);
            GUILayout.Label("LMB paint · RMB erase · Wheel zoom · MMB/Alt pan · Ctrl+Z undo", _sMini);
            if (_statusSaving)
                GUILayout.Label("  saving…", _sMini);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ cap editor overlay

        void OpenCapEditor(CapType cap)
        {
            _editIsNew = cap == null;
            if (_editIsNew)
            {
                _editCap = ScriptableObject.CreateInstance<CapType>();
                _editCap.name = "New Cap";
            }
            else
            {
                _editCap = cap;
            }
            _editSnapshotName = _editCap.name;
            _editSnapshotTexture = _editCap.texture;
            _editSnapshotColor = _editCap.color;
            _editSnapshotAmount = _editCap.amount;
            _editSnapshotZoom = _editCap.cropZoom;
            _editSnapshotCenter = _editCap.cropCenter;
            _editPendingB64 = null;
            _editPhotoChanged = false;
            _overlay = Overlay.CapEditor;
        }

        void CloseCapEditor(bool save)
        {
            if (_editCap == null)
            {
                _overlay = Overlay.None;
                return;
            }
            if (save)
            {
                if (_editPhotoChanged)
                {
                    if (_editSnapshotTexture != null && _editSnapshotTexture != _editCap.texture)
                        Destroy(_editSnapshotTexture);
                    _project.SetCapPhoto(_editCap, _editCap.texture, _editCap.texture != null ? _editPendingB64 : null);
                }
                if (_editIsNew)
                {
                    int maxOrder = -1;
                    foreach (CapType c in _project.caps)
                        maxOrder = Mathf.Max(maxOrder, c.sortOrder);
                    _editCap.sortOrder = maxOrder + 1;
                    _project.caps.Add(_editCap);
                    _project.GetCapId(_editCap);
                    _selected = _editCap;
                    _eraser = false;
                }
                MarkDirty();
            }
            else
            {
                if (_editPhotoChanged && _editCap.texture != null && _editCap.texture != _editSnapshotTexture)
                    Destroy(_editCap.texture);
                if (_editIsNew)
                {
                    CapCropBaker.Release(_editCap);
                    Destroy(_editCap);
                }
                else
                {
                    _editCap.name = _editSnapshotName;
                    _editCap.texture = _editSnapshotTexture;
                    _editCap.color = _editSnapshotColor;
                    _editCap.amount = _editSnapshotAmount;
                    _editCap.cropZoom = _editSnapshotZoom;
                    _editCap.cropCenter = _editSnapshotCenter;
                }
            }
            _editCap = null;
            _overlay = Overlay.None;
        }

        void DrawCapEditor(Rect window)
        {
            CapArtDraw.DrawRect(window, new Color(0.16f, 0.16f, 0.17f, 1f));
            GUILayout.BeginArea(new Rect(window.x + 10f, window.y + 8f, window.width - 20f, window.height - 16f));
            _capEditorScroll = GUILayout.BeginScrollView(_capEditorScroll);

            GUILayout.Label(_editIsNew ? "Create a Bottle Cap Type" : "Edit Cap Type", _sTitle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(60f));
            _editCap.name = GUILayout.TextField(_editCap.name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Owned", GUILayout.Width(60f));
            string amountText = GUILayout.TextField(_editCap.amount.ToString(), GUILayout.Width(70f));
            if (int.TryParse(amountText, out int amountParsed))
                _editCap.amount = Mathf.Max(0, amountParsed);
            GUILayout.Label("caps in your collection", _sMini);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_editCap.texture == null ? "Import Photo..." : "Replace Photo...", GUILayout.Width(130f)))
                PickPhotoFile();
            if (_editCap.texture != null)
                GUILayout.Label(_editCap.texture.width + " × " + _editCap.texture.height + " px", _sMini);
            if (_editCap.texture != null && GUILayout.Button("Remove Photo", GUILayout.Width(110f)))
            {
                if (_editCap.texture != _editSnapshotTexture)
                    Destroy(_editCap.texture);
                _editCap.texture = null;
                _editPendingB64 = null;
                _editPhotoChanged = true;
            }
            GUILayout.EndHorizontal();

            if (_editCap.texture != null)
            {
                GUILayout.Space(4f);
                GUILayout.Label("Frame the cap — drag the circle, scroll to zoom", _sMiniBold);
                Rect cropArea = GUILayoutUtility.GetRect(10f, 250f, GUILayout.ExpandWidth(true));
                CapCropRuntime.Draw(cropArea, _editCap);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Zoom", GUILayout.Width(44f));
                _editCap.cropZoom = GUILayout.HorizontalSlider(_editCap.cropZoom, 1f, 16f);
                if (GUILayout.Button("Reset", GUILayout.Width(60f)))
                {
                    _editCap.cropZoom = 1f;
                    _editCap.cropCenter = new Vector2(0.5f, 0.5f);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Space(4f);
                GUILayout.Label("No photo — the cap uses a plain color:", _sMiniBold);
                Color.RGBToHSV(_editCap.color, out float hue, out float sat, out float val);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Hue", GUILayout.Width(70f));
                hue = GUILayout.HorizontalSlider(hue, 0f, 1f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Saturation", GUILayout.Width(70f));
                sat = GUILayout.HorizontalSlider(sat, 0f, 1f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Brightness", GUILayout.Width(70f));
                val = GUILayout.HorizontalSlider(val, 0f, 1f);
                GUILayout.EndHorizontal();
                _editCap.color = Color.HSVToRGB(hue, sat, val);

                Rect previewArea = GUILayoutUtility.GetRect(10f, 150f, GUILayout.ExpandWidth(true));
                CapArtDraw.DrawRect(previewArea, new Color(0.11f, 0.11f, 0.115f, 1f));
                float size = Mathf.Min(previewArea.width, previewArea.height) - 16f;
                if (size > 8f)
                {
                    CapArtDraw.DrawCapRaw(new Rect(
                        previewArea.x + (previewArea.width - size) * 0.5f,
                        previewArea.y + (previewArea.height - size) * 0.5f,
                        size, size), null, _editCap.color);
                }
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_editIsNew ? "Create Cap" : "Save", GUILayout.Height(30f)))
                CloseCapEditor(true);
            if (GUILayout.Button("Cancel", GUILayout.Height(30f), GUILayout.Width(90f)))
                CloseCapEditor(false);
            GUILayout.EndHorizontal();
            if (!_editIsNew && GUILayout.Button("Delete this cap type…"))
            {
                CapType toDelete = _editCap;
                Confirm("Delete cap type \"" + toDelete.name + "\"?\nAll tiles using it become empty.", () =>
                {
                    CloseCapEditor(false);
                    if (_selected == toDelete)
                        _selected = null;
                    _undo.Clear();
                    _redo.Clear();
                    _project.DeleteCap(toDelete);
                    MarkDirty();
                }, Overlay.CapEditor);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ mosaic manager overlay

        void DrawMosaicManager(Rect window)
        {
            CapArtDraw.DrawRect(window, new Color(0.16f, 0.16f, 0.17f, 1f));
            GUILayout.BeginArea(new Rect(window.x + 10f, window.y + 8f, window.width - 20f, window.height - 16f));
            GUILayout.Label("Mosaics", _sTitle);
            GUILayout.Space(6f);

            for (int i = 0; i < _project.mosaics.Count; i++)
            {
                GUILayout.BeginHorizontal();
                bool isCurrent = i == _mosaicIndex;
                if (GUILayout.Button((isCurrent ? "▶ " : "   ") + _project.mosaicNames[i], GUILayout.ExpandWidth(true)))
                {
                    _mosaicIndex = i;
                    _selected = null;
                    _fitPending = true;
                    _countsDirty = true;
                    _undo.Clear();
                    _redo.Clear();
                }
                CapMosaic m = _project.mosaics[i];
                GUILayout.Label(m.width + "×" + m.height, _sMini, GUILayout.Width(50f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rename", GUILayout.Width(60f));
            _project.mosaicNames[_mosaicIndex] = GUILayout.TextField(_project.mosaicNames[_mosaicIndex]);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New"))
            {
                _project.NewMosaic("Mosaic " + (_project.mosaics.Count + 1));
                _mosaicIndex = _project.mosaics.Count - 1;
                _fitPending = true;
                MarkDirty();
            }
            if (GUILayout.Button("Duplicate"))
            {
                _project.DuplicateMosaic(_mosaicIndex);
                _mosaicIndex = _project.mosaics.Count - 1;
                _fitPending = true;
                MarkDirty();
            }
            if (GUILayout.Button("Delete…"))
            {
                int deleteIndex = _mosaicIndex;
                Confirm("Delete mosaic \"" + _project.mosaicNames[deleteIndex] + "\"?", () =>
                {
                    _undo.Clear();
                    _redo.Clear();
                    _project.DeleteMosaic(deleteIndex);
                    if (_project.mosaics.Count == 0)
                        _project.NewMosaic("My Mosaic");
                    _mosaicIndex = Mathf.Clamp(_mosaicIndex, 0, _project.mosaics.Count - 1);
                    _fitPending = true;
                    MarkDirty();
                }, Overlay.Mosaics);
            }
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Height(28f)))
                _overlay = Overlay.None;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ export overlay

        void DrawExportMenu(Rect window)
        {
            CapArtDraw.DrawRect(window, new Color(0.16f, 0.16f, 0.17f, 1f));
            GUILayout.BeginArea(new Rect(window.x + 10f, window.y + 8f, window.width - 20f, window.height - 16f));
            GUILayout.Label("Export / Import", _sTitle);
            GUILayout.Space(8f);

            GUILayout.Label("Current mosaic: " + CurName, _sMiniBold);
            if (GUILayout.Button("Marking guide (.txt) — pin coordinates & guide lines", GUILayout.Height(28f)))
            {
                SaveTextFile(CurName + " - marking guide.txt",
                    CapArtExports.BuildMarkingGuide(Cur, CurName), "text/plain");
            }
            if (GUILayout.Button("Pin template 1:1 (.svg) — print at 100% scale", GUILayout.Height(28f)))
            {
                SaveTextFile(CurName + " - pin template.svg",
                    CapArtExports.BuildSvgTemplate(Cur, CurName, CapCropBaker.AverageColor), "image/svg+xml");
            }

            GUILayout.Space(10f);
            GUILayout.Label("Whole project (all caps + all mosaics, photos included)", _sMiniBold);
            if (GUILayout.Button("Export project file (.json) — backup / share", GUILayout.Height(28f)))
                SaveTextFile("capart-project.json", _project.ToJson(), "application/json");
            if (GUILayout.Button("Import project file (.json)…", GUILayout.Height(28f)))
            {
                Confirm("Importing replaces your current project.\nExport a backup first if you want to keep it.", () =>
                {
                    _overlay = Overlay.None;
                    PickProjectFile();
                }, Overlay.Export);
            }
            if (GUILayout.Button("Load sample project (bundled caps)…", GUILayout.Height(28f)))
            {
                Confirm("Load the bundled sample caps?\nThis replaces your current caps and mosaics.", () =>
                {
                    _overlay = Overlay.None;
                    if (_project.LoadBundledDefault())
                        AfterProjectReplaced("Sample project loaded.");
                    else
                        SetToast("No sample project is bundled in this build.", 5f);
                }, Overlay.Export);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Height(28f)))
                _overlay = Overlay.None;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ confirm + toast

        void Confirm(string message, Action action, Overlay returnTo)
        {
            _confirmMessage = message;
            _confirmAction = action;
            _confirmReturnTo = returnTo;
            _overlay = Overlay.Confirm;
        }

        void DrawConfirm(Rect window)
        {
            CapArtDraw.DrawRect(window, new Color(0.18f, 0.16f, 0.14f, 1f));
            GUILayout.BeginArea(new Rect(window.x + 12f, window.y + 10f, window.width - 24f, window.height - 20f));
            GUILayout.Label(_confirmMessage);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yes", GUILayout.Height(28f)))
            {
                Action action = _confirmAction;
                _confirmAction = null;
                _overlay = _confirmReturnTo;
                if (action != null)
                    action();
            }
            if (GUILayout.Button("No", GUILayout.Height(28f)))
            {
                _confirmAction = null;
                _overlay = _confirmReturnTo;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawToast()
        {
            if (string.IsNullOrEmpty(_toast) || Time.realtimeSinceStartup > _toastUntil)
                return;
            var content = new GUIContent(_toast);
            Vector2 size = _sMiniBold.CalcSize(content);
            var rect = new Rect((Screen.width - size.x - 24f) * 0.5f, Screen.height - kStatusH - 40f, size.x + 24f, 26f);
            CapArtDraw.DrawRect(rect, new Color(0.1f, 0.1f, 0.1f, 0.92f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, size.x, 18f), content, _sMiniBold);
        }
    }
}
