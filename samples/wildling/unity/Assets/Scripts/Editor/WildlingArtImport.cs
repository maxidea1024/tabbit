using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wildling.Check
{
    /// <summary>
    /// 원본 그림을 아이콘 크기로 줄여 `Resources/art/icon/` 에 넣는다.
    /// </summary>
    /// <remarks>
    /// **줄이는 것을 유니티가 합니다.** 원본이 JPEG 이고 `design-data/tools/` 의 도구는 PNG만
    /// 다룹니다 — JPEG 디코더를 하나 더 쓰는 대신 이미 있는 것을 씁니다.
    ///
    /// 원본은 커밋되지 않고 **줄인 결과가 커밋됩니다.** 장당 250 KB 짜리 54장을 저장소에 두는
    /// 대신 192픽셀 PNG 를 둡니다.
    /// </remarks>
    public static class WildlingArtImport
    {
        private const int Size = 192;

        [MenuItem("Wildling/아이콘 원본 가져오기")]
        public static void RunFromMenu() => Debug.Log(Run(out _));

        public static void RunFromCommandLine()
        {
            string report;
            bool ok;
            try
            {
                report = Run(out ok);
            }
            catch (Exception e)
            {
                ok = false;
                report = $"!! 가져오기가 예외로 끝났습니다 — {e}";
            }

            Debug.Log(report);

            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "design-data", "out", "unity-art.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (ok ? "OK" : "FAIL") + Environment.NewLine + report);
        }

        private static string Run(out bool ok)
        {
            string source = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "design-data", "art-source", "monster"));
            string target = Path.Combine(Application.dataPath, "Resources", "art", "icon");

            if (!Directory.Exists(source))
            {
                ok = true;
                return $"원본 폴더가 없습니다 — {source}. 도형으로 그린 아이콘을 그대로 씁니다.";
            }

            Directory.CreateDirectory(target);

            var files = Directory.GetFiles(source, "*.jpg")
                .Concat(Directory.GetFiles(source, "*.png"))
                .OrderBy(f => f)
                .ToList();

            int done = 0;
            foreach (string file in files)
            {
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!raw.LoadImage(File.ReadAllBytes(file)))
                {
                    Debug.LogWarning($"읽지 못했습니다 — {file}");
                    continue;
                }

                var small = Shrink(raw, Size);
                RoundCorners(small, Size * 0.16f);
                string name = Path.GetFileNameWithoutExtension(file);
                File.WriteAllBytes(Path.Combine(target, name + ".png"), small.EncodeToPNG());

                UnityEngine.Object.DestroyImmediate(raw);
                UnityEngine.Object.DestroyImmediate(small);
                done++;
            }

            AssetDatabase.Refresh();

            // 스프라이트로 읽히도록 임포터를 맞춥니다.
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string asset = $"Assets/Resources/art/icon/{name}.png";
                if (AssetImporter.GetAtPath(asset) is not TextureImporter importer)
                    continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            ok = done > 0;
            return $"원본 {files.Count}장 중 {done}장을 {Size}픽셀로 넣었습니다.";
        }

        /// <summary>
        /// 상자 평균으로 줄인다. 가장자리는 잘라 낸다.
        /// </summary>
        /// <remarks>
        /// **가장자리를 잘라 내는 이유가 있습니다.** 생성된 그림에는 바깥에 흰 테두리나 둥근
        /// 자국이 남는 경우가 있어, 슬롯에 넣으면 다듬지 않은 티가 납니다. 안쪽으로 조금
        /// 들어가서 담으면 그 자국이 사라지고 캐릭터도 칸을 꽉 채웁니다.
        /// </remarks>
        private static Texture2D Shrink(Texture2D source, int size)
        {
            var pixels = source.GetPixels32();

            // 바깥 7%를 버립니다.
            int inset = Mathf.RoundToInt(Mathf.Min(source.width, source.height) * 0.07f);
            int left = inset;
            int bottom = inset;
            int width = source.width - inset * 2;
            int height = source.height - inset * 2;
            var output = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                int y0 = bottom + y * height / size;
                int y1 = Mathf.Max(y0 + 1, bottom + (y + 1) * height / size);

                for (int x = 0; x < size; x++)
                {
                    int x0 = left + x * width / size;
                    int x1 = Mathf.Max(x0 + 1, left + (x + 1) * width / size);

                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * source.width;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            var p = pixels[row + sx];
                            r += p.r;
                            g += p.g;
                            b += p.b;
                            a += p.a;
                            n++;
                        }
                    }

                    output[y * size + x] = new Color32(
                        (byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }

            var small = new Texture2D(size, size, TextureFormat.RGBA32, false);
            small.SetPixels32(output);
            small.Apply();
            return small;
        }

        /// <summary>
        /// 모서리를 둥글게 깎는다.
        /// </summary>
        /// <remarks>
        /// 슬롯이 둥근 사각형이므로 그림도 같은 모양이어야 합니다. 남아 있던 네 귀퉁이의
        /// 흰 자국도 여기서 함께 사라집니다.
        /// </remarks>
        private static void RoundCorners(Texture2D texture, float radius)
        {
            int size = texture.width;
            var pixels = texture.GetPixels32();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    if (dx <= 0f || dy <= 0f)
                        continue;

                    float away = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    if (away <= 0f)
                        continue;

                    int i = y * size + x;
                    byte alpha = (byte)(pixels[i].a * Mathf.Clamp01(1f - away));
                    pixels[i] = new Color32(pixels[i].r, pixels[i].g, pixels[i].b, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
        }
    }
}
