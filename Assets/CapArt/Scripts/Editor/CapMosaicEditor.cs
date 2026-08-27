using UnityEditor;
using UnityEngine;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Inspector for CapMosaic assets: shows a summary and a button to open the
    /// painter. Grid data itself is only edited through the Mosaic Painter window.
    /// </summary>
    [CustomEditor(typeof(CapMosaic))]
    public class CapMosaicEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var mosaic = (CapMosaic)target;
            mosaic.EnsureSize();

            EditorGUILayout.LabelField("Grid", mosaic.width + " × " + mosaic.height + " tiles");
            mosaic.CountCaps(out int filled, out int empty);
            EditorGUILayout.LabelField("Caps placed", filled.ToString());
            EditorGUILayout.LabelField("Empty tiles", empty.ToString());
            Vector2 mm = mosaic.ArtworkSizeMm();
            EditorGUILayout.LabelField("Artwork size",
                string.Format("≈ {0:0.0} × {1:0.0} cm (Ø {2:0.#} mm caps)", mm.x / 10f, mm.y / 10f, mosaic.capDiameterMm));

            GUILayout.Space(10f);
            if (GUILayout.Button("Open in Mosaic Painter", GUILayout.Height(32f)))
                CapMosaicWindow.Open(mosaic);
        }
    }
}
