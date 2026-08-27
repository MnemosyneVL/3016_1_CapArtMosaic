using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CapArt
{
    /// <summary>
    /// Minimal IMGUI file browser for desktop builds (WebGL uses the browser's
    /// own file dialog instead). Lists folders and files matching the given
    /// extensions; calls back with the chosen path.
    /// </summary>
    public class SimpleFileBrowser
    {
        public bool IsOpen { get; private set; }

        string _currentDir;
        string[] _extensions = new string[0];
        Action<string> _onPick;
        string _title = "Choose a file";
        Vector2 _scroll;
        string _error;

        public void Open(string title, string[] extensions, Action<string> onPick)
        {
            _title = title;
            _extensions = extensions ?? new string[0];
            _onPick = onPick;
            _error = null;
            if (string.IsNullOrEmpty(_currentDir) || !Directory.Exists(_currentDir))
            {
                _currentDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(_currentDir) || !Directory.Exists(_currentDir))
                    _currentDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(_currentDir) || !Directory.Exists(_currentDir))
                    _currentDir = Directory.GetCurrentDirectory();
            }
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            _onPick = null;
        }

        /// <summary>Draws the browser inside the given window rect. Call only while IsOpen.</summary>
        public void Draw(Rect window)
        {
            CapArtDraw.DrawRect(window, new Color(0.16f, 0.16f, 0.17f, 1f));
            CapArtDraw.DrawRect(new Rect(window.x, window.y, window.width, 26f), new Color(0.12f, 0.12f, 0.13f, 1f));
            GUI.Label(new Rect(window.x + 8f, window.y + 4f, window.width - 90f, 20f), _title);
            if (GUI.Button(new Rect(window.xMax - 70f, window.y + 3f, 62f, 20f), "Cancel"))
            {
                Close();
                return;
            }

            GUILayout.BeginArea(new Rect(window.x + 6f, window.y + 30f, window.width - 12f, window.height - 36f));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Up", GUILayout.Width(40f)))
            {
                try
                {
                    DirectoryInfo parent = Directory.GetParent(_currentDir);
                    if (parent != null)
                        _currentDir = parent.FullName;
                }
                catch (Exception) { }
            }
            GUILayout.Label(_currentDir, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string[] roots;
            try { roots = Directory.GetLogicalDrives(); }
            catch (Exception) { roots = new string[0]; }
            foreach (string root in roots.Take(8))
            {
                if (GUILayout.Button(root, GUILayout.Width(46f)))
                    _currentDir = root;
            }
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            try
            {
                foreach (string dir in Directory.GetDirectories(_currentDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string label = "▸  " + Path.GetFileName(dir);
                    if (GUILayout.Button(label, GUI.skin.label))
                        _currentDir = dir;
                }
                foreach (string file in Directory.GetFiles(_currentDir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (_extensions.Length > 0 && Array.IndexOf(_extensions, ext) < 0)
                        continue;
                    if (GUILayout.Button("    " + Path.GetFileName(file), GUI.skin.label))
                    {
                        Action<string> cb = _onPick;
                        Close();
                        if (cb != null)
                            cb(file);
                        break;
                    }
                }
                _error = null;
            }
            catch (Exception e)
            {
                _error = e.Message;
            }
            if (_error != null)
                GUILayout.Label("Cannot read this folder: " + _error);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
