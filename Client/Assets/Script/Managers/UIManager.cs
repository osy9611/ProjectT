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
            // 종료 순서상 Actor는 UIManager보다 늦게 반납되므로 HUD를 분리만 한다. 인스턴스는 루트 파괴와 풀 정리가 처리한다.
            foreach (var hud in huds)
                hud.DetachInternal();
            huds.Clear();
            uiContainer?.Shutdown();
            uiContainer = null;
        }

        protected internal override void OnUpdate(float dt)
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

        // 씬 전환 경계에서 SceneManager가 호출한다. UI는 System UI만 남긴다. HUD는 Actor가 반납하므로 건드리지 않는다.
        public void ClearTransientWidgets()
        {
            uiContainer.ClearTransientWidgets();
        }

        public T GetHud<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            return RentHudInternal<T>(Global.Resource.LoadAndGet<GameObject>(HudDefine.GetPath(type), Global.Resource.AppScope), pivotInfo);
        }

        public async UniTask<T> GetHudAsync<T>(HudDefine.eHudType type, PivotInfo pivotInfo, CancellationToken cancelToken = default) where T : ComHudAgent
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancelToken))
            {
                var original = await Global.Resource.LoadAndGetAsync<GameObject>(HudDefine.GetPath(type), linked.Token, Global.Resource.AppScope);
                return RentHudInternal<T>(original, pivotInfo);
            }
        }

        internal void ReturnHudInternal(ComHudAgent hud)
        {
            if (!huds.Remove(hud))
                throw new InvalidOperationException("HUD is not attached to UIManager.");

            hud.DetachInternal();
            Global.Pool.Return(hud.gameObject);
        }

        // 대여 전에 프리팹을 검사해야 실패 시 반납할 인스턴스가 생기지 않는다.
        private T RentHudInternal<T>(GameObject original, PivotInfo pivotInfo) where T : ComHudAgent
        {
            if (original.GetComponent<T>() == null)
                throw new InvalidOperationException($"HUD prefab has no {typeof(T).Name}.");

            var hud = Global.Pool.Get(original, uiContainer.HudRoot, Global.Resource.AppScope).GetComponent<T>();
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
    }
}
