# 배너 재생성 프롬프트 (보류 — 이미지 생성이 되지 않아 나중에)

팔레트 출처: `brand/build-assets.mjs:40` · 구성 출처: `brand/readme.md`

## 고쳐야 하는 것 셋

- `Game Data Authoring & Build Tool` — 위치 표기를 바꾸면 어긋납니다
- `Smart encoding & compression` — 사실과 다릅니다. 범용 압축을 쓰지 않고 `flags`의 압축 비트가 비어 있습니다
- `TCB — Fast. Compact. Reliable.` 알약 — 지금 파생 단계에서 두 단계를 들여 지우고 있습니다

## 방침

- **글자는 그림에 넣지 않습니다.** 문구를 고칠 때마다 이미지를 다시 생성하지 않기 위해서입니다
- **라운드와 흰 여백도 넣지 않습니다.** 파생 단계에서 마스크로 처리합니다

## A판 — 글자 없음 · 라운드 없음 (권장)

```
Create a single full-bleed banner illustration, 1536x1024 landscape, no text of
any kind.

CANVAS: the artwork must fill the entire frame edge to edge. SQUARE CORNERS -
no rounded corners, no card, no panel, no frame, no border, no white or light
margin around the artwork, no outer drop shadow, no mockup or device framing.
The background gradient bleeds off all four edges. The image will be cropped and
corner-rounded later, so the outer 5% on every side must contain nothing
important.

STYLE: glossy 3D rendered cartoon, claymorphism, soft studio lighting, rounded
soft shapes, subtle drop shadows within the scene, playful and friendly, high
finish. Not photorealistic, not flat vector.

BACKGROUND: deep purple gradient, #24186C at the top fading to #180C60 at the
bottom, with a soft vignette. Scattered tiny floating sparkles (amber #FCC024 and
light violet) and two or three small rounded 3D squares drifting in the upper
right.

MASCOT (right of center): a cute chubby white rabbit, front-facing, upper body
only, large upright ears with soft pink inner ears (#FC8490), big glossy dark
eyes, ONE EYE WINKING, open happy smile, pink blush cheeks, small pink nose.
It wears a deep violet hoodie with a rounded violet (#846CF0) badge on the chest
bearing a single capital letter T. Its left paw rests on top of a spreadsheet
card, the right paw on the ground.

SPREADSHEET CARD (left of the rabbit, slightly overlapping it): a pastel lavender
window with a title bar carrying three small dots, and inside it a light grid of
empty cells. Three cells are filled with color: one violet, one amber, one green.
The cells must be EMPTY of any letters or numbers. This card is an object inside
the scene - it is the only rounded element, and it must not be mistaken for the
frame of the image.

CUBES (right side, not touching the edge): a vertical stack of three rounded 3D
cubes with glossy faces
 - top: violet #846CF0 cube with a curly-braces glyph
 - middle: green cube with a simple table/grid glyph
 - bottom: amber #FCC024 cube with an angle-brackets glyph
A small green sprout with two leaves at the base of the stack.

COMPOSITION: leave the LEFT THIRD of the canvas as clean empty gradient - it is
reserved for a wordmark and a headline added later. Nothing may overlap it. Keep
the bottom strip clean as well.

DO NOT INCLUDE: any words, letters, numbers, labels, captions, watermarks or
signatures anywhere, except the single letter T on the chest badge. No rounded
image corners, no border, no white margin. No feature chips or pills, no product
logos, no Excel or Google branding, no humans.
```

## B판 — 글자까지 넣는 경우

A판의 `CANVAS` · `BACKGROUND` · `MASCOT` · `SPREADSHEET CARD` · `CUBES` 를 그대로 두고 마지막
두 블록만 아래로 바꿉니다.

```
TEXT (left third, left-aligned, generous spacing):
 - Wordmark at the top: "Tabbit" in a bold rounded sans-serif, white, where the
   capital T is stylized as a pair of rabbit ears and the dot of the i is pink
   (#FC8490).
 - Headline below it, two lines, bold white: "Game Data" / "Compiler" - render
   the word "Compiler" in light violet (#A892F7).
 - One small paragraph under the headline in light lavender, four short lines:
   "Turn spreadsheet data into" / "validated, optimized" / "runtime data with" /
   "readers for every language."

FEATURE ROW (a single rounded white panel across the bottom, four equal cells,
each with a small rounded square icon on the left and two lines of text):
 - violet sheet icon    | "Author"   / "Keep using Excel"
 - green shield icon    | "Validate" / "Catch errors early"
 - amber bar-chart icon | "Optimize" / "Per-column encoding"
 - violet rocket icon   | "Build"    / "Data and readers"

SPELLING IS CRITICAL: every word above must be rendered exactly as written, with
correct spelling and no invented extra words.

The white feature panel at the bottom is an object inside the scene. It must not
reach the left, right or bottom edge of the canvas - keep purple gradient visible
around it, and keep the canvas corners square.

DO NOT INCLUDE: the word "compression" anywhere. No "TCB" pill, no
"Fast. Compact. Reliable." strip, no file-format abbreviations. No other text
than what is listed above. No product logos, no Excel or Google branding, no
humans.
```

## 받은 뒤 확인할 것

|확인|이유|
|--|--|
|`compression` 이라는 낱말이 없는지|형식은 범용 압축을 쓰지 않습니다|
|`TCB` 알약이 없는지|파일 형식의 약자는 브랜드 이미지가 말할 것이 아닙니다|
|시트 격자의 셀이 비어 있는지|생성 모델이 셀에 알아볼 수 없는 글자를 채우는 일이 잦습니다|
|가슴 배지가 `T` 한 글자인지|`mabbit` 아이콘은 같은 마스코트에 `M` 배지입니다|
|왼쪽 3분의 1이 비어 있는지 (A판)|문구를 얹을 자리입니다|
|네 변에 흰 테두리나 라운드가 없는지|라운드와 og 카드 1200×630 크롭에서 잘립니다|

## 파생 쪽에 딸려오는 작업

`inspect.mjs`는 흰 배경 위의 패널 경계를 찾는 방식이라 **꽉 찬 그림에는 찾을 경계가 없습니다.**
`build-assets.mjs`의 배너 항목은 잘라내기 좌표 대신 라운드 마스크 한 단계로 다시 적어야 하고,
그때 `TCB` 알약을 지우는 두 단계가 함께 없어집니다.
