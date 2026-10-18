using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

static partial class Program
{
    static async UniTask RunControllerSetupTests()
    {
        await RunControllerCase("steady dispatch allocates no managed snapshots", () =>
        {
            var f = CreateInputFixture();
            int calls = 0;
            f.Controller.AddContext(new InputContext("input", 0).Bind("Player/Fire", _ => calls++, eInputEvent.Performed));
            for (int i = 0; i < 512; i++)
                f.Fire.Trigger(InputActionPhase.Performed);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
                f.Fire.Trigger(InputActionPhase.Performed);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0 && calls == 1536, "Steady dispatch allocated " + allocated + " bytes.");
            f.Controller.Release();
        });

        await RunControllerCase("invalid paths and duplicate ownership fail", () =>
        {
            var f = CreateInputFixture();
            ThrowsSync<ArgumentException>(() => new InputContext("bad", 0).Bind("Fire", _ => { })).GetAwaiter().GetResult();
            ThrowsSync<ArgumentException>(() => f.Controller.AddContext(new InputContext("missing", 0).Bind("Player/Missing", _ => { }))).GetAwaiter().GetResult();
            var context = new InputContext("shared", 0).Bind("Player/Fire", _ => { });
            f.Controller.AddContext(context);
            var otherAsset = new InputActionAsset();
            var other = new ControllerProbe();
            other.Init(otherAsset, "Player");
            ThrowsSync<InvalidOperationException>(() => other.AddContext(context)).GetAwaiter().GetResult();
            other.Release();
            f.Controller.Release();
        });

        await RunControllerCase("other maps are never enabled or disabled by the controller after Init", () =>
        {
            var asset = new InputActionAsset();
            var submit = asset.FindAction("UI/Submit", true);
            var fire = asset.FindAction("Player/Fire", true);
            submit.enabled = fire.enabled = true;
            var controller = new ControllerProbe();
            controller.Init(asset, "Player");
            Check(!submit.enabled && !fire.enabled, "Init did not disable the auto-enabled project-wide actions once.");
            controller.Enable();
            Check(fire.enabled && !submit.enabled, "Controller enabled another map.");
            submit.enabled = true;
            controller.SetInputAllowed(false);
            Check(!fire.enabled && submit.enabled, "Blocking disabled another map.");
            controller.SetInputAllowed(true);
            controller.Disable();
            Check(!fire.enabled && submit.enabled, "Disable changed another map.");
            controller.Enable();
            submit.enabled = false;
            controller.SetInputAllowed(false);
            controller.SetInputAllowed(true);
            Check(fire.enabled && !submit.enabled, "Reactivation enabled another map.");
            submit.enabled = true;
            controller.Release();
            Check(submit.enabled && submit.DisableCalls == 1 && submit.SubscriberCount == 0, "Release or subscription touched another map.");
            var missing = new InputActionAsset();
            missing.Map.name = "Other";
            ThrowsSync<ArgumentException>(() => new ControllerProbe().Init(missing, "Player")).GetAwaiter().GetResult();
            ThrowsSync<ArgumentException>(() => new ControllerProbe().Init(new InputActionAsset(), " ")).GetAwaiter().GetResult();
        });

        await RunControllerCase("only its own map dispatches and deactivation resets held bindings without cancellation", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            int resets = 0;
            f.Controller.AddContext(new InputContext("both", 0)
                .Bind("Player/Move", c => trace.Add(c.canceled ? "move-cancel" : "move"), onReset: () => resets++)
                .Bind("Menu/Click", _ => trace.Add("click"), eInputEvent.Performed));
            Check(f.Fire.enabled && f.Move.enabled && !f.Click.enabled, "Initial activation enabled another map.");
            f.Click.enabled = true;
            f.Click.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Started);
            f.Controller.SetInputAllowed(false);
            Check(resets == 1 && !f.Move.enabled && !f.Fire.enabled && f.Click.enabled, "Deactivation did not reset or changed another map.");
            f.Move.Trigger(InputActionPhase.Started);
            CheckInputTrace(trace, "move");
            f.Controller.SetInputAllowed(true);
            Check(f.Move.enabled && f.Fire.enabled && f.Click.enabled, "Reactivation did not restore its map.");
            f.Controller.Release();
        });

        await Run("input manager: uses project-wide actions without cloning and releases the controller on shutdown", async () =>
        {
            var asset = new InputActionAsset();
            var submit = asset.FindAction("UI/Submit", true);
            submit.enabled = true;
            var host = StartInput(asset);
            var input = Global.Input;
            var fire = asset.FindAction("Player/Fire", true);
            Check(fire.enabled && !submit.enabled, "Scene input was not allowed by default or the controller enabled the UI map.");
            Check(ReferenceEquals(input.Controller.ResolveActionInternal("Player/Fire"), fire), "Manager cloned the project-wide actions.");
            Check(fire.enabled && !submit.enabled, "Controller enabled the UI map owned by the UI module.");
            int calls = 0;
            var context = new InputContext("probe", 0).Bind("Player/Fire", _ => calls++, eInputEvent.Performed);
            input.Controller.AddContext(context);
            input.SetInputAllowed(false);
            fire.Trigger(InputActionPhase.Performed);
            input.SetInputAllowed(true);
            fire.Trigger(InputActionPhase.Performed);
            Check(calls == 1, "Input allowed state was not forwarded to the controller.");
            var root = input.RootObject.gameObject;
            host.Shutdown();
            Check(input.Controller == null && context.Owner == null && fire.SubscriberCount == 0 && !fire.enabled, "Shutdown did not release the controller.");
            Check(UnityEngine.Object.Destroyed.Contains(root), "Shutdown did not destroy the EventSystem root.");
            Check(!UnityEngine.Object.Destroyed.Contains(asset), "Shutdown destroyed the project-wide asset.");
            await UniTask.CompletedTask;
        });

        await Run("input manager: owns one EventSystem whose UI module uses the shared asset's UI map", async () =>
        {
            var asset = new InputActionAsset();
            var host = StartInput(asset);
            var module = UIModule(Global.Input);
            Check(module.gameObject.GetComponent<UnityEngine.EventSystems.EventSystem>() != null && module.gameObject.activeSelf, "EventSystem was not created with the module.");
            Check(ReferenceEquals(module.gameObject.transform.parent, Global.Input.RootObject), "EventSystem is not owned by the InputManager root.");
            Check(ReferenceEquals(module.actionsAsset, asset), "UI module does not share the project-wide asset.");
            Check(!module.deselectOnBackgroundClick, "Background clicks can clear pad selection.");
            var references = new[] { (module.point, "Point"), (module.leftClick, "Click"), (module.rightClick, "RightClick"), (module.middleClick, "MiddleClick"),
                (module.scrollWheel, "ScrollWheel"), (module.move, "Navigate"), (module.submit, "Submit"), (module.cancel, "Cancel"),
                (module.trackedDevicePosition, "TrackedDevicePosition"), (module.trackedDeviceOrientation, "TrackedDeviceOrientation") };
            foreach (var (reference, name) in references)
                Check(ReferenceEquals(reference.action, asset.FindAction("UI/" + name, true)), "UI module reference is not the UI map action: " + name);
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input manager: UI module follows the scene lock, navigation follows UI focus, and gameplay requires scene input without UI focus", async () =>
        {
            var host = StartInput();
            var input = Global.Input;
            var module = UIModule(input);
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            var eventSystem = module.gameObject.GetComponent<UnityEngine.EventSystems.EventSystem>();
            Check(module.enabled && fire.enabled && !eventSystem.sendNavigationEvents, "Default state did not allow scene input without UI navigation.");
            var cases = new[] { (true, false, true), (true, true, false), (false, true, false), (false, false, false), (true, false, true) };
            foreach (var (scene, ui, gameplay) in cases)
            {
                input.SetInputAllowed(scene);
                input.SetUIFocused(ui);
                UnityEngine.Time.frameCount++;
                host.Update(0f);
                Check(module.enabled == scene, "UI module did not follow the scene lock: scene=" + scene + " ui=" + ui);
                Check(fire.enabled == gameplay, "Gameplay gating is wrong: scene=" + scene + " ui=" + ui);
                Check(eventSystem.sendNavigationEvents == ui, "Navigation events did not follow UI focus: scene=" + scene + " ui=" + ui);
            }
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input manager: navigation starts the frame after UI focus is gained and stops immediately", async () =>
        {
            var host = StartInput();
            var input = Global.Input;
            var eventSystem = UIModule(input).gameObject.GetComponent<UnityEngine.EventSystems.EventSystem>();
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            input.SetUIFocused(true);
            host.Update(0f);
            Check(!eventSystem.sendNavigationEvents && !fire.enabled, "Navigation was sent in the frame UI focus was gained.");
            input.SetUIFocused(true);
            UnityEngine.Time.frameCount++;
            host.Update(0f);
            Check(eventSystem.sendNavigationEvents, "Navigation did not start on the next frame.");
            input.SetUIFocused(true);
            Check(eventSystem.sendNavigationEvents, "Repeated focus delayed navigation again.");
            input.SetUIFocused(false);
            Check(!eventSystem.sendNavigationEvents && fire.enabled, "Losing focus did not stop navigation immediately.");
            input.SetUIFocused(true);
            input.SetInputAllowed(false);
            UnityEngine.Time.frameCount++;
            host.Update(0f);
            input.SetInputAllowed(true);
            Check(eventSystem.sendNavigationEvents && !fire.enabled, "Scene lock changed the focus frame.");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input manager: UI focus blocks actor input and resets held bindings", async () =>
        {
            var host = StartInput();
            var input = Global.Input;
            var actor = CreateInputActor();
            var component = AttachActor(actor);
            int held = 0, resets = 0, canceled = 0;
            var holder = new InputContext("holder", 0).Bind("Player/Fire", c =>
            {
                if (c.canceled)
                    canceled++;
                else
                    held = 1;
            }, consume: false, onReset: () => { held = 0; resets++; });
            input.Controller.AddContext(holder);
            ProjectFire.Trigger(InputActionPhase.Started);
            Check(held == 1 && Dispatches(actor), "Gameplay input was not delivered without UI focus.");
            input.SetUIFocused(true);
            Check(held == 0 && resets == 1 && canceled == 0, "UI focus did not reset held gameplay input.");
            Check(!Dispatches(actor), "Actor input was dispatched while UI was focused.");
            input.SetUIFocused(false);
            Check(Dispatches(actor), "Actor input did not resume after UI focus was lost.");
            ActorMessage(component, "OnDestroy");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: initial pairing prefers keyboard and mouse off console, a gamepad on console, and keeps an empty user without devices", async () =>
        {
            var pad = new Gamepad();
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            InputUser.Reset(keyboard, mouse, pad);
            var host = StartInput();
            var user = UserOf(Global.Input);
            Check(Global.Input.CurrentDevice == keyboard && Paired(user) == "Keyboard,Mouse" && user.controlScheme?.name == "Keyboard&Mouse",
                "Keyboard and mouse were not preferred off console: " + Paired(user));
            Check(ReferenceEquals(user.actions, InputSystem.actions), "Actions were not associated with the user.");
            Check(InputSystem.actions.FindAction("Player/Fire", true).enabled, "Initial pairing blocked gameplay like a device switch.");
            host.Shutdown();

            InputUser.Reset(pad);
            host = StartInput();
            Check(Global.Input.CurrentDevice == pad && Paired(UserOf(Global.Input)) == "Gamepad", "Gamepad was not the fallback off console.");
            host.Shutdown();

            UnityEngine.Application.isConsolePlatform = true;
            try
            {
                InputUser.Reset(keyboard, mouse, pad);
                host = StartInput();
                user = UserOf(Global.Input);
                Check(Global.Input.CurrentDevice == pad && Paired(user) == "Gamepad" && user.controlScheme?.name == "Gamepad", "Gamepad was not preferred on console.");
                host.Shutdown();

                InputUser.Reset(keyboard, mouse);
                host = StartInput();
                Check(Global.Input.CurrentDevice == keyboard && Paired(UserOf(Global.Input)) == "Keyboard,Mouse", "Keyboard was not the fallback on console.");
                host.Shutdown();
            }
            finally
            {
                UnityEngine.Application.isConsolePlatform = false;
            }

            InputUser.Reset();
            host = StartInput();
            user = UserOf(Global.Input);
            Check(user.valid && Global.Input.CurrentDevice == null && Paired(user) == "" && InputUser.listenForUnpairedDeviceActivity == 1,
                "A missing device prevented the user from waiting for activity.");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: a pressed unpaired button switches the user to that device's scheme and other activity is ignored", async () =>
        {
            var pad = new Gamepad();
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            InputUser.Reset(keyboard, mouse, pad);
            var host = StartInput();
            var input = Global.Input;
            var user = UserOf(input);
            InputUser.SimulateUse(pad.Axis());
            InputUser.SimulateUse(new UnityEngine.InputSystem.Controls.ButtonControl { device = pad, noisy = true });
            InputUser.SimulateUse(pad.Button(), 0.2f);
            InputUser.SimulateUse(new InputDevice().Button());
            Check(input.CurrentDevice == keyboard && Paired(user) == "Keyboard,Mouse", "Axis, noisy, drifting, or unsupported activity switched the user.");
            Check(InputSystem.actions.FindAction("Player/Fire", true).enabled, "Ignored activity blocked gameplay.");
            InputUser.SimulateUse(pad.Button());
            Check(input.CurrentDevice == pad && Paired(user) == "Gamepad" && user.controlScheme?.name == "Gamepad", "Gamepad press did not replace the keyboard.");
            InputUser.SimulateUse(keyboard.Button());
            Check(input.CurrentDevice == keyboard && Paired(user) == "Keyboard,Mouse" && user.controlScheme?.name == "Keyboard&Mouse",
                "Keyboard press did not switch back: " + Paired(user));
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: the press that switches devices blocks gameplay and navigation until the next frame", async () =>
        {
            var pad = new Gamepad();
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            InputUser.Reset(keyboard, mouse, pad);
            var host = StartInput();
            var input = Global.Input;
            var eventSystem = UIModule(input).gameObject.GetComponent<UnityEngine.EventSystems.EventSystem>();
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            InputUser.SimulateUse(pad.Button());
            Check(!fire.enabled, "The switching press could reach gameplay.");
            host.Update(0f);
            Check(!fire.enabled, "Gameplay resumed in the switching frame.");
            NextInputFrame(host);
            Check(fire.enabled, "Gameplay did not resume on the next frame.");

            input.SetUIFocused(true);
            NextInputFrame(host);
            Check(eventSystem.sendNavigationEvents, "Navigation did not start.");
            InputUser.SimulateUse(keyboard.Button());
            Check(!eventSystem.sendNavigationEvents, "The switching press could reach UI navigation.");
            host.Update(0f);
            Check(!eventSystem.sendNavigationEvents, "Navigation resumed in the switching frame.");
            NextInputFrame(host);
            Check(eventSystem.sendNavigationEvents && !fire.enabled, "Navigation did not resume or UI focus stopped blocking gameplay.");
            input.SetUIFocused(false);
            Check(fire.enabled, "Gameplay did not resume after UI focus was lost.");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: a lost device blocks gameplay but keeps the UI module and navigation, and regaining restores gameplay", async () =>
        {
            var pad = new Gamepad();
            InputUser.Reset(pad);
            var host = StartInput();
            var input = Global.Input;
            var module = UIModule(input);
            var eventSystem = module.gameObject.GetComponent<UnityEngine.EventSystems.EventSystem>();
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            int held = 0, resets = 0;
            input.Controller.AddContext(new InputContext("holder", 0).Bind("Player/Fire", _ => held = 1, consume: false, onReset: () => { held = 0; resets++; }));
            fire.Trigger(InputActionPhase.Started);
            InputUser.SimulateDeviceRemoved(pad);
            Check(input.IsDeviceLost && !fire.enabled && held == 0 && resets == 1, "Device loss did not block and reset gameplay input.");
            Check(module.enabled, "Device loss disabled the UI module.");
            input.SetUIFocused(true);
            NextInputFrame(host);
            Check(eventSystem.sendNavigationEvents, "Device loss blocked UI navigation.");
            input.SetUIFocused(false);
            input.SetInputAllowed(true);
            Check(!fire.enabled, "Scene or UI state re-enabled gameplay while the device was lost.");
            var reconnected = new Gamepad();
            InputUser.SimulateDeviceAdded(reconnected, pad);
            Check(!input.IsDeviceLost && fire.enabled && UserOf(input).lostDevices.Count == 0, "Regaining the device did not restore gameplay.");
            Check(input.CurrentDevice == reconnected, "The regained device instance did not become the current device.");
            InputUser.SimulateDeviceRemoved(reconnected);
            input.SetInputAllowed(false);
            InputUser.SimulateDeviceAdded(reconnected);
            Check(!fire.enabled, "Regaining the device ignored the scene lock.");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: losing the mouse while the keyboard stays paired does not count as device loss", async () =>
        {
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            InputUser.Reset(keyboard, mouse);
            var host = StartInput();
            var input = Global.Input;
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            InputUser.SimulateDeviceRemoved(mouse);
            Check(UserOf(input).lostDevices.Count == 1 && !input.IsDeviceLost && fire.enabled, "A lost mouse locked gameplay while the keyboard was paired.");
            InputUser.SimulateDeviceRemoved(keyboard);
            Check(input.IsDeviceLost && !fire.enabled, "Losing every paired device was not device loss.");
            host.Shutdown();
            await UniTask.CompletedTask;
        });

        await Run("input user: notifications are published once per edge only while all managers are ready", async () =>
        {
            var pad = new Gamepad();
            var keyboard = new Keyboard();
            var mouse = new Mouse();
            InputUser.Reset(keyboard, mouse, pad);
            InputSystem.actions = new InputActionAsset();
            var host = new ManagerHost();
            Global.BindForTests(host);
            host.Register(new InputManager());
            var notify = host.Register(new NotificationManager());
            var gate = new UniTaskCompletionSource();
            host.Register(new Probe<A>(new(), "pending") { Gate = gate.Task });
            UnityEngine.Application.isConsolePlatform = true;
            UniTask initialization;
            try
            {
                initialization = host.InitializeAsync();
            }
            finally
            {
                UnityEngine.Application.isConsolePlatform = false;
            }
            var published = new List<NotificationId>();
            foreach (var id in new[] { NotificationId.InputDeviceLost, NotificationId.InputDeviceRegained, NotificationId.InputControlsChanged })
                notify.Subscribe(id, () => published.Add(id), CancellationToken.None);
            var fire = InputSystem.actions.FindAction("Player/Fire", true);
            InputUser.SimulateDeviceRemoved(pad);
            Check(!fire.enabled, "Device loss was not applied before all managers were ready.");
            InputUser.SimulateDeviceAdded(pad);
            Check(fire.enabled, "Regaining was not applied before all managers were ready.");
            InputUser.SimulateUse(keyboard.Button());
            Check(published.Count == 0, "Input notifications were published during initialization: " + string.Join(",", published));
            gate.TrySetResult();
            await initialization;
            NextInputFrame(host);
            Check(fire.enabled, "The switch during initialization kept gameplay blocked after the host became ready.");

            InputUser.SimulateUse(pad.Button());
            CheckPublished(published, "InputControlsChanged");
            InputUser.SimulateDeviceRemoved(pad);
            CheckPublished(published, "InputDeviceLost");
            InputUser.SimulateDeviceAdded(pad);
            CheckPublished(published, "InputDeviceRegained");
            InputUser.SimulateDeviceRemoved(pad);
            CheckPublished(published, "InputDeviceLost");
            InputUser.SimulateUse(keyboard.Button());
            CheckPublished(published, "InputDeviceRegained,InputControlsChanged");
            NextInputFrame(host);
            Check(Global.Input.CurrentDevice == keyboard && fire.enabled, "Switching away from a lost device did not restore gameplay.");

            host.Shutdown();
            InputUser.SimulateDeviceRemoved(keyboard);
            InputUser.SimulateUse(pad.Button());
            Check(published.Count == 0, "Input notifications were published during or after shutdown.");
        });

        await Run("input user: shutdown removes the user, reverses the listen count and subscriptions, and frees the asset devices", async () =>
        {
            var pad = new Gamepad();
            InputUser.Reset(pad);
            InputUser.listenForUnpairedDeviceActivity = 1;
            var host = StartInput();
            var input = Global.Input;
            var user = UserOf(input);
            Check(InputUser.listenForUnpairedDeviceActivity == 2 && InputUser.ChangeSubscriberCount == 1 && InputUser.UnpairedSubscriberCount == 1 && InputUser.UserCount == 1,
                "Initialization did not listen for unpaired activity.");
            Check(InputSystem.actions.devices != null && InputSystem.actions.devices.Count == 1, "Pairing did not restrict the asset devices.");
            InputUser.Changes.Clear();
            host.Shutdown();
            Check(!user.valid && InputUser.UserCount == 0, "Shutdown did not remove the user.");
            Check(InputUser.listenForUnpairedDeviceActivity == 1 && InputUser.ChangeSubscriberCount == 0 && InputUser.UnpairedSubscriberCount == 0,
                "Shutdown did not reverse only its own listen count and subscriptions.");
            Check(string.Join(",", InputUser.Changes) == "DeviceUnpaired,Removed", "Unexpected removal changes: " + string.Join(",", InputUser.Changes));
            Check(InputSystem.actions.devices == null, "Shutdown left the shared asset bound to an empty device list.");
            Check(input.Controller == null && !InputSystem.actions.FindAction("Player/Fire", true).enabled, "Shutdown did not release the controller.");
            await UniTask.CompletedTask;
        });

        await Run("input user: Switch swaps only the gamepad submit and cancel bindings", async () =>
        {
            InputUser.Reset();
            var asset = new InputActionAsset();
            asset.FindAction("Player/Fire", true).bindings.Add(new InputBinding { path = "<Gamepad>/buttonSouth", groups = "Gamepad" });
            var host = StartInput(asset);
            Check(Overrides(asset) == "|||", "Overrides were applied outside Switch: " + Overrides(asset));
            host.Shutdown();

            UnityEngine.Application.platform = UnityEngine.RuntimePlatform.Switch;
            try
            {
                InputUser.Reset();
                asset = new InputActionAsset();
                asset.FindAction("Player/Fire", true).bindings.Add(new InputBinding { path = "<Gamepad>/buttonSouth", groups = "Gamepad" });
                host = StartInput(asset);
                Check(Overrides(asset) == "<Gamepad>/buttonEast||<Gamepad>/buttonSouth|", "Switch overrides are wrong: " + Overrides(asset));
                Check(asset.FindAction("Player/Fire", true).bindings[0].overridePath == null, "Switch overrides changed the Player map.");
                host.Shutdown();
            }
            finally
            {
                UnityEngine.Application.platform = UnityEngine.RuntimePlatform.WindowsPlayer;
            }
            await UniTask.CompletedTask;
        });

        await Run("input manager: missing project-wide actions fails initialization", async () =>
        {
            InputSystem.actions = null;
            var host = new ManagerHost();
            Global.BindForTests(host);
            var input = host.Register(new InputManager());
            await Throws<InvalidOperationException>(() => host.InitializeAsync());
            Check(host.State == ManagerState.Failed && input.Controller == null, "Missing actions did not fail initialization.");
        });
    }
}
