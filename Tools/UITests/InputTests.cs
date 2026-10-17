using System;
using System.Reflection;
using ProjectT;
using ProjectT.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;

static class InputTests
{
    public static void Run()
    {
        var tests = new (string name, Action test)[]
        {
            ("Retained senders expire across block and disable", RetainedSendersExpire),
            ("Reentrant block rejects the current callback sender", ReentrantBlockRejectsSender),
            ("Captured input is canceled across block and re-enable", BlockedInputRequiresFreshDown),
            ("Normal down drag up click sequence is delivered once", NormalSequence),
            ("Disable clears input and invokes the base lifecycle", DisableClearsInput),
            ("Pointer and button sessions do not cross", IndependentPointerAndButtonSessions),
            ("Fresh down clears abandoned click eligibility", FreshDownClearsAbandonedClick),
            ("Container gate cancels descendant input", ContainerGateCancelsDescendantInput),
            ("Handlers bound while blocked inherit the container gate", CreatedWhileBlockedInheritsContainerGate)
        };

        var passed = 0;
        foreach (var test in tests)
        {
            try
            {
                test.test();
                passed++;
                Console.WriteLine("PASS " + test.name);
            }
            catch (Exception error)
            {
                throw new Exception($"Input test failed after {passed}/{tests.Length} passed: {test.name}", error);
            }
        }

        Console.WriteLine($"{passed}/{tests.Length} input tests passed");
    }

    private static void BlockedInputRequiresFreshDown()
    {
        var handler = CreateHandler();
        var down = 0;
        var drag = 0;
        var up = 0;
        var click = 0;
        handler.OnPointerDownHandler = (_, send) => { down++; send(new Vector2(1, 0)); };
        handler.OnDragHandler = (_, send) => { drag++; send(new Vector2(2, 0)); };
        handler.OnPointerUpHandler = (_, send) => { up++; send(new Vector2(3, 0)); };
        handler.OnClickHandler = (_, send) => { click++; send(new Vector2(4, 0)); };
        var pointer = Pointer(1, PointerEventData.InputButton.Left, true);

        handler.OnPointerDown(pointer);
        handler.SetInputAllowedInternal(false);
        Check(handler.control.ResetCount == 1, "Blocking did not reset the bound input control.");
        handler.SetInputAllowedInternal(true);
        handler.OnDrag(pointer);
        handler.OnPointerUp(pointer);
        handler.OnPointerClick(pointer);
        Check(down == 1 && drag == 0 && up == 0 && click == 0, "A canceled press survived re-enable.");

        handler.OnPointerDown(pointer);
        handler.OnDrag(pointer);
        handler.OnPointerUp(pointer);
        handler.OnPointerClick(pointer);
        Check(down == 2 && drag == 1 && up == 1 && click == 1, "Fresh input was not restored after re-enable.");
    }

    private static void NormalSequence()
    {
        var handler = CreateHandler();
        var down = 0;
        var drag = 0;
        var up = 0;
        var click = 0;
        handler.OnPointerDownHandler = (_, send) => { down++; send(new Vector2()); };
        handler.OnDragHandler = (_, send) => { drag++; send(new Vector2()); };
        handler.OnPointerUpHandler = (_, send) => { up++; send(new Vector2()); };
        handler.OnClickHandler = (_, send) => { click++; send(new Vector2()); };
        var pointer = Pointer(2, PointerEventData.InputButton.Left, true);

        handler.OnPointerDown(pointer);
        handler.OnDrag(pointer);
        handler.OnPointerUp(pointer);
        handler.OnPointerClick(pointer);
        handler.OnPointerClick(pointer);

        Check(down == 1 && drag == 1 && up == 1 && click == 1, "The normal pointer sequence was not delivered exactly once.");
        Check(handler.control.Values.Count == 4, "Callbacks did not write all four control values.");
    }

    private static void DisableClearsInput()
    {
        var handler = CreateHandler();
        var up = 0;
        handler.OnPointerUpHandler = (_, __) => up++;
        var pointer = Pointer(3, PointerEventData.InputButton.Left, true);
        handler.OnPointerDown(pointer);

        typeof(UIEventHandler).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handler, null);

        Check(handler.control.ResetCount == 1, "Disable did not reset the bound input control.");
        Check(((OnScreenControl)handler).BaseDisableCount == 1, "Disable did not invoke the OnScreenControl lifecycle.");
        handler.OnPointerUp(pointer);
        Check(up == 0, "Disable emitted or retained an Up callback.");
        handler.OnPointerDown(pointer);
        handler.OnPointerUp(pointer);
        Check(up == 1, "A fresh press was not accepted after disable cleanup.");
    }

    private static void IndependentPointerAndButtonSessions()
    {
        var handler = CreateHandler();
        var drag = 0;
        var up = 0;
        var click = 0;
        handler.OnDragHandler = (_, __) => drag++;
        handler.OnPointerUpHandler = (_, __) => up++;
        handler.OnClickHandler = (_, __) => click++;
        var left = Pointer(4, PointerEventData.InputButton.Left, true);
        var right = Pointer(4, PointerEventData.InputButton.Right, true);
        var touch = Pointer(5, PointerEventData.InputButton.Left, true);
        var unrelated = Pointer(5, PointerEventData.InputButton.Right, true);

        handler.OnPointerDown(left);
        handler.OnPointerDown(right);
        handler.OnPointerDown(touch);
        handler.OnDrag(unrelated);
        handler.OnDrag(touch);
        handler.OnPointerUp(left);
        handler.OnPointerClick(right);
        handler.OnPointerClick(left);
        handler.OnPointerUp(right);
        handler.OnPointerClick(right);
        handler.OnPointerUp(touch);
        handler.OnPointerClick(touch);

        Check(drag == 1 && up == 3 && click == 3, "Pointer or button sessions crossed each other.");
    }

    private static void FreshDownClearsAbandonedClick()
    {
        var handler = CreateHandler();
        var click = 0;
        handler.OnClickHandler = (_, __) => click++;
        var abandoned = Pointer(10, PointerEventData.InputButton.Left, true);
        handler.OnPointerDown(abandoned);
        handler.OnPointerUp(abandoned);

        var releasedOutside = Pointer(11, PointerEventData.InputButton.Left, false);
        handler.OnPointerDown(releasedOutside);
        handler.OnPointerClick(abandoned);
        handler.OnPointerUp(releasedOutside);
        releasedOutside.eligibleForClick = true;
        handler.OnPointerClick(releasedOutside);

        var fresh = Pointer(12, PointerEventData.InputButton.Left, true);
        handler.OnPointerDown(fresh);
        handler.OnPointerUp(fresh);
        handler.OnPointerClick(fresh);
        Check(click == 1, "Abandoned or ineligible releases remained clickable.");
    }

    private static void ContainerGateCancelsDescendantInput()
    {
        var root = new GameObject("Root").transform;
        var container = new UIContainer(root, default);
        container.Initialize();
        var child = new GameObject("Input");
        child.transform.SetParent(root.Find("Canvas2D"), false);
        var handler = child.AddComponent<UIEventHandler>();
        var up = 0;
        handler.OnPointerUpHandler = (_, __) => up++;
        var pointer = Pointer(20, PointerEventData.InputButton.Left, true);
        handler.OnPointerDown(pointer);

        container.SetInputAllowed(false);
        container.SetInputAllowed(true);
        handler.OnPointerUp(pointer);
        Check(up == 0 && handler.control.ResetCount == 1, "Container blocking did not cancel captured descendant input.");

        handler.OnPointerDown(pointer);
        handler.OnPointerUp(pointer);
        Check(up == 1, "Container re-enable did not accept a fresh descendant press.");
        container.Shutdown();
    }

    private static void CreatedWhileBlockedInheritsContainerGate()
    {
        var container = new UIContainer(new GameObject("Root").transform, default);
        container.Initialize();
        container.SetInputAllowed(false);
        UIEventHandler handler = null;
        var down = 0;
        Probe.EnterAction = probe =>
        {
            handler = probe.gameObject.AddComponent<UIEventHandler>();
            handler.OnPointerDownHandler = (_, __) => down++;
        };
        Global.Resource = new FakeResource { Prefab = CreateProbePrefab() };

        try
        {
            container.CreateWidget<Probe>(UIDefine.eUIType.Test, "UI");
            var pointer = Pointer(21, PointerEventData.InputButton.Left, true);
            handler.OnPointerDown(pointer);
            Check(down == 0, "A handler bound while blocked accepted input.");

            container.SetInputAllowed(true);
            handler.OnPointerDown(pointer);
            Check(down == 1, "A handler bound while blocked did not accept input after re-enable.");
        }
        finally
        {
            Probe.EnterAction = null;
            container.Shutdown();
        }
    }

    private static GameObject CreateProbePrefab()
    {
        var prefab = new GameObject("Probe");
        var probe = prefab.AddComponent<Probe>();
        typeof(UIBase).GetField("type", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(probe, eUIContainerType.Static);
        return prefab;
    }

    private static void RetainedSendersExpire()
    {
        var handler = CreateHandler();
        Action<Vector2> first = null;
        Action<Vector2> current = null;
        handler.OnPointerDownHandler = (_, send) => current = send;
        var pointer = Pointer(80, PointerEventData.InputButton.Left, true);
        handler.OnPointerDown(pointer);
        first = current;
        first(new Vector2(1, 0));
        handler.SetInputAllowedInternal(false);
        first(new Vector2(2, 0));
        handler.SetInputAllowedInternal(true);
        first(new Vector2(3, 0));
        Check(handler.control.Values.Count == 1, "Old sender survived blocking/re-enable.");
        handler.OnPointerDown(pointer);
        current(new Vector2(4, 0));
        Check(handler.control.Values.Count == 2, "Fresh sender cannot write.");
        var sameGeneration = current;
        handler.OnPointerDown(pointer);
        Check(ReferenceEquals(current, sameGeneration), "Sender allocates on every event.");
        typeof(UIEventHandler).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(handler, null);
        current(new Vector2(5, 0));
        Check(handler.control.Values.Count == 2, "Sender survived disabling.");
    }

    private static void ReentrantBlockRejectsSender()
    {
        var handler = CreateHandler();
        handler.OnPointerDownHandler = (_, send) =>
        {
            handler.SetInputAllowedInternal(false);
            handler.SetInputAllowedInternal(true);
            send(new Vector2(1, 0));
        };
        handler.OnPointerDown(Pointer(81, PointerEventData.InputButton.Left, true));
        Check(handler.control.Values.Count == 0, "In-flight callback wrote after cancellation.");
    }

    private static UIEventHandler CreateHandler()
    {
        return new GameObject("Input").AddComponent<UIEventHandler>();
    }

    private static PointerEventData Pointer(int id, PointerEventData.InputButton button, bool eligibleForClick)
    {
        return new PointerEventData { pointerId = id, button = button, eligibleForClick = eligibleForClick };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
