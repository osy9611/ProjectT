using Cysharp.Threading.Tasks;
using ProjectT.Pivot;
using ProjectT.UGUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using Unity.VisualScripting;
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

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject(Context.Root, "UIManager");

            uiContainer = new UIContainer(this);
            uiContainer.OnEnter();
            return UniTask.CompletedTask;
        }
        protected override void OnShutdown(ShutdownReason reason)
        {
            uiContainer?.OnLeave();
            uiContainer = null;
        }
        public override void OnUpdate(float dt)
        {
            uiContainer.OnUpdate(dt);
        }


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

            T hud = Context.Get<ResourceManager>().LoadAndGet<T>(path);
            hud = Context.Get<PoolManager>().Get(hud.gameObject).GetComponent<T>();
            hud.RegisterInfo(pivotInfo);

            return hud;
        }

        public async UniTask<T> GetHudAsync<T>(HudDefine.eHudType type, PivotInfo pivotInfo) where T : ComHudAgent
        {
            string path = HudDefine.GetPath(type);
            if (string.IsNullOrEmpty(path))
                return default(T);

            T hud = await Context.Get<ResourceManager>().LoadAndGetAsync<T>(path);
            ThrowIfStopped();
            GameObject poolObj = await Context.Get<PoolManager>().GetAsync(hud.gameObject);
            ThrowIfStopped();
            hud = poolObj.gameObject.GetComponent<T>();
            hud.RegisterInfo(pivotInfo);
            return hud;
        }

        public void Release<T>(T hudAgent) where T : ComHudAgent
        {
            if (hudAgent == null)
                return;

            Context.Get<PoolManager>().Release(hudAgent.gameObject);
        }

        public async UniTask ReleaseAsync<T>(T hudAgent) where T : ComHudAgent
        {
            if (hudAgent == null)
                return;

            await Context.Get<PoolManager>().ReleaseAsync(hudAgent.gameObject);
        }
    }
}
