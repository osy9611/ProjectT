using Cysharp.Threading.Tasks;
using ProjectT.Pivot;
using ProjectT.UGUI;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProjectT
{
    public class UIManager : ManagerBase
    {
        public enum eHudType
        {

        }

        private UIContainer uiContainer;

        public List<UIBase> UIStack => uiContainer.UIStack;
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

        public void RegisterStackUI(UIBase uiBase)
        {
            if (uiBase == null)
            {
                Global.Instance.Log($"[UIManager] RegisterStackUI() UIBase is Null", "A45FF8");
                return;
            }

            if (UIStack.Count == 0 || UIStack.Last() != uiBase)
            {
                if (uiBase.Type != eUIContainerType.Static)
                {
                    Global.Instance.Log($"[UIManager] This UIBase Is Not Static UI : {uiBase.name}", "A45FF8");
                    return;
                }

                Global.Instance.Log($"[UIManager] RegisterStackUI() Success Register UI : {uiBase.name}", "A45FF8");

                UIStack.RemoveAll(x => x == uiBase);
                UIStack.Add(uiBase);

                string testDebugStr = $"After Register Remain StackUI \n ";

                foreach (var ui in UIStack)
                {
                    testDebugStr += ui.name;
                    if (ui != UIStack.Last())
                        testDebugStr += "\n ";
                }

                Global.Instance.Log(testDebugStr, "A45FF8");
            }
        }

        public void UnRegisterStackUI()
        {
            var uiBase = UIStack.Last();
            if (uiBase == null)
                Global.Instance.Log($"[UIManager] UnRegister() Success UnRegister UI But UIBase Is Null", "A45FF8");
            else
            {
                Global.Instance.Log($"[UIManager] UnRegister() Success UnRegister UI : {uiBase.name}", "A45FF8");
                UIStack.RemoveAll(x => x == uiBase);

                string testDebugStr = $"After UnRegister Remain StackUI \n ";
                foreach (var ui in UIStack)
                {
                    testDebugStr += ui.name;
                    if (ui != UIStack.Last())
                        testDebugStr += "\n ";
                }

                Global.Instance.Log(testDebugStr, "A45FF8");
            }
        }

        public UIBase GetCurrentStackUI()
        {
            if (UIStack.Count == 0)
                return null;

            return UIStack.Last();
        }

        public void ResetStackUI()
        {
            Global.Instance.Log($"[UIManager] ResetStackUI()", "A45FF8");
            UIStack.Clear();
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
