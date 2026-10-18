using System;
using System.Collections.Generic;

// 테스트 프로젝트가 함께 쓰는 UnityEngine 최소 대체물이다. 시나리오 전용 계측은 각 프로젝트 스텁에 둔다.
namespace UnityEngine
{
    public class Object
    {
        // Destroy 호출 기록이다. 같은 객체를 두 번 파괴한 결함을 테스트가 잡도록 중복도 그대로 남긴다.
        public static readonly List<Object> Destroyed = new List<Object>();
        private static readonly List<Object> pendingDestroy = new List<Object>();
        private string objectName;

        internal bool IsDestroyed { get; private set; }

        public virtual string name
        {
            get => objectName;
            set => objectName = value;
        }

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.IsDestroyed;
            bool rightNull = ReferenceEquals(right, null) || right.IsDestroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public static implicit operator bool(Object value) => value != null;
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();

        public static void DontDestroyOnLoad(Object value)
        {
        }

        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) => Array.Empty<T>();

        public static T Instantiate<T>(T original) where T : Object
        {
            if (original is ICloneable cloneable)
                return (T)cloneable.Clone();
            if (original is GameObject gameObject)
                return (T)(Object)InstantiateInternal(gameObject, null, false);
            throw new NotSupportedException();
        }

        public static GameObject Instantiate(GameObject original, Transform parent, bool instantiateInWorldSpace)
        {
            return InstantiateInternal(original, parent, instantiateInWorldSpace);
        }

        // Unity처럼 직렬화 필드(public 또는 [SerializeField])만 복사한다.
        private static GameObject InstantiateInternal(GameObject original, Transform parent, bool instantiateInWorldSpace)
        {
            if (original == null)
                return null;

            var result = original.transform is RectTransform ? new GameObject(original.name, typeof(RectTransform)) : new GameObject(original.name);
            result.activeSelf = original.activeSelf;
            result.transform.SetParent(parent, instantiateInWorldSpace);
            foreach (var component in original.Components)
            {
                if (component is Transform)
                    continue;

                var copy = result.AddComponent(component.GetType());
                for (var type = component.GetType(); type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
                {
                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (field.IsInitOnly || field.IsNotSerialized)
                            continue;
                        if (field.IsPublic || field.IsDefined(typeof(SerializeField), false))
                            field.SetValue(copy, field.GetValue(component));
                    }
                }
            }

            return result;
        }

        // Unity의 Destroy처럼 실제 파괴는 프레임 끝(FlushDeferredDestroy)에 일어난다. 그 전까지 객체는 null이 아니다.
        public static void Destroy(Object target)
        {
            if (ReferenceEquals(target, null))
                return;

            Destroyed.Add(target);
            if (!pendingDestroy.Contains(target))
                pendingDestroy.Add(target);
        }

        public static void DestroyImmediate(Object target)
        {
            Destroyed.Add(target);
            MarkDestroyedInternal(target);
        }

        public static void FlushDeferredDestroy()
        {
            var targets = pendingDestroy.ToArray();
            pendingDestroy.Clear();
            foreach (var target in targets)
                MarkDestroyedInternal(target);
        }

        public static void ResetDestroyed()
        {
            Destroyed.Clear();
            pendingDestroy.Clear();
        }

        private static void MarkDestroyedInternal(Object target)
        {
            if (ReferenceEquals(target, null) || target.IsDestroyed)
                return;

            if (target is Component component && !(component is Transform))
                component.gameObject.RemoveComponentInternal(component);

            MarkHierarchyDestroyedInternal(target);
        }

        // GameObject와 함께 사라지는 컴포넌트·자식은 목록에서 떼지 않고 파괴 상태만 표시한다.
        private static void MarkHierarchyDestroyedInternal(Object target)
        {
            target.IsDestroyed = true;
            if (!(target is GameObject gameObject))
                return;

            foreach (var child in gameObject.transform.Children.ToArray())
            {
                if (!child.gameObject.IsDestroyed)
                    MarkHierarchyDestroyedInternal(child.gameObject);
            }

            foreach (var component in gameObject.Components)
                component.IsDestroyed = true;
            gameObject.transform.SetParent(null, false);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;

        public override string name
        {
            get => gameObject.name;
            set => gameObject.name = value;
        }

        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : class => gameObject.TryGetComponent(out component);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour
    {
        public void StopAllCoroutines()
        {
        }
    }

    public sealed class GameObject : Object
    {
        public static int CreatedCount { get; private set; }
        private readonly List<Component> components = new List<Component>();
        public bool activeSelf = true;

        public GameObject(string name = "", params Type[] componentTypes)
        {
            CreatedCount++;
            this.name = name;
            transform = Array.IndexOf(componentTypes, typeof(RectTransform)) >= 0 ? new RectTransform() : new Transform();
            transform.gameObject = this;
            components.Add(transform);
            foreach (var type in componentTypes)
            {
                if (!typeof(Transform).IsAssignableFrom(type))
                    AddComponent(type);
            }
        }

        public Transform transform { get; private set; }
        public IReadOnlyList<Component> Components => components;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public void SetActive(bool value)
        {
            activeSelf = value;
        }

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));

        // Unity에는 없다. 생성자 인자가 필요한 테스트용 컴포넌트를 붙일 때 쓴다.
        public T Attach<T>(T component) where T : Component
        {
            component.gameObject = this;
            components.Add(component);
            return component;
        }

        public Component AddComponent(Type type)
        {
            if (type == typeof(RectTransform) && !(transform is RectTransform))
                return ReplaceTransformInternal(new RectTransform());
            if (typeof(Transform).IsAssignableFrom(type))
                return transform;

            var component = (Component)Activator.CreateInstance(type, true);
            component.gameObject = this;
            components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            foreach (var component in components)
            {
                if (component is T result)
                    return result;
            }

            return null;
        }

        public Component GetComponent(Type type)
        {
            foreach (var component in components)
            {
                if (type.IsInstanceOfType(component))
                    return component;
            }

            return null;
        }

        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var found = new List<T>();
            CollectInternal(this, includeInactive, found);
            return found.ToArray();
        }

        internal void RemoveComponentInternal(Component component)
        {
            components.Remove(component);
        }

        private static void CollectInternal<T>(GameObject current, bool includeInactive, List<T> found) where T : class
        {
            if (!includeInactive && !current.activeInHierarchy)
                return;

            foreach (var component in current.components)
            {
                if (component is T value)
                    found.Add(value);
            }

            foreach (var child in current.transform.Children)
                CollectInternal(child.gameObject, includeInactive, found);
        }

        // Unity는 RectTransform을 추가하면 기존 Transform을 대체한다.
        private Transform ReplaceTransformInternal(Transform replacement)
        {
            var previous = transform;
            replacement.gameObject = this;
            replacement.SetParent(previous.parent, false);
            previous.SetParent(null, false);
            foreach (var child in previous.Children.ToArray())
                child.SetParent(replacement, false);
            components[components.IndexOf(previous)] = replacement;
            transform = replacement;
            return replacement;
        }
    }

    public class Transform : Component
    {
        private Transform parentValue;
        public readonly List<Transform> Children = new List<Transform>();
        public Vector3 position;
        public Vector3 localPosition;
        public Vector3 localScale;
        public int FrontChanges;

        public Transform parent
        {
            get => parentValue;
            set => SetParent(value, true);
        }

        public int childCount => Children.Count;

        public void SetParent(Transform value)
        {
            SetParent(value, true);
        }

        public void SetParent(Transform value, bool worldPositionStays)
        {
            parentValue?.Children.Remove(this);
            parentValue = value;
            parentValue?.Children.Add(this);
        }

        public Transform GetChild(int index) => Children[index];
        public int GetSiblingIndex() => parentValue == null ? 0 : parentValue.Children.IndexOf(this);

        public void SetSiblingIndex(int index)
        {
        }

        public void SetAsLastSibling()
        {
            FrontChanges++;
        }

        public bool IsChildOf(Transform value)
        {
            for (var current = this; current != null; current = current.parentValue)
            {
                if (current == value)
                    return true;
            }

            return false;
        }

        public Transform Find(string value) => Children.Find(child => child.name == value);
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 offsetMin;
        public Vector2 offsetMax;
        public Vector2 pivot;
        public Vector2 sizeDelta;
    }

    public struct Vector2
    {
        public static Vector2 zero;
        public static Vector2 one = new Vector2(1f, 1f);
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public static Vector3 zero;
        public static Vector3 one = new Vector3(1f, 1f, 1f);
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }

    public static class Time
    {
        public static float deltaTime = 0.02f;
        public static float unscaledDeltaTime = 0.02f;
        public static int frameCount;
    }

    public enum LogType
    {
        Error,
        Assert,
        Warning,
        Log,
        Exception
    }

    public enum RuntimePlatform
    {
        WindowsPlayer,
        Switch
    }

    public enum FindObjectsSortMode
    {
        None
    }

    public enum RuntimeInitializeLoadType
    {
        SubsystemRegistration,
        AfterSceneLoad
    }

    public static class Application
    {
        public static bool isConsolePlatform;
        public static RuntimePlatform platform = RuntimePlatform.WindowsPlayer;
        public static string persistentDataPath = "";

        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceivedThreaded;
        public static int LogSubscriberCount => logMessageReceivedThreaded?.GetInvocationList().Length ?? 0;
        public static void RaiseLog(string condition, string stackTrace, LogType type) => logMessageReceivedThreaded?.Invoke(condition, stackTrace, type);
    }

    public static class Debug
    {
        public static readonly List<Exception> Errors = new List<Exception>();

        public static void Log(object message)
        {
        }

        public static void LogWarning(object message)
        {
        }

        // Unity는 logMessageReceivedThreaded를 로그를 남긴 스레드에서 동기 호출한다.
        public static void LogError(object message)
        {
            Errors.Add(new Exception(message.ToString()));
            Application.RaiseLog(message.ToString(), "UnityEngine.Debug:LogError (object)\n" + UnityStackTrace(new System.Diagnostics.StackTrace(1)), LogType.Error);
        }

        public static void LogException(Exception error)
        {
            Errors.Add(error);
            Application.RaiseLog(error.GetType().Name + ": " + error.Message, error.StackTrace ?? "", LogType.Exception);
        }

        // Unity 형식("Namespace.Type:Method (args)")을 흉내 내며 소스 위치는 생략한다.
        public static string UnityStackTrace(System.Diagnostics.StackTrace trace)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var frame in trace.GetFrames())
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType != null)
                    builder.Append(method.DeclaringType.FullName).Append(':').Append(method.Name).Append(" ()\n");
            }

            return builder.ToString();
        }
    }

    public sealed class SerializeField : Attribute
    {
    }

    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text)
        {
        }
    }

    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type type)
        {
        }
    }

    public sealed class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order)
        {
        }
    }

    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type)
        {
        }
    }
}
