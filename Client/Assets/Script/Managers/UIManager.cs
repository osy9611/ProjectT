using Cysharp.Threading.Tasks;
using ProjectT.Pivot;
using ProjectT.UGUI;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    public class UIManager : ManagerBase
    {
        private UIContainer uiContainer;
        private readonly List<ComHudAgent> huds = new List<ComHudAgent>();
        public IReadOnlyList<UIBase> WidgetStack => uiContainer.WidgetStack;
        public UIBase TopWidget => uiContainer.TopWidget;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("UIManager");

            uiContainer = new UIContainer(RootObject, token);
            uiContainer.Initialize();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            // HUD는 UIManager 루트 아래에 있으므로 루트와 함께 파괴되기 전에 풀로 돌려보낸다.
            ReturnAllHudsInternal();
            uiContainer?.Shutdown();
            uiContainer = null;
        }

        public override void OnUpdate(float dt)
        {
            uiContainer.UpdateAutoScale();
            UpdateHudsInternal();
        }

        public void SetInputAllowed(bool allowed)
        {
            uiContainer.SetInputAllowed(allowed);
        }

        public T CreateWidget<T>(UIDefine.eUIType type) where T : UIBase
        {
            return uiContainer.CreateWidget<T>(type, UIDefine.GetUIPath(type));
        }

        public async UniTask<T> CreateWidgetAsync<T>(UIDefine.eUIType type, CancellationToken cancelToken = default) where T : UIBase
        {
            return await uiContainer.CreateWidgetAsync<T>(type, UIDefine.GetUIPath(type), cancelToken);
        }

        public T FindWidget<T>(UIDefine.eUIType type) where T : UIBase
        {
            return uiContainer.FindWidget<T>(type);
        }

        public void RemoveWidget(UIDefine.eUIType type)
        {
            uiContainer.RemoveWidget(type);
        }

        // 씬 전환 경계에서 SceneManager가 호출한다. 씬 오브젝트를 따라가던 HUD는 모두 반납하고, UI는 System UI만 남긴다.
        public void ClearTransientWidgets()
        {
            ReturnAllHudsInternal();
            uiContainer.ClearTransientWidgets();
        }

        public T GetHud<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            return AttachHudInternal<T>(Global.Pool.Get(HudDefine.GetPath(type), uiContainer.HudRoot), pivotInfo);
        }

        public async UniTask<T> GetHudAsync<T>(HudDefine.eHudType type, PivotInfo pivotInfo, CancellationToken cancelToken = default) where T : ComHudAgent
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancelToken))
            {
                var obj = await Global.Pool.GetAsync(HudDefine.GetPath(type), uiContainer.HudRoot, cancelToken: linked.Token);
                return AttachHudInternal<T>(obj, pivotInfo);
            }
        }

        public void ReturnHud(ComHudAgent hud)
        {
            // 씬 전환에서 UIManager가 먼저 일괄 반납한 HUD를 뒤이은 풀 정리의 Actor 반납 경로가 다시 반납하므로 추적 중인 HUD만 반납한다.
            if (!huds.Remove(hud))
                return;

            Global.Pool.Return(hud.gameObject);
        }

        private T AttachHudInternal<T>(GameObject obj, PivotInfo pivotInfo) where T : ComHudAgent
        {
            var hud = obj.GetComponent<T>();
            if (hud == null)
            {
                Global.Pool.Return(obj);
                throw new InvalidOperationException($"HUD prefab has no {typeof(T).Name}.");
            }

            hud.AttachInternal(pivotInfo);
            huds.Add(hud);

            // 다음 갱신까지 풀에서 꺼낸 위치에 한 프레임 보이지 않도록 바로 배치한다.
            var worldCamera = Camera.main;
            if (worldCamera != null)
                hud.UpdatePositionInternal(worldCamera, uiContainer.HudRoot, uiContainer.UICamera);

            return hud;
        }

        private void UpdateHudsInternal()
        {
            if (huds.Count == 0)
                return;

            // 씬 전환 중처럼 메인 카메라가 없는 프레임에는 위치를 갱신하지 않는다.
            var worldCamera = Camera.main;
            if (worldCamera == null)
                return;

            var root = uiContainer.HudRoot;
            var uiCamera = uiContainer.UICamera;
            for (int i = 0; i < huds.Count; ++i)
                huds[i].UpdatePositionInternal(worldCamera, root, uiCamera);
        }

        private void ReturnAllHudsInternal()
        {
            var returning = huds.ToArray();
            huds.Clear();

            foreach (var hud in returning)
                Global.Pool.Return(hud.gameObject);
        }
    }
}
