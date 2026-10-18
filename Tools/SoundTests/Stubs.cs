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
            // 이전 프레임 끝에 Unity가 처리하는 지연 파괴를 다음 프레임 재개 전에 반영한다.
            UnityEngine.Object.FlushDeferredDestroy();
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
    public class AudioClip : Object
    {
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

    public static class AudioListener
    {
        public static bool pause;
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

        public T LoadAndGet<T>(string path, ResourceScope scope = null) where T : class
        {
            LastLoadScope = ResolveScope(scope);
            LoadCount++;
            return resources.TryGetValue(path, out var resource) ? resource as T : null;
        }

        public UniTask<T> LoadAndGetAsync<T>(string path, CancellationToken cancelToken = default, ResourceScope scope = null) where T : class
        {
            cancelToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(LoadAndGet<T>(path, scope));
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
