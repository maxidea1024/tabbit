# -*- coding: utf-8 -*-
"""와일드링 54종의 그림 프롬프트를 표에서 만든다.

**종·속성·단계가 프롬프트를 정합니다.** 문장 틀은 하나이고, 종이 생김새를, 속성이 색을,
단계가 나이를 채웁니다 — 그래서 같은 종의 세 단계가 같은 생김새로 자라고, 같은 속성끼리
같은 색을 씁니다.

    python samples/wildling/design-data/tools/monster_art.py

`out/monster-prompts.tsv` 로 나갑니다. 그림 자체는 이미지 생성 서비스가 만들고,
`monster_import.py` 가 그것을 아이콘 크기로 줄여 등급 테두리를 얹습니다.
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from art import records  # noqa: E402

OUT = os.path.normpath(os.path.join(HERE, "..", "out", "monster-prompts.tsv"))

# 종이 무엇처럼 생겼는가. `species_id` 하나에 한 줄이다.
SPECIES = {
    "sprout_deer": "fawn deer with fresh sprouting leaves on its head",
    "moss_toad": "round toad covered in soft moss and tiny ferns",
    "thorn_beetle": "armored beetle with sharp thorn spikes on its back",
    "vine_ape": "nimble monkey wrapped in climbing vines",
    "bloom_moth": "moth with flower-petal wings and pollen dust",
    "elder_bark": "walking tree spirit of ancient cracked bark",
    "ember_fox": "slender fox with ember flames on its tail",
    "cinder_pup": "puppy with a burning cinder mane",
    "ash_boar": "sturdy boar with ash-grey bristles and glowing cracks",
    "flare_hawk": "hawk with wings of searing flare light",
    "magma_crab": "crab with a shell of cooled magma and molten seams",
    "pyre_lion": "lion with a mane of roaring pyre fire",
    "tide_serpent": "sea serpent with rippling water scales",
    "dew_slug": "snail with a dewdrop shell",
    "reef_turtle": "turtle with a coral reef growing on its shell",
    "mist_ray": "manta ray gliding through mist",
    "brine_otter": "otter with salt crystals in its wet fur",
    "abyss_whale": "small whale of the deep abyss with faint lights",
    "spark_mouse": "mouse crackling with static sparks",
    "bolt_lynx": "lynx with lightning-bolt markings",
    "coil_snake": "snake coiled into a spiral with arcing current",
    "storm_crane": "crane with storm clouds around its wings",
    "volt_golem": "small stone golem with glowing voltage veins",
    "thunder_stag": "stag with antlers of forked lightning",
    "shade_bat": "bat wrapped in soft shadow",
    "dusk_weasel": "weasel with dusk-purple fur",
    "gloom_spider": "spider with a gloom-silk web pattern",
    "night_hound": "hound of the night with faint violet eyes",
    "umbra_owl": "owl with feathers of layered shadow",
    "void_seraph": "winged being of void light with many feathers",
}

# 속성이 색을 정한다.
ELEMENT = {
    "Leaf": ("leafy green and moss palette", "dark forest green"),
    "Flame": ("ember orange and deep red palette", "dark ember red"),
    "Tide": ("aqua blue and teal palette", "deep ocean blue"),
    "Arc": ("electric yellow with violet sparks palette", "dark indigo"),
    "Umbra": ("violet and charcoal palette with faint purple glow", "dark violet"),
}

# 단계가 나이를 정한다.
STAGE = {
    1: "small cute chibi juvenile",
    2: "grown adolescent, larger and sharper",
    3: "mighty awakened elder form, imposing and ornate",
}

# 역할이 자세를 정한다.
ROLE = {
    "Vanguard": "braced defensive stance",
    "Breaker": "aggressive lunging stance",
    "Warden": "calm protective stance",
    "Tuner": "poised graceful stance",
}

STYLE = ("front three-quarter view, centered full body, flat vector game art, "
         "thick dark outline, soft cel shading, clean and readable at small size, "
         "no text, no border, no frame")


def prompt(row):
    """행 하나의 프롬프트이다."""
    look = SPECIES.get(row["species_id"], "small wild creature")
    palette, background = ELEMENT.get(row["element"], ELEMENT["Leaf"])
    stage = STAGE.get(int(row["stage"]), STAGE[1])
    pose = ROLE.get(row["role"], "")

    return (f"Mobile RPG monster icon, {stage} {look}, {pose}, "
            f"{palette}, plain {background} background, {STYLE}")


def main():
    rows = records("Monster.tsv")
    lines = ["icon\tmonster_id\tprompt"]
    for row in rows:
        lines.append("%s\t%s\t%s" % (row["icon"], row["monster_id"], prompt(row)))

    io.open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    print("%s 에 %d줄" % (OUT, len(rows)))


if __name__ == "__main__":
    main()
