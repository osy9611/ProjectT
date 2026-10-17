#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ProjectT.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;

public class ProjectTUIPlayProbe : UIBase
{
    public Action Shown;
    public int EnableCount, DisableCount;

    private void OnEnable() { EnableCount++; }

    private void OnDisable() { DisableCount++; }

    protected override void OnShow() { Shown?.Invoke(); }

    protected override void OnHide() { }

    public static IEnumerator Verify()
    {
        var root = new GameObject("UI integration root");
        var container = new UIContainer(root.transform, default);
        container.Initialize();
        var canvas = root.transform.Find("Canvas2D");
        var a = Create(container, canvas, 0);
        var b = Create(container, canvas, 1);
        var c = Create(container, canvas, 2);
        a.Show();
        b.Show();
        Check(!a.gameObject.activeSelf && b.gameObject.activeSelf, "Static navigation");
        UnityEngine.Object.Destroy(b.gameObject);
        yield return null;
        Check(container.TopWidget == a && a.gameObject.activeSelf, "Delayed Destroy restores previous");
        Check(a.EnableCount == 2 && a.DisableCount == 1, "Native activation callbacks during restore");
        var d = Create(container, canvas, 3);
        d.Shown = () => { c.Show(); throw new InvalidOperationException("expected show error"); };
        try { d.Show(); throw new Exception("Missing show error"); }
        catch (InvalidOperationException error)
        {
            if (error.Message != "expected show error")
                throw;
        }
        // 가려진 d는 복원될 때 OnShow를 다시 받으므로 실패 핸들러는 이 시나리오에서만 쓴다.
        d.Shown = null;
        Check(container.TopWidget == c && !a.gameObject.activeSelf && c.gameObject.activeSelf, "Nested Show failure");
        var input = c.gameObject.AddComponent<UIEventHandler>();
        input.controlPath = "<Gamepad>/leftStick";
        Action<Vector2> retained = null;
        input.OnPointerDownHandler = (_, send) => retained = send;
        var pointer = new PointerEventData(null) { pointerId = 10, button = PointerEventData.InputButton.Left, eligibleForClick = true };
        input.OnPointerDown(pointer);
        retained(new Vector2(1, 0));
        UnityEngine.InputSystem.InputSystem.Update();
        Check(input.control != null, "Real on-screen control setup");
        container.SetInputAllowed(false);
        container.SetInputAllowed(true);
        retained(new Vector2(1, 0));
        UnityEngine.InputSystem.InputSystem.Update();
        Check(((UnityEngine.InputSystem.InputControl<Vector2>)input.control).ReadValue() == Vector2.zero, "Canceled sender cannot restore input");
        input.OnPointerDown(pointer);
        var prior = retained;
        c.Hide();
        prior(new Vector2(1, 0));
        c.Show();
        prior(new Vector2(1, 0));
        UnityEngine.InputSystem.InputSystem.Update();
        Check(((UnityEngine.InputSystem.InputControl<Vector2>)input.control).ReadValue() == Vector2.zero, "Disabled sender stays invalid after re-enable");
        Check(c.EnableCount == 2 && c.DisableCount == 1, "Native activation callbacks during hide/show");
        input.OnPointerDown(pointer);
        retained(new Vector2(0.5f, 0));
        UnityEngine.InputSystem.InputSystem.Update();
        Check(((UnityEngine.InputSystem.InputControl<Vector2>)input.control).ReadValue().x > 0, "Fresh sender after real OnDisable/OnEnable");
        container.Shutdown();
        UnityEngine.Object.Destroy(root);
        yield return null;
        Debug.Log("PROJECTT_UI_PLAYMODE_PASS");
    }

    private static ProjectTUIPlayProbe Create(UIContainer container, Transform canvas, int key)
    {
        var go = new GameObject("Probe " + key, typeof(RectTransform));
        go.SetActive(false);
        go.transform.SetParent(canvas, false);
        var probe = go.AddComponent<ProjectTUIPlayProbe>();
        typeof(UIBase).GetField("type", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(probe, eUIContainerType.Static);
        probe.InitializeInternal(container, (UIDefine.eUIType)key);
        var registry = (Dictionary<UIDefine.eUIType, UIBase>)typeof(UIContainer).GetField("uiDatas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(container);
        registry.Add((UIDefine.eUIType)key, probe);
        return probe;
    }

    private static void Check(bool valid, string name)
    {
        if (!valid)
            throw new Exception("Unity integration failed: " + name);
    }
}
#endif
