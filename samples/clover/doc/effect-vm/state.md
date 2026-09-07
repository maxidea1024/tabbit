# 상태와 세이브

> [효과 VM으로](../effect-vm.md)

---

테이블은 **변하지 않는 것**입니다. 런에서 변하는 것은 전부 런 상태이고, 세이브에 들어가고,
리플레이가 재현해야 하는 것입니다.

## 데이터와 상태의 경계

|무엇|어디|
|--|--|
|조커 500종의 값과 효과|테이블. **변하지 않습니다**|
|지금 들고 있는 조커의 목록과 순서|런 상태|
|그 조커가 누적한 값 (`GrowSelf`)|런 상태|
|그 조커의 에디션과 스티커|런 상태|
|기본 덱 52장|테이블|
|**지금 덱의 카드들**|런 상태 — 강화·인장·에디션·영구 칩이 붙고 장수가 바뀝니다|
|족보 12종의 기본값과 증분|테이블|
|족보의 현재 레벨과 사용 횟수|런 상태|

**「지금 덱」이 런 상태인 것이 이 게임의 성질입니다.** 카드 한 장이 개체로 존재하고 자기
이력을 가지므로, 세이브가 카드 52장 이상을 각각 적습니다.

## 런 상태의 모양

```
Run
├─ seed · ante · blind_index · money · stake · deck_id
├─ hands_left · discards_left · hand_size
├─ jokers[]      id · edition · sticker · counters{} · order
├─ consumables[] id · edition
├─ deck[]        base_card_id · enhancement · seal · edition · bonus_chips · uid
├─ hand[] · played[] · discarded[]     deck 의 uid 참조
├─ hand_levels[] · hand_play_counts[]
├─ vouchers[] · tags_pending[]
├─ boss_id · boss_state{}
├─ round_targets{}   지정 족보 · 지정 랭크 · 지정 무늬 · 지정 카드
└─ rng{}         스트림마다의 상태
```

`uid`가 있는 이유는 하나입니다 — 같은 `Ace of Spades`가 덱에 둘 있을 수 있고, 하나에만
`Red Seal`이 붙습니다. **카드를 값으로 가리키면 그 둘이 구분되지 않습니다.**

## 카운터

`GrowSelf`가 늘리는 값입니다. 조커마다 이름이 다르지 않고 **다섯 칸으로 고정**입니다.

|칸|누가 쓰는가|
|--|--|
|`chips`|`creeper` · `square_trellis` · `cairn` · `tiny_tot` · `stone_keep`|
|`mult_add`|`long_path` · `green_shoot` · `red_ticket` · `flash_note` · `spare_gloves`|
|`mult_mul`|`star_chart` · `leech_vine` · `glass_ghost` · `shard_jar` · `bonfire` · …|
|`money`|`sky_rocket`|
|`sell_value`|`seed_pod` · `gift_tag`|

칸을 고정한 이유는 세이브의 모양이 조커 목록에 따라 달라지지 않게 하는 것입니다. **조커를
하나 더해도 세이브 형식이 바뀌지 않습니다.**

`charge`와 `tick` 두 칸이 더 있습니다 — `fizz_bottle`의 남은 10핸드와 `faint_outline`의
2라운드가 그것입니다.

## 리플레이

리플레이는 **시드와 액션 배열**입니다. 상태를 담지 않습니다.

```
{ "seed": "CLOVER-0001", "deck": "red_deck", "stake": "White",
  "actions": [
    { "t": "select_blind" },
    { "t": "reorder", "what": "hand", "order": [11, 23, 9, 13] },
    { "t": "play", "cards": [11, 23, 9] },
    { "t": "discard", "cards": [13] },
    { "t": "buy", "slot": 0 },
    { "t": "reorder", "what": "joker", "order": [42, 41] },
    { "t": "sell_joker", "index": 1 },
    { "t": "use_consumable", "index": 0, "targets": [5, 6] },
    { "t": "reroll" }, { "t": "leave_shop" }
  ] }
```

**카드는 `uid` 로 가리킵니다.** 자리는 액션 하나로 바뀌므로 인덱스로 가리키면 같은 수가
다른 카드를 가리키게 됩니다. 조커와 소모품은 인덱스인데, 그 줄의 차례도 `reorder` 로만
바뀌므로 되돌린 자리에서 같은 것을 가리킵니다.

**액션 목록이 판의 전부를 담습니다.** 액션을 거치지 않고 바뀐 것은 다시 돌린 판에 없습니다 —
`reorder` 가 있는 이유가 이것입니다. 자리는 규칙이고(득점은 낸 카드의 왼쪽부터, 조커도
왼쪽부터), 오랫동안 화면에서만 바뀌고 있었습니다.

구워 둔 리플레이를 다시 돌려 마지막 상태의 해시를 비교합니다. 해시가 다르면 어느
액션에서 갈라졌는지를 이분해서 찾습니다 — 그래서 각 액션 뒤의 상태 해시를 함께 적습니다.

## 세이브

**세이브도 액션 목록입니다.** 상태를 통째로 적지 않습니다 — 적으면 `Pcg32` 의 내부 값을 손으로
옮겨야 하고, 상태에 칸이 하나 늘 때마다 예전 세이브를 못 쓰게 됩니다. 되살리는 것은 `newRun`
뒤에 `apply` 를 차례로 돌리는 것이고, 그 길은 서버의 판정과 `headless` 가 지나는 길과
같습니다.

|무엇|형식|
|--|--|
|웹|`localStorage['clover.run']` 의 JSON. 브라우저마다 따로 남습니다|
|담는 것|시드 · 덱 · 스테이크 · 조커 풀 · 챌린지 · 랭크 · 액션 목록 · **마지막 상태의 해시**|
|되살릴 때 보는 것|다시 돌린 상태의 해시가 적어 둔 것과 같은가. 다르면 세이브를 버립니다|

데스크탑과 안드로이드도 같은 웹 빌드이므로 같은 형식입니다 — Electron 과 WebView 가 각자의
프로필에 둡니다. 규격은 [도중에 그만둔 판](../ui/start.md#도중에-그만둔-판)에 있습니다.

---

EOD
