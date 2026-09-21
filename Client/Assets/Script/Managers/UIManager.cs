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

        public IReadOnlyList<UIBase> UIStack => uiContainer.UIStack;
        public Camera Canvas2DCam => uiContainer.Canvas2DCam;
        public Transform Canvas2D => uiContainer.UICanvas2D;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("UIManager");

            uiContainer = new UIContainer(RootObject, token);
            uiContainer.OnEnter();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                uiContainer?.OnLeave();
            }
            finally
            {
                uiContainer = null;
            }
        }

        public override void OnUpdate(float dt)
        {
            uiContainer.OnUpdate(dt);
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

        // 씬 전환 경계에서 SceneManager가 호출한다. System UI만 남기고 정리한다.
        public void ClearTransientWidgets()
        {
            uiContainer.ClearTransientWidgets();
        }

        public UIBase GetCurrentStackUI()
        {
            return uiContainer.GetCurrentStackUI();
        }

        public T GetHud<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            string path = HudDefine.GetPath(type);

            if (string.IsNullOrEmpty(path))
                return default(T);

            var obj = Global.Pool.Get(path);
            T hud = obj.GetComponent<T>();
            if (hud == null)
            {
                Global.Pool.Return(obj);
                throw new InvalidOperationException($"HUD prefab has no {typeof(T).Name}.");
            }
            hud.RegisterInfo(pivotInfo);

            return hud;
        }

        public async UniTask<T> GetHudAsync<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            string path = HudDefine.GetPath(type);
            if (string.IsNullOrEmpty(path))
                return default(T);

            GameObject poolObj = await Global.Pool.GetAsync(path, cancelToken: LifetimeToken);
            T hud = poolObj.GetComponent<T>();
            if (hud == null)
            {
                Global.Pool.Return(poolObj);
                throw new InvalidOperationException($"HUD prefab has no {typeof(T).Name}.");
            }
            hud.RegisterInfo(pivotInfo);
            return hud;
        }

        public void Release<T>(T hudAgent) where T : ComHudAgent
        {
            if (hudAgent == null)
                return;

            Global.Pool.Release(hudAgent.gameObject);
        }
    }
}
