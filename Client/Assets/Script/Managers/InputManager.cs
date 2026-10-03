using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ProjectT
{
    public sealed class InputManager : ManagerBase
    {
        internal ProjectT.Controller.Controller Controller { get; private set; }

        private EventSystem eventSystem;
        private InputSystemUIInputModule uiInputModule;
        private bool sceneInputAllowed = true;
        private bool uiFocused;
        private bool navigationAllowed;
        private int focusFrame;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            // 프로젝트 전역 에셋은 Input System이 소유하므로 복제하거나 파괴하지 않는다.
            var asset = InputSystem.actions;
            if (asset == null)
                throw new InvalidOperationException("Project-wide input actions are not assigned.");

            Controller = new ProjectT.Controller.Controller();
            Controller.Init(asset, "Player");
            CreateRootObject("InputManager");
            CreateEventSystemInternal(asset);
            ApplyInputStateInternal();
            Controller.Enable();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            eventSystem = null;
            uiInputModule = null;
            var controller = Controller;
            Controller = null;
            controller?.Release();
        }

        public void SetInputAllowed(bool allowed)
        {
            sceneInputAllowed = allowed;
            ApplyInputStateInternal();
        }

        internal void SetUIFocused(bool focused)
        {
            if (focused && !uiFocused)
                focusFrame = Time.frameCount;

            uiFocused = focused;
            ApplyInputStateInternal();
        }

        public override void OnUpdate(float dt)
        {
            if (uiFocused && !navigationAllowed && Time.frameCount > focusFrame)
                ApplyInputStateInternal();
        }

        // UI 상태를 먼저 반영해야 Controller의 onReset 예외가 UI 상태를 이전 값으로 남기지 않는다.
        private void ApplyInputStateInternal()
        {
            // 게임 입력 콜백이 UI를 연 프레임에 같은 Submit이 새로 선택된 버튼을 누르지 않도록 탐색은 다음 프레임부터 보낸다.
            navigationAllowed = uiFocused && Time.frameCount > focusFrame;
            uiInputModule.enabled = sceneInputAllowed;
            eventSystem.sendNavigationEvents = navigationAllowed;
            Controller.SetInputAllowed(sceneInputAllowed && !uiFocused);
        }

        private void CreateEventSystemInternal(InputActionAsset asset)
        {
            // 활성 오브젝트에 추가하면 OnEnable에서 기본 DefaultInputActions가 생성·활성화되므로 액션을 지정한 뒤 활성화한다.
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.SetActive(false);
            eventSystemObject.transform.SetParent(RootObject, false);
            eventSystem = eventSystemObject.AddComponent<EventSystem>();

            var module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            module.actionsAsset = asset;
            module.point = InputActionReference.Create(asset.FindAction("UI/Point", true));
            module.leftClick = InputActionReference.Create(asset.FindAction("UI/Click", true));
            module.rightClick = InputActionReference.Create(asset.FindAction("UI/RightClick", true));
            module.middleClick = InputActionReference.Create(asset.FindAction("UI/MiddleClick", true));
            module.scrollWheel = InputActionReference.Create(asset.FindAction("UI/ScrollWheel", true));
            module.move = InputActionReference.Create(asset.FindAction("UI/Navigate", true));
            module.submit = InputActionReference.Create(asset.FindAction("UI/Submit", true));
            module.cancel = InputActionReference.Create(asset.FindAction("UI/Cancel", true));
            module.trackedDevicePosition = InputActionReference.Create(asset.FindAction("UI/TrackedDevicePosition", true));
            module.trackedDeviceOrientation = InputActionReference.Create(asset.FindAction("UI/TrackedDeviceOrientation", true));
            module.deselectOnBackgroundClick = false;
            module.enabled = false;
            uiInputModule = module;
            eventSystemObject.SetActive(true);
        }
    }
}
