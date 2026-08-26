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

        /// <summary>상자 평균으로 줄인다. 늘리는 데는 쓰지 않는다.</summary>
        private static Texture2D Shrink(Texture2D source, int size)
        {
            var pixels = source.GetPixels32();
            int width = source.width;
            int height = source.height;
            var output = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                int y0 = y * height / size;
                int y1 = Mathf.Max(y0 + 1, (y + 1) * height / size);

                for (int x = 0; x < size; x++)
                {
                    int x0 = x * width / size;
                    int x1 = Mathf.Max(x0 + 1, (x + 1) * width / size);

                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * width;
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
    }
}
