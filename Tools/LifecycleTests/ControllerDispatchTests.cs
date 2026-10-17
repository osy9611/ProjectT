using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ProjectT.Controller;
using UnityEngine.InputSystem;

static partial class Program
{
    static async UniTask RunControllerDispatchTests()
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
    }
}
