using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Check
{
    /// <summary>
    /// 생성 코드가 유니티에서 `.bytes` 를 읽어 값을 내는지 확인한다.
    /// </summary>
    /// <remarks>
    /// **게임이 아니라 확인입니다.** 자동 전투도 화면도 없고, 이 파일이 하는 것은 테이블을
    /// 로드하고 값이 시트에 적힌 그대로인지 보는 것뿐입니다 — 그 자리까지가 이 샘플의 범위이고
    /// 나머지는 `samples/wildling/readme.md` 에 적혀 있습니다.
    ///
    /// 에디터에서는 `Wildling ▸ 데이터 확인`, 배치 모드에서는 아래처럼 돌립니다.
    ///
    ///     Unity.exe -batchmode -quit -projectPath samples/wildling/unity \
    ///               -executeMethod Wildling.Check.WildlingDataCheck.RunFromCommandLine \
    ///               -logFile -
    ///
    /// 배치 모드에서는 실패가 **종료 코드**로 나갑니다. 로그만 남기면 CI 가 통과로 읽습니다.
    /// </remarks>
    public static class WildlingDataCheck
    {
        private const string DataFolder = "tables";

        [MenuItem("Wildling/데이터 확인")]
        public static void RunFromMenu()
        {
            var report = Run(out bool ok);
            if (ok)
                Debug.Log(report);
            else
                Debug.LogError(report);
        }

        /// <summary>배치 모드의 진입점.</summary>
        /// <remarks>
        /// **보고를 파일로 씁니다.** `EditorApplication.Exit` 를 `-executeMethod` 안에서 부르면
        /// 이 기계의 에디터가 셧다운 경로에서 크래시합니다(`SubsystemManager::CleanupInstances`).
        /// 그래서 종료는 `-quit` 에 맡기고, 판정은 파일의 첫 줄이 합니다 — 읽는 쪽이 종료 코드
        /// 대신 그것을 봅니다. 로그만 남기면 크래시와 실패를 구별할 수 없습니다.
        /// </remarks>
        public static void RunFromCommandLine()
        {
            var report = Run(out bool ok);
            Debug.Log(report);

            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "기획데이터", "out", "unity-check.txt"));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (ok ? "OK" : "FAIL") + Environment.NewLine + report);

            Debug.Log($"보고를 {path} 에 썼습니다.");
        }

        private static string Run(out bool ok)
        {
            var log = new StringBuilder();
            var failures = new List<string>();

            log.AppendLine("=== 와일드링 데이터 확인 ===");

            string root = Path.Combine(Application.streamingAssetsPath, DataFolder);
            log.AppendLine($"경로 {root}");

            try
            {
                // 에디터와 배치 모드는 StreamingAssets 를 파일로 읽으므로 `UnityWebRequest` 를
                // 거치지 않습니다 — 어댑터가 경로에 `://` 가 있는지로 판단합니다.
                //
                // **`Task.Run` 으로 감쌉니다.** 그냥 기다리면 유니티의 동기화 컨텍스트에서
                // 블로킹하게 되고, 이어지는 작업이 그 컨텍스트로 되돌아오려다 멈춥니다 —
                // 배치 모드에서는 「Setting up scripting invocation from unattached thread」
                // 뒤에 에디터가 크래시합니다. 풀 스레드에서 시작하면 컨텍스트가 없으므로
                // 이어지는 작업도 풀에 남고, 메인 스레드가 기다리는 것이 안전해집니다.
                Task.Run(() => WildlingData.ReadAllAsync(root)).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                ok = false;
                return log.AppendLine($"!! 로드가 실패했습니다 — {e.Message}").ToString();
            }

            // ---------------------------------------------------------- 행 수

            var counts = new (string Name, int Rows, int Expected)[]
            {
                ("Monster", WildlingData.Monster.Records.Count, 54),
                ("MonsterAwakening", WildlingData.MonsterAwakening.Records.Count, 24),
                ("Skill", WildlingData.Skill.Records.Count, 46),
                ("SkillEffect", WildlingData.SkillEffect.Records.Count, 55),
                ("GrowthCurve", WildlingData.GrowthCurve.Records.Count, 350),
                ("ElementAffinity", WildlingData.ElementAffinity.Records.Count, 25),
                ("Region", WildlingData.Region.Records.Count, 5),
                ("Stage", WildlingData.Stage.Records.Count, 90),
                ("RewardEntry", WildlingData.RewardEntry.Records.Count, 369),
            };

            foreach (var (name, rows, expected) in counts)
            {
                log.AppendLine($"  {name,-18} {rows,6}행");
                if (rows != expected)
                    failures.Add($"{name} 이 {rows}행입니다 — {expected}행이어야 합니다.");
            }

            // ---------------------------------------------------------- 값

            // 시트에 적힌 그대로인지. 행 수만 보면 「읽었다」까지이고, 값을 보면 「맞게
            // 읽었다」까지입니다.
            var deer = WildlingData.Monster.FindByMonsterId("sprout_deer_1");
            if (deer is null)
            {
                failures.Add("`sprout_deer_1` 을 찾지 못했습니다.");
            }
            else
            {
                log.AppendLine($"  새싹사슴  hp={deer.Base.Hp} atk={deer.Base.Attack} "
                               + $"element={deer.Element} grade={deer.Grade}");

                Expect(failures, "Monster.Base.Hp", deer.Base.Hp, 420);
                Expect(failures, "Monster.Element", (int)deer.Element, (int)Element.Leaf);
                Expect(failures, "Monster.MaxStage", deer.MaxStage, 3);

                // `bitset` — 여울숲과 소금해안입니다.
                Expect(failures, "Monster.Habitat", (int)deer.Habitat, 0b00011);

                // 셀 배열. 구분자로 이어 적은 것이 원소 3개로 왔는지.
                int tags = deer.Tags?.Length ?? 0;
                log.AppendLine($"  태그 {tags}개 — {string.Join(", ", deer.Tags ?? Array.Empty<string>())}");
                Expect(failures, "Monster.Tags.Length", tags, 3);
            }

            // ---------------------------------------------------------- 참조

            // 참조가 링킹으로 실제 행이 되었는지. 키가 아니라 행입니다.
            var awakening = WildlingData.MonsterAwakening.FindByFromMonsterId("sprout_deer_1");
            if (awakening is null)
            {
                failures.Add("각성 관계 `sprout_deer_1` 을 찾지 못했습니다.");
            }
            else
            {
                var to = awakening.MonsterByToMonsterId;
                log.AppendLine($"  각성  {awakening.FromMonsterId} -> "
                               + $"{to?.MonsterId} ({to?.Name}) hp+{awakening.Gain.Hp}");

                if (to is null)
                    failures.Add("각성 후 행이 연결되지 않았습니다.");
                else if (to.Base.Hp <= 420)
                    failures.Add($"각성 후 hp 가 {to.Base.Hp} 로 늘지 않았습니다.");
            }

            // ---------------------------------------------------------- 다형

            // 판별자가 변종 타입으로 왔는지. `is` 로 좁혀지는 것이 이 표현의 요점입니다.
            var byVariant = new Dictionary<string, int>();

            foreach (var entry in WildlingData.RewardEntry.Records)
            {
                string kind = entry.Reward switch
                {
                    ItemReward => "ItemReward",
                    CurrencyReward => "CurrencyReward",
                    MonsterReward => "MonsterReward",
                    ShardReward => "ShardReward",
                    _ => "?",
                };

                byVariant.TryGetValue(kind, out int seen);
                byVariant[kind] = seen + 1;
            }

            log.AppendLine("  보상 변종 — "
                           + string.Join(", ", byVariant.Select(p => $"{p.Key} {p.Value}")));

            if (byVariant.ContainsKey("?"))
                failures.Add("어느 변종도 아닌 보상이 있습니다.");

            foreach (string wanted in new[] { "ItemReward", "CurrencyReward", "MonsterReward" })
            {
                if (!byVariant.ContainsKey(wanted))
                    failures.Add($"변종 `{wanted}` 이 하나도 나오지 않았습니다.");
            }

            // 변종의 참조가 행으로 연결되었는지. **참조는 이름 둘을 냅니다** — 컬럼 이름은
            // 키의 것이고 행은 `<대상>By<컬럼>` 입니다. 그 둘을 바꿔 고른 것이 도구 보고
            // §7 · §8 · §10 이었으므로, 여기서 둘 다 봅니다.
            var item = WildlingData.RewardEntry.Records
                .Select(row => row.Reward as ItemReward)
                .FirstOrDefault(reward => reward is not null);

            if (item is null)
                failures.Add("`ItemReward` 를 하나도 찾지 못했습니다.");
            else if (item.ItemByItemId is null)
                failures.Add("`ItemReward` 의 참조가 행으로 연결되지 않았습니다.");
            else
                log.AppendLine($"  아이템 보상 — {item.ItemByItemId.Name}"
                               + $"(`{item.ItemId}`) × {item.Amount}");

            // ---------------------------------------------------------- 복합 키

            var yield = WildlingData.RegionYield.FindByRegionIdAndHourBand("weir_forest", 0);
            if (yield is null)
                failures.Add("복합 키 조회 `(weir_forest, 0)` 가 아무것도 찾지 못했습니다.");
            else
                log.AppendLine($"  시간당 산출 — 골드 {yield.GoldPerHour} · 먹이 {yield.FoodPerHour}");

            // ---------------------------------------------------------- 멀티 로우

            var encounter = WildlingData.EncounterTable.FindByEncounterId("enc_weir_forest");
            if (encounter is null)
            {
                failures.Add("`enc_weir_forest` 을 찾지 못했습니다.");
            }
            else
            {
                int entries = encounter.Entries?.Length ?? 0;
                log.AppendLine($"  여울숲 출현 {entries}종");

                if (entries < 4)
                    failures.Add($"여울숲의 출현이 {entries}종입니다 — 4종 이상이어야 합니다.");
            }

            // ---------------------------------------------------------- 상수셋

            log.AppendLine($"  상수 — 방치 상한 {IdleConst.CapHours}시간 · "
                           + $"파티 {PartyConst.PartySize}마리 · 최대 {BattleConst.MaxTurn}턴");

            Expect(failures, "IdleConst.CapHours", IdleConst.CapHours, 8);
            Expect(failures, "PartyConst.PartySize", PartyConst.PartySize, 3);

            // ---------------------------------------------------------- 결과

            log.AppendLine();

            if (failures.Count == 0)
            {
                log.AppendLine("전부 통과했습니다.");
                ok = true;
                return log.ToString();
            }

            log.AppendLine($"실패 {failures.Count}건");
            foreach (string failure in failures)
                log.AppendLine($"  !! {failure}");

            ok = false;
            return log.ToString();
        }

        private static void Expect(List<string> failures, string what, int got, int wanted)
        {
            if (got != wanted)
            {
                failures.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} 이 {1} 입니다 — {2} 여야 합니다.", what, got, wanted));
            }
        }
    }
}
