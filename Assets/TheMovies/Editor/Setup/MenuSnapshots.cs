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
    /// CaptureRoomBatch instead photographs a room (environment variable THEMOVIES_ROOM, e.g. "PbrTest") from Seat2 at
    /// eye height looking at the screen, 90° wide, house lights on and dimmed – the same view as the Blender previews.
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

        public static void CaptureRoomBatch()
        {
            SessionState.SetString(k_RoomKey, System.Environment.GetEnvironmentVariable("THEMOVIES_ROOM") ?? "PbrTest");
            Begin(true);
        }

        const string k_RoomKey = "TheMovies.MenuSnapshots.Room";

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
                SessionState.EraseString(k_RoomKey);
                if (state == 2) EditorApplication.Exit(0);
            }
        }

        static void WaitAndCapture()
        {
            if (EditorApplication.timeSinceStartup < s_StartAt) return;
            EditorApplication.update -= WaitAndCapture;
            try
            {
                string room = SessionState.GetString(k_RoomKey, string.Empty);
                if (room.Length > 0)
                {
                    // The room needs a moment: house lights fade, the model is built on first use.
                    if (!PrepareRoom(room)) { EditorApplication.isPlaying = false; return; }
                    s_StartAt = EditorApplication.timeSinceStartup + 3.0;
                    EditorApplication.update += CaptureRoomLater;
                    return;
                }
                Capture();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.isPlaying = false;
        }

        static TheMovies.App.AppController s_App;

        static bool PrepareRoom(string roomName)
        {
            s_App = Object.FindAnyObjectByType<TheMovies.App.AppController>();
            if (s_App == null || !System.Enum.TryParse(roomName, true, out TheMovies.World.CinemaEnvironment.Room room))
            {
                Debug.LogError($"[TheMovies] Room snapshot: no app or unknown room '{roomName}'.");
                return false;
            }
            s_App.SetRoom(room);
            float.TryParse(System.Environment.GetEnvironmentVariable("THEMOVIES_DEBUGVIEW") ?? "0", out float debugView);
            Shader.SetGlobalFloat("_TM_DebugView", debugView);
            s_App.SetLightMode(TheMovies.Video.LightMode.On);
            s_App.Menu.Hide();
            return true;
        }

        static int s_RoomStep;

        static void CaptureRoomLater()
        {
            if (EditorApplication.timeSinceStartup < s_StartAt) return;
            try
            {
                string room = SessionState.GetString(k_RoomKey, "room");
                string suffix = s_RoomStep == 0 ? "lights-on" : "dimmed";
                CaptureRoom(Path.Combine(k_OutputFolder, $"room-{room.ToLowerInvariant()}-seat2-{suffix}.png"));
                if (s_RoomStep == 0)
                {
                    s_RoomStep = 1;
                    s_App.SetLightMode(TheMovies.Video.LightMode.Off);
                    s_StartAt = EditorApplication.timeSinceStartup + 3.0;
                    return;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.update -= CaptureRoomLater;
            s_RoomStep = 0;
            EditorApplication.isPlaying = false;
        }

        /// <summary>Seat2 (x −0.375), 1.25 m eye height, looking +Z, 90° horizontal field of view, 16:10.</summary>
        static void CaptureRoom(string path)
        {
            Directory.CreateDirectory(k_OutputFolder);
            const int width = 1440, height = 900; // as the Blender previews
            var camera = new GameObject("Room Snapshot Camera").AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 3000f;
            camera.aspect = (float)width / height;
            camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(45f * Mathf.Deg2Rad) / camera.aspect) * Mathf.Rad2Deg;
            var seat = TheMovies.World.CinemaEnvironment.SeatPose(1);
            camera.transform.SetPositionAndRotation(seat.position + Vector3.up * 1.25f, Quaternion.LookRotation(Vector3.forward, Vector3.up));
            var target = new RenderTexture(width, height, 24) { antiAliasing = 8 };
            camera.targetTexture = target;
            camera.Render();
            Save(target, path);
            Object.DestroyImmediate(camera.gameObject);
            target.Release();
            Debug.Log($"[TheMovies] Room snapshot saved to {Path.GetFullPath(path)}.");
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
