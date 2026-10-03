using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Users;

namespace ProjectT
{
    public sealed class InputManager : ManagerBase
    {
        internal ProjectT.Controller.Controller Controller { get; private set; }

        public InputDevice CurrentDevice { get; private set; }
        public bool IsDeviceLost { get; private set; }

        private EventSystem eventSystem;
        private InputSystemUIInputModule uiInputModule;
        private InputUser user;
        private bool sceneInputAllowed = true;
        private bool uiFocused;
        private bool navigationAllowed;
        private bool switchPending;
        private int focusFrame;
        private int switchFrame = -1;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            // 프로젝트 전역 에셋은 Input System이 소유하므로 복제하거나 파괴하지 않는다.
            var asset = InputSystem.actions;
            if (asset == null)
                throw new InvalidOperationException("Project-wide input actions are not assigned.");

            if (Application.platform == RuntimePlatform.Switch)
                ApplySwitchConfirmButtonsInternal(asset);

            Controller = new ProjectT.Controller.Controller();
            Controller.Init(asset, "Player");
            CreateRootObject("InputManager");
            CreateEventSystemInternal(asset);
            CreateUserInternal(asset);
            ApplyInputStateInternal();
            Controller.Enable();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                ReleaseUserInternal();
            }
            finally
            {
                eventSystem = null;
                uiInputModule = null;
                var controller = Controller;
                Controller = null;
                controller?.Release();
            }
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
            if ((switchPending && Time.frameCount > switchFrame) || (uiFocused && !navigationAllowed && Time.frameCount > focusFrame))
                ApplyInputStateInternal();
        }

        // UI 상태를 먼저 반영해야 Controller의 onReset 예외가 UI 상태를 이전 값으로 남기지 않는다.
        // 장치 연결 해제 중에도 일시정지·재연결 UI를 조작할 수 있도록 장치 상태는 게임 입력에만 반영한다.
        private void ApplyInputStateInternal()
        {
            // 게임 입력 콜백이 UI를 연 프레임에 같은 Submit이 새로 선택된 버튼을 누르지 않도록 탐색은 다음 프레임부터 보낸다.
            // 장치를 전환한 입력은 상태 반영 전에 전달되어 액션도 발동시키므로 전환 프레임에는 게임 입력과 탐색을 막는다.
            switchPending = Time.frameCount <= switchFrame;
            navigationAllowed = uiFocused && Time.frameCount > focusFrame && !switchPending;
            uiInputModule.enabled = sceneInputAllowed;
            eventSystem.sendNavigationEvents = navigationAllowed;
            Controller.SetInputAllowed(sceneInputAllowed && !uiFocused && !IsDeviceLost && !switchPending);
        }

        // 닌텐도 규약은 오른쪽 버튼(A)이 확인, 아래쪽 버튼(B)이 취소이므로 패드 위치 기반 바인딩만 맞바꾼다.
        private static void ApplySwitchConfirmButtonsInternal(InputActionAsset asset)
        {
            asset.FindAction("UI/Submit", true).ApplyBindingOverride("<Gamepad>/buttonEast", path: "<Gamepad>/buttonSouth");
            asset.FindAction("UI/Cancel", true).ApplyBindingOverride("<Gamepad>/buttonSouth", path: "<Gamepad>/buttonEast");
        }

        private void CreateUserInternal(InputActionAsset asset)
        {
            // 종료 시 user.valid 하나로 정리 여부를 판단하도록 유저 생성·액션 연결 직후 구독한다.
            user = InputUser.CreateUserWithoutPairedDevices();
            user.AssociateActionsWithUser(asset);
            ++InputUser.listenForUnpairedDeviceActivity;
            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsedInternal;
            InputUser.onChange += OnUserChangedInternal;

            InputDevice preferred = Keyboard.current;
            InputDevice fallback = Gamepad.current;
            if (Application.isConsolePlatform)
            {
                preferred = Gamepad.current;
                fallback = Keyboard.current;
            }

            var device = preferred ?? fallback;
            if (device != null && TryFindControlSchemeInternal(device, out var scheme))
                PairDeviceInternal(device, scheme);
        }

        private void ReleaseUserInternal()
        {
            if (!user.valid)
                return;

            InputUser.onChange -= OnUserChangedInternal;
            InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsedInternal;
            --InputUser.listenForUnpairedDeviceActivity;
            var actions = user.actions;
            user.UnpairDevicesAndRemoveUser();
            // 유저 제거 후에도 공유 에셋에 빈 장치 목록이 남아 모든 장치 입력이 막히지 않도록 장치 제한을 해제한다.
            actions.devices = null;
        }

        private void PairDeviceInternal(InputDevice device, InputControlScheme scheme)
        {
            // 페어링 중 발생하는 연결 복구 통지에서도 새 장치를 읽도록 먼저 갱신한다.
            CurrentDevice = device;
            InputUser.PerformPairingWithDevice(device, user, InputUserPairingOptions.UnpairCurrentDevicesFromUser);
            user.ActivateControlScheme(scheme).AndPairRemainingDevices();
            PublishInternal(NotificationId.InputControlsChanged);
        }

        private bool TryFindControlSchemeInternal(InputDevice device, out InputControlScheme scheme)
        {
            // FindControlSchemeForDevice는 Keyboard&Mouse처럼 여러 장치를 요구하는 스킴을 장치 하나로 찾지 못한다.
            foreach (var candidate in user.actions.controlSchemes)
            {
                if (candidate.SupportsDevice(device))
                {
                    scheme = candidate;
                    return true;
                }
            }

            scheme = default;
            return false;
        }

        private void OnUnpairedDeviceUsedInternal(InputControl control, InputEventPtr eventPtr)
        {
            // 변경된 컨트롤 열거는 노이즈 컨트롤과 축·트리거의 미세 입력도 포함하므로 이 이벤트에서 눌림 기준을 넘은 버튼만 장치 전환으로 본다.
            if (!(control is ButtonControl button) || button.noisy || !button.IsValueConsideredPressed(button.ReadValueFromEvent(eventPtr)))
                return;

            // 지원하는 스킴이 없는 장치는 유저 장치를 바꾸지 않고 무시한다.
            var device = control.device;
            if (!TryFindControlSchemeInternal(device, out var scheme))
                return;

            switchFrame = Time.frameCount;
            PairDeviceInternal(device, scheme);
            ApplyInputStateInternal();
        }

        private void OnUserChangedInternal(InputUser changedUser, InputUserChange change, InputDevice device)
        {
            if (changedUser != user)
                return;

            if (change == InputUserChange.DeviceRegained)
                CurrentDevice = device;

            // 분실 장치는 페어링 목록에서 빠지므로 남은 장치가 없을 때만 연결 해제로 본다. 키보드만 남은 마우스 분실은 입력을 막지 않는다.
            var lost = user.lostDevices.Count > 0 && user.pairedDevices.Count == 0;
            if (lost == IsDeviceLost)
                return;

            IsDeviceLost = lost;
            ApplyInputStateInternal();
            PublishInternal(lost ? NotificationId.InputDeviceLost : NotificationId.InputDeviceRegained);
        }

        private static void PublishInternal(NotificationId id)
        {
            if (Global.TryGetReady<NotificationManager>(out var notify))
                notify.Publish(id);
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
