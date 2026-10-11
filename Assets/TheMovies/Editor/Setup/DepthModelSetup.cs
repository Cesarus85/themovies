using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Menu "The Movies > 2D→3D mit KI einrichten": installs Unity's Inference Engine package and downloads the
    /// depth model (Depth Anything V2 Small, Apache 2.0, ~100 MB) into a Resources folder, so the app estimates
    /// depth with it instead of the simple guess from the picture.
    /// </summary>
    [InitializeOnLoad]
    public static class DepthModelSetup
    {
        // Survives the script reload that follows the package installation (static fields don't).
        const string k_PendingKey = "TheMovies.DepthModelSetupPending";
        public const string Package = "com.unity.ai.inference";
        public const string ModelPath = "Assets/TheMovies/Resources/TheMovies/DepthModel.onnx";
        public const string ModelUrl = "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model.onnx";

        static UnityEditor.PackageManager.Requests.AddRequest s_Add;
        static UnityWebRequest s_Download;
        static string s_TempFile;

        static bool PackageInstalled => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + Package) != null;

        static DepthModelSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += ContinueAfterReload;
        }

        /// <summary>After the package installation Unity reloads all scripts: continue with the model download then.</summary>
        static void ContinueAfterReload()
        {
            if (!SessionState.GetBool(k_PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ContinueAfterReload;
                return;
            }
            if (!PackageInstalled) return; // still installing; the next reload comes back here
            SessionState.SetBool(k_PendingKey, false);
            DownloadModel();
        }

        /// <summary>For the build check: package there but no model (2D→3D then only uses the simple estimate).</summary>
        public static bool ModelMissing => PackageInstalled && !File.Exists(ModelPath);

        [MenuItem("The Movies/2D→3D mit KI einrichten", priority = 34)]
        public static void Setup()
        {
            if (s_Add != null || s_Download != null)
            {
                EditorUtility.DisplayDialog("The Movies", "Die Einrichtung läuft schon.", "OK");
                return;
            }
            if (!PackageInstalled)
            {
                if (!EditorUtility.DisplayDialog("The Movies",
                        "Für 2D→3D mit KI braucht das Projekt Unitys Paket „Inference Engine“ (" + Package + ") und das " +
                        "Tiefen-Modell Depth Anything V2 Small (Apache-2.0-Lizenz, ca. 100 MB, macht die APK entsprechend größer).\n\n" +
                        "Jetzt das Paket installieren? Danach wird das Modell heruntergeladen.", "Installieren", "Abbrechen"))
                    return;
                Debug.Log("[TheMovies] Installing " + Package + " for 2D→3D.");
                SessionState.SetBool(k_PendingKey, true);
                s_Add = UnityEditor.PackageManager.Client.Add(Package);
                EditorApplication.update += WatchAdd;
                return;
            }
            DownloadModel();
        }

        static void WatchAdd()
        {
            if (s_Add == null || !s_Add.IsCompleted) return;
            EditorApplication.update -= WatchAdd;
            var request = s_Add;
            s_Add = null;
            if (request.Status != UnityEditor.PackageManager.StatusCode.Success)
            {
                string error = request.Error != null ? request.Error.message : "unbekannter Fehler";
                SessionState.SetBool(k_PendingKey, false);
                Debug.LogError("[TheMovies] Installing " + Package + " failed: " + error);
                EditorUtility.DisplayDialog("The Movies", "Paket-Installation fehlgeschlagen:\n" + error, "OK");
                return;
            }
            Debug.Log("[TheMovies] Installed " + request.Result.packageId);
            // Usually Unity reloads the scripts now and ContinueAfterReload takes over; if not, go on directly.
            SessionState.SetBool(k_PendingKey, false);
            DownloadModel();
        }

        static void DownloadModel()
        {
            if (s_Download != null) return;
            if (File.Exists(ModelPath))
            {
                EditorUtility.DisplayDialog("The Movies",
                    "Alles eingerichtet: Paket installiert, Modell liegt in " + ModelPath + " (" + ImportedAs() + ").\n\nNeu bauen – dann rechnet " +
                    "„Einstellungen > Bild & 3D > 2D → 3D“ mit KI-Tiefe.", "OK");
                return;
            }
            Debug.Log("[TheMovies] Downloading the depth model from " + ModelUrl);
            s_TempFile = Path.Combine(Path.GetTempPath(), "themovies-depth-model.onnx");
            s_Download = new UnityWebRequest(ModelUrl, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerFile(s_TempFile) { removeFileOnAbort = true },
            };
            s_Download.SendWebRequest();
            EditorApplication.update += WatchDownload;
        }

        /// <summary>Type Unity imported the model file as ("ModelAsset" when the Inference Engine package handles it).</summary>
        static string ImportedAs()
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(ModelPath);
            return type != null ? type.Name : "nicht importiert";
        }

        static void WatchDownload()
        {
            if (s_Download == null) return;
            if (!s_Download.isDone)
            {
                float progress = s_Download.downloadProgress;
                ulong bytes = s_Download.downloadedBytes;
                if (EditorUtility.DisplayCancelableProgressBar("The Movies – KI-Modell für 2D→3D",
                        $"Lade Depth Anything V2 Small … {bytes / (1024 * 1024)} MB", Mathf.Clamp01(progress)))
                {
                    s_Download.Abort();
                }
                return;
            }
            EditorApplication.update -= WatchDownload;
            EditorUtility.ClearProgressBar();
            var request = s_Download;
            s_Download = null;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[TheMovies] Downloading the depth model failed: " + request.error);
                    EditorUtility.DisplayDialog("The Movies",
                        "Download fehlgeschlagen: " + request.error + "\n\nManuell: " + ModelUrl + "\nherunterladen und als\n" + ModelPath + "\nspeichern.", "OK");
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(ModelPath));
                File.Copy(s_TempFile, ModelPath, true);
                File.Delete(s_TempFile);
                AssetDatabase.ImportAsset(ModelPath);
                string imported = ImportedAs();
                Debug.Log("[TheMovies] Depth model saved to " + ModelPath + " – imported as " + imported);
                if (imported != "ModelAsset")
                {
                    EditorUtility.DisplayDialog("The Movies",
                        "Das Modell liegt in " + ModelPath + ", wurde aber als „" + imported + "“ statt als ModelAsset importiert. " +
                        "Bitte die Fehlermeldungen in der Console an den Entwickler schicken.", "OK");
                    return;
                }
                EditorUtility.DisplayDialog("The Movies",
                    "KI-Modell installiert (" + ModelPath + ").\n\nNeu bauen – dann rechnet „Einstellungen > Bild & 3D > 2D → 3D“ " +
                    "mit KI-Tiefe. Die Datei ist zu groß für GitHub und wird von git ignoriert.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[TheMovies] Saving the depth model failed: " + e);
                EditorUtility.DisplayDialog("The Movies", "Speichern fehlgeschlagen: " + e.Message, "OK");
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
