using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Wildling.Check
{
    /// <summary>
    /// 씬을 만들고 standalone 으로 빌드한다.
    /// </summary>
    /// <remarks>
    /// **씬도 코드가 만듭니다.** 씬 파일에는 부트 오브젝트 하나만 들어가고 나머지는 실행 중에
    /// 조립되므로, 씬 YAML 을 손으로 고칠 일이 없고 diff 도 남지 않습니다.
    /// </remarks>
    public static class WildlingBuild
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Wildling/씬 만들기")]
        public static void MakeScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                    NewSceneMode.Single);
            var boot = new GameObject("boot");
            boot.AddComponent<Game.Boot>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            Debug.Log($"씬을 {ScenePath} 에 만들었습니다.");
        }

        [MenuItem("Wildling/Windows 빌드")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "wildling.exe");

        /// <summary>배치 모드의 진입점이다. 보고를 파일로 쓴다.</summary>
        public static void BuildFromCommandLine()
        {
            string report;
            bool ok;

            try
            {
                var result = Build(BuildTarget.StandaloneWindows64, "wildling.exe");
                ok = result.summary.result == BuildResult.Succeeded;
                report = $"결과 {result.summary.result}\n"
                         + $"크기 {result.summary.totalSize / (1024 * 1024)} MB\n"
                         + $"오류 {result.summary.totalErrors} · 경고 {result.summary.totalWarnings}\n"
                         + $"산출 {result.summary.outputPath}";
            }
            catch (Exception e)
            {
                ok = false;
                report = $"!! 빌드가 예외로 끝났습니다 — {e}";
            }

            Debug.Log(report);

            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "design-data", "out", "unity-build.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (ok ? "OK" : "FAIL") + Environment.NewLine + report);
        }

        private static BuildReport Build(BuildTarget target, string executable)
        {
            if (!File.Exists(ScenePath))
                MakeScene();

            if (EditorBuildSettings.scenes.Length == 0
                || EditorBuildSettings.scenes.All(s => s.path != ScenePath))
            {
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(ScenePath, true),
                };
            }

            string output = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "build", "windows", executable));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };

            return BuildPipeline.BuildPlayer(options);
        }
    }
}
