using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 화면을 차례로 열어 그림으로 남긴다.
    /// </summary>
    /// <remarks>
    /// **눈으로 확인하는 자리입니다.** 자동 플레이 검사는 값이 맞는지를 보고, 이것은 그 값이
    /// 화면에 어떻게 나오는지를 봅니다 — 글자가 잘리거나 카드가 겹치는 것은 값으로는 잡히지
    /// 않습니다.
    ///
    ///     wildling.exe -shots &lt;폴더&gt;
    ///
    /// 순회가 끝나면 스스로 종료합니다. **마우스를 쓰지 않습니다** — 창 밖으로 입력이 새지
    /// 않고, 사람이 쓰는 기계에서 돌려도 방해가 되지 않습니다.
    /// </remarks>
    public sealed class ScreenTour : MonoBehaviour
    {
        public const string Flag = "-shots";

        private string _folder;
        private App _app;

        /// <summary>명령줄에 그 인자가 있으면 폴더를 낸다. 없으면 빈 문자열이다.</summary>
        public static string FolderFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == Flag)
                    return args[i + 1];
            }
            return "";
        }

        public static void Begin(App app, string folder)
        {
            var tour = app.gameObject.AddComponent<ScreenTour>();
            tour._app = app;
            tour._folder = folder;
            tour.StartCoroutine(tour.Walk());
        }

        private IEnumerator Walk()
        {
            Directory.CreateDirectory(_folder);
            var state = _app.State;

            // 순회가 볼 것이 있게 진행 상태를 조금 만들어 둡니다.
            state.ExpeditionRegionId = state.UnlockedRegions.First();
            state.ExpeditionStartedUtc = Clock.NowUtc - 5 * 3600;
            state.AddCurrency("gold", 400_000);
            state.AddCurrency("food", 20_000);

            var first = state.PartyMembers().FirstOrDefault();
            if (first != null)
            {
                int guard = 0;
                while (state.CanLevelUp(first) && guard++ < 40)
                    state.LevelUp(first);
                state.AddShards(first.SpeciesId, 400);
                state.SetCodex(first.MonsterId, CodexState.Studied);
            }

            // 등장이 여럿인 스테이지라야 광역기와 피해가 함께 보입니다.
            var stage = WildlingData.Stage.Records
                .FirstOrDefault(s => s.RegionId == state.UnlockedRegions.First()
                                     && s.Index == 7);

            var steps = new (string Name, Func<Screen> Make)[]
            {
                ("01-home", () => new HomeScreen()),
                ("02-expedition", () => new ExpeditionScreen()),
                ("03-codex", () => new CodexScreen()),
                ("04-codex-entry", () => new CodexEntryScreen(
                    first?.MonsterId ?? WildlingData.Monster.Records[0].MonsterId)),
                ("05-party", () => new PartyScreen()),
                ("06-monster", () => new MonsterScreen(first?.Uid ?? 0)),
                ("07-region", () => new RegionScreen()),
                ("08-shop", () => new ShopScreen()),
            };

            foreach (var step in steps)
            {
                _app.Go(step.Make(), false);
                yield return null;
                yield return new WaitForEndOfFrame();
                yield return null;

                string path = Path.Combine(_folder, step.Name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"화면 {step.Name} 을 남겼습니다.");
                yield return new WaitForSeconds(0.6f);
            }

            // **전투는 여러 번 담습니다.** 재생이 흐르므로 한 장으로는 무엇이 일어나는지
            // 보이지 않습니다.
            if (stage != null)
            {
                _app.Go(new BattleScreen(stage.StageId), false);
                for (int i = 0; i < 8; i++)
                {
                    yield return new WaitForSeconds(i == 0 ? 1.4f : 2.2f);
                    string name = $"09-battle-{i + 1}";
                    ScreenCapture.CaptureScreenshot(Path.Combine(_folder, name + ".png"));
                    Debug.Log($"화면 {name} 을 남겼습니다.");
                }
                yield return new WaitForSeconds(1.0f);
            }

            yield return new WaitForSeconds(0.8f);
            Application.Quit();
        }
    }
}
