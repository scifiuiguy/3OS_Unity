using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeOS
{
    internal enum DemoSeedPolicy
    {
        RequireContent,
        SoftSeedGodMarker
    }

    internal static class ThreeOSDemoRootBootstrap
    {
        internal static string DocumentsPath(string folderName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "/storage/emulated/0/Documents/" + folderName;
#else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), folderName);
#endif
        }

        internal static string PersistentPath(string folderName) =>
            Path.Combine(Application.persistentDataPath, folderName);

        public static string PreferredDocumentsDemoPath() =>
            DocumentsPath(ThreeOSStorageController.DemoFolderName);

        public static string FallbackPersistentDemoPath() =>
            PersistentPath(ThreeOSStorageController.DemoFolderName);

        internal static string PickDemoRoot(string docs, string persistent, bool docsPopulated,
            bool persistentPopulated)
        {
            if (docsPopulated) return docs;
            if (persistentPopulated) return persistent;
            return string.Empty;
        }

        internal static bool DemoRootLooksPopulated(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root))
            {
                return false;
            }

            if (ThreeOSAndroidStorage.ListDirectoryNames(root).Count > 0)
            {
                return true;
            }

            return ThreeOSAndroidStorage.ListFileNames(root).Count > 0;
        }

        internal static bool FileLooksPresent(string path) =>
            !string.IsNullOrEmpty(path) &&
            (File.Exists(path) || ThreeOSAndroidStorage.Exists(path));

        internal static bool DirectoryLooksPresent(string path) =>
            !string.IsNullOrEmpty(path) &&
            (Directory.Exists(path) || ThreeOSAndroidStorage.Exists(path));

        /// <summary>True when root has any file (root or one level down). Folders alone do not count.</summary>
        internal static bool DemoTreeHasAnyContent(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (ThreeOSAndroidStorage.ListFileNames(root).Count > 0)
            {
                return true;
            }

            foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(root))
            {
                if (ThreeOSAndroidStorage.ListFileNames(Path.Combine(root, dir)).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static IEnumerator SeedRootObject(string root, string fileName)
        {
            var dest = Path.Combine(root, fileName);
            var docs = Path.Combine(DocumentsPath(ThreeOSStorageController.DemoFolderName), fileName);
            if (File.Exists(docs))
            {
                try
                {
                    File.Copy(docs, dest, true);
                    yield break;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] seed copy from Documents: {ex.Message}");
                }
            }

            // Already-inserted copy under any box folder counts as content (leave it there).
            foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(root))
            {
                if (File.Exists(Path.Combine(root, dir, fileName)))
                {
                    yield break;
                }
            }

            var seedUrl = Path.Combine(Application.streamingAssetsPath, "3OS", "seed", fileName);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(seedUrl))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success &&
                    req.downloadHandler?.data != null &&
                    req.downloadHandler.data.Length > 0)
                {
                    try
                    {
                        Directory.CreateDirectory(root);
                        File.WriteAllBytes(dest, req.downloadHandler.data);
                        Debug.Log($"[3OS] seeded {dest} ({req.downloadHandler.data.Length} bytes)");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[3OS] seed write failed {dest}: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[3OS] seed download failed: {req.error} url={seedUrl}");
                }
            }
#else
            if (File.Exists(seedUrl))
            {
                File.Copy(seedUrl, dest, true);
            }

            yield break;
#endif
        }

        /// <summary>
        /// Additive copy of files + box folders from→to. Never deletes destination entries
        /// (Quest Documents must not be wiped when persistent is the primary write root).
        /// </summary>
        internal static void TryMirrorDemoTree(string fromRoot, string toRoot)
        {
            if (string.IsNullOrEmpty(fromRoot) || string.IsNullOrEmpty(toRoot) ||
                string.Equals(fromRoot, toRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Directory.Exists(fromRoot) && !ThreeOSAndroidStorage.Exists(fromRoot))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(toRoot);
                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(fromRoot))
                {
                    if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    {
                        continue;
                    }

                    var fromDir = Path.Combine(fromRoot, name);
                    var toDir = Path.Combine(toRoot, name);
                    Directory.CreateDirectory(toDir);
                    foreach (var fileName in ThreeOSAndroidStorage.ListFileNames(fromDir))
                    {
                        TryCopyFile(Path.Combine(fromDir, fileName), Path.Combine(toDir, fileName));
                    }
                }

                foreach (var fileName in ThreeOSAndroidStorage.ListFileNames(fromRoot))
                {
                    TryCopyFile(Path.Combine(fromRoot, fileName), Path.Combine(toRoot, fileName));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] TryMirrorDemoTree: {ex.Message}");
            }
        }

        internal static void TryCopyFile(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) ||
                !FileLooksPresent(from) || FileLooksPresent(to))
            {
                return;
            }

            try
            {
                var parent = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(from, to, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] TryCopyFile {from} → {to}: {ex.Message}");
            }
        }

        internal static bool DirectoryIsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".3os_write_probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }


        internal static IEnumerator EnsureDemoRoot(
            string folderName,
            DemoSeedPolicy policy,
            string peerRootForSoftSeed,
            Action<string> setRoot,
            Action<string> setStatus)
        {
            var docs = DocumentsPath(folderName);
            var fallback = PersistentPath(folderName);
            try { Directory.CreateDirectory(fallback); }
            catch (Exception ex) { Debug.LogWarning($"[3OS] persistent demo create failed: {ex.Message}"); }
            try { Directory.CreateDirectory(docs); }
            catch (Exception ex) { Debug.LogWarning($"[3OS] Documents demo create failed: {ex.Message}"); }

            var docsPopulated = DemoRootLooksPopulated(docs);
            var persistentPopulated = DemoRootLooksPopulated(fallback);

            if (policy == DemoSeedPolicy.RequireContent)
            {
                if (!FileLooksPresent(Path.Combine(fallback, "simple.glb")))
                    yield return SeedRootObject(fallback, "simple.glb");
                persistentPopulated = DemoRootLooksPopulated(fallback) ||
                                      FileLooksPresent(Path.Combine(fallback, "simple.glb"));
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            TryMirrorDemoTree(docs, fallback);
            TryMirrorDemoTree(fallback, docs);
            docsPopulated = DemoRootLooksPopulated(docs);
            persistentPopulated = DemoRootLooksPopulated(fallback);
            if (policy == DemoSeedPolicy.RequireContent &&
                DemoRootLooksPopulated(docs) &&
                ThreeOSAndroidStorage.ListFileNames(docs).Count == 0 &&
                !ThreeOSAndroidStorage.TryProbeFileName(docs, "simple.glb") &&
                !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.LogWarning(
                    "[3OS] Documents has folders but no listable files — grant all-files access later if needed");
            }
#else
            if (policy == DemoSeedPolicy.RequireContent)
            {
                var early = PickDemoRoot(docs, fallback, docsPopulated, persistentPopulated);
                if (!string.IsNullOrEmpty(early))
                    TryMirrorDemoTree(early, early == docs ? fallback : docs);
            }
#endif

            docsPopulated = DemoRootLooksPopulated(docs);
            persistentPopulated = DemoRootLooksPopulated(fallback);
            var root = PickDemoRoot(docs, fallback, docsPopulated, persistentPopulated);
            if (string.IsNullOrEmpty(root))
            {
                if (policy == DemoSeedPolicy.RequireContent)
                {
                    setStatus($"no demo content — add files/folders under {folderName}");
                    Debug.LogError($"[3OS] no demo content (persistent={fallback} docs={docs})");
                    setRoot(string.Empty);
                    yield break;
                }
                root = fallback;
                try
                {
                    var dest = Path.Combine(root, "simple.glb");
                    var src = !string.IsNullOrEmpty(peerRootForSoftSeed)
                        ? Path.Combine(peerRootForSoftSeed, "simple.glb") : string.Empty;
                    if (!string.IsNullOrEmpty(src) && File.Exists(src))
                        File.Copy(src, dest, overwrite: false);
                    else if (!File.Exists(dest) && !File.Exists(Path.Combine(root, "marker.txt")))
                        File.WriteAllText(Path.Combine(root, "marker.txt"), folderName);
                }
                catch (Exception ex) { Debug.LogWarning($"[3OS] soft-seed {folderName} failed: {ex.Message}"); }
            }

            setRoot(root);
            Debug.Log($"[3OS] demo root({folderName}) = {root} (docsPopulated={docsPopulated} persistentPopulated={persistentPopulated})");
            yield return null;
        }
    }
}
