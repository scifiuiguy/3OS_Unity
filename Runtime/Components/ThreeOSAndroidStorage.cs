using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Android filesystem helpers for Quest demo write-through. MTP-staged files under
    /// public Documents often cannot be removed via <see cref="File.Delete"/> without
    /// all-files access; java.io.File rename/delete and MediaStore delete work more often.
    /// </summary>
    public static class ThreeOSAndroidStorage
    {
        public static bool IsExternalStorageManager()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                if (version.GetStatic<int>("SDK_INT") < 30)
                {
                    return true;
                }

                using var env = new AndroidJavaClass("android.os.Environment");
                return env.CallStatic<bool>("isExternalStorageManager");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] IsExternalStorageManager: {ex.Message}");
            }
#endif
            return true;
        }

        /// <summary>Opens the system screen to grant “All files access” for this app.</summary>
        public static void RequestManageAllFilesAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (IsExternalStorageManager())
                {
                    return;
                }

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var uriCls = new AndroidJavaClass("android.net.Uri");
                using var uri = uriCls.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier);
                using var intent = new AndroidJavaObject(
                    "android.content.Intent",
                    "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION");
                intent.Call<AndroidJavaObject>("setData", uri);
                activity.Call("startActivity", intent);
                Debug.Log("[3OS] launched MANAGE_APP_ALL_FILES_ACCESS_PERMISSION settings");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] RequestManageAllFilesAccess: {ex.Message}");
            }
#endif
        }

        public static bool JavaRename(string src, string dst)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var srcFile = new AndroidJavaObject("java.io.File", src);
                using var dstFile = new AndroidJavaObject("java.io.File", dst);
                using var parent = dstFile.Call<AndroidJavaObject>("getParentFile");
                parent?.Call<bool>("mkdirs");
                if (dstFile.Call<bool>("exists"))
                {
                    dstFile.Call<bool>("delete");
                }

                if (srcFile.Call<bool>("renameTo", dstFile))
                {
                    Debug.Log($"[3OS] Java renameTo OK {src} → {dst}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] Java renameTo: {ex.Message}");
            }
#endif
            return false;
        }

        public static bool JavaDelete(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var f = new AndroidJavaObject("java.io.File", path);
                if (!f.Call<bool>("exists"))
                {
                    return true;
                }

                // MTP-staged files are often read-only until attributes are cleared.
                f.Call<bool>("setWritable", true);
                f.Call<bool>("setReadable", true);
                if (f.Call<bool>("delete"))
                {
                    Debug.Log($"[3OS] Java delete OK {path}");
                    return true;
                }

                // android.system.Os.remove — works more often for MTP/Documents with all-files.
                try
                {
                    using var os = new AndroidJavaClass("android.system.Os");
                    os.CallStatic("remove", path);
                    if (!f.Call<bool>("exists"))
                    {
                        Debug.Log($"[3OS] Os.remove OK {path}");
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] Os.remove: {ex.Message}");
                }

                Debug.LogWarning($"[3OS] Java delete returned false for {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] Java delete: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// Same-volume atomic move via <c>java.nio.file.Files.move</c> (REPLACE_EXISTING).
        /// </summary>
        public static bool JavaNioMove(string src, string dst)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var dstFile = new AndroidJavaObject("java.io.File", dst);
                using var parent = dstFile.Call<AndroidJavaObject>("getParentFile");
                parent?.Call<bool>("mkdirs");

                using var paths = new AndroidJavaClass("java.nio.file.Paths");
                using var filesCls = new AndroidJavaClass("java.nio.file.Files");
                var emptyMore = new string[0];
                using var srcPath = paths.CallStatic<AndroidJavaObject>("get", src, emptyMore);
                using var dstPath = paths.CallStatic<AndroidJavaObject>("get", dst, emptyMore);
                using var optionCls = new AndroidJavaClass("java.nio.file.StandardCopyOption");
                using var replace = optionCls.GetStatic<AndroidJavaObject>("REPLACE_EXISTING");
                filesCls.CallStatic<AndroidJavaObject>("move", srcPath, dstPath, replace);

                if (Exists(dst) && !Exists(src))
                {
                    Debug.Log($"[3OS] NIO move OK {src} → {dst}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] JavaNioMove: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// Delete MediaStore rows for <paramref name="fileName"/> under a public relative folder
        /// (e.g. <c>Documents/3OS_Demo/</c>) even when <see cref="Exists"/> is false — MTP ghosts.
        /// </summary>
        public static bool MediaStoreDeleteByRelativeName(string relativeDirWithSlash, string fileName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(relativeDirWithSlash) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            try
            {
                var relative = relativeDirWithSlash.Replace('\\', '/');
                if (!relative.EndsWith("/"))
                {
                    relative += "/";
                }

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var files = new AndroidJavaClass("android.provider.MediaStore$Files");
                using var uri = files.CallStatic<AndroidJavaObject>("getContentUri", "external");

                var deleted = resolver.Call<int>(
                    "delete",
                    uri,
                    "relative_path=? AND _display_name=?",
                    new[] { relative, fileName });
                if (deleted <= 0)
                {
                    deleted = resolver.Call<int>(
                        "delete",
                        uri,
                        "relative_path=? AND display_name=?",
                        new[] { relative, fileName });
                }

                // Also match _data suffix (legacy).
                if (deleted <= 0)
                {
                    deleted = resolver.Call<int>(
                        "delete",
                        uri,
                        "_data LIKE ?",
                        new[] { "%/" + relative.TrimEnd('/') + "/" + fileName });
                }

                Debug.Log(
                    $"[3OS] MediaStoreDeleteByRelativeName {relative}{fileName} rows={deleted}");
                return deleted > 0;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] MediaStoreDeleteByRelativeName: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// Best-effort MediaStore row delete. Tries legacy <c>_data</c> match, then
        /// <c>relative_path</c> + <c>display_name</c> (API 29+ / Quest MTP).
        /// </summary>
        public static bool MediaStoreDelete(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var files = new AndroidJavaClass("android.provider.MediaStore$Files");
                using var uri = files.CallStatic<AndroidJavaObject>("getContentUri", "external");

                var normalized = path.Replace('\\', '/');
                var deleted = resolver.Call<int>("delete", uri, "_data=?", new[] { normalized });
                if (deleted <= 0 && normalized.StartsWith("/storage/emulated/0/", System.StringComparison.Ordinal))
                {
                    // Some rows store without the /storage/emulated/0 prefix variant.
                    deleted = resolver.Call<int>("delete", uri, "_data=?", new[] { path });
                }

                // API 29+: match Documents/3OS_Demo/simple.glb via relative_path + display_name.
                if (deleted <= 0)
                {
                    var fileName = Path.GetFileName(normalized);
                    var parent = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
                    const string prefix = "/storage/emulated/0/";
                    if (!string.IsNullOrEmpty(fileName) &&
                        parent.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        var relative = parent.Substring(prefix.Length).Trim('/') + "/";
                        deleted = resolver.Call<int>(
                            "delete",
                            uri,
                            "relative_path=? AND _display_name=?",
                            new[] { relative, fileName });
                        if (deleted <= 0)
                        {
                            deleted = resolver.Call<int>(
                                "delete",
                                uri,
                                "relative_path=? AND display_name=?",
                                new[] { relative, fileName });
                        }

                        Debug.Log(
                            $"[3OS] MediaStore delete by relative_path={relative} name={fileName} rows={deleted}");
                    }
                }
                else
                {
                    Debug.Log($"[3OS] MediaStore delete by _data {normalized} rows={deleted}");
                }

                return deleted > 0 && !Exists(path);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] MediaStoreDelete: {ex.Message}");
            }
#endif
            return false;
        }

        public static bool Exists(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var f = new AndroidJavaObject("java.io.File", path);
                if (f.Call<bool>("exists"))
                {
                    return true;
                }
            }
            catch
            {
                // fall through
            }
#endif
            return File.Exists(path);
        }

        /// <summary>
        /// Count non-hidden files directly inside a directory (Java list — more reliable than
        /// <see cref="Directory.GetFiles"/> for MTP/Documents on Quest).
        /// </summary>
        public static uint CountFilesInDirectory(string dirPath)
        {
            uint count = 0;
            foreach (var _ in ListFileNames(dirPath))
            {
                count++;
            }

            return count;
        }

        public static List<string> ListFileNames(string dirPath)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(dirPath))
            {
                return names;
            }

            try
            {
                if (Directory.Exists(dirPath))
                {
                    foreach (var file in Directory.GetFiles(dirPath))
                    {
                        var name = Path.GetFileName(file);
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("."))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] ListFiles .NET {dirPath}: {ex.Message}");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // Always merge Java — Quest MTP/Documents often under-reports files via .NET,
            // and java.io.File.isFile() can be false for MediaStore-staged entries.
            foreach (var name in JavaListNames(dirPath, filesOnly: true))
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            foreach (var name in MediaStoreListDirectFileNames(dirPath))
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
#endif
            return names;
        }

        /// <summary>
        /// Probe a concrete file path even when directory listing omits it (common for
        /// MTP-copied non-media files like <c>.glb</c> on Quest).
        /// </summary>
        public static bool TryProbeFileName(string dirPath, string fileName)
        {
            if (string.IsNullOrEmpty(dirPath) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            return Exists(Path.Combine(dirPath, fileName));
        }

        public static List<string> ListDirectoryNames(string dirPath)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(dirPath))
            {
                return names;
            }

            try
            {
                if (Directory.Exists(dirPath))
                {
                    foreach (var dir in Directory.GetDirectories(dirPath))
                    {
                        var name = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("."))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] ListDirs .NET {dirPath}: {ex.Message}");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // Always merge Java listing — Quest Documents often under-reports via .NET.
            foreach (var name in JavaListNames(dirPath, filesOnly: false))
            {
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }
#endif
            return names;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static List<string> JavaListNames(string dirPath, bool filesOnly)
        {
            var names = new List<string>();
            try
            {
                using var f = new AndroidJavaObject("java.io.File", dirPath);
                if (!f.Call<bool>("isDirectory"))
                {
                    return names;
                }

                var listed = f.Call<string[]>("list");
                if (listed == null)
                {
                    return names;
                }

                foreach (var name in listed)
                {
                    if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    {
                        continue;
                    }

                    using var child = new AndroidJavaObject("java.io.File", f, name);
                    var isFile = child.Call<bool>("isFile");
                    var isDir = child.Call<bool>("isDirectory");
                    // MTP-staged files sometimes report !isFile && !isDirectory; treat
                    // any non-directory name as a file when listing files.
                    if (filesOnly && (isFile || !isDir))
                    {
                        names.Add(name);
                    }
                    else if (!filesOnly && isDir)
                    {
                        names.Add(name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] JavaListNames {dirPath}: {ex.Message}");
            }

            return names;
        }

        /// <summary>
        /// Direct children files under <paramref name="dirPath"/> via MediaStore (catches
        /// MTP entries that <c>File.list</c> skips). Non-media types like .glb may still
        /// be missing until a media scan — pair with <see cref="TryProbeFileName"/>.
        /// </summary>
        private static List<string> MediaStoreListDirectFileNames(string dirPath)
        {
            var names = new List<string>();
            try
            {
                var normalized = dirPath.Replace('\\', '/').TrimEnd('/');
                if (string.IsNullOrEmpty(normalized))
                {
                    return names;
                }

                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var files = new AndroidJavaClass("android.provider.MediaStore$Files");
                using var uri = files.CallStatic<AndroidJavaObject>("getContentUri", "external");
                var projection = new[] { "_data", "media_type" };
                // Direct children only: path/file but not path/sub/file
                var selection = "_data LIKE ? AND _data NOT LIKE ?";
                var args = new[] { normalized + "/%", normalized + "/%/%" };
                using var cursor = resolver.Call<AndroidJavaObject>(
                    "query", uri, projection, selection, args, null);
                if (cursor == null)
                {
                    return names;
                }

                var dataIdx = cursor.Call<int>("getColumnIndexOrThrow", "_data");
                var typeIdx = cursor.Call<int>("getColumnIndex", "media_type");
                while (cursor.Call<bool>("moveToNext"))
                {
                    var full = cursor.Call<string>("getString", dataIdx);
                    if (string.IsNullOrEmpty(full))
                    {
                        continue;
                    }

                    // media_type 0 = none/file; skip rows that are clearly directories if typed.
                    if (typeIdx >= 0)
                    {
                        var mediaType = cursor.Call<int>("getInt", typeIdx);
                        // MediaStore.Files.FileColumns.MEDIA_TYPE_NONE = 0; also accept any
                        // non-directory row. Directories are often absent from this table.
                        if (mediaType < 0)
                        {
                            continue;
                        }
                    }

                    var name = Path.GetFileName(full.Replace('\\', '/'));
                    if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    {
                        continue;
                    }

                    // Prefer entries that look like files (have an extension) when unsure.
                    if (!name.Contains("."))
                    {
                        using var child = new AndroidJavaObject("java.io.File", full);
                        if (child.Call<bool>("isDirectory"))
                        {
                            continue;
                        }
                    }

                    if (!names.Contains(name))
                    {
                        names.Add(name);
                    }
                }

                cursor.Call("close");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] MediaStoreListDirectFileNames {dirPath}: {ex.Message}");
            }

            return names;
        }
#endif
    }
}
