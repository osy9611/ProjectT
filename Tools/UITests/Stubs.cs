using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Destroyed = new();
        public static GameObject Instantiate(GameObject source)
        {
            var go = new GameObject(source.name);
            foreach (var component in source.Components.Values)
            {
                if (component is Transform) continue;
                var copy = go.AddComponent(component.GetType());
                for (var t = component.GetType(); t != typeof(MonoBehaviour) && t != null; t = t.BaseType)
                    foreach (var field in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                        if (field.Name == "type") field.SetValue(copy, field.GetValue(component));
            }
            return go;
        }
        public static GameObject Instantiate(GameObject source, Transform parent, bool instantiateInWorldSpace) { var go = Instantiate(source); go.transform.SetParent(parent, instantiateInWorldSpace); return go; }
        public static void Destroy(Object obj) { Destroyed.Add(obj); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class GameObject : Object
    {
        public string name;
        public Dictionary<Type, MonoBehaviour> Components = new();
        public Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public GameObject(string name = "") { this.name = name; transform = new RectTransform { gameObject = this }; }
        public MonoBehaviour AddComponent(Type type)
        {
            if (typeof(Transform).IsAssignableFrom(type) && type.IsInstanceOfType(transform)) return transform;
            var c = (MonoBehaviour)Activator.CreateInstance(type, true); c.gameObject = this; Components[type] = c; return c;
        }
        public T AddComponent<T>() where T : MonoBehaviour => (T)AddComponent(typeof(T));
        public T GetComponent<T>() where T : class { if (transform is T tr) return tr; foreach (var c in Components.Values) if (c is T v) return v; return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var found = new List<T>();
            CollectComponentsInChildren(this, includeInactive, found);
            return found.ToArray();
        }
        private static void CollectComponentsInChildren<T>(GameObject current, bool includeInactive, List<T> found) where T : class
        {
            if (includeInactive || current.activeInHierarchy)
            {
                if (current.transform is T transform) found.Add(transform);
                foreach (var component in current.Components.Values) if (component is T value) found.Add(value);
            }
            foreach (var child in current.transform.Children) CollectComponentsInChildren(child.gameObject, includeInactive, found);
        }
        public bool TryGetComponent<T>(out T component) where T : class { component = GetComponent<T>(); return component != null; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Transform : MonoBehaviour
    {
        public Transform parent { get; private set; }
        public readonly List<Transform> Children = new();
        public Vector3 localPosition;
        public int FrontChanges;
        public void SetParent(Transform value, bool worldPositionStays) { parent?.Children.Remove(this); parent = value; parent?.Children.Add(this); }
        public void SetSiblingIndex(int index) { }
        public void SetAsLastSibling() { FrontChanges++; }
        public bool IsChildOf(Transform value) { for (var t = this; t != null; t = t.parent) if (t == value) return true; return false; }
        public Transform Find(string value) => Children.Find(child => child.name == value);
    }
    public class RectTransform : Transform { }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } }
    public struct Vector2Int { public int x,y; public Vector2Int(int x,int y) {this.x=x;this.y=y;} }
    public struct Vector3 { public Vector3(float x,float y,float z) { } }
    public struct Color { public static Color black; }
    public enum ScreenOrientation { AutoRotation }
    public static class Screen { public static int width=1920,height=1080; public static ScreenOrientation orientation; }
    public static class LayerMask { public static int NameToLayer(string name)=>0; public static int GetMask(string name)=>1; }
    public enum CameraClearFlags { Depth }
    public enum RenderMode { ScreenSpaceCamera }
    public class Camera : MonoBehaviour { public static Camera main; public float fieldOfView,nearClipPlane,farClipPlane,depth; public int cullingMask; public CameraClearFlags clearFlags; public Color backgroundColor; public bool useOcclusionCulling; }
    public class Canvas : MonoBehaviour { public RenderMode renderMode; public Camera worldCamera; public float planeDistance; }
    public class CanvasGroup : MonoBehaviour { public bool interactable,blocksRaycasts; }
    public class SerializeField : Attribute { }
    public static class Debug { public static void LogError(object value) { } }
}
namespace UnityEngine.UI
{
    public class CanvasScaler : UnityEngine.MonoBehaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public UnityEngine.Vector2 referenceResolution; public float matchWidthOrHeight; }
    public class GraphicRaycaster : UnityEngine.MonoBehaviour { public enum BlockingObjects { None } public bool ignoreReversedGraphics; public BlockingObjects blockingObjects; public int blockingMask; }
    public class Text : UnityEngine.MonoBehaviour { }
    public class Selectable : UnityEngine.MonoBehaviour { public bool interactable = true, enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class Button : Selectable { }
    public class Image : UnityEngine.MonoBehaviour { }
}
namespace TMPro { public class TextMeshProUGUI : UnityEngine.MonoBehaviour { } }
namespace UnityEngine.EventSystems
{
    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public int pointerId;
        public InputButton button;
        public bool eligibleForClick;
    }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData eventData); }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IDragHandler { void OnDrag(PointerEventData eventData); }
    public class EventSystem : UnityEngine.MonoBehaviour
    {
        public static EventSystem current;
        public UnityEngine.GameObject currentSelectedGameObject { get; private set; }
        public int SelectCalls;
        public void SetSelectedGameObject(UnityEngine.GameObject selected) { SelectCalls++; currentSelectedGameObject = selected; }
    }
}
namespace UnityEngine.InputSystem.Layouts
{
    public class InputControlAttribute : Attribute { public string layout; }
}
namespace UnityEngine.InputSystem
{
    public class InputControl
    {
        public readonly List<object> Values = new();
        public int ResetCount;
    }
}
namespace UnityEngine.InputSystem.OnScreen
{
    public abstract class OnScreenControl : UnityEngine.MonoBehaviour
    {
        private readonly UnityEngine.InputSystem.InputControl inputControl = new();
        public int BaseDisableCount { get; private set; }
        public string controlPath { get => controlPathInternal; set => controlPathInternal = value; }
        public UnityEngine.InputSystem.InputControl control => inputControl;
        protected abstract string controlPathInternal { get; set; }
        protected void SendValueToControl<TValue>(TValue value) where TValue : struct { inputControl.Values.Add(value); }
        protected void SentDefaultValueToControl() { inputControl.ResetCount++; }
        protected virtual void OnDisable() { BaseDisableCount++; }
    }
}
namespace ProjectT.Util
{
    public enum AnchorPresets { StretchAll }
    public enum PivotPresets { MiddleCenter }
    public static class ComUtilFunc
    {
        public static void SetLayer(UnityEngine.GameObject go,int layer,bool recursive) { }
        public static void InitRectTransform(this UnityEngine.RectTransform tr,UnityEngine.RectTransform parent,AnchorPresets a,PivotPresets p) { tr.SetParent(parent,false); }
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
        public virtual void OnUpdate(float dt) { }
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
        public async UniTask<T> LoadAndGetAsync<T>(string path,CancellationToken cancelToken=default) where T : class
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
        public UnityEngine.GameObject Get(string path,UnityEngine.Transform parent=null) { var go=Create();go.transform.SetParent(parent,false);return go; }
        public UniTask<UnityEngine.GameObject> GetAsync(string path,UnityEngine.Transform parent=null,CancellationToken cancelToken=default) { cancelToken.ThrowIfCancellationRequested();return UniTask.FromResult(Get(path,parent)); }
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
