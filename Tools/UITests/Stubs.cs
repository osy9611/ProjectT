using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
namespace UnityEngine
{
    public struct Vector2Int { public int x,y; public Vector2Int(int x,int y) {this.x=x;this.y=y;} }
    public struct Color { public static Color black; }
    public enum ScreenOrientation { AutoRotation }
    public static class Screen { public static int width=1920,height=1080; public static ScreenOrientation orientation; }
    public static class LayerMask { public static int NameToLayer(string name)=>0; public static int GetMask(string name)=>1; }
    public enum CameraClearFlags { Depth }
    public enum RenderMode { ScreenSpaceCamera }
    public class Camera : MonoBehaviour { public static Camera main; public float fieldOfView,nearClipPlane,farClipPlane,depth; public int cullingMask; public CameraClearFlags clearFlags; public Color backgroundColor; public bool useOcclusionCulling; }
    public class Canvas : MonoBehaviour { public RenderMode renderMode; public Camera worldCamera; public float planeDistance; }
    public class CanvasGroup : MonoBehaviour { public bool interactable,blocksRaycasts; }
}
namespace UnityEngine.UI
{
    public class CanvasScaler : UnityEngine.MonoBehaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public UnityEngine.Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : UnityEngine.MonoBehaviour { public enum BlockingObjects { None } public bool ignoreReversedGraphics; public BlockingObjects blockingObjects; public int blockingMask; }
    public class Text : UnityEngine.MonoBehaviour { }
    public class Selectable : UnityEngine.MonoBehaviour { public bool interactable = true; }
    public class Button : Selectable { }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem : UnityEngine.MonoBehaviour
    {
        public static EventSystem current;
        public UnityEngine.GameObject currentSelectedGameObject { get; private set; }
        public int SelectCalls;
        public void SetSelectedGameObject(UnityEngine.GameObject selected) { SelectCalls++; currentSelectedGameObject = selected; }
    }
}
namespace ProjectT.Util
{
    public static class ComUtilFunc
    {
        public static void SetLayer(UnityEngine.GameObject go,int layer,bool recursive) { }
        public static UnityEngine.GameObject FindChild(UnityEngine.GameObject go,string name,bool recursive)=>null;
        public static T FindChild<T>(UnityEngine.GameObject go,string name,bool recursive) where T : UnityEngine.Object => null;
    }
}
namespace ProjectT.UGUI
{
    public class ComHudAgent : UnityEngine.MonoBehaviour
    {
        public ProjectT.Pivot.PivotInfo Pivot;
        public int PositionUpdates;
        internal void AttachInternal(ProjectT.Pivot.PivotInfo pivotInfo) { Pivot = pivotInfo; }
        internal void DetachInternal() { Pivot = null; }
        internal void UpdatePositionInternal(UnityEngine.Camera worldCamera, UnityEngine.RectTransform root, UnityEngine.Camera uiCamera) { PositionUpdates++; }
    }
    public static class HudDefine { public enum eHudType { Test } public static string GetPath(eHudType type)=>"Hud"; }
}
namespace ProjectT.Pivot { public class PivotInfo { } }
namespace ProjectT
{
    public enum ShutdownReason { Normal }
    public abstract class ManagerBase
    {
        protected CancellationToken LifetimeToken;
        public UnityEngine.Transform RootObject;
        protected void CreateRootObject(string name) { RootObject=new UnityEngine.GameObject(name).transform; }
        protected virtual UniTask OnInitializeAsync(CancellationToken token)=>UniTask.CompletedTask;
        protected virtual void OnShutdown(ShutdownReason reason) { }
        protected internal virtual void OnUpdate(float dt) { }
        public UniTask Initialize(CancellationToken token=default) { LifetimeToken=token;return OnInitializeAsync(token); }
        public void Shutdown() { OnShutdown(ShutdownReason.Normal); }
    }
    public class FakeResource
    {
        public UnityEngine.GameObject Prefab;
        public UniTaskCompletionSource<UnityEngine.GameObject> Gate;
        public int Loads;
        public object AppScope => null;
        public T LoadAndGet<T>(string path,object scope=null) where T : class => Prefab as T;
        public async UniTask<T> LoadAndGetAsync<T>(string path,CancellationToken cancelToken=default,object scope=null) where T : class
        {
            Loads++;
            cancelToken.ThrowIfCancellationRequested();
            return (Gate == null ? Prefab : await Gate.Task.AttachExternalCancellation(cancelToken)) as T;
        }
    }
    public class FakePool
    {
        public Func<UnityEngine.GameObject> Create = () => new();
        public readonly List<UnityEngine.GameObject> Returned = new();
        public int Rents;
        public UnityEngine.GameObject Get(UnityEngine.GameObject original,UnityEngine.Transform parent,object scope) { Rents++;var go=Create();go.transform.SetParent(parent,false);return go; }
        public bool Return(UnityEngine.GameObject go) { Returned.Add(go);return true; }
    }
    public class FakeInput
    {
        public readonly List<bool> Focus = new();
        public Action<bool> OnFocus;
        internal void SetUIFocused(bool focused) { Focus.Add(focused); OnFocus?.Invoke(focused); }
    }
    public class Global
    {
        public static FakeInput Input=new();
        public static Global Instance=new();
        public static FakeResource Resource=new();
        public static FakePool Pool=new();
        public static readonly List<Exception> Exceptions=new();
        public void Log(string text,string color="") { }
        public static void LogError(string text) { }
        public static void LogException(Exception error) { Exceptions.Add(error); }
    }
}
