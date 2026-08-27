using System;
using System.IO;
using UnityEngine;

namespace Wildling.Game
{
    /// <summary>
    /// 지금 시각이다.
    /// </summary>
    /// <remarks>
    /// **자동 플레이 검사가 8시간을 기다릴 수는 없습니다.** 시각을 한 자리로 모아 두면 검사가
    /// `Offset` 을 밀어 방치 정산을 그 자리에서 확인할 수 있습니다. 게임 코드는 이 값만 읽고
    /// `DateTime.UtcNow` 를 직접 부르지 않습니다.
    /// </remarks>
    public static class Clock
    {
        /// <summary>검사가 시간을 앞으로 미는 자리이다. 초이다.</summary>
        public static long Offset;

        public static long NowUtc
            => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Offset;
    }

    /// <summary>세이브를 읽고 쓴다.</summary>
    /// <remarks>
    /// 유니티의 `JsonUtility` 를 씁니다 — 외부 라이브러리가 없고, 저장 파일이 사람이 읽을 수
    /// 있는 형태로 남습니다.
    /// </remarks>
    public static class SaveStore
    {
        private static string Path
            => System.IO.Path.Combine(Application.persistentDataPath, "wildling-save.json");

        /// <summary>검사가 세이브를 건드리지 않게 하는 자리이다.</summary>
        public static bool Enabled = true;

        public static void Save(GameState state)
        {
            if (!Enabled || state is null)
                return;

            try
            {
                state.LastSeenUtc = Clock.NowUtc;
                File.WriteAllText(Path, JsonUtility.ToJson(state.ToSave(), true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"세이브를 쓰지 못했습니다 — {e.Message}");
            }
        }

        /// <summary>세이브가 없거나 읽을 수 없으면 새로 시작한다.</summary>
        public static GameState Load()
        {
            if (Enabled)
            {
                try
                {
                    if (File.Exists(Path))
                    {
                        var save = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path));
                        if (save != null)
                            return GameState.FromSave(save);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"세이브를 읽지 못해 새로 시작합니다 — {e.Message}");
                }
            }

            return GameState.NewGame(Clock.NowUtc);
        }

        public static void Erase()
        {
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"세이브를 지우지 못했습니다 — {e.Message}");
            }
        }
    }
}
