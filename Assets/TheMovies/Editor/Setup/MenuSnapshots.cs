using System.IO;
using System.Linq;
using System.Reflection;
using TheMovies.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Renders every page of the 3D menu to a PNG (Logs/MenuSnapshots), to check layout and spacing without the
    /// headset: starts the cinema scene in play mode, waits until the app is up, then photographs the menu from the
    /// front with its own camera. Not a sharpness check – the headset shows the text at far fewer pixels.
    /// Command line (needs a graphics device, so without -nographics):
    /// Unity -batchmode -projectPath . -executeMethod TheMovies.EditorTools.MenuSnapshots.CaptureBatch
    /// </summary>
    [InitializeOnLoad]
    public static class MenuSnapshots
    {
        const string k_StateKey = "TheMovies.MenuSnapshots.Pending";
        const string k_OutputFolder = "Logs/MenuSnapshots";
        const float k_StartDelay = 4f;
        const int k_Width = 1840, k_Height = 1440;

        static double s_StartAt;

        static MenuSnapshots()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("The Movies/Menü-Seiten als Bilder speichern", priority = 71)]
        static void CaptureMenu() => Begin(false);

        public static void CaptureBatch() => Begin(true);

        static void Begin(bool exitWhenDone)
        {
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            SessionState.SetInt(k_StateKey, exitWhenDone ? 2 : 1);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            int state = SessionState.GetInt(k_StateKey, 0);
            if (state == 0) return;
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                s_StartAt = EditorApplication.timeSinceStartup + k_StartDelay;
                EditorApplication.update += WaitAndCapture;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseInt(k_StateKey);
                if (state == 2) EditorApplication.Exit(0);
            }
        }

        static void WaitAndCapture()
        {
            if (EditorApplication.timeSinceStartup < s_StartAt) return;
            EditorApplication.update -= WaitAndCapture;
            try
            {
                Capture();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.isPlaying = false;
        }

        static void Capture()
        {
            var menu = Object.FindAnyObjectByType<MenuUI>(FindObjectsInactive.Include);
            if (menu == null)
            {
                Debug.LogError("[TheMovies] Menu snapshots: no menu in the scene.");
                return;
            }
            menu.Show();
            Directory.CreateDirectory(k_OutputFolder);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var showPage = typeof(MenuUI).GetMethod("ShowPage", flags);
            var pages = typeof(MenuUI).GetFields(flags)
                .Where(f => f.FieldType == typeof(GameObject) && f.Name.EndsWith("Page") && f.Name != "m_CurrentPage");

            var camera = new GameObject("Snapshot Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 1f);
            camera.fieldOfView = 44f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 10f;
            // The menu faces -Z of its transform (towards the user).
            var t = menu.transform;
            camera.transform.SetPositionAndRotation(t.position - t.forward * 1.1f, Quaternion.LookRotation(t.forward, t.up));
            var target = new RenderTexture(k_Width, k_Height, 24) { antiAliasing = 8 };
            camera.targetTexture = target;
            camera.aspect = (float)k_Width / k_Height;

            int count = 0;
            foreach (var field in pages)
            {
                var page = field.GetValue(menu) as GameObject;
                if (page == null) continue;
                showPage.Invoke(menu, new object[] { page });
                Canvas.ForceUpdateCanvases();
                foreach (var text in menu.GetComponentsInChildren<TMPro.TMP_Text>()) text.ForceMeshUpdate();
                camera.Render();
                Save(target, Path.Combine(k_OutputFolder, field.Name.Substring(2) + ".png"));
                count++;
            }
            Object.DestroyImmediate(camera.gameObject);
            target.Release();
            Debug.Log($"[TheMovies] Menu snapshots: {count} pages saved to {Path.GetFullPath(k_OutputFolder)}.");
        }

        static void Save(RenderTexture source, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            var image = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
