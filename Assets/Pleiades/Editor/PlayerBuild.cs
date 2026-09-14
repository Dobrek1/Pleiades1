using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Pleiades.Editor
{
    public static class PlayerBuild
    {
        const string ScenePath = "Assets/Pleiades/Scenes/Boot.unity";

        [MenuItem("Pleiades/Build Windows x64 Player")]
        public static void BuildWindows64()
        {
            EnsureBootScene();
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Win64"));
            Directory.CreateDirectory(outDir);
            var exe = Path.Combine(outDir, "Pleiades1.exe");

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Build failed: " + report.summary.result);

            Debug.Log("Pleiades Win64 build OK: " + exe);
        }

        // Unity -batchmode -quit -projectPath ... -executeMethod Pleiades.Editor.PlayerBuild.BuildWindows64
        public static void BuildWindows64Cli() => BuildWindows64();

        static void EnsureBootScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Pleiades/Scenes");
            if (File.Exists(ScenePath))
            {
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(ScenePath, true)
                };
                return;
            }

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            // Boot auto-spawns via RuntimeInitializeOnLoad; empty/default scene is enough.
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}
