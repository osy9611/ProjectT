using System;
using System.Threading;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.UGUI;
using UnityEngine;

class Probe : UIBase
{
    public static Action<Probe> EnterAction, ShowAction, HideAction, LeaveAction;
    public static int Enters, Shows, Hides, Leaves;
    protected override void OnEnter() { Enters++; EnterAction?.Invoke(this); }
    protected override void OnShow() { Shows++; ShowAction?.Invoke(this); }
    protected override void OnHide() { Hides++; HideAction?.Invoke(this); }
    protected override void OnLeave() { Leaves++; LeaveAction?.Invoke(this); }
}
class OtherProbe : UIBase { protected override void OnShow() { } protected override void OnHide() { } }
static class Program
{
    static int failures, count;
    static readonly UIDefine.eUIType Key = UIDefine.eUIType.Test;
    static GameObject Prefab(eUIContainerType type = eUIContainerType.Static)
    {
        var go = new GameObject("Probe", typeof(RectTransform));
        var probe = go.AddComponent<Probe>();
        typeof(UIBase).GetField("type",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(probe,type);
        return go;
    }
    static UIContainer Setup(CancellationToken token=default)
    {
        Probe.Enters=Probe.Shows=Probe.Hides=Probe.Leaves=0;
        Probe.EnterAction=Probe.ShowAction=Probe.HideAction=Probe.LeaveAction=null;
        UnityEngine.Object.Destroyed.Clear();
        Global.Exceptions.Clear();
        Global.Input=new FakeInput();
        UnityEngine.EventSystems.EventSystem.current=null;
        Global.Resource=new FakeResource { Prefab=Prefab() };
        Global.Pool=new FakePool();
        UnityEngine.Camera.main=null;
        var container=new UIContainer(new GameObject("Root").transform,token);
        container.Initialize();
        return container;
    }
    static UnityEngine.UI.Button AddButton(UIBase ui,string name)
    {
        var go=new GameObject(name);go.transform.SetParent(ui.transform,false);return go.AddComponent<UnityEngine.UI.Button>();
    }
    static string Focus() => string.Join(",",Global.Input.Focus);
    static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    static async UniTask Expect<T>(Func<UniTask> work) where T:Exception
    {
        try { await work(); } catch(T) { return; }
        throw new Exception("Expected "+typeof(T).Name);
    }
    static UniTask ExpectSync<T>(Action work) where T:Exception => Expect<T>(()=> { work();return UniTask.CompletedTask; });
    static async UniTask Run(string name,Func<UniTask> test)
    {
        count++;
        try { await test();Console.WriteLine("PASS "+name); }
        catch(Exception error) { failures++;Console.WriteLine("FAIL "+name+" :: "+error); }
    }
    static int Main() { Tests().GetAwaiter().GetResult();BindingTests.Run();Console.WriteLine($"{count-failures}/{count} passed");return failures==0?0:1; }
    static async UniTask Tests()
    {
        await Run("Concurrent callers share one initialized instance",async()=> {
            var c=Setup();Global.Resource.Gate=new();
            var a=c.CreateWidgetAsync<Probe>(Key,"UI",default);var b=c.CreateWidgetAsync<Probe>(Key,"UI",default);
            Global.Resource.Gate.TrySetResult(Global.Resource.Prefab);
            Check(ReferenceEquals(await a,await b)&&Global.Resource.Loads==1&&Probe.Enters==1,"duplicate creation");
        });
        await Run("Caller cancellation does not cancel shared creation",async()=> {
            var c=Setup();Global.Resource.Gate=new();using var token=new CancellationTokenSource();
            var a=c.CreateWidgetAsync<Probe>(Key,"UI",token.Token);var b=c.CreateWidgetAsync<Probe>(Key,"UI",default);
            token.Cancel();await Expect<OperationCanceledException>(async()=> { await a; });
            Global.Resource.Gate.TrySetResult(Global.Resource.Prefab);Check(await b != null&&Probe.Enters==1,"shared creation canceled");
        });
        await Run("Scene clear cancels pending creation and allows retry",async()=> {
            var c=Setup();Global.Resource.Gate=new();var a=c.CreateWidgetAsync<Probe>(Key,"UI",default);
            c.ClearTransientWidgets();await Expect<OperationCanceledException>(async()=> { await a; });
            Global.Resource.Gate.TrySetResult(Global.Resource.Prefab);
            Check(c.FindWidget<Probe>(Key)==null&&Probe.Enters==0,"late registration");
            Global.Resource.Gate=null;Check(await c.CreateWidgetAsync<Probe>(Key,"UI",default)!=null,"retry failed");
        });
        await Run("Manager lifetime cancels pending creation",async()=> {
            using var token=new CancellationTokenSource();var c=Setup(token.Token);Global.Resource.Gate=new();
            var a=c.CreateWidgetAsync<Probe>(Key,"UI",default);token.Cancel();await Expect<OperationCanceledException>(async()=> { await a; });
            await ExpectSync<OperationCanceledException>(()=>c.CreateWidget<Probe>(Key,"UI"));
        });
        await Run("Initialization failure rolls back and permits retry",async()=> {
            var c=Setup();Probe.EnterAction=_=>throw new InvalidOperationException("init");
            await Expect<InvalidOperationException>(async()=> { await c.CreateWidgetAsync<Probe>(Key,"UI",default); });
            Check(c.FindWidget<Probe>(Key)==null&&Probe.Leaves==1&&UnityEngine.Object.Destroyed.Count==1,"rollback incomplete");
            Probe.EnterAction=null;Check(await c.CreateWidgetAsync<Probe>(Key,"UI",default)!=null,"retry failed");
        });
        await Run("Cleanup failure after initialization failure still destroys the instance",async()=> {
            var c=Setup();Probe.EnterAction=_=>throw new InvalidOperationException("init");Probe.LeaveAction=_=>throw new Exception("leave");
            string message=null;
            try {await c.CreateWidgetAsync<Probe>(Key,"UI",default);} catch(Exception e) {message=e.Message;}
            Check(message=="leave"&&UnityEngine.Object.Destroyed.Count==1&&c.FindWidget<Probe>(Key)==null,"cleanup failure lost or instance kept");
        });
        await Run("Wrong generic type fails without replacing instance",async()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");
            await ExpectSync<InvalidOperationException>(()=>c.CreateWidget<OtherProbe>(Key,"UI"));
            Check(ReferenceEquals(a,c.FindWidget<Probe>(Key))&&Probe.Enters==1,"instance replaced");
        });
        await Run("Inactive prefab root is rejected before instantiation",async()=> {
            var c=Setup();Global.Resource.Prefab.SetActive(false);
            await ExpectSync<InvalidOperationException>(()=>c.CreateWidget<Probe>(Key,"UI"));
            Check(c.FindWidget<Probe>(Key)==null&&Probe.Enters==0&&UnityEngine.Object.Destroyed.Count==0,"inactive prefab was instantiated");
            Global.Resource.Prefab.SetActive(true);Check(c.CreateWidget<Probe>(Key,"UI")!=null&&Probe.Enters==1,"retry after fixing prefab failed");
        });
        await Run("Layer roots are drawn back to front and an undefined container type is rejected before instantiation",async()=> {
            Setup();var root=new GameObject("Root").transform;var c=new UIContainer(root,default);c.Initialize();
            var canvas=root.Find("Canvas2D");
            Check(string.Join(",",canvas.Children.ConvertAll(t=>t.name))=="Static,HUD,Dynamic,System","layer order");
            Global.Resource.Prefab=Prefab((eUIContainerType)3);
            await ExpectSync<ArgumentOutOfRangeException>(()=>c.CreateWidget<Probe>(Key,"UI"));
            Check(c.FindWidget<Probe>(Key)==null&&Probe.Enters==0&&canvas.Children.TrueForAll(t=>t.childCount==0),"undefined type was instantiated");
        });
        await Run("Sync creation during async creation is rejected",async()=> {
            var c=Setup();Global.Resource.Gate=new();var a=c.CreateWidgetAsync<Probe>(Key,"UI",default);
            await ExpectSync<InvalidOperationException>(()=>c.CreateWidget<Probe>(Key,"UI"));
            Global.Resource.Gate.TrySetResult(Global.Resource.Prefab);await a;Check(Probe.Enters==1,"duplicate");
        });
        await Run("Hide then remove calls OnHide and OnLeave once",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");a.Show();a.Hide();a.Hide();c.RemoveWidget(Key);c.RemoveWidget(Key);
            Check(Probe.Hides==1&&Probe.Leaves==1&&c.WidgetStack.Count==0,"duplicate cleanup");return UniTask.CompletedTask;
        });
        await Run("Static navigation hides covered UI and re-shows it on restore",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");
            a.Show();b.Show();Check(!a.gameObject.activeSelf&&c.WidgetStack.Count==2,"previous not suspended");b.Hide();
            Check(a.gameObject.activeSelf&&Probe.Shows==3&&c.TopWidget==a,"restore contract");return UniTask.CompletedTask;
        });
        await Run("Popup reopening raises depth without repeated initialization",()=> {
            var c=Setup();Global.Resource.Prefab=Prefab(eUIContainerType.Dynamic);var a=c.CreateWidget<Probe>(Key,"UI");a.Show();a.Show();
            Check(a.transform.FrontChanges==2&&Probe.Shows==1&&c.WidgetStack.Count==0,"popup depth/state");return UniTask.CompletedTask;
        });
        await Run("Scene cleanup retains system and removes static history",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");a.Show();Global.Resource.Prefab=Prefab(eUIContainerType.System);
            var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");b.Show();c.ClearTransientWidgets();
            Check(c.FindWidget<Probe>(Key)==null&&c.FindWidget<Probe>((UIDefine.eUIType)1)==b&&c.WidgetStack.Count==0,"scope cleanup");return UniTask.CompletedTask;
        });
        await Run("Shutdown destroys every UI despite callback errors",async()=> {
            var c=Setup();c.CreateWidget<Probe>(Key,"UI").Show();c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI").Show();
            Probe.HideAction=_=>throw new Exception("hide");await ExpectSync<AggregateException>(c.Shutdown);
            Check(Probe.Leaves==1&&UnityEngine.Object.Destroyed.Count==2&&c.WidgetStack.Count==0,"incomplete shutdown or Leave ran after failed Hide");
        });
        await Run("Reentrant scene clear during initialization cannot register late UI",async()=> {
            var c=Setup();Probe.EnterAction=_=>c.ClearTransientWidgets();
            await Expect<OperationCanceledException>(async()=> { await c.CreateWidgetAsync<Probe>(Key,"UI",default); });
            Check(c.FindWidget<Probe>(Key)==null&&Probe.Leaves==1,"late initialized UI");
        });
        await Run("Failed show stays open on top with consistent focus and can be hidden",async()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");Probe.ShowAction=_=>throw new InvalidOperationException("show");
            await ExpectSync<InvalidOperationException>(a.Show);
            Check(a.IsActive&&a.gameObject.activeSelf&&c.TopWidget==a&&Focus()=="True","failed show state");
            Probe.ShowAction=null;a.Hide();
            Check(!a.IsActive&&!a.gameObject.activeSelf&&Probe.Hides==1&&Focus()=="True,False","failed show could not be hidden");
        });
        await Run("Removing current static restores and re-shows previous",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();c.RemoveWidget((UIDefine.eUIType)1);
            Check(c.TopWidget==a&&a.gameObject.activeSelf&&Probe.Shows==3,"previous not restored");return UniTask.CompletedTask;
        });
        await Run("Removing pending UI cancels all waiters",async()=> {
            var c=Setup();Global.Resource.Gate=new();var a=c.CreateWidgetAsync<Probe>(Key,"UI",default);var b=c.CreateWidgetAsync<Probe>(Key,"UI",default);c.RemoveWidget(Key);
            await Expect<OperationCanceledException>(async()=> { await a; });await Expect<OperationCanceledException>(async()=> { await b; });Check(Probe.Enters==0,"created canceled UI");
        });
        await Run("External destruction removes registry and runs cleanup once",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");a.Show();
            var destroy=typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance);destroy.Invoke(a,null);destroy.Invoke(a,null);
            Check(c.FindWidget<Probe>(Key)==null&&c.WidgetStack.Count==0&&Probe.Hides==1&&Probe.Leaves==1,"destroy cleanup");return UniTask.CompletedTask;
        });
        await Run("Sync initialization can publish result to a reentrant async waiter",async()=> {
            var c=Setup();UniTask<Probe> waiting=default;Probe.EnterAction=_=>waiting=c.CreateWidgetAsync<Probe>(Key,"UI",default);
            var a=c.CreateWidget<Probe>(Key,"UI");Check(ReferenceEquals(a,await waiting),"waiter was not completed");
        });
        await Run("Stack view rejects external mutation",async()=> {
            var c=Setup();c.CreateWidget<Probe>(Key,"UI").Show();
            await ExpectSync<NotSupportedException>(()=>((System.Collections.Generic.IList<UIBase>)c.WidgetStack).Clear());Check(c.WidgetStack.Count==1,"history modified");
        });
        await Run("Failure after every caller cancels is reported once",async()=> {
            var c=Setup();Global.Resource.Gate=new();using var token=new CancellationTokenSource();
            var a=c.CreateWidgetAsync<Probe>(Key,"UI",token.Token);token.Cancel();await Expect<OperationCanceledException>(async()=> { await a; });
            Probe.EnterAction=_=>throw new InvalidOperationException("init");Global.Resource.Gate.TrySetResult(Global.Resource.Prefab);
            Check(Global.Exceptions.Count==1&&Global.Exceptions[0] is InvalidOperationException&&c.FindWidget<Probe>(Key)==null&&Probe.Leaves==1,"orphaned failure lost");
        });
        await Run("Failure with a remaining caller is delivered without boundary report",async()=> {
            var c=Setup();Probe.EnterAction=_=>throw new InvalidOperationException("init");
            await Expect<InvalidOperationException>(async()=> { await c.CreateWidgetAsync<Probe>(Key,"UI",default); });
            Check(Global.Exceptions.Count==0,"duplicate report");
        });
        await Run("Cancellation after every caller leaves is not reported",async()=> {
            var c=Setup();Global.Resource.Gate=new();using var token=new CancellationTokenSource();
            var a=c.CreateWidgetAsync<Probe>(Key,"UI",token.Token);token.Cancel();await Expect<OperationCanceledException>(async()=> { await a; });
            c.ClearTransientWidgets();
            Check(Global.Exceptions.Count==0&&Probe.Enters==0,"cancellation reported");
        });
        await Run("Static opened during OnShow stays in front",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");var d=c.CreateWidget<Probe>((UIDefine.eUIType)2,"UI");
            a.Show();Probe.ShowAction=p=> { if(p==b) d.Show(); };b.Show();
            Check(c.TopWidget==d&&d.gameObject.activeSelf&&!b.gameObject.activeSelf&&!a.gameObject.activeSelf&&c.WidgetStack.Count==3,"reentrant order");return UniTask.CompletedTask;
        });
        await Run("Reopening previous static during OnShow keeps it visible",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");
            a.Show();Probe.ShowAction=p=> { if(p==b) a.Show(); };b.Show();
            Check(c.TopWidget==a&&a.gameObject.activeSelf&&!b.gameObject.activeSelf,"previous hidden");return UniTask.CompletedTask;
        });
        await Run("External destruction of current static restores previous",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);
            Check(c.TopWidget==a&&a.gameObject.activeSelf,"previous stack entry exists but remains invisible");return UniTask.CompletedTask;
        });
        await Run("Failed outer Show after nested Show leaves only top visible",async()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");var d=c.CreateWidget<Probe>((UIDefine.eUIType)2,"UI");a.Show();
            Probe.ShowAction=p=> {if(p==b) {d.Show();throw new Exception("outer show");}};
            await ExpectSync<Exception>(b.Show);
            Check(c.TopWidget==d&&d.gameObject.activeSelf&&!a.gameObject.activeSelf,"old screen and newest screen are both visible");
        });
        await Run("Previous screen is restored before release callbacks",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            Probe.HideAction=p=> {if(p==b) Check(a.gameObject.activeSelf&&c.TopWidget==a,"hide ran before restore");};
            Probe.LeaveAction=p=> {if(p==b) Check(a.gameObject.activeSelf&&c.TopWidget==a,"leave ran before restore");};
            typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);
            Check(a.gameObject.activeSelf&&Probe.Hides==2&&Probe.Leaves==1,"cleanup/restore incomplete");return UniTask.CompletedTask;
        });
        await Run("Release callback failure propagates the first error after restoring previous",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            Probe.HideAction=p=> {if(p==b) throw new InvalidOperationException("hide");};
            Probe.LeaveAction=p=> {if(p==b) throw new Exception("leave");};
            string message=null;
            try {typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);}
            catch(TargetInvocationException e) {message=e.InnerException is InvalidOperationException ? e.InnerException.Message : null;}
            Check(message=="hide"&&a.gameObject.activeSelf&&c.TopWidget==a&&c.FindWidget<Probe>((UIDefine.eUIType)1)==null&&Probe.Leaves==0,"first error lost or state inconsistent");
            return UniTask.CompletedTask;
        });
        await Run("Destroying covered static does not change current",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(a,null);
            Check(c.TopWidget==b&&b.gameObject.activeSelf&&Probe.Shows==2,"current screen changed");return UniTask.CompletedTask;
        });
        await Run("Show failure still reconciles the stack",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");var d=c.CreateWidget<Probe>((UIDefine.eUIType)2,"UI");a.Show();
            Probe.ShowAction=p=> {if(p==b) {d.Show();throw new InvalidOperationException("outer show");}};
            bool reported=false;
            try {b.Show();}
            catch(InvalidOperationException e) {reported=e.Message=="outer show";}
            Check(reported&&c.TopWidget==d&&d.gameObject.activeSelf&&!a.gameObject.activeSelf,"show failure lost or stack not reconciled");return UniTask.CompletedTask;
        });
        await Run("Restore failure leaves the released UI detached and skips its callbacks",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            Probe.ShowAction=p=> {if(p==a) throw new InvalidOperationException("restore");};
            string message=null;
            try {typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);}
            catch(TargetInvocationException e) {message=e.InnerException.Message;}
            Check(message=="restore"&&c.TopWidget==a&&a.gameObject.activeSelf&&!b.gameObject.activeSelf&&c.FindWidget<Probe>((UIDefine.eUIType)1)==null&&Probe.Hides==1&&Probe.Leaves==0,"restore failure state");
            return UniTask.CompletedTask;
        });
        await Run("UI focus is reported only when the static stack top changes",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");
            Global.Resource.Prefab=Prefab(eUIContainerType.Dynamic);var popup=c.CreateWidget<Probe>((UIDefine.eUIType)2,"UI");
            popup.Show();Check(Focus()=="","dynamic UI took focus");
            a.Show();b.Show();a.Show();Check(Focus()=="True","stack top change was not reported once");
            b.Hide();a.Hide();Check(Focus()=="True,False","hide did not release focus");
            a.Show();b.Show();c.RemoveWidget((UIDefine.eUIType)1);Check(Focus()=="True,False,True","remove of a covering UI changed focus");
            c.RemoveWidget(Key);Check(Focus()=="True,False,True,False","remove of the last UI kept focus");
            Global.Resource.Prefab=Prefab();b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");b.Show();c.ClearTransientWidgets();
            Check(Focus()=="True,False,True,False,True,False","scene clear did not release focus once");
            b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");b.Show();b.Hide(false);Check(Focus().EndsWith("True,False"),"hide without restore kept focus");
            return UniTask.CompletedTask;
        });
        await Run("External destruction and shutdown do not report focus while stopping",()=> {
            var c=Setup();var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");a.Show();b.Show();
            typeof(UIBase).GetMethod("OnDestroy",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(b,null);
            Check(Focus()=="True"&&c.TopWidget==a,"external destruction reported a focus change");
            c.Shutdown();Check(Focus()=="True","stopped container reported focus");return UniTask.CompletedTask;
        });
        await Run("Pad selection follows the visible stack top and restores the last selection",()=> {
            var c=Setup();var es=new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();UnityEngine.EventSystems.EventSystem.current=es;
            var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");
            var a1=AddButton(a,"a1");var a2=AddButton(a,"a2");var b1=AddButton(b,"b1");var b2=AddButton(b,"b2");
            a1.interactable=false;
            a.Show();Check(es.currentSelectedGameObject==a2.gameObject,"first interactable selectable was not selected");
            a1.interactable=true;es.SetSelectedGameObject(a1.gameObject);
            typeof(UIBase).GetField("firstSelected",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(b,b2);
            b.Show();Check(es.currentSelectedGameObject==b2.gameObject,"serialized first selection was not used");
            b.Hide();Check(es.currentSelectedGameObject==a1.gameObject,"previous selection was not restored");
            int calls=es.SelectCalls;a.Show();Check(es.SelectCalls==calls,"reshowing the current top changed selection");
            a1.gameObject.SetActive(false);b.Show();b.Hide();Check(es.currentSelectedGameObject==a2.gameObject,"inactive remembered selection was restored");
            b.Show();es.SetSelectedGameObject(a2.gameObject);b.Hide();
            Check(es.currentSelectedGameObject==a2.gameObject,"foreign selection was remembered as b's");
            b.Show();Check(es.currentSelectedGameObject==b2.gameObject,"foreign selection replaced b's selection");
            b.Hide(false);Check(es.currentSelectedGameObject==null&&Focus().EndsWith("True,False"),"hidden top kept a selection or focus");
            a.Show();Check(es.currentSelectedGameObject==a2.gameObject&&Focus().EndsWith("False,True"),"selection or focus was not applied when the top became visible");
            a.Hide();Check(es.currentSelectedGameObject==null&&Focus().EndsWith("False"),"empty stack kept selection");
            return UniTask.CompletedTask;
        });
        await Run("UI changed by a focus reset callback keeps the newer focus and selection",()=> {
            var c=Setup();var es=new GameObject("EventSystem").AddComponent<UnityEngine.EventSystems.EventSystem>();UnityEngine.EventSystems.EventSystem.current=es;
            var a=c.CreateWidget<Probe>(Key,"UI");var b=c.CreateWidget<Probe>((UIDefine.eUIType)1,"UI");AddButton(a,"a1");var b1=AddButton(b,"b1");
            Global.Input.OnFocus=focused=> { Global.Input.OnFocus=null;if(focused) b.Show(); };
            a.Show();Check(c.TopWidget==b&&es.currentSelectedGameObject==b1.gameObject&&Focus()=="True","outer refresh overwrote the newer focus");
            return UniTask.CompletedTask;
        });
        await Run("UIManager delegates lifecycle to real container",async()=> {
            Setup();var manager=new UIManager();await manager.Initialize();var a=await manager.CreateWidgetAsync<Probe>(Key);a.Show();
            Check(manager.TopWidget==a,"manager navigation");manager.Shutdown();Check(Probe.Leaves==1,"manager cleanup");
        });
        await Run("HUD is placed under the HUD root, updated once per frame, and only returned by its actor",async()=> {
            Setup();var manager=new UIManager();await manager.Initialize();
            Global.Pool.Create=()=> {var go=new GameObject("Hud");go.AddComponent<ComHudAgent>();return go;};
            Global.Resource.Prefab=new GameObject("HudOriginal");Global.Resource.Prefab.AddComponent<ComHudAgent>();
            var pivot=new ProjectT.Pivot.PivotInfo();
            var hud=manager.GetHud<ComHudAgent>(HudDefine.eHudType.Test,pivot);
            Check(hud.transform.parent==manager.RootObject.Find("Canvas2D").Find("HUD")&&hud.Pivot==pivot&&hud.PositionUpdates==0,"hud attach");
            UnityEngine.Camera.main=new GameObject("Main").AddComponent<UnityEngine.Camera>();
            manager.OnUpdate(0);Check(hud.PositionUpdates==1,"hud was not updated");
            var other=await manager.GetHudAsync<ComHudAgent>(HudDefine.eHudType.Test,pivot);Check(other.PositionUpdates==1,"hud was not placed on attach");
            manager.ReturnHudInternal(hud);manager.OnUpdate(0);
            Check(Global.Pool.Returned.Count==1&&hud.PositionUpdates==1&&hud.Pivot==null&&other.PositionUpdates==2,"returned hud kept updating");
            await ExpectSync<InvalidOperationException>(()=>manager.ReturnHudInternal(hud));
            Check(Global.Pool.Returned.Count==1,"untracked hud was returned");
            manager.Shutdown();
            Check(Global.Pool.Returned.Count==1&&other.Pivot==null,"shutdown returned huds instead of detaching them");
        });
        await Run("HUD prefab without the requested component is rejected before renting",async()=> {
            Setup();var manager=new UIManager();await manager.Initialize();
            Global.Resource.Prefab=new GameObject("Plain");
            await ExpectSync<InvalidOperationException>(()=>manager.GetHud<ComHudAgent>(HudDefine.eHudType.Test,new ProjectT.Pivot.PivotInfo()));
            await Expect<InvalidOperationException>(async()=> { await manager.GetHudAsync<ComHudAgent>(HudDefine.eHudType.Test,new ProjectT.Pivot.PivotInfo()); });
            Check(Global.Pool.Rents==0&&Global.Pool.Returned.Count==0,"rejected hud was rented");
        });
        await Run("App scope actor keeps its HUD across scene clear and never returns another actor's HUD",async()=> {
            Setup();var manager=new UIManager();await manager.Initialize();
            int reused=0;
            Global.Pool.Create=()=> {
                if(reused<Global.Pool.Returned.Count) return Global.Pool.Returned[reused++];
                var go=new GameObject("Hud");go.AddComponent<ComHudAgent>();return go;
            };
            Global.Resource.Prefab=new GameObject("HudOriginal");Global.Resource.Prefab.AddComponent<ComHudAgent>();
            UnityEngine.Camera.main=new GameObject("Main").AddComponent<UnityEngine.Camera>();
            var appHud=manager.GetHud<ComHudAgent>(HudDefine.eHudType.Test,new ProjectT.Pivot.PivotInfo());
            manager.ClearTransientWidgets();manager.OnUpdate(0);
            Check(Global.Pool.Returned.Count==0&&appHud.PositionUpdates==2,"scene clear took the app scope actor's hud");
            var otherHud=manager.GetHud<ComHudAgent>(HudDefine.eHudType.Test,new ProjectT.Pivot.PivotInfo());
            Check(!ReferenceEquals(otherHud,appHud),"live hud was rented to another actor");
            manager.ReturnHudInternal(appHud);manager.OnUpdate(0);
            Check(Global.Pool.Returned.Count==1&&Global.Pool.Returned[0]==appHud.gameObject&&otherHud.PositionUpdates==2,"returning the app scope hud affected another actor's hud");
        });
    }
}
