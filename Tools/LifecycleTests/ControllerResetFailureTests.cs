using System;
using Cysharp.Threading.Tasks;
using ProjectT.Controller;
using UnityEngine.InputSystem;

static partial class Program
{
    static async UniTask RunControllerResetFailureTests()
    {
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
    }
}
