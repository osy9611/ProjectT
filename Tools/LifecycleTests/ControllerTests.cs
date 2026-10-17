using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

static partial class Program
{
    sealed class ControllerProbe : Controller
    {
        public bool HasAsset => inputActionAsset != null;
    }

    sealed class InputFixture
    {
        public ControllerProbe Controller;
        public InputActionAsset Asset;
        public InputAction Fire, Move, Click;
    }

    static InputFixture CreateInputFixture()
    {
        var asset = new InputActionAsset();
        var player = asset.Map;
        player.actions.Clear();
        var fire = new InputAction { name = "Fire", actionMap = player };
        var move = new InputAction { name = "Move", actionMap = player };
        player.actions.Add(fire);
        player.actions.Add(move);
        var menu = new InputActionMap { name = "Menu", actions = new List<InputAction>() };
        asset.Maps.Add("Menu", menu);
        var click = new InputAction { name = "Click", actionMap = menu };
        menu.actions.Add(click);
        var controller = new ControllerProbe();
        controller.Init(asset, "Player");
        controller.Enable();
        return new InputFixture { Controller = controller, Asset = asset, Fire = fire, Move = move, Click = click };
    }

    static UnityEngine.InputSystem.UI.InputSystemUIInputModule UIModule(InputManager input)
    {
        return (UnityEngine.InputSystem.UI.InputSystemUIInputModule)typeof(InputManager)
            .GetField("uiInputModule", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(input);
    }

    static UniTask RunControllerCase(string name, Action test)
    {
        return Run("input: " + name, () =>
        {
            test();
            return UniTask.CompletedTask;
        });
    }

    static void CheckInputTrace(List<string> trace, string expected)
    {
        Check(string.Join(",", trace) == expected, "Expected " + expected + "; received " + string.Join(",", trace));
    }

    static InputUser UserOf(InputManager input)
    {
        return (InputUser)typeof(InputManager)
            .GetField("user", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(input);
    }

    static void NextInputFrame(ManagerHost host)
    {
        UnityEngine.Time.frameCount++;
        host.Update(0f);
    }

    static string Paired(InputUser user)
    {
        var names = new List<string>();
        foreach (var device in user.pairedDevices)
            names.Add(device.GetType().Name);
        return string.Join(",", names);
    }

    static void CheckPublished(List<NotificationId> published, string expected)
    {
        Check(string.Join(",", published) == expected, "Expected " + expected + "; published " + string.Join(",", published));
        published.Clear();
    }

    static string Overrides(InputActionAsset asset)
    {
        var paths = new List<string>();
        foreach (var name in new[] { "UI/Submit", "UI/Cancel" })
        {
            foreach (var binding in asset.FindAction(name, true).bindings)
                paths.Add(binding.overridePath);
        }
        return string.Join("|", paths);
    }

    static async UniTask RunControllerTests()
    {
        await RunControllerCase("priority consumes only the same action event", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            var low = new InputContext("player", 0)
                .Bind("Player/Fire", _ => trace.Add("low"))
                .Bind("Player/Move", _ => trace.Add("move"));
            var high = new InputContext("menu", 10)
                .Bind("Player/Fire", _ => trace.Add("high"));
            f.Controller.AddContext(low);
            f.Controller.AddContext(high);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Performed);
            f.Click.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "high,move");
            Check(f.Fire.enabled && f.Move.enabled && !f.Click.enabled, "A context changed action enablement.");
            f.Controller.RemoveContext(high);
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "high,move,low");
            f.Controller.Release();
        });

        await RunControllerCase("pass through and equal priority are deterministic", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            f.Controller.AddContext(new InputContext("first", 5).Bind("Player/Fire", _ => trace.Add("first")));
            f.Controller.AddContext(new InputContext("later", 5).Bind("Player/Fire", _ => trace.Add("later"), consume: false));
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "later,first");
            f.Controller.Release();
        });

        await RunControllerCase("phase flags route independently", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            f.Controller.AddContext(new InputContext("low", 0)
                .Bind("Player/Fire", c => trace.Add(c.phase.ToString())));
            f.Controller.AddContext(new InputContext("high", 10)
                .Bind("Player/Fire", _ => trace.Add("high"), eInputEvent.Performed));
            f.Fire.Trigger(InputActionPhase.Started);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Fire.Trigger(InputActionPhase.Canceled);
            CheckInputTrace(trace, "Started,high,Canceled");
            f.Controller.Release();
        });

        await RunControllerCase("a removed context is skipped and an added context waits for the next event", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            var old = new InputContext("old", 0).Bind("Player/Fire", _ => trace.Add("old"));
            var added = new InputContext("added", 20).Bind("Player/Fire", _ => trace.Add("added"), consume: false);
            var first = new InputContext("first", 10).Bind("Player/Fire", _ =>
            {
                trace.Add("first");
                f.Controller.RemoveContext(old);
                f.Controller.AddContext(added);
            }, consume: false);
            f.Controller.AddContext(old);
            f.Controller.AddContext(first);
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "first");
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "first,added,first");
            f.Controller.Release();
        });

        await RunControllerCase("remove and readd of the same context cannot use a stale snapshot", () =>
        {
            var f = CreateInputFixture();
            int tail = 0;
            var target = new InputContext("target", 0).Bind("Player/Fire", _ => tail++);
            f.Controller.AddContext(target);
            f.Controller.AddContext(new InputContext("head", 10).Bind("Player/Fire", _ =>
            {
                f.Controller.RemoveContext(target);
                f.Controller.AddContext(target);
            }, consume: false));
            f.Fire.Trigger(InputActionPhase.Performed);
            Check(tail == 0, "Readded context received the current event.");
            f.Fire.Trigger(InputActionPhase.Performed);
            Check(tail == 0, "Repeated readdition delivered a stale event.");
            f.Controller.Release();
        });

        await RunControllerCase("self removing consumer still consumes current event", () =>
        {
            var f = CreateInputFixture();
            int low = 0;
            var high = new InputContext("high", 10);
            high.Bind("Player/Fire", _ => f.Controller.RemoveContext(high));
            f.Controller.AddContext(new InputContext("low", 0).Bind("Player/Fire", _ => low++));
            f.Controller.AddContext(high);
            f.Fire.Trigger(InputActionPhase.Performed);
            Check(low == 0, "Self removal leaked the event.");
            f.Fire.Trigger(InputActionPhase.Performed);
            Check(low == 1, "Lower context did not resume.");
            f.Controller.Release();
        });

        await RunControllerCase("binding mutation applies to the next dispatch", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            var context = new InputContext("mutable", 0);
            context.Bind("Player/Fire", _ =>
            {
                trace.Add("old");
                context.Bind("Player/Fire", __ => trace.Add("new"));
            });
            f.Controller.AddContext(context);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "old,new");
            context.Unbind("Player/Fire");
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "old,new");
            f.Controller.Release();
        });

        await RunControllerCase("a higher callback cannot expose a new lower binding in the current event", () =>
        {
            var f = CreateInputFixture();
            var trace = new List<string>();
            bool changed = false;
            var lower = new InputContext("lower", 0).Bind("Player/Fire", _ => trace.Add("old"));
            f.Controller.AddContext(lower);
            f.Controller.AddContext(new InputContext("higher", 10).Bind("Player/Fire", _ =>
            {
                if (!changed)
                {
                    changed = true;
                    lower.Bind("Player/Fire", __ => trace.Add("new"));
                }
            }, consume: false));
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "");
            f.Fire.Trigger(InputActionPhase.Performed);
            CheckInputTrace(trace, "new");
            f.Controller.Release();
        });

        await RunControllerCase("only the hidden held binding resets", () =>
        {
            var f = CreateInputFixture();
            int fire = 0, move = 0, fireResets = 0, moveResets = 0;
            var low = new InputContext("low", 0)
                .Bind("Player/Fire", _ => fire = 1, eInputEvent.Performed, onReset: () => { fire = 0; fireResets++; })
                .Bind("Player/Move", _ => move = 1, eInputEvent.Performed, onReset: () => { move = 0; moveResets++; });
            f.Controller.AddContext(low);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Performed);
            f.Controller.AddContext(new InputContext("high", 10)
                .Bind("Player/Fire", _ => { }, eInputEvent.Performed));
            Check(fire == 0 && move == 1 && fireResets == 1 && moveResets == 0,
                "Priority reset affected an unrelated held action.");
            f.Controller.Release();
        });

        await RunControllerCase("held input is not replayed to an added or unblocked context", () =>
        {
            var f = CreateInputFixture();
            int lowMove = 0, addedMove = 0;
            var low = new InputContext("low", 0).Bind("Player/Move", _ => lowMove++, eInputEvent.Performed);
            var high = new InputContext("high", 10).Bind("Player/Move", _ => { }, eInputEvent.Performed);
            f.Controller.AddContext(low);
            f.Controller.AddContext(high);
            f.Move.Trigger(InputActionPhase.Performed);
            f.Controller.RemoveContext(high);
            f.Controller.AddContext(new InputContext("added", 20)
                .Bind("Player/Move", _ => addedMove++, eInputEvent.Performed, consume: false));
            Check(lowMove == 0 && addedMove == 0, "Held input was replayed when a context was added or unblocked.");
            f.Move.Trigger(InputActionPhase.Performed);
            Check(lowMove == 1 && addedMove == 1, "The next input event did not reach the added and unblocked contexts.");
            f.Controller.Release();
        });

        await RunControllerCase("phase specific registration waits to reset until access is lost", () =>
        {
            var f = CreateInputFixture();
            int held = 0;
            f.Controller.AddContext(new InputContext("low", 0)
                .Bind("Player/Fire", _ => held = 1, eInputEvent.Start, onReset: () => held = 0));
            f.Fire.Trigger(InputActionPhase.Started);
            f.Controller.AddContext(new InputContext("high", 10)
                .Bind("Player/Fire", _ => { }, eInputEvent.Cancel));
            Check(held == 1, "Cancel only binding reset a started action.");
            f.Fire.Trigger(InputActionPhase.Canceled);
            Check(held == 0, "Cancellation left held state.");
            f.Controller.Release();
        });

        await RunControllerCase("block disable and release reset held state without fabricated cancellation", () =>
        {
            var f = CreateInputFixture();
            int held = 0, resets = 0, canceled = 0;
            f.Controller.AddContext(new InputContext("move", 0)
                .Bind("Player/Move", c =>
                {
                    if (c.canceled)
                        canceled++;
                    else
                        held = 1;
                }, onReset: () => { held = 0; resets++; }));
            f.Move.Trigger(InputActionPhase.Started);
            f.Controller.SetInputAllowed(false);
            Check(held == 0 && resets == 1 && canceled == 0 && !f.Move.enabled, "Block did not reset and disable.");
            f.Controller.SetInputAllowed(true);
            f.Move.Trigger(InputActionPhase.Started);
            f.Controller.Disable();
            Check(held == 0 && resets == 2 && canceled == 0 && !f.Move.enabled, "Disable delivered a synthetic cancellation.");
            f.Controller.Enable();
            f.Move.Trigger(InputActionPhase.Started);
            f.Controller.Release();
            Check(held == 0 && resets == 3 && canceled == 0 && f.Move.SubscriberCount == 0 && !f.Controller.HasAsset,
                "Release retained state or subscriptions.");
        });

        await RunControllerCase("release in a callback stops delivery and detaches contexts", () =>
        {
            var f = CreateInputFixture();
            int tail = 0;
            var context = new InputContext("head", 10).Bind("Player/Fire", _ => f.Controller.Release(), consume: false);
            f.Controller.AddContext(context);
            f.Controller.AddContext(new InputContext("tail", 0).Bind("Player/Fire", _ => tail++));
            f.Fire.Trigger(InputActionPhase.Performed);
            Check(tail == 0 && !f.Controller.HasAsset && context.Unbind("Player/Fire") == context,
                "Release did not stop delivery or detach the context.");
        });

        await RunControllerCase("a rebound context can move to another controller", () =>
        {
            var first = CreateInputFixture();
            var second = CreateInputFixture();
            int calls = 0;
            var context = new InputContext("portable", 0).Bind("Player/Fire", _ => { });
            first.Controller.AddContext(context);
            context.Bind("Player/Fire", _ => calls++);
            first.Controller.RemoveContext(context);
            second.Controller.AddContext(context);
            second.Fire.Trigger(InputActionPhase.Performed);
            Check(calls == 1, "A previous owner's binding generation suppressed the new owner.");
            first.Controller.Release();
            second.Controller.Release();
        });

        await RunControllerCase("reset callback removing a consumer preserves newly unblocked held input", () =>
        {
            var f = CreateInputFixture();
            int tailHeld = 0;
            var high = new InputContext("high", 20).Bind("Player/Fire", _ => { });
            var middle = new InputContext("middle", 10).Bind("Player/Fire", _ => { },
                eInputEvent.Performed, consume: false, onReset: () => f.Controller.RemoveContext(high));
            var tail = new InputContext("tail", 0).Bind("Player/Fire", _ => tailHeld = 1,
                eInputEvent.Performed, consume: false, onReset: () => tailHeld = 0);
            f.Controller.AddContext(tail);
            f.Controller.AddContext(middle);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Controller.AddContext(high);
            Check(tailHeld == 1, "A removed consumer still reset an unblocked binding.");
            f.Controller.Release();
        });

        await RunControllerCase("adding a consumer during dispatch cannot leave lower held input active", () =>
        {
            var f = CreateInputFixture();
            int held = 0, highCalls = 0;
            var high = new InputContext("high", 20).Bind("Player/Move", _ => highCalls++);
            f.Controller.AddContext(new InputContext("low", 0).Bind("Player/Move", _ => held = 1,
                eInputEvent.Performed, onReset: () => held = 0));
            f.Controller.AddContext(new InputContext("switch", 10).Bind("Player/Move", _ => f.Controller.AddContext(high),
                eInputEvent.Performed, consume: false));
            f.Move.Trigger(InputActionPhase.Performed);
            Check(highCalls == 0 && held == 0, "New context received an old event or left blocked input held.");
            f.Controller.Release();
        });

        await RunControllerCase("a consuming cancellation also resets earlier performed-only listeners", () =>
        {
            var f = CreateInputFixture();
            int held = 0;
            f.Controller.AddContext(new InputContext("high", 10).Bind("Player/Move", _ => held = 1,
                eInputEvent.Performed, consume: false, onReset: () => held = 0));
            f.Controller.AddContext(new InputContext("low", 0).Bind("Player/Move", _ => { }, eInputEvent.Cancel));
            f.Move.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Canceled);
            Check(held == 0, "Cancellation consumption skipped an earlier listener's reset.");
            f.Controller.Release();
        });

        await RunControllerCase("reenable requested during reset applies after disabling completes", () =>
        {
            var f = CreateInputFixture();
            int resets = 0;
            f.Controller.AddContext(new InputContext("input", 0).Bind("Player/Move", _ => { },
                eInputEvent.Performed, onReset: () => { resets++; f.Controller.Enable(); }));
            f.Move.Trigger(InputActionPhase.Performed);
            f.Controller.Disable();
            Check(resets == 1 && f.Move.enabled && f.Fire.enabled, "Reset lost a reenable request.");
            f.Controller.Release();
        });

        await RunControllerCase("release detaches every context before a reset failure propagates unchanged", () =>
        {
            var f = CreateInputFixture();
            int resets = 0;
            var failure = new InvalidOperationException("reset failure");
            var failing = new InputContext("failing", 10)
                .Bind("Player/Fire", _ => { }, consume: false, onReset: () => throw failure);
            var later = new InputContext("later", 0)
                .Bind("Player/Move", _ => { }, onReset: () => resets++);
            f.Controller.AddContext(failing);
            f.Controller.AddContext(later);
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Performed);
            Exception caught = null;
            try
            {
                f.Controller.Release();
            }
            catch (Exception error)
            {
                caught = error;
            }
            Check(ReferenceEquals(caught, failure), "Reset error was wrapped or lost.");
            Check(resets == 0 && !f.Controller.HasAsset && f.Fire.SubscriberCount == 0 && failing.Owner == null && later.Owner == null,
                "A reset exception left the controller holding contexts or subscriptions.");
            Check(failing.Resolved.Count == 0 && later.Resolved.Count == 0, "A reset exception left a context resolved.");
            Check(later.Unbind("Player/Move") == later && resets == 1, "A skipped reset was not delivered by the later cleanup.");
            var reused = CreateInputFixture();
            reused.Controller.AddContext(failing);
            Check(ReferenceEquals(failing.Owner, reused.Controller), "A released context could not be registered again.");
            reused.Controller.Release();
        });

        await RunControllerCase("a reset failure during release does not leave the controller releasing", () =>
        {
            var f = CreateInputFixture();
            var context = new InputContext("input", 0)
                .Bind("Player/Fire", _ => { }, onReset: () => throw new InvalidOperationException("reset failure"));
            f.Controller.AddContext(context);
            f.Fire.Trigger(InputActionPhase.Performed);
            ThrowsSync<InvalidOperationException>(f.Controller.Release).GetAwaiter().GetResult();
            var asset = new InputActionAsset();
            f.Controller.Init(asset, "Player");
            f.Controller.Enable();
            Check(f.Controller.HasAsset && asset.FindAction("Player/Fire", true).enabled, "Release left the controller stuck releasing.");
            f.Controller.Release();
        });

        await RunControllerCase("a failing hidden binding reset propagates unchanged and later bindings reset on cleanup", () =>
        {
            var f = CreateInputFixture();
            int held = 0;
            var failure = new InvalidOperationException("hidden reset");
            var first = new InputContext("first", 10).Bind("Player/Fire", _ => { }, consume: false,
                onReset: () => throw failure);
            f.Controller.AddContext(first);
            f.Controller.AddContext(new InputContext("tail", 0).Bind("Player/Fire", _ => held = 1,
                onReset: () => held = 0));
            f.Fire.Trigger(InputActionPhase.Performed);
            var high = new InputContext("high", 20).Bind("Player/Fire", _ => { });
            Exception caught = null;
            try
            {
                f.Controller.AddContext(high);
            }
            catch (Exception error)
            {
                caught = error;
            }
            Check(ReferenceEquals(caught, failure) && ReferenceEquals(high.Owner, f.Controller), "Hidden reset error was wrapped or registration was lost.");
            Check(held == 1, "Remaining hidden resets ran after the first failure.");
            f.Controller.Disable();
            Check(held == 0, "Deactivation did not reset the skipped binding.");
            f.Controller.Release();
        });

        await RunControllerCase("rebinding a held binding resets it even when a hidden reset fails", () =>
        {
            var f = CreateInputFixture();
            int held = 0;
            var failure = new InvalidOperationException("hidden reset");
            var hidden = new InputContext("hidden", 0).Bind("Player/Fire", _ => { }, eInputEvent.Performed,
                onReset: () => throw failure);
            var target = new InputContext("target", 10).Bind("Player/Fire", _ => held = 1, eInputEvent.Performed,
                consume: false, onReset: () => held = 0);
            f.Controller.AddContext(hidden);
            f.Controller.AddContext(target);
            f.Fire.Trigger(InputActionPhase.Performed);
            Exception caught = null;
            try
            {
                target.Bind("Player/Fire", _ => { }, eInputEvent.Performed);
            }
            catch (Exception error)
            {
                caught = error;
            }
            Check(ReferenceEquals(caught, failure), "Hidden reset error was wrapped or lost.");
            Check(held == 0, "A hidden reset failure skipped the replaced binding's reset.");
            f.Controller.Release();
        });

        await RunControllerCase("an owed reset failure does not prevent reactivation", () =>
        {
            var f = CreateInputFixture();
            f.Controller.AddContext(new InputContext("input", 0)
                .Bind("Player/Fire", _ => { }, eInputEvent.Performed, onReset: () => throw new InvalidOperationException("fire reset"))
                .Bind("Player/Move", _ => { }, eInputEvent.Performed, onReset: () => throw new InvalidOperationException("move reset")));
            f.Fire.Trigger(InputActionPhase.Performed);
            f.Move.Trigger(InputActionPhase.Performed);
            ThrowsSync<InvalidOperationException>(() => f.Controller.SetInputAllowed(false)).GetAwaiter().GetResult();
            f.Controller.SetInputAllowed(true);
            Check(f.Fire.enabled && f.Move.enabled, "An owed reset blocked reactivation.");
            ThrowsSync<InvalidOperationException>(f.Controller.Disable).GetAwaiter().GetResult();
            f.Controller.Enable();
            Check(f.Fire.enabled && f.Move.enabled, "An owed reset blocked reenable.");
            f.Controller.Release();
        });

        await RunControllerCase("consumed cancellation reset failure propagates unchanged", () =>
        {
            var f = CreateInputFixture();
            int held = 0;
            f.Controller.AddContext(new InputContext("high", 20).Bind("Player/Move", _ => { },
                eInputEvent.Performed, consume: false, onReset: () => throw new InvalidOperationException("cancel reset failure")));
            f.Controller.AddContext(new InputContext("middle", 10).Bind("Player/Move", _ => { }, eInputEvent.Cancel));
            f.Controller.AddContext(new InputContext("low", 0).Bind("Player/Move", _ => held = 1, eInputEvent.Performed,
                onReset: () => held = 0));
            f.Move.Trigger(InputActionPhase.Performed);
            ThrowsSync<InvalidOperationException>(() => f.Move.Trigger(InputActionPhase.Canceled)).GetAwaiter().GetResult();
            Check(held == 1, "Remaining cancellation resets ran after the first failure.");
            f.Controller.Disable();
            Check(held == 0, "Deactivation did not reset the skipped binding.");
            f.Controller.Release();
        });

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
            BindGlobal(host);
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
                notify.Subscribe(id, _ => published.Add(id));
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
            BindGlobal(host);
            var input = host.Register(new InputManager());
            await Throws<InvalidOperationException>(() => host.InitializeAsync());
            Check(host.State == ManagerState.Failed && input.Controller == null, "Missing actions did not fail initialization.");
        });
    }
}
