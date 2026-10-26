using System;
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

        /// <summary>android.system.Os.rename — same-volume; no JNI CopyOption array quirks.</summary>
        public static bool OsRename(string src, string dst)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
                return false;
            try
            {
                using var dstFile = new AndroidJavaObject("java.io.File", dst);
                using var parent = dstFile.Call<AndroidJavaObject>("getParentFile");
                parent?.Call<bool>("mkdirs");
                using var os = new AndroidJavaClass("android.system.Os");
                os.CallStatic("rename", src, dst);
                if (Exists(dst))
                {
                    Debug.Log($"[3OS] Os.rename OK {src} → {dst}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] Os.rename: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// Last-resort same as adb shell mv — proven to relocate Quest Documents folders when
        /// Java NIO/renameTo fail from Unity JNI. Uses ProcessBuilder (Runtime.exec(String[])
        /// often binds the wrong overload under IL2CPP).
        /// </summary>
        public static bool ShellMv(string src, string dst)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
                return false;
            try
            {
                using var dstFile = new AndroidJavaObject("java.io.File", dst);
                using var parent = dstFile.Call<AndroidJavaObject>("getParentFile");
                parent?.Call<bool>("mkdirs");

                var quotedSrc = "'" + src.Replace("'", "'\\''") + "'";
                var quotedDst = "'" + dst.Replace("'", "'\\''") + "'";
                var script = "mv -f -- " + quotedSrc + " " + quotedDst;
                using var list = new AndroidJavaObject("java.util.ArrayList");
                list.Call<bool>("add", "/system/bin/sh");
                list.Call<bool>("add", "-c");
                list.Call<bool>("add", script);
                using var pb = new AndroidJavaObject("java.lang.ProcessBuilder", list);
                pb.Call<AndroidJavaObject>("redirectErrorStream", true);
                using var proc = pb.Call<AndroidJavaObject>("start");
                var exit = proc.Call<int>("waitFor");
                if (exit == 0 && Exists(dst))
                {
                    Debug.Log($"[3OS] ShellMv OK {src} → {dst}");
                    return true;
                }

                Debug.LogWarning($"[3OS] ShellMv exit={exit} {src} → {dst}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] ShellMv: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// <c>rm -rf</c> via ProcessBuilder — Quest Documents often keeps folders after
        /// java.io.File.delete / NIO even when children were removed.
        /// </summary>
        public static bool ShellRm(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(path))
                return true;
            try
            {
                if (!Exists(path))
                    return true;

                var quoted = "'" + path.Replace("'", "'\\''") + "'";
                var script = "rm -rf -- " + quoted;
                using var list = new AndroidJavaObject("java.util.ArrayList");
                list.Call<bool>("add", "/system/bin/sh");
                list.Call<bool>("add", "-c");
                list.Call<bool>("add", script);
                using var pb = new AndroidJavaObject("java.lang.ProcessBuilder", list);
                pb.Call<AndroidJavaObject>("redirectErrorStream", true);
                using var proc = pb.Call<AndroidJavaObject>("start");
                var exit = proc.Call<int>("waitFor");
                if (exit == 0 && !Exists(path))
                {
                    Debug.Log($"[3OS] ShellRm OK {path}");
                    return true;
                }

                Debug.LogWarning($"[3OS] ShellRm exit={exit} remains={Exists(path)} {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] ShellRm: {ex.Message}");
            }
#endif
            return !Exists(path);
        }

        /// <summary>Append one line to persistent paste_fs.log (survives logcat clears).</summary>
        public static void AppendPasteFsLog(string line)
        {
            try
            {
                var path = Path.Combine(Application.persistentDataPath, "paste_fs.log");
                File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + line + "\n");
            }
            catch
            {
                // never break paste on logging
            }
        }

        /// <summary>
        /// Run JNI / <see cref="AndroidJavaObject"/> work from a ThreadPool worker.
        /// Quest Documents folder moves need NIO; doing that on the Unity main thread
        /// stalls frames (~80–100ms per folder → ~20 fps during multi-folder paste).
        /// </summary>
        public static void RunWithJni(Action action)
        {
            if (action == null)
                return;
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
            try
            {
                action();
            }
            finally
            {
                AndroidJNI.DetachCurrentThread();
            }
#else
            action();
#endif
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
        /// Recursive delete for Documents folders. Plain <see cref="JavaDelete"/> fails on
        /// non-empty dirs — Act I restore left DemonProject duplicates because of that.
        /// </summary>
        public static bool JavaDeleteRecursive(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            try
            {
                if (Directory.Exists(path))
                {
                    try { Directory.Delete(path, true); }
                    catch { /* fall through */ }
                }
                else if (File.Exists(path))
                {
                    try { File.Delete(path); }
                    catch { /* fall through */ }
                }
            }
            catch { /* best-effort */ }

            if (!Exists(path))
                return true;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var f = new AndroidJavaObject("java.io.File", path);
                if (!f.Call<bool>("exists"))
                    return true;

                if (f.Call<bool>("isDirectory"))
                {
                    var children = f.Call<AndroidJavaObject[]>("listFiles");
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            if (child == null)
                                continue;
                            using (child)
                            {
                                var childPath = child.Call<string>("getAbsolutePath");
                                JavaDeleteRecursive(childPath);
                            }
                        }
                    }
                }

                f.Call<bool>("setWritable", true);
                if (f.Call<bool>("delete") && !Exists(path))
                {
                    Debug.Log($"[3OS] JavaDeleteRecursive OK {path}");
                    return true;
                }

                try
                {
                    using var os = new AndroidJavaClass("android.system.Os");
                    os.CallStatic("remove", path);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] JavaDeleteRecursive Os.remove: {ex.Message}");
                }

                if (!Exists(path))
                    return true;

                // Documents MTP ghosts: shell rm -rf is the path paste already trusts for mv.
                if (ShellRm(path))
                    return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] JavaDeleteRecursive: {ex.Message}");
            }
#endif
            return !Exists(path);
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
                // JNI varargs: pass CopyOption[] explicitly.
                var opts = new AndroidJavaObject[] { replace };
                filesCls.CallStatic<AndroidJavaObject>("move", srcPath, dstPath, opts);

                // MTP Documents often still lists src after a successful move. Dest present
                // is enough — callers scrub the ghost. Requiring !Exists(src) made every
                // folder paste report failure while Demo/Demo2 stayed put.
                if (Exists(dst))
                {
                    Debug.Log($"[3OS] NIO move OK {src} → {dst} (srcGone={!Exists(src)})");
                    return true;
                }

                Debug.LogWarning($"[3OS] JavaNioMove returned but dest missing {src} → {dst}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] JavaNioMove: {ex.Message}");
            }
#endif
            return false;
        }

        /// <summary>
        /// Recursive directory copy via java.io.File (main thread). Used when rename/move fails
        /// on MTP-staged Documents folders.
        /// </summary>
        public static bool JavaCopyDirectory(string src, string dst)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst))
                return false;
            try
            {
                using var srcFile = new AndroidJavaObject("java.io.File", src);
                if (!srcFile.Call<bool>("isDirectory"))
                    return false;
                using var dstFile = new AndroidJavaObject("java.io.File", dst);
                if (!dstFile.Call<bool>("exists") && !dstFile.Call<bool>("mkdirs"))
                    return false;

                var children = srcFile.Call<AndroidJavaObject[]>("listFiles");
                if (children == null)
                    return true;

                foreach (var child in children)
                {
                    if (child == null)
                        continue;
                    using (child)
                    {
                        var name = child.Call<string>("getName");
                        if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                            continue;
                        var childDst = Path.Combine(dst, name);
                        if (child.Call<bool>("isDirectory"))
                        {
                            if (!JavaCopyDirectory(child.Call<string>("getAbsolutePath"), childDst))
                                return false;
                        }
                        else
                        {
                            using var outFile = new AndroidJavaObject("java.io.File", childDst);
                            using var filesCls = new AndroidJavaClass("java.nio.file.Files");
                            using var paths = new AndroidJavaClass("java.nio.file.Paths");
                            var emptyMore = new string[0];
                            using var sp = paths.CallStatic<AndroidJavaObject>(
                                "get", child.Call<string>("getAbsolutePath"), emptyMore);
                            using var dp = paths.CallStatic<AndroidJavaObject>("get", childDst, emptyMore);
                            using var optionCls = new AndroidJavaClass("java.nio.file.StandardCopyOption");
                            using var replace = optionCls.GetStatic<AndroidJavaObject>("REPLACE_EXISTING");
                            var opts = new AndroidJavaObject[] { replace };
                            filesCls.CallStatic<AndroidJavaObject>("copy", sp, dp, opts);
                        }
                    }
                }

                Debug.Log($"[3OS] JavaCopyDirectory OK {src} → {dst}");
                return Exists(dst);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] JavaCopyDirectory: {ex.Message}");
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
            // File.Exists is false for directories — always pair with Directory.Exists.
            return File.Exists(path) || Directory.Exists(path);
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
