using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CapArt.EditorTools
{
    /// <summary>
    /// Creates (or repairs) the scene that runs the standalone Cap Art app,
    /// and registers it in the build settings so it can be built right away.
    /// </summary>
    public static class CapArtAppSceneCreator
    {
        const string kScenePath = "Assets/CapArt/CapArtApp.unity";

        [MenuItem("Tools/Cap Art/Create App Scene (for builds)", false, 20)]
        public static void CreateAppScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.13f, 0.135f, 1f);
            camera.orthographic = true;

            new GameObject("CapArtApp").AddComponent<CapArtApp>();

            EditorSceneManager.SaveScene(scene, kScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool present = scenes.Exists(s => s.path == kScenePath);
            if (!present)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(kScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            EditorUtility.DisplayDialog("Cap Art",
                "App scene created at " + kScenePath + " and added to Build Settings.\n\n" +
                "Press Play to try the app, or use File > Build Profiles to build it.", "OK");
        }
    }
}
