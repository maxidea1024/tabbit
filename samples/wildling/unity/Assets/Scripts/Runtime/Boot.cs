using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 게임을 세운다 — 표를 읽고, 세이브를 읽고, 첫 화면을 연다.
    /// </summary>
    /// <remarks>
    /// **씬에는 이 오브젝트 하나만 있습니다.** 카메라와 `Canvas` 도 여기서 만듭니다. 화면을
    /// 코드에서 조립하므로 씬 파일이 거의 비어 있고, 그래서 변경이 diff 로 읽힙니다.
    /// </remarks>
    public sealed class Boot : MonoBehaviour
    {
        public const string TableFolder = "tables";

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            Application.targetFrameRate = 60;

            var canvas = MakeCanvas();
            var app = gameObject.AddComponent<App>();
            app.Attach(canvas);

            var loading = Ui.Node("loading", canvas.transform);
            Ui.Stretch(loading);
            Ui.Panel(loading.transform, Theme.Background);
            Ui.Label(loading.transform, "표를 읽는 중입니다.", 26, Theme.TextDim,
                     TextAnchor.MiddleCenter);

            var task = LoadTables();
            while (!task.IsCompleted)
                yield return null;

            if (task.Exception != null)
            {
                Ui.Clear(loading.transform);
                Ui.Panel(loading.transform, Theme.Background);
                Ui.Label(loading.transform,
                         "표를 읽지 못했습니다.\n"
                         + task.Exception.GetBaseException().Message,
                         22, Theme.Warn, TextAnchor.MiddleCenter);
                yield break;
            }

            Destroy(loading);

            // 화면 순회는 세이브를 건드리지 않고 늘 같은 자리에서 시작합니다.
            string shots = ScreenTour.FolderFromCommandLine();
            SaveStore.Enabled = string.IsNullOrEmpty(shots);

            app.State = SaveStore.Load();
            app.State.UnlockReady();
            app.State.AutoFillParty();
            SaveStore.Save(app.State);

            app.Go(new HomeScreen(), false);

            if (!string.IsNullOrEmpty(shots))
                ScreenTour.Begin(app, shots);
        }

        /// <summary>
        /// `.bytes` 30개를 읽는다.
        /// </summary>
        /// <remarks>
        /// **`Task.Run` 으로 감쌉니다.** 유니티의 동기화 컨텍스트에서 그냥 기다리면 이어지는
        /// 작업이 그 컨텍스트로 되돌아오려다 멈춥니다. 풀 스레드에서 시작하면 컨텍스트가 없으므로
        /// 이어지는 작업도 풀에 남습니다 — `WildlingDataCheck.cs` 에 같은 설명이 있습니다.
        /// </remarks>
        public static Task LoadTables()
        {
            string root = Path.Combine(Application.streamingAssetsPath, TableFolder);
            return Task.Run(async () =>
            {
                await WildlingData.ReadAllAsync(root);
                Stats.Forget();
            });
        }

        public static Canvas MakeCanvas()
        {
            var cameraGo = new GameObject("camera", typeof(Camera));
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Theme.Background;
            camera.orthographic = true;

            var canvasGo = new GameObject("canvas", typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Ui.Width, Ui.Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            if (EventSystem.current is null)
            {
                var events = new GameObject("events", typeof(EventSystem));
                events.AddComponent<StandaloneInputModule>();
            }

            return canvas;
        }
    }
}
