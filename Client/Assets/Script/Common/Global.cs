using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ProjectT
{
    [DefaultExecutionOrder(-10000)]
    public class Global : MonoBehaviour
    {
        private static Global s_instance;
        public static Global Instance => s_instance;
        public bool LoadData = false;
        public bool UseDebugLog = true;
        private ManagerHost host;
        private bool focused = true;
        private bool paused;
        public ManagerState State => host?.State ?? ManagerState.Created;
        public bool IsReady => State == ManagerState.Ready;

        private ResourceManager resource;
        public static ResourceManager Resource { get => ReadyInstance.resource; }

        private DataManager data;
        public static DataManager Data { get => ReadyInstance.data; }

        private PoolManager pool;
        public static PoolManager Pool { get => ReadyInstance.pool; }

        private SceneManager scene;
        public static SceneManager Scene { get => ReadyInstance.scene; }

        private UIManager ui;
        public static UIManager UI { get => ReadyInstance.ui; }

        private SoundManager sound;
        public static SoundManager Sound { get => ReadyInstance.sound; }

        private NotificationManager notify;
        public static NotificationManager Notify { get => ReadyInstance.notify; }

        private ClientLocalStorageManager localStorage;
        public static ClientLocalStorageManager LocalStorage { get => ReadyInstance.localStorage; }

        private CostumeManager costume;
        public static CostumeManager Costume { get => ReadyInstance.costume; }

        private static Global ReadyInstance
        {
            get
            {
                if (s_instance == null || !s_instance.IsReady)
                    throw new InvalidOperationException("Global is not ready; await WhenReadyAsync first.");

                return s_instance;
            }
        }

        public static DesignTable.DataMgr Table => Data.Table;

        // 시스템 등록 시점
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() 
        {
            s_instance = null;
        }

        //첫 씬 로드 및 오브젝트들의 Awake 호출 후 실행함 
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapRetainedObjects()
        {
            foreach (var global in FindObjectsByType<Global>(FindObjectsSortMode.None))
                if (global.enabled) global.Init();
        }

        private void Awake() 
        {
            Init();
        }

        private void Init()
        {
            if (ReferenceEquals(s_instance, this))
                return;

            if (s_instance != null)
            {
                enabled = false;    //컴포넌트를 제외함
                Destroy(this);
                return;
            }

            s_instance = this;

            host?.Shutdown();
            DontDestroyOnLoad(gameObject);
            focused = Application.isFocused;

            host = new ManagerHost(transform, LoadData);
            resource = host.Register(new ResourceManager());
            notify = host.Register(new NotificationManager());
            localStorage = host.Register(new ClientLocalStorageManager());
            data = host.Register(new DataManager());
            pool = host.Register(new PoolManager());
            scene = host.Register(new SceneManager());
            ui = host.Register(new UIManager());
            sound = host.Register(new SoundManager());
            costume = host.Register(new CostumeManager());

            InitializeAsync(host).Forget();
        }

        private async UniTask InitializeAsync(ManagerHost owner)
        {
            try
            {
                await owner.InitializeAsync();
                if (!ReferenceEquals(host, owner) || owner.State != ManagerState.Ready)
                    return;

                owner.Focus(focused);
                owner.Pause(paused);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                LogException(error);
            }
            finally
            {
                foreach (var error in owner.ShutdownErrors) 
                    LogException(error);
            }
        }

        public async UniTask WhenReadyAsync(CancellationToken cancellationToken = default)
        {
            if (host == null)
                throw new InvalidOperationException("Global has not awakened.");

            await host.WhenReady.AttachExternalCancellation(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsReady)
                throw new OperationCanceledException("Global stopped before the caller resumed.");
        }

        public static T GetManager<T>() where T : ManagerBase
        {
            if (s_instance == null || s_instance.host == null)
                throw new InvalidOperationException("Global is not available.");

            return s_instance.host.GetReady<T>();
        }

        public static bool TryGetReady<T>(out T manager) where T : ManagerBase
        {
            manager = null;
            return s_instance != null && s_instance.host != null && s_instance.host.TryGetReady(out manager);
        }

        private void Update()
        {
            if (ReferenceEquals(s_instance, this))
                host?.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (ReferenceEquals(s_instance, this))
                host?.FixedUpdate(Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (ReferenceEquals(s_instance, this))
                host?.LateUpdate();
        }

        private void OnApplicationFocus(bool value)
        {
            focused = value;
            if (ReferenceEquals(s_instance, this))
                host?.Focus(value);
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (ReferenceEquals(s_instance, this))
                host?.Pause(value);
        }

        private void OnApplicationQuit()
        {
            Shutdown(ShutdownReason.ApplicationExit);
        }

        private void OnDestroy() 
        {
            Shutdown();
        }

        public void Shutdown(ShutdownReason reason = ShutdownReason.Normal)
        {
            if (!ReferenceEquals(s_instance, this))
                return;
            try
            {
                host?.Shutdown(reason);
                if (host != null)
                {
                    foreach (var error in host.ShutdownErrors)
                        LogException(error);
                }
            }
            finally
            {
                s_instance = null;
            }
        }

        #region Log Methods
        public void Log(string msg, string colorHex = default)
        {
            if (UseDebugLog)
                Debug.Log(MakeTimeStampLog(UtilFunc.MakeColorRichText($"[Global] {msg}", colorHex), "INFO"));
        }

        public void LogWarning(string msg)
        {
            if (UseDebugLog)
                Debug.LogWarning(MakeTimeStampLog($"[Global] {msg}", "WARNING"));
        }

        public void LogError(string msg)
        {
            Debug.LogError(MakeTimeStampLog($"[Global] {msg}", "ERROR"));
        }

        public static void LogException(Exception error)
        {
            Debug.LogError(MakeTimeStampLog($"[Global] {error}", "ERROR"));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string MakeTimeStampLog(string msg, string type)
        {
#if UNITY_EDITOR
            return msg;
#else
            var timestamp = $"[{type}][{System.DateTime.Now.ToString("yy-MM-dd HH:mm:ss")}]";
            msg = $"{timestamp} {msg}";
            return msg;
#endif
        }
        #endregion
    }
}

