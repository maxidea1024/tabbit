# `art-source/` — 아이콘의 원본

**이 폴더는 커밋되지 않습니다.** 원본이 장당 250 KB 남짓이고 54장이라 14 MB 입니다. 게임에
들어가는 것은 이것을 192픽셀로 줄인 `unity/Assets/Resources/art/icon/wl_*.png` 이고, 그쪽이
커밋됩니다.

## 어디서 오는가

`design-data/out/monster-prompts.tsv` 의 문장으로 이미지 생성 서비스가 만듭니다. 그 표는
`tools/monster_art.py` 가 `Monster.tsv` 에서 만들고 — **종이 생김새를, 속성이 색을, 단계가
나이를 정합니다.** 그래서 같은 종의 세 단계가 같은 생김새로 자라고, 같은 속성끼리 같은 색을
씁니다.

## 다시 만드는 절차

```
python samples/wildling/design-data/tools/monster_art.py     # 프롬프트 표
(이미지 생성 서비스로 54장을 이 폴더에 wl_<id>.jpg 로)
Unity.exe -batchmode -quit -nographics -projectPath samples/wildling/unity \
          -executeMethod Wildling.Check.WildlingArtImport.RunFromCommandLine -logFile -
```

**원본이 없어도 게임은 돕니다.** 줄여 놓은 아이콘이 커밋되어 있고, 그것마저 없으면
`tools/art.py` 가 도형으로 그린 아이콘을 만듭니다 — 그쪽은 의존성이 하나도 없습니다.
