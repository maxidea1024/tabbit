# 그림 생성 규격

> [clover 문서로](readme.md)

카드 그림을 무엇으로 어떻게 굽는지의 정본입니다. 프롬프트 골격과 소재별로 채우는 칸,
판정 절차, 반복되는 실패 유형이 여기 있습니다.

---

## 규격의 요지

**화풍은 손으로 그린 양식화된 게임 일러스트입니다.** 형태를 두껍게 과장하고 표면을 넓은
면으로 단순화합니다. 사실적으로 정교하게 그리지 않습니다.

이 화풍을 고른 근거가 둘입니다.

- **88×124 에서 읽힙니다.** 카드는 [`theme.ts`](../web/src/render/theme.ts) 의
  `jokerWidth 88` · `jokerHeight 124` 로 그려지고, 640×960 그림이 3.6배 축소되어
  들어갑니다. 두꺼운 실루엣과 아래 그림자가 그 크기에서 형태를 유지합니다.
- **AI 이미지의 기본 화풍과 겹치지 않습니다.** `painterly digital oil painting` ·
  `cinematic` · `concept-art quality` 같은 말은 생성기의 기본 화풍을 이름으로 부르는
  것입니다. 형태를 과장하고 면을 단순화하면 그 신호가 나타나지 않습니다.

---

## 프롬프트 골격

`%s` 자리가 소재별로 채우는 칸입니다. 나머지는 전부 공통입니다.

```
Hand-painted stylized fantasy game illustration of <소재>.

FORM - THE PROPORTIONS ARE DELIBERATELY WRONG: exaggerated chunky shapes with a
bold readable silhouette, thick rounded masses, small details thrown away into
the big shapes, <과장할 곳>. It must NOT be anatomically, botanically or
mechanically accurate.
OVER-BUILD IT, like a hand-carved painted wooden toy. IF THE SUBJECT IS
MAN-MADE: fat chamfered edges and thick bevels everywhere, oversized rivets,
bolts, straps, bands, studs and metal trim, a heavy solid base it sits on.
IF THE SUBJECT IS A PLANT, AN ANIMAL OR A PERSON: add NO hardware, NO rivets,
NO bolts, NO metal fittings at all - instead exaggerate its own natural masses,
fat rounded petals, thick stems, big simple leaves, heavy stubby limbs,
oversized head and hands. Either way nothing is thin, delicate or fiddly, and
the silhouette reads as one solid heavy thing at thumbnail size.

SURFACE: simplified into a few broad planes with confident economical brush
strokes left visible, crisp hard specular highlights, clean deliberate edges,
deep heavy shadow beneath the form. Polished high-craft finish, not sketchy,
not photographic.

COLOUR: <팔레트>

LIGHT: <광원>

BACKGROUND - build it in FOUR FLAT LAYERS. It is RICH and FURNISHED, never a
plain fill and never bare:
(1) a field of <배경색> with loose visible brush texture across the whole frame;
(2) one large simple shape behind the subject - <배경 형태> - CLEARLY READABLE,
    distinctly lighter and shifted in hue from the field, reaching the top and
    side edges;
(3) A SECOND LAYER OF STRUCTURE behind and beside that shape, painted in a close
    value so it reads as depth rather than clutter: <구조층>;
(4) several simple secondary elements painted loosely and spread across the
    frame, not clustered at the subject: <부차 요소>.
The background is BUSY ENOUGH TO LOOK AT but every part of it is PAINTED MORE
LOOSELY and in a NARROWER VALUE RANGE than the subject, so the subject still
reads first.
Depth comes from these flat layers overlapping, NOT from a vignette and NOT from
a spotlight. The background value STAYS EVEN FROM CORNER TO CORNER - no dark
corners, no darkened border, no inner frame.

THE ARTWORK FILLS THE ENTIRE IMAGE EDGE TO EDGE. No canvas edge, no paper sheet,
no torn edge, no picture frame, no mount, no table, no mockup.
No glowing particles, no nebula, no lens flare.
Vertical composition, the subject very large and centred, nearly touching the
edges. NO TEXT WHATSOEVER - no letters, no words, no numbers, no logos,
no signature.
```

**대문자는 의도한 것입니다.** 그 자리들이 지시를 반복해도 지켜지지 않던 곳이고,
대문자로 적은 뒤에 지켜졌습니다.

---

## 항목 다섯

|항목|공통 · 소재별|내용|
|--|--|--|
|`FORM`|**소재별**|무엇을 부풀릴지. 이 한 줄이 화풍을 만듭니다|
|`SURFACE`|**공통**|넓은 면 · 남은 붓질 · 굵은 하이라이트 · 아래 그림자|
|`COLOUR`|**소재별**|4색 안팎의 팔레트|
|`LIGHT`|**소재별**|빛이 어디서 나오는지. 초점을 만들어야 합니다|
|`BACKGROUND`|**공통 문법 + 소재별 값 4개**|층 넷. 배경색 · 배경 형태 · **구조층** · 부차 요소|

**`LIGHT` 가 작은 크기에서의 읽힘을 결정합니다.** 화면의 가장 밝은 한 점이 시선을
고정하므로, 광원이 초점을 만들지 않는 그림은 88px 에서 얼룩으로 보입니다. 흐린 낮빛처럼
초점 없는 광원을 적으면 그 결과가 됩니다.

---

## 소재별로 채우는 칸

조커는 6개, 행성은 3개입니다. 갈래의 성질이 균일할수록 공통으로 내려갑니다.

|칸|예 — 금고|예 — 화성|
|--|--|--|
|`FORM` 과장할 곳|납작하고 무겁게, 리벳과 무쇠 띠를 크게|(갈래 공통)|
|`COLOUR` 팔레트|검정 · 벼린 금 · 놋 · 우설홍|녹빛 붉은흙 · 마른 황토 · 검정 · 창백한 극관|
|`LIGHT` 광원|뚜껑 틈에서 위로 나옵니다|(갈래 공통) 오른쪽 위에서 오는 강한 빛|
|배경색|은은한 따뜻한 갈색|은은한 짙은 벽돌색|
|배경 형태|아치형 돌 벽감|(갈래 공통) 넓은 궤도 띠|
|부차 요소|뚜껑에서 던져진 빛살 · 굵은 금빛 알갱이|(갈래 공통) 굵은 별점 · 얇은 먼지 띠|

### 배경의 밀도

**배경에 볼 것이 있어야 합니다.** 층 셋으로 두었던 동안 배경이 색면 하나에 형태
하나뿐이라 헐렁했습니다 — 원인은 금지 목록에 있던 `No landscape, no fine detail`
입니다. 풍경 삽화가 되는 것을 막으려던 것인데 밀도까지 함께 막혔습니다.

**세부를 없애는 대신 대비를 낮춥니다.** 배경을 주제보다 느슨하게 칠하고 명도 폭을
좁히면, 배경이 풍부해도 주제가 먼저 읽힙니다. 2026-09-11에 층 넷으로 16장을 뽑아
확인하였습니다 — 배경 편차는 그대로인데 볼 것이 늘었습니다.

**둘째 구조층은 소재의 세계에서 고릅니다.** 돈놀이꾼 뒤의 장부 선반, 동전 압착기 뒤의
공구 벽감과 작업대, 링의 투사 뒤의 관중 실루엣입니다.

### 과하게 짓기

**이 항목 하나가 화풍의 절반입니다.** `FORM` 에 「두껍고 뭉툭하게」만 적으면 모델이
형태를 거의 바꾸지 않습니다 — 2026-09-11에 양귀비를 뽑았더니 식물학적으로 정확한
그림이 나왔고, 「비율이 일부러 틀려야 한다」와 「깎아 만든 나무 장난감처럼 과하게
짓는다」를 넣은 뒤에 바뀌었습니다.

**어휘를 소재의 종류로 나눕니다.** 나누지 않고 「과장된 리벳과 띠」를 공통으로 적었더니
양귀비 꽃술에 볼트를 박았습니다.

|소재|과하게 짓는 법|
|--|--|
|사람이 만든 것|굵은 모따기와 두꺼운 턱, 과장된 리벳 · 볼트 · 띠 · 쇠장식, 두꺼운 받침|
|식물 · 동물 · 사람|쇠장식을 붙이지 않습니다. 제 덩어리를 과장합니다 — 두꺼운 꽃잎, 굵은 줄기, 큰 잎, 뭉툭한 팔다리, 큰 머리와 손|

어느 쪽이든 얇거나 섬세하거나 자잘한 것이 없어야 하고, 실루엣이 작은 크기에서 무거운
덩어리 하나로 읽혀야 합니다.

### 배경 형태의 어법

**소재의 세계에서 고릅니다.** 문지기 뒤에는 무쇠 문, 두 가면 뒤에는 무대 아치,
이빨 덫 뒤에는 판자벽입니다. 형태가 소재를 설명하면 배경이 장식에 그치지 않습니다.

**장소를 그리지 않습니다.** 배경이 주제를 둘러싼 세계의 한 조각이지 무대가 아닙니다 —
들판과 하늘을 그리게 하면 카드가 아니라 풍경 삽화로 읽힙니다.

돌려 쓰는 형태는 아치 · 원반 · 창 · 벽감 · 문 · 판자벽입니다.

---

## 갈래별 상태

|갈래|장수|상태|공통으로 내린 것|
|--|--|--|--|
|조커|500|**확정** — 표본 확인|`SURFACE` · 금지 목록|
|행성|12|**확정** — 전량 확인|`FORM` · `LIGHT` 방향 규칙 · 배경 형태|
|타로|22|미확인|`COLOUR` · `LIGHT` · 배경 전부를 공통으로 내릴 것으로 예상합니다|
|스펙트럴|18|미확인|형태가 모호한 갈래이므로 두꺼운 실루엣 규격과 충돌할 수 있습니다|
|보스|28|미확인|정사각(`1:1`)입니다|
|태그|24|미확인|정사각입니다|
|트럼프 한 벌|168|미확인|세트마다 화풍이 따로 있습니다 — [카드 한 벌](card-set.md)|
|팩|5|미확인|—|

---

## 금지 목록

공통입니다. 각각이 실제로 나타난 실패를 방지합니다.

|금지|막는 것|
|--|--|
|`no vignette, no dark corners, no darkened border, no inner frame`|모서리가 검게 죽어 그림이 안쪽 원만 쓰는 것|
|`no canvas edge, no paper sheet, no torn edge, no picture frame, no mount, no table, no mockup`|액자에 든 그림을 책상에서 찍은 사진|
|`no glowing particles, no nebula, no lens flare`|AI 기본 화풍의 신호|
|`NO TEXT WHATSOEVER`|그림 안의 글자. 이름과 수치는 화면이 적습니다|
|배경의 세부를 금지하지 않습니다|`no landscape, no fine detail` 을 두었더니 밀도까지 막혔습니다. 대신 **주제보다 느슨하게 칠하고 명도 폭을 좁게** 요구합니다 — 「배경의 밀도」 절|

**갈래별 예외를 둡니다.** 행성에서는 `no starfield` 를 `no dense starfield` 로 바꾸고
별을 부차 요소에 이름 붙여 넣었습니다 — 금지하면서 그리라고 적으면 모순입니다.

---

## 판정 절차

**640에서의 우열이 88px 에서 뒤집힙니다.** 그러므로 두 크기를 함께 확인합니다.

|순서|무엇|
|--|--|
|1|2K 원본을 640×960 으로 축소하고 `q88` webp 로 변환합니다|
|2|**640 판정** — 팔레트가 갈리는지, 배경에 내용이 있는지|
|3|**176×248 판정** — 실제 표시 크기(88×124 에 DPR 2)로 축소해 형태가 읽히는지|
|4|그림을 `web/public/art/<갈래>/` 에 넣습니다. **백업하지 않습니다** — 아래 참조|
|5|**판 스크린샷** — 조커는 판에, 소모품은 도감에 세워 찍습니다|
|6|확인이 끝나면 원본으로 복구하고 임시 도구를 삭제합니다|

**원본을 폴더로 복사하지 않습니다.** git 이 이미 갖고 있습니다 — 그림은 추적되는
파일이므로 커밋 전이면 `HEAD` 가 원본이고, 커밋한 뒤에는 그 커밋의 앞이 원본입니다.
폴더로 한 벌 더 두고 그것을 커밋하면 같은 그림이 저장소에 두 벌 들어가고, 지워도
히스토리에서는 없어지지 않습니다.

```
git checkout HEAD -- samples/clover/web/public/art/joker/      # 커밋 전
git checkout <그 커밋>^ -- samples/clover/web/public/art/joker/  # 커밋한 뒤
```

**5번을 건너뛰면 판정이 성립하지 않습니다.** 카드 틀 · 희귀도 테두리 · 이름판과 함께
보아야 실제 조건입니다. 그리고 그림을 교체한 뒤 다시 찍지 않으면 스크린샷이 이전
그림의 것으로 남습니다.

수치로 확인할 것이 둘 있습니다.

- **비네트** — 모서리 4곳의 평균 명도를 전체 평균으로 나눕니다. `0.95 ~ 1.15` 가 정상이고,
  `0.8` 미만이면 모서리가 죽은 것입니다.
  **다만 광원이 주제 안에 있으면 예외입니다** — 모닥불(0.65)과 별 대장간(0.56)처럼 주제가
  빛을 내는 그림은 모서리가 어두운 것이 물리적으로 맞습니다.
- **명도 대비** — 명도 표준편차가 `35` 미만이면 88px 에서 형태가 약합니다.

### 소모품을 화면에 세우는 방법

`__clover.grantConsumable` 은 타로만 놓습니다. 행성과 스펙트럴은 도감을 씁니다.
도감은 만나 본 것만 앞면이므로 저장에 미리 기록합니다.

```js
localStorage.setItem('clover.collection', JSON.stringify({ planet: [...ids] }))
```

키와 형식은 [`core/collection.ts`](../web/src/core/collection.ts) 의 `KEY` 와 같습니다.

---

## 실패 유형

**반복해서 나타난 것들입니다.** 새 갈래를 시작할 때 먼저 확인합니다.

|증상|원인|고치는 문구|
|--|--|--|
|액자 · 캔버스 실물을 그립니다|매체를 실물 재료로 지정|`THE ARTWORK FILLS THE ENTIRE IMAGE EDGE TO EDGE` + 실물 금지 목록|
|모서리가 검게 죽습니다|`vignette` · `dark corners`|`VALUE STAYS EVEN FROM CORNER TO CORNER`|
|배경이 단색 칠이 됩니다|`only a little lighter than the field`|층 넷으로 명시하고 `never a plain fill` 을 부기|
|배경이 헐렁합니다|`No landscape, no fine detail` 이 밀도까지 막음|**구조층을 더하고** 금지를 「주제보다 느슨하게, 명도 폭을 좁게」로 바꿉니다|
|생물이 귀여워집니다|`머리를 크게` · `cartoon proportions`|`eyes SMALL and NARROW` · `NOT cute, no big round eyes`|
|풍경 삽화로 읽힙니다|배경에 장소를 그리게 함|형태 하나 + **구조층은 주제의 세계 안에서**|
|빛이 주제 뒤로 갑니다|`LIGHT` 를 적지 않음|광원을 명시하고 `Nothing glows behind <주제>` 를 부기|
|사물이 떠 있습니다|바닥을 적지 않음|`It SITS ON THE GROUND` + 바닥띠를 부차 요소에|
|주제가 배경에 잠깁니다|주제와 배경의 명도가 근접|팔레트에서 명도를 벌리고 `LIGHT` 로 초점을 만듭니다|
|형태가 사실적입니다|`FORM` 에 「두껍게」만 적음|「비율이 일부러 틀려야 한다」와 [과하게 짓기](#과하게-짓기)|
|꽃과 짐승에 볼트가 박힙니다|과하게 짓는 어휘를 소재 종류로 나누지 않음|사람이 만든 것에만 쇠장식, 생물은 제 덩어리를 과장|
|같은 묶음의 소재가 한 그림이 됩니다|팔레트 · 광원 · 배경을 묶음 하나에 한 벌만 둠|**배경은 장마다 따로 적습니다.** 새 5종이 같은 파란 새가 된 것이 이것입니다|
|소재의 고유색이 팔레트에 덮입니다|팔레트가 소재까지 지배|`The subject keeps its own natural colours - the palette governs the background and the light, never the subject`|
|숫자가 그려져 들어갑니다|프롬프트에 「(1)(2)(3)(4)」로 층을 번호 매김|**프롬프트에 숫자를 한 글자도 쓰지 않습니다.** `NO TEXT` 를 적어도 새어 들어옵니다|
|낱말이 다른 뜻으로 읽힙니다|뜻이 둘인 낱말을 그대로 넘김|문장으로 풀어 적습니다 — `creeper` 가 마인크래프트 괴물로, `bunting` 이 새로 나왔습니다|
|추상어가 골렘이 됩니다|`a hunch` 처럼 그릴 것이 없는 낱말|눈에 보이는 장면으로 바꿔 적습니다|

**같은 문구가 모델에 따라 반대로 작동합니다.** 「층 넷 · `RICH and FURNISHED` ·
`BUSY ENOUGH TO LOOK AT`」은 배경을 헐렁하게 내놓던 Nano Banana Pro 를 밀어 올리려고 쓴
말입니다. 2026-09-11에 같은 문장을 ChatGPT 에 넣었더니 배경의 장미와 잎을 주제만큼 그려
카드가 아니라 풍경 삽화가 되었습니다. **문구는 그 모델에서 확인한 것이고, 모델을 바꾸면
다시 확인해야 합니다.**

**출력 검열은 프롬프트로 예측되지 않습니다.** 광대 소재가 여러 시도에서 모두 거부된 사례가
있습니다. 소재의 낱말을 바꾸는 것이 유일한 대응이고, [`art.py`](../design-data/tools/art.py) 의
`OVERRIDE` 가 그 자리입니다.

---

## 모델과 API

|항목|값|
|--|--|
|모델|`gemini-3-pro-image`|
|`endpoint`|`POST https://generativelanguage.googleapis.com/v1beta/interactions`|
|인증|헤더 `x-goog-api-key`|
|장당 단가|**$0.134**. Batch API 는 절반|
|장당 소요|40 ~ 60초. 모델이 생각 단계를 거칩니다|
|동시 요청|**2개.** 4개로 124장을 돌렸더니 68장째부터 50장이 잇달아 `429 TooManyRequests` 로 거부되었습니다 — 거부된 것은 과금되지 않습니다|

**형식과 크기의 제약이 셋입니다.**

- **`image/png` 은 거부됩니다.** `image/jpeg` 만 받습니다. webp 로 다시 변환하므로
  손실은 문제가 되지 않습니다.
- **`aspect_ratio` 로 지정합니다.** 픽셀이 아닙니다. `2:3` 이 640×960 과 정확히 같고,
  정사각 갈래는 `1:1` 입니다.
- **`1K` 와 `2K` 가 같은 값입니다.** 둘 다 이미지 출력 1,120토큰입니다(실측). `2K` 가
  1696×2528 이고 `1K` 가 848×1264 이므로, **항상 `2K` 로 요청하고 축소해서 씁니다.**
  원본을 보관하면 나중에 표시 해상도를 올릴 때 다시 굽지 않습니다.

응답은 base64 이고 구조가 문서와 다를 수 있으므로 탐색해서 찾습니다.

**여러 장을 이어 구울 때는 재시도를 둡니다.** 제한은 시간이 지나면 풀리므로, 실패한 것을
20초 · 40초 · 60초 기다렸다 다시 부르면 대부분 통과합니다. **이미 있는 파일은 건너뛰게**
두면 중간에 끊겨도 다시 돌려 이어집니다.

**대시보드에서 확인합니다.** `429` 가 실패 수와 같고 `400 BadRequest` 가 0이면 프롬프트
문제가 아니라 제한입니다.

---

## 용량

|화풍|장당 `q88` webp|
|--|--|
|이 규격|**39 ~ 66KB**|
|이전 화풍(평평한 스크린프린트)|17 ~ 28KB|

**96MB 상한과는 무관합니다.** [`render/art.ts`](../web/src/render/art.ts) 의 `BUDGET` 은
GPU 텍스처 예산이고 픽셀마다 4바이트이므로, 640×960 이면 화풍과 무관하게 2.4MB 입니다.
늘어나는 것은 배포 용량입니다.

---

## 남은 일

|무엇|
|--|
|`art.py` 의 프롬프트 세트를 이 규격으로 재작성|
|`art_gen.py` 에 `gemini-3-pro-image` 경로 추가 — JPEG 수신 · 2K 원본 보관 · 축소 · 병렬 · 실패 검출|
|카드 틀 조정 — 희귀도 테두리를 두껍게, [`joker-view.ts`](../web/src/render/joker-view.ts) 의 `plate` 색조를 중립으로|
|소재별 칸을 `Joker.tsv` 옆에 컬럼으로 추가|
|미확인 갈래 6개의 갈래별 공통값 확정|

EOD
