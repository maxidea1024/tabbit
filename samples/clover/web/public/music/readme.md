# 배경음 3곡 — 가져온 것

**자작이 아닙니다.** 트럼프 52장의 얼굴, 효과음 38개와 같은 예외입니다.

|무엇|값|
|--|--|
|출처|[HoliznaCC0](https://freemusicarchive.org/music/holiznacc0/) 의 [Busted Guitar (JAZZ)](https://freemusicarchive.org/music/holiznacc0/busted-guitar-jazz)|
|라이선스|[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) — 공유 재산. 표기 의무가 없습니다|
|받은 날|2026-09-01|
|손댄 것|320kbps mp3 를 vorbis 로 다시 구웠습니다. 곡 자체는 그대로입니다|

## 파일의 이름

**파일 이름이 곧 그 화면입니다.** 효과음이 신호의 이름을 쓰는 것과 같습니다.

|파일|어디서|원곡|
|--|--|--|
|`title.ogg`|타이틀|`1 (jazz)` · 2분 3초|
|`round.ogg`|블라인드 고르기와 라운드|`3 (jazz)` · 2분 42초|
|`shop.ogg`|상점|`2 (jazz)` · 2분 44초|

## 다시 만드는 법

곡의 주소는 앨범 쪽에서 찾습니다. 파일 이름이 해시라 여기 적어 두어도 언젠가 어긋납니다.

```
curl -sSL "https://freemusicarchive.org/music/holiznacc0/busted-guitar-jazz/1-jazz/" \
  | grep -oE 'files\.freemusicarchive\.org[^"]*\.mp3'
```

받은 mp3 를 그대로 담지 않습니다. **320kbps 로 4곡이 28MB 이고, 웹 빌드 전체가 16MB
입니다.** vorbis 로 다시 구우면 셋이 4.5MB 입니다.

```
ffmpeg -i 1-jazz.mp3 -c:a libvorbis -q:a 2 -ar 44100 title.ogg
```

## 이 곡을 고른 이유

**잔잔한 재즈 기타입니다.** 이 게임에서 사람의 역할은 패를 보고 고르는 것이라, 몰아치는
곡은 그 판단을 방해합니다 — 카드가 놓이고 칩이 꽂히는 소리가 위에서 들려야 하므로 배경은
자리를 비켜 주어야 합니다.

그래서 기본 음량도 효과음보다 낮습니다. 옵션에서 효과음과 **따로** 끄고 줄일 수 있습니다.
