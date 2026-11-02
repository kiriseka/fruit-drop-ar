using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Development tool, not part of the game. Builds the Android APK for a version from a menu item, so a build
/// can be started without the Build Profiles window. The result goes to the console and to Temp/Build.log,
/// which can be polled while the build runs.
/// </summary>
public static class BuildTools
{
    const string k_ApkPathKey = "BuildTools.ApkPath";
    const string k_DefaultApkPath = "Builds/Latest.apk";
    const string k_LogPath = "Temp/Build.log";

    /// <summary>
    /// Where the next APK goes, such as Builds/V0.5.apk. Kept for the editor session only.
    /// </summary>
    public static string apkPath
    {
        get => SessionState.GetString(k_ApkPathKey, k_DefaultApkPath);
        set => SessionState.SetString(k_ApkPathKey, value);
    }

    [MenuItem("Fruit Drop/Build Android APK")]
    static void BuildAndroidApk()
    {
        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                scenes.Add(scene.path);
        }

        string path = apkPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(path))
            File.Delete(path);

        File.WriteAllText(k_LogPath, $"Building {path} from {string.Join(", ", scenes)}\n");
        Debug.Log($"[BuildTools] Building {path}");

        // Started after the menu call returns, so whatever ran the menu item is not held for the whole build.
        EditorApplication.delayCall += () =>
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            // The summary's totalSize counts the content before compression, so report the APK file instead.
            var summary = report.summary;
            float apkMegabytes = File.Exists(path) ? new FileInfo(path).Length / (1024f * 1024f) : 0f;
            string line = $"{summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                $"APK {apkMegabytes:F1} MB, {summary.totalTime.TotalMinutes:F1} min";
            File.AppendAllText(k_LogPath, line + "\n");
            Debug.Log($"[BuildTools] {line}");
        };
    }
}
