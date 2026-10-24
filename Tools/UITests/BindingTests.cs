using System;
using ProjectT;
using ProjectT.UGUI;
using UnityEngine;

class BindingProbe : UIBase
{
    protected override void OnShow() { }

    protected override void OnHide() { }

    public void BindEnum(Type type) => Bind<GameObject>(type);

    public GameObject At(int index) => Get<GameObject>(index);
}

static class BindingTests
{
    private enum Valid { Root }
    private enum Sparse { Root, Other = 3 }
    private enum Alias { Root, Other = 0 }
    private enum Missing { Missing }
    public static void Run()
    {
        var p = new GameObject("Root").AddComponent<BindingProbe>();
        Expect<ArgumentException>(() => p.BindEnum(typeof(Sparse)));
        Expect<ArgumentException>(() => p.BindEnum(typeof(Alias)));
        Expect<ArgumentException>(() => p.BindEnum(typeof(string)));
        p.BindEnum(typeof(Valid));
        if (p.At(0) != p.gameObject)
            throw new Exception("Root enum binding failed.");
        Expect<InvalidOperationException>(() => p.BindEnum(typeof(Valid)));
        Expect<ArgumentOutOfRangeException>(() => p.At(1));
        var retry = new GameObject("Root").AddComponent<BindingProbe>();
        Expect<InvalidOperationException>(() => retry.BindEnum(typeof(Missing)));
        retry.BindEnum(typeof(Valid));
        if (retry.At(0) != retry.gameObject)
            throw new Exception("Failed bind left partial registration.");
        var container = new UIContainer(new GameObject("RootContainer").transform, default);
        container.Initialize();
        Global.Resource = new FakeResource { Prefab = new GameObject("Broken", typeof(RectTransform)) };
        Global.Resource.Prefab.AddComponent<Probe>();
        Probe.EnterAction = ui => ui.gameObject.AddComponent<BindingProbe>().BindEnum(typeof(Missing));
        try
        {
            Expect<InvalidOperationException>(() => container.CreateWidget<Probe>((UIDefine.eUIType)0, "UI"));
            if (container.FindWidget<Probe>((UIDefine.eUIType)0) != null)
                throw new Exception("Invalid bindings were registered.");
        }
        finally { Probe.EnterAction = null; container.Shutdown(); }
        Console.WriteLine("PASS Binding enum validation, required references, atomic registration, bounds, and creation rollback");
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
