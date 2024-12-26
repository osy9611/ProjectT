using Cysharp.Threading.Tasks;
using ProjectT.Pivot;
using ProjectT.UGUI;
using UnityEngine;

namespace ProjectT
{
    public class UIManager : ManagerBase
    {
        public enum eHudType
        {

        }

        private UIContainer uiContainer;

        public Camera Canvas2DCam => uiContainer.Canvas2DCam;
        public Transform Canvas2D => uiContainer.UICanvas2D;

        #region ManagerBase
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }

        public override void OnAppStart()
        {
        }

        public override void OnEnter()
        {
            CreateRootObject(Global.Instance.transform, "UIManager");

            uiContainer = new UIContainer();
            uiContainer.OnEnter();
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
        }

        public override void OnUpdate(float dt)
        {
            uiContainer.OnUpdate(dt);
        }
        #endregion

        public string GetUIPath(UIDefine.eUIType type)
        {
            return UIDefine.GetUIPath(type);
        }

        public T CreateWidget<T>(UIDefine.eUIType type) where T : UIBase
        {
            string path = GetUIPath(type);
            if (string.IsNullOrEmpty(path))
            {
                Global.Instance.LogError($"UI Path Is Null Container Type: {type}");
                return default(T);
            }

            return uiContainer.CreateWidget<T>(type, path);
        }

        public T FindWidget<T>(UIDefine.eUIType type) where T : UIBase
        {
            return uiContainer.FindWiget<T>(type);
        }

        public void RemoveWidget(UIDefine.eUIType type)
        {
            string path = GetUIPath(type);
            if (string.IsNullOrEmpty(path))
            {
                Global.Instance.LogError($"UI Path Is Null Container Type: {type}");
                return;
            }
            uiContainer.RemoveWidget(type, path);
        }

        public T GetHud<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            string path = HudDefine.GetPath(type);

            if (string.IsNullOrEmpty(path))
                return default(T);

            T hud = Global.Resource.LoadAndGet<T>(path);
            hud = Global.Pool.Get(hud.gameObject).GetComponent<T>();
            hud.RegisterInfo(pivotInfo);

            return hud;
        }

        public async UniTask<T> GetHudAsync<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            string path = HudDefine.GetPath(type);
            if (string.IsNullOrEmpty(path))
                return default(T);

            T hud = await Global.Resource.LoadAndGetAsync<T>(path);
            GameObject poolObj = await Global.Pool.GetAsync(hud.gameObject);
            hud = poolObj.gameObject.GetComponent<T>();
            hud.RegisterInfo(pivotInfo);
            return hud;
        }

        public void Release<T>(T hudAgent) where T : ComHudAgent
        {
            if (hudAgent == null)
                return;

            Global.Pool.Release(hudAgent.gameObject);
        }

        public async UniTask ReleaseAsync<T>(T hudAgent) where T : ComHudAgent
        {
            if (hudAgent == null)
                return;

            await Global.Pool.ReleaseAsync(hudAgent.gameObject);
        }
    }
}
