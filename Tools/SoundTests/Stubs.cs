using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    public enum PlayerLoopTiming
    {
        Update
    }

    public readonly partial struct UniTask
    {
        public static UniTask Yield(CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return ManualFrameScheduler.Schedule(cancellationToken, cancelImmediately);
        }

        public static UniTask Yield(PlayerLoopTiming timing, CancellationToken cancellationToken, bool cancelImmediately = false)
        {
            return ManualFrameScheduler.Schedule(cancellationToken, cancelImmediately);
        }
    }

    public static class ManualFrameScheduler
    {
        private sealed class Wait
        {
            public readonly UniTaskCompletionSource Source = new UniTaskCompletionSource();
            public CancellationTokenRegistration Registration;
            public CancellationToken Token;
        }

        private static readonly List<Wait> pending = new List<Wait>();
        public static int PendingCount => pending.Count;

        internal static UniTask Schedule(CancellationToken token, bool cancelImmediately)
        {
            var wait = new Wait { Token = token };
            if (cancelImmediately && token.CanBeCanceled)
                wait.Registration = token.Register(state =>
                {
                    var item = (Wait)state;
                    item.Source.TrySetCanceled(item.Token);
                }, wait);

            pending.Add(wait);
            return wait.Source.Task;
        }

        public static void Step()
        {
            var frame = pending.ToArray();
            pending.Clear();
            foreach (var wait in frame)
            {
                wait.Registration.Dispose();
                if (wait.Token.IsCancellationRequested)
                    wait.Source.TrySetCanceled(wait.Token);
                else
                    wait.Source.TrySetResult();
            }
        }

        public static void Reset()
        {
            var frame = pending.ToArray();
            pending.Clear();
            foreach (var wait in frame)
            {
                wait.Registration.Dispose();
                wait.Source.TrySetCanceled();
            }
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Destroyed = new List<Object>();
        private static readonly List<Object> deferredDestroy = new List<Object>();
        private static bool deferDestroy;
        internal bool IsDestroyed { get; private set; }

        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.IsDestroyed;
            bool rightNull = ReferenceEquals(right, null) || right.IsDestroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }

        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();

        public static GameObject Instantiate(GameObject original, Transform parent, bool instantiateInWorldSpace)
        {
            if (original == null)
                return null;

            var result = new GameObject(original.name);
            result.transform.SetParent(parent, instantiateInWorldSpace);
            foreach (var component in original.Components)
            {
                if (component is Transform)
                    continue;

                var copy = result.AddComponent(component.GetType());
                if (component is AudioSource source && copy is AudioSource audio)
                {
                    audio.clip = source.clip;
                    audio.loop = source.loop;
                    audio.playOnAwake = source.playOnAwake;
                    audio.pitch = source.pitch;
                    audio.volume = source.volume;
                    audio.spatialBlend = source.spatialBlend;
                    audio.minDistance = source.minDistance;
                    audio.maxDistance = source.maxDistance;
                    audio.dopplerLevel = source.dopplerLevel;
                    audio.rolloffMode = source.rolloffMode;
                }
            }

            return result;
        }

        public static void Destroy(Object target)
        {
            if (ReferenceEquals(target, null) || target.IsDestroyed)
                return;

            if (deferDestroy)
            {
                if (!deferredDestroy.Contains(target))
                    deferredDestroy.Add(target);
                return;
            }

            MarkDestroyed(target);
        }

        public static void BeginDeferredDestroy()
        {
            deferDestroy = true;
        }

        public static void FlushDeferredDestroy()
        {
            deferDestroy = false;
            foreach (var target in deferredDestroy)
                MarkDestroyed(target);
            deferredDestroy.Clear();
        }

        private static void MarkDestroyed(Object target)
        {
            if (ReferenceEquals(target, null) || target.IsDestroyed)
                return;

            target.IsDestroyed = true;
            Destroyed.Add(target);
            if (target is GameObject gameObject)
            {
                foreach (var child in gameObject.transform.Children.ToArray())
                    MarkDestroyed(child.gameObject);
                foreach (var component in gameObject.Components)
                    MarkDestroyed(component);
                gameObject.transform.SetParent(null, false);
            }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeSelf;
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public class GameObject : Object
    {
        public static int CreatedCount { get; private set; }
        private readonly List<Component> components = new List<Component>();
        internal IEnumerable<Component> Components => components;
        public string name;
        public bool activeSelf = true;
        public Transform transform { get; }

        public GameObject(string name = "")
        {
            CreatedCount++;
            this.name = name;
            transform = new Transform { gameObject = this };
            components.Add(transform);
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            components.Add(component);
            return component;
        }

        public Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type, true);
            component.gameObject = this;
            components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            foreach (var component in components)
                if (component is T result)
                    return result;

            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var found = new List<T>();
            Collect(this, includeInactive, found);
            return found.ToArray();
        }

        private static void Collect<T>(GameObject current, bool includeInactive, List<T> found) where T : class
        {
            if (includeInactive || current.activeSelf)
            {
                foreach (var component in current.components)
                    if (component is T value)
                        found.Add(value);
            }

            foreach (var child in current.transform.Children)
                Collect(child.gameObject, includeInactive, found);
        }

        public void SetActive(bool value)
        {
            activeSelf = value;
        }
    }

    public class Transform : Component
    {
        private Transform parentValue;
        public Transform parent
        {
            get => parentValue;
            set => SetParent(value, true);
        }
        public readonly List<Transform> Children = new List<Transform>();
        public Vector3 position;

        public void SetParent(Transform value, bool worldPositionStays)
        {
            parentValue?.Children.Remove(this);
            parentValue = value;
            parentValue?.Children.Add(this);
        }
    }

    public class AudioClip : Object
    {
        public string name;
        public AudioDataLoadState loadState = AudioDataLoadState.Loaded;

        public AudioClip(string name = "")
        {
            this.name = name;
        }
    }

    public enum AudioDataLoadState
    {
        Unloaded,
        Loading,
        Loaded,
        Failed
    }

    public enum AudioRolloffMode
    {
        Logarithmic,
        Linear,
        Custom
    }

    public class AudioSource : MonoBehaviour
    {
        public AudioClip clip;
        public bool loop;
        public bool playOnAwake = true;
        private bool playing;
        private bool paused;
        public int IsPlayingReadCount;
        public bool isPlaying
        {
            get
            {
                IsPlayingReadCount++;
                return playing && !paused;
            }
            set
            {
                playing = value;
                if (!value)
                    paused = false;
            }
        }
        public float pitch = 1f;
        public float volume = 1f;
        public float spatialBlend;
        public float minDistance = 1f;
        public float maxDistance = 500f;
        public float dopplerLevel = 1f;
        public AudioRolloffMode rolloffMode;
        public int PlayCount;
        public int StopCount;
        public int PauseCount;
        public int UnPauseCount;
        public readonly List<AudioClip> OneShots = new List<AudioClip>();

        public void Play()
        {
            PlayCount++;
            paused = false;
            isPlaying = true;
            ProjectT.TestTrace.Events.Add("play:" + (clip?.name ?? "null"));
        }

        public void Pause()
        {
            PauseCount++;
            if (playing)
                paused = true;
        }

        public void UnPause()
        {
            UnPauseCount++;
            if (playing)
                paused = false;
        }

        public void Stop()
        {
            StopCount++;
            isPlaying = false;
            ProjectT.TestTrace.Events.Add("stop:" + gameObject.name);
        }

        public void PlayOneShot(AudioClip value)
        {
            OneShots.Add(value);
            isPlaying = true;
            ProjectT.TestTrace.Events.Add("oneshot:" + (value?.name ?? "null"));
        }
    }

    public static class Time
    {
        public static float deltaTime = 0.02f;
        public static float unscaledDeltaTime = 0.02f;
        public static int frameCount;
    }

    public static class AudioListener
    {
        public static bool pause;
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }

    public struct Vector3
    {
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

    public sealed class SerializeField : Attribute
    {
    }

    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text)
        {
        }
    }
}

namespace UnityEngine.Events
{
    public class UnityEvent
    {
        private event Action handlers;
        public int ListenerCount => handlers?.GetInvocationList().Length ?? 0;
        public void AddListener(Action action) => handlers += action;
        public void RemoveListener(Action action) => handlers -= action;
        public void Invoke() => handlers?.Invoke();
    }

    public class UnityEvent<T>
    {
        private event Action<T> handlers;
        public int ListenerCount => handlers?.GetInvocationList().Length ?? 0;
        public void AddListener(Action<T> action) => handlers += action;
        public void RemoveListener(Action<T> action) => handlers -= action;
        public void Invoke(T value) => handlers?.Invoke(value);
    }
}

namespace UnityEngine.UI
{
    public class Button : UnityEngine.MonoBehaviour
    {
        public readonly UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent();
    }

    public class Toggle : UnityEngine.MonoBehaviour
    {
        public readonly UnityEngine.Events.UnityEvent<bool> onValueChanged = new UnityEngine.Events.UnityEvent<bool>();
    }
}

namespace UnityEngine.EventSystems
{
    public class BaseEventData
    {
    }

    public enum EventTriggerType
    {
        PointerClick
    }

    public class EventTrigger : UnityEngine.MonoBehaviour
    {
        public sealed class TriggerEvent : UnityEngine.Events.UnityEvent<BaseEventData>
        {
        }

        public sealed class Entry
        {
            public EventTriggerType eventID;
            public TriggerEvent callback = new TriggerEvent();
        }

        public List<Entry> triggers = new List<Entry>();
    }
}

namespace DesignEnum
{
    public enum SoundList
    {
        None
    }
}

namespace ProjectT
{
    using ProjectT.Addressable;
    using ProjectT.Pool;
    using UnityEngine;

    public static class TestTrace
    {
        public static readonly List<string> Events = new List<string>();
    }

    public sealed class FakeResourceManager
    {
        private readonly Dictionary<string, Object> resources = new Dictionary<string, Object>();
        private readonly HashSet<ResourceScope> scopes = new HashSet<ResourceScope>();
        private CancellationToken lifetimeToken;
        private bool stopped;
        public int LoadCount { get; private set; }
        public int ScopeCount { get; private set; }
        public int ScopeReleaseCount { get; private set; }
        public int ActiveScopeCount => scopes.Count;
        public bool ThrowOnNextScopeRelease { get; set; }
        public ResourceScope SceneScope { get; private set; }
        public ResourceScope LastLoadScope { get; private set; }

        public void Initialize(CancellationToken token)
        {
            lifetimeToken = token;
            stopped = false;
            SceneScope = CreateScope();
        }

        public void ReplaceSceneScope()
        {
            var previous = SceneScope;
            SceneScope = CreateScope();
            previous.Dispose();
        }

        public void Add(string path, AudioClip clip)
        {
            resources[path] = clip;
        }

        public void Add(string path, GameObject prefab)
        {
            resources[path] = prefab;
        }

        public ResourceScope CreateScope()
        {
            lifetimeToken.ThrowIfCancellationRequested();
            if (stopped)
                throw new InvalidOperationException("Resource manager is stopped.");

            ScopeCount++;
            ResourceScope scope = null;
            scope = new ResourceScope(_ =>
            {
                scopes.Remove(scope);
                ScopeReleaseCount++;
                TestTrace.Events.Add("scope-release");
                if (ThrowOnNextScopeRelease)
                {
                    ThrowOnNextScopeRelease = false;
                    throw new InvalidOperationException("scope release");
                }
            });
            scopes.Add(scope);
            return scope;
        }

        internal ResourceScope ResolveScope(ResourceScope scope)
        {
            lifetimeToken.ThrowIfCancellationRequested();
            if (stopped)
                throw new InvalidOperationException("Resource manager is stopped.");

            scope = scope ?? SceneScope;
            if (scope == null || !scopes.Contains(scope) || scope.Token.IsCancellationRequested)
                throw new InvalidOperationException("Resource scope is unavailable.");

            return scope;
        }

        public T LoadAndGet<T>(string path, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables) where T : class
        {
            LastLoadScope = ResolveScope(scope);
            LoadCount++;
            return resources.TryGetValue(path, out var resource) ? resource as T : null;
        }

        public UniTask<T> LoadAndGetAsync<T>(string path, CancellationToken cancelToken = default, ResourceScope scope = null, ResourceSource source = ResourceSource.Addressables) where T : class
        {
            cancelToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(LoadAndGet<T>(path, scope, source));
        }

        public void Shutdown()
        {
            stopped = true;
            List<Exception> errors = null;
            foreach (var scope in new List<ResourceScope>(scopes))
                ErrorCollector.Run(ref errors, scope, item => item.Dispose());
            ErrorCollector.ThrowIfAny(errors);
        }
    }

    public class Global : MonoBehaviour
    {
        private static Global instance;
        public static Global Instance
        {
            get
            {
                if (instance == null)
                    instance = new GameObject("Global").AddComponent<Global>();

                return instance;
            }
        }

        public static FakeResourceManager Resource { get; set; } = new FakeResourceManager();
        private static PoolManager pool;
        public static PoolManager Pool
        {
            get => pool != null && pool.State == ManagerState.Ready ? pool : null;
            set => pool = value;
        }
        public static SoundManager Sound { get; set; }
        public static readonly List<Exception> Exceptions = new List<Exception>();
        public static void LogException(Exception error) => Exceptions.Add(error);

        public static bool TryGetReady<T>(out T manager) where T : ManagerBase
        {
            manager = Sound as T;
            return manager != null && manager.State == ManagerState.Ready;
        }
    }
}

namespace ProjectT.Addressable
{
    public enum ResourceSource
    {
        Addressables,
        Resources
    }
}
