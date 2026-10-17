using System.Collections.Generic;

namespace ProjectT.Pivot
{
    public class ComPivotAgent : UnityEngine.MonoBehaviour { }
}

namespace UnityEngine
{
    public sealed class CanvasGroup : MonoBehaviour { public bool interactable, blocksRaycasts; }
    public sealed class AudioClip : Object { }
    public sealed class AudioSource : MonoBehaviour
    {
        public AudioClip clip;
        public float pitch;
        public bool loop, isPlaying;
        public void Play() => isPlaying = true;
        public void PlayOneShot(AudioClip value) => isPlaying = true;
        public void Stop() => isPlaying = false;
    }
}

namespace UnityEngine.InputSystem
{
    public enum InputActionChange { ActionEnabled, ActionDisabled, BoundControlsChanged }

    public class InputControl
    {
        public string path;
        public bool noisy;
        public InputDevice device;
        public InputControl(string path) { this.path = path; }
    }

    // Devices are matched to control schemes by type; native layout matching is not simulated.
    public class InputDevice : InputControl
    {
        public InputDevice() : base("device") { device = this; }
        public UnityEngine.InputSystem.Controls.ButtonControl Button() => new UnityEngine.InputSystem.Controls.ButtonControl { device = this };
        public InputControl Axis() => new InputControl("axis") { device = this };
    }
    public class Gamepad : InputDevice { public static Gamepad current; }
    public class Keyboard : InputDevice { public static Keyboard current; }
    public class Mouse : InputDevice { public static Mouse current; }

    public struct InputControlScheme
    {
        public string name;
        public System.Type[] deviceTypes;
        public InputControlScheme(string name, params System.Type[] deviceTypes) { this.name = name; this.deviceTypes = deviceTypes; }
        public bool SupportsDevice(InputDevice device) => System.Array.Exists(deviceTypes, type => type.IsInstanceOfType(device));
        public static bool operator ==(InputControlScheme left, InputControlScheme right) => left.name == right.name;
        public static bool operator !=(InputControlScheme left, InputControlScheme right) => left.name != right.name;
        public override bool Equals(object obj) => obj is InputControlScheme other && other == this;
        public override int GetHashCode() => name?.GetHashCode() ?? 0;
    }

    public interface IInputActionCollection
    {
        IReadOnlyList<InputControlScheme> controlSchemes { get; }
        IReadOnlyList<InputDevice> devices { get; set; }
    }

    public static class InputControlExtensions
    {
        public static float ReadValueFromEvent(this UnityEngine.InputSystem.Controls.ButtonControl control, LowLevel.InputEventPtr inputEvent) => inputEvent.Value;
    }

    public struct InputBinding
    {
        public string path, overridePath, groups;
    }

    public static class InputActionRebindingExtensions
    {
        // Matches bindings by their original path like InputBinding.Matches.
        public static void ApplyBindingOverride(this InputAction action, string newPath, string group = null, string path = null)
        {
            for (int i = 0; i < action.bindings.Count; ++i)
            {
                var binding = action.bindings[i];
                if (path != null && binding.path != path)
                    continue;
                binding.overridePath = newPath;
                action.bindings[i] = binding;
            }
        }
    }

    public static class InputSystem
    {
        public static InputActionAsset actions;
        public static event System.Action<object, InputActionChange> onActionChange;
        public static int ActionChangeSubscriberCount => onActionChange?.GetInvocationList().Length ?? 0;
        public static void NotifyActionChange(object target, InputActionChange change) => onActionChange?.Invoke(target, change);
    }

    public class InputActionAsset : UnityEngine.Object, IInputActionCollection, System.ICloneable
    {
        public InputActionMap Map = new();
        public IReadOnlyList<InputDevice> devices { get; set; }
        public IReadOnlyList<InputControlScheme> controlSchemes { get; } = new[]
        {
            new InputControlScheme("Keyboard&Mouse", typeof(Keyboard), typeof(Mouse)),
            new InputControlScheme("Gamepad", typeof(Gamepad))
        };
        public readonly Dictionary<string, InputActionMap> Maps = new(System.StringComparer.OrdinalIgnoreCase) { ["UI"] = CreateUIMap() };

        // Mirrors the UI map of InputSystem_Actions so InputManager can resolve its UI module references.
        private static InputActionMap CreateUIMap()
        {
            var map = new InputActionMap { name = "UI", actions = new List<InputAction>() };
            foreach (var name in new[] { "Navigate", "Submit", "Cancel", "Point", "Click", "RightClick", "MiddleClick", "ScrollWheel", "TrackedDevicePosition", "TrackedDeviceOrientation" })
                map.actions.Add(new InputAction { name = name, actionMap = map });
            map.actions.Find(action => action.name == "Submit").bindings.AddRange(new[]
            {
                new InputBinding { path = "<Gamepad>/buttonSouth", groups = "Gamepad" },
                new InputBinding { path = "*/{Submit}", groups = "Keyboard&Mouse;Touch;Joystick;XR" }
            });
            map.actions.Find(action => action.name == "Cancel").bindings.AddRange(new[]
            {
                new InputBinding { path = "<Gamepad>/buttonEast", groups = "Gamepad" },
                new InputBinding { path = "*/{Cancel}", groups = "Keyboard&Mouse;Touch;Joystick;XR" }
            });
            return map;
        }

        public IEnumerable<InputActionMap> actionMaps
        {
            get
            {
                yield return AttachInternal(Map);
                foreach (var map in Maps.Values)
                    yield return AttachInternal(map);
            }
        }

        object System.ICloneable.Clone() => Clone();
        public InputActionAsset Clone()
        {
            var clone = new InputActionAsset { Map = CloneMap(Map) };
            foreach (var pair in Maps)
                clone.Maps.Add(pair.Key, CloneMap(pair.Value));
            return clone;
        }

        private static InputActionMap CloneMap(InputActionMap source)
        {
            var clone = new InputActionMap { name = source.name, actions = new List<InputAction>() };
            foreach (var action in source.actions)
                clone.actions.Add(new InputAction { name = action.name, actionMap = clone, controls = new List<InputControl>(action.controls) });
            return clone;
        }

        public InputActionMap FindActionMap(string name, bool throwIfNotFound = false)
        {
            InputActionMap map = null;
            if (System.String.Equals(Map.name, name, System.StringComparison.OrdinalIgnoreCase))
                map = Map;
            else
                Maps.TryGetValue(name, out map);

            if (map == null && throwIfNotFound)
                throw new System.ArgumentException("Cannot find action map '" + name + "'.", nameof(name));

            return map == null ? null : AttachInternal(map);
        }

        public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false)
        {
            if (actionNameOrId == null)
                throw new System.ArgumentNullException(nameof(actionNameOrId));

            int separator = actionNameOrId.IndexOf('/');
            string actionName = separator < 0 ? actionNameOrId : actionNameOrId.Substring(separator + 1);
            foreach (var map in actionMaps)
            {
                if (separator >= 0 && !System.String.Equals(map.name, actionNameOrId.Substring(0, separator), System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var action = map.actions.Find(item => System.String.Equals(item.name, actionName, System.StringComparison.OrdinalIgnoreCase));
                if (action != null)
                    return action;
            }

            if (throwIfNotFound)
                throw new System.ArgumentException("No action '" + actionNameOrId + "' in asset.", nameof(actionNameOrId));

            return null;
        }

        private InputActionMap AttachInternal(InputActionMap map)
        {
            map.asset = this;
            foreach (var action in map.actions)
                action.actionMap = map;
            return map;
        }
    }
    public class InputActionMap
    {
        public string name = "Player";
        public InputActionAsset asset;
        public bool enabled => actions.Exists(action => action.enabled);
        public List<InputAction> actions = new() { new InputAction() };

        public void Enable()
        {
            foreach (var action in actions)
            {
                action.actionMap = this;
                action.Enable();
            }
        }

        public void Disable()
        {
            foreach (var action in actions)
                action.Disable();
        }
    }

    public enum InputActionPhase { Disabled, Waiting, Started, Performed, Canceled }

    public class InputAction
    {
        public string name = "Fire";
        public bool enabled;
        public bool inProgress;
        public int DisableCalls;
        public InputActionMap actionMap;
        public List<InputControl> controls = new();
        public List<InputBinding> bindings = new();
        public struct CallbackContext
        {
            public InputAction action;
            public InputActionPhase phase;
            public bool started => phase == InputActionPhase.Started;
            public bool performed => phase == InputActionPhase.Performed;
            public bool canceled => phase == InputActionPhase.Canceled;
        }
        public event System.Action<CallbackContext> started, performed, canceled;
        public int SubscriberCount => (started?.GetInvocationList().Length ?? 0) +
            (performed?.GetInvocationList().Length ?? 0) + (canceled?.GetInvocationList().Length ?? 0);
        public bool IsPressed() => true;
        public bool WasPressedThisFrame() => true;

        // The probe supplies synchronous phase events; native Input System timing is not simulated.
        public void Trigger(InputActionPhase phase)
        {
            if (!enabled)
                return;

            inProgress = phase == InputActionPhase.Started || phase == InputActionPhase.Performed;
            var context = new CallbackContext { action = this, phase = phase };
            if (phase == InputActionPhase.Started)
                started?.Invoke(context);
            else if (phase == InputActionPhase.Performed)
                performed?.Invoke(context);
            else if (phase == InputActionPhase.Canceled)
                canceled?.Invoke(context);
        }

        public void Enable() => enabled = true;

        public void Disable()
        {
            if (!enabled)
                return;

            DisableCalls++;
            try
            {
                if (inProgress)
                {
                    inProgress = false;
                    canceled?.Invoke(new CallbackContext { action = this, phase = InputActionPhase.Canceled });
                }
            }
            finally
            {
                enabled = false;
            }
        }
    }
}

namespace UnityEngine.InputSystem
{
    public sealed class InputActionReference : UnityEngine.Object
    {
        public InputAction action { get; private set; }
        public static InputActionReference Create(InputAction action) => new InputActionReference { action = action };
    }
}

namespace UnityEngine.EventSystems
{
    // Unity selection routing and module ticking are not simulated; tests only observe ownership and enabled state.
    public class EventSystem : UnityEngine.MonoBehaviour { public bool sendNavigationEvents = true; }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.MonoBehaviour
    {
        public InputActionAsset actionsAsset;
        public bool deselectOnBackgroundClick = true;
        public InputActionReference point, leftClick, rightClick, middleClick, scrollWheel, move, submit, cancel, trackedDevicePosition, trackedDeviceOrientation;
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl : InputControl
    {
        public float pressPointOrDefault = 0.5f;
        public ButtonControl() : base("button") { }
        public bool IsValueConsideredPressed(float value) => value >= pressPointOrDefault;
    }
}

namespace UnityEngine.InputSystem.LowLevel
{
    // Carries the value the control has in the simulated event.
    public struct InputEventPtr { public float Value; }
}

namespace UnityEngine.InputSystem.Users
{
    public enum InputUserChange { Added, Removed, DevicePaired, DeviceUnpaired, DeviceLost, DeviceRegained, ControlSchemeChanged, ControlsChanged }

    [System.Flags]
    public enum InputUserPairingOptions { None = 0, UnpairCurrentDevicesFromUser = 1 << 3 }

    // Mirrors the InputUser transitions InputManager relies on (Plugins/Users/InputUser.cs); native event processing is not simulated.
    public struct InputUser
    {
        private sealed class UserData
        {
            public readonly List<InputDevice> Paired = new(), Lost = new();
            public IInputActionCollection Actions;
            public InputControlScheme? Scheme;
        }

        private static readonly Dictionary<int, UserData> users = new();
        private static int nextId;
        private static int listenCount;
        private static event System.Action<InputUser, InputUserChange, InputDevice> change;
        private static event System.Action<InputControl, LowLevel.InputEventPtr> unpairedDeviceUsed;
        public static readonly List<InputDevice> ConnectedDevices = new();
        public static readonly List<InputUserChange> Changes = new();

        private int id;

        public static event System.Action<InputUser, InputUserChange, InputDevice> onChange { add => change += value; remove => change -= value; }
        public static event System.Action<InputControl, LowLevel.InputEventPtr> onUnpairedDeviceUsed { add => unpairedDeviceUsed += value; remove => unpairedDeviceUsed -= value; }
        public static int ChangeSubscriberCount => change?.GetInvocationList().Length ?? 0;
        public static int UnpairedSubscriberCount => unpairedDeviceUsed?.GetInvocationList().Length ?? 0;
        public static int UserCount => users.Count;

        public static int listenForUnpairedDeviceActivity
        {
            get => listenCount;
            set
            {
                if (value < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(value));
                listenCount = value;
            }
        }

        public bool valid => id != 0 && users.ContainsKey(id);
        public IReadOnlyList<InputDevice> pairedDevices => users[id].Paired;
        public IReadOnlyList<InputDevice> lostDevices => users[id].Lost;
        public IInputActionCollection actions => users[id].Actions;
        public InputControlScheme? controlScheme => users[id].Scheme;

        public static void Reset(params InputDevice[] connected)
        {
            users.Clear();
            Changes.Clear();
            change = null;
            unpairedDeviceUsed = null;
            listenCount = 0;
            ConnectedDevices.Clear();
            ConnectedDevices.AddRange(connected);
            Gamepad.current = (Gamepad)ConnectedDevices.Find(device => device is Gamepad);
            Keyboard.current = (Keyboard)ConnectedDevices.Find(device => device is Keyboard);
            Mouse.current = (Mouse)ConnectedDevices.Find(device => device is Mouse);
        }

        public static InputUser CreateUserWithoutPairedDevices()
        {
            var user = new InputUser { id = ++nextId };
            users.Add(user.id, new UserData());
            Notify(user, InputUserChange.Added, null);
            return user;
        }

        public static InputUser PerformPairingWithDevice(InputDevice device, InputUser user = default, InputUserPairingOptions options = InputUserPairingOptions.None)
        {
            if ((options & InputUserPairingOptions.UnpairCurrentDevicesFromUser) != 0)
                user.UnpairDevices();
            if (!users[user.id].Paired.Contains(device))
            {
                users[user.id].Paired.Add(device);
                SyncDevices(users[user.id]);
                Notify(user, InputUserChange.DevicePaired, device);
            }
            return user;
        }

        public void AssociateActionsWithUser(IInputActionCollection actions)
        {
            users[id].Actions = actions;
            SyncDevices(users[id]);
        }

        private static void SyncDevices(UserData data)
        {
            if (data.Actions != null)
                data.Actions.devices = data.Paired.ToArray();
        }

        public ControlSchemeChangeSyntax ActivateControlScheme(InputControlScheme scheme)
        {
            var data = users[id];
            if (data.Scheme != scheme)
            {
                data.Scheme = scheme;
                Notify(this, InputUserChange.ControlSchemeChanged, null);
            }
            return new ControlSchemeChangeSyntax { User = this };
        }

        // Lost devices are dropped without a notification like InputUser.UnpairDevices.
        public void UnpairDevices()
        {
            var data = users[id];
            data.Lost.Clear();
            while (data.Paired.Count > 0)
            {
                var device = data.Paired[data.Paired.Count - 1];
                data.Paired.RemoveAt(data.Paired.Count - 1);
                SyncDevices(data);
                Notify(this, InputUserChange.DeviceUnpaired, device);
            }
        }

        public void UnpairDevicesAndRemoveUser()
        {
            UnpairDevices();
            Notify(this, InputUserChange.Removed, null);
            users.Remove(id);
            id = 0;
        }

        // InputDeviceChange.Removed: the device is added to the lost list (DeviceLost) before it is unpaired.
        public static void SimulateDeviceRemoved(InputDevice device)
        {
            ConnectedDevices.Remove(device);
            foreach (var pair in new List<KeyValuePair<int, UserData>>(users))
            {
                if (!pair.Value.Paired.Contains(device))
                    continue;
                pair.Value.Lost.Add(device);
                Notify(new InputUser { id = pair.Key }, InputUserChange.DeviceLost, device);
                pair.Value.Paired.Remove(device);
                SyncDevices(pair.Value);
                Notify(new InputUser { id = pair.Key }, InputUserChange.DeviceUnpaired, device);
            }
        }

        // InputDeviceChange.Added: the device leaves the lost list before DeviceRegained, then is paired again.
        // A reconnected device may be a new instance; the lost entry it replaces is matched by the caller.
        public static void SimulateDeviceAdded(InputDevice device, InputDevice lostDevice = null)
        {
            ConnectedDevices.Add(device);
            foreach (var pair in new List<KeyValuePair<int, UserData>>(users))
            {
                if (!pair.Value.Lost.Remove(lostDevice ?? device))
                    continue;
                Notify(new InputUser { id = pair.Key }, InputUserChange.DeviceRegained, device);
                pair.Value.Paired.Add(device);
                SyncDevices(pair.Value);
                Notify(new InputUser { id = pair.Key }, InputUserChange.DevicePaired, device);
            }
        }

        // Mirrors InputUser.OnEvent: only listened activity on unpaired devices is reported.
        public static void SimulateUse(InputControl control, float value = 1f)
        {
            if (listenCount == 0)
                return;
            foreach (var data in users.Values)
            {
                if (data.Paired.Contains(control.device))
                    return;
            }
            unpairedDeviceUsed?.Invoke(control, new LowLevel.InputEventPtr { Value = value });
        }

        private static void Notify(InputUser user, InputUserChange value, InputDevice device)
        {
            Changes.Add(value);
            change?.Invoke(user, value, device);
        }

        public static bool operator ==(InputUser left, InputUser right) => left.id == right.id;
        public static bool operator !=(InputUser left, InputUser right) => left.id != right.id;
        public override bool Equals(object obj) => obj is InputUser other && other.id == id;
        public override int GetHashCode() => id;

        public struct ControlSchemeChangeSyntax
        {
            internal InputUser User;

            public ControlSchemeChangeSyntax AndPairRemainingDevices()
            {
                var data = users[User.id];
                foreach (var type in data.Scheme.Value.deviceTypes)
                {
                    if (data.Paired.Exists(type.IsInstanceOfType))
                        continue;
                    var available = ConnectedDevices.Find(device => type.IsInstanceOfType(device) && !IsPaired(device));
                    if (available != null)
                        PerformPairingWithDevice(available, User);
                }
                return this;
            }

            private static bool IsPaired(InputDevice device)
            {
                foreach (var other in users.Values)
                {
                    if (other.Paired.Contains(device))
                        return true;
                }
                return false;
            }
        }
    }
}
