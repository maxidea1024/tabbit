# 노이즈 그림

**가져온 것입니다.** [Screaming Brain Studios](https://screamingbrainstudios.com) 의
「Noise Texture Pack」이고 라이선스는 CC0 입니다 — 표시 없이 쓰고 고치고 나눌 수 있습니다.
[opengameart 의 꾸러미](https://opengameart.org/content/noise-texture-pack)에서 받았고,
파일 이름은 꾸러미 안의 이름을 소문자로 적은 것입니다.

**픽셀 값은 손대지 않았습니다.** 다만 꾸러미의 파일이 회색 그림을 RGBA 로 담고 있어서
**8비트 회색으로 다시 인코딩**했습니다 — 셰이더가 빨강 채널만 읽으므로 값이 같고, 다섯 장의
합이 1.9MB 에서 495KB 가 됩니다.

**셰이더가 노이즈를 만들지 않습니다.** 픽셀마다 해시를 열두 번 부르던 자리를 그림 한 번 읽는
것으로 바꿨습니다. 어느 셰이더가 어느 그림을 어떻게 쓰는지는
[재가 되는 것](../../../doc/ui/ash.md) 과 [`src/shader/noise.ts`](../../src/shader/noise.ts)
에 있습니다.

|파일|꾸러미의 이름|무엇|쓰는 자리|
|--|--|--|--|
|`grainy-1.png`|Grainy 1|**모래알.** 픽셀마다 독립인 잔 결에 큰 뭉침이 겹쳐 있습니다|재의 알갱이 하나하나 · 표면이 뚫리는 구멍 · 삭는 카드의 칸마다의 성질|
|`super-perlin-12.png`|Super Perlin 12|대비가 큰 큰 얼룩|재가 되는 앞. **앞이 선으로 보이지 않게 하는 것**|
|`cracks-9.png`|Cracks 9|가는 어두운 선. 칸이 30픽셀쯤입니다|삭기 직전에 벌어지는 금|
|`swirl-11.png`|Swirl 11|매끄럽게 굽이치는 결|바람. **기울기를 90도 돌려 흐름으로 씁니다** — 매끄러워야 기울기가 매끄럽습니다. 재가 날리는 것과 삭는 카드의 모래가 이것에 실립니다|
|`perlin-21.png`|Perlin 21|낮은 대비의 부드러운 결|배경의 프랙탈 · 재의 연기 · 삭는 카드의 소멸선|
|`*-256.png`|위의 256 판|모래알 · 큰 얼룩 · 부드러운 결 셋|**핸드폰이 읽는 것.** 그쪽 셰이더가 쓰는 것이 이 셋이고, 합쳐서 98KB 입니다|

## 고른 방법

**눈으로 고르지 않았습니다.** 꾸러미가 262장이고 결의 굵기는 눈으로 견주기 어려워서, 장마다
**이웃 픽셀의 차이와 64픽셀 떨어진 픽셀의 차이의 비**를 재어 줄을 세웠습니다 — 그 비가 1에
가까우면 픽셀마다 독립인 모래알이고, 0에 가까우면 부드러운 큰 결입니다.

|고른 것|그 비|왜 그 자리인가|
|--|--|--|
|Grainy 1|0.66|모래알이면서 **큰 뭉침이 함께 있습니다.** 순수한 잡음(Grainy 5, 0.99)은 뭉침이 없어 재가 고르게 깔립니다|
|Super Perlin 12|0.02|가장 부드러운 축에 **대비가 가장 큽니다**(표준편차 0.36). 앞을 크게 휘게 하는 데 그 둘이 다 필요합니다|
|Swirl 11|0.03|기울기를 쓰므로 매끄러운 것 가운데 골랐습니다|

**이음이 없는 것만 후보였습니다.** 262장 가운데 33장이 이어 붙였을 때 자리가 어긋나고, 그런
그림은 되풀이되는 자리마다 선이 보입니다. Voronoi 는 후보에 있었으나 **다각형 하나가
44픽셀**이라 재가 아니라 깨진 판이 되어 뺐습니다.

```
All Screaming Brain Studios assets have been released under the CC0/Public Domain License.
You are free to use these assets in any and all projects, commercial or non-commercial,
with no restrictions, and can be released with or without credit.
```
