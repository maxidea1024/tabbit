using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Wildling.Game;

namespace Wildling.Check
{
    /// <summary>
    /// 자동 플레이 검사를 에디터와 배치 모드에서 돌린다.
    /// </summary>
    /// <remarks>
    /// **보고를 파일로 씁니다.** `EditorApplication.Exit` 를 `-executeMethod` 안에서 부르면
    /// 이 기계의 에디터가 셧다운 경로에서 크래시하므로, 종료는 `-quit` 에 맡기고 판정은 파일의
    /// 첫 줄이 합니다 — `WildlingDataCheck.cs` 와 같은 규칙입니다.
    /// </remarks>
    public static class WildlingPlayCheck
    {
        [MenuItem("Wildling/자동 플레이 검사")]
        public static void RunFromMenu()
        {
            string report = AutoPlay.Run(out bool ok);
            if (ok)
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        public static void RunFromCommandLine()
        {
            string report;
            bool ok;

            try
            {
                report = AutoPlay.Run(out ok);
            }
            catch (Exception e)
            {
                ok = false;
                report = $"!! 검사가 예외로 끝났습니다 — {e}";
            }

            Debug.Log(report);

            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "design-data", "out", "unity-play.txt"));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (ok ? "OK" : "FAIL") + Environment.NewLine + report);

            Debug.Log($"보고를 {path} 에 썼습니다.");
        }
    }
}
