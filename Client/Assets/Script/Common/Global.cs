using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ProjectT
{
    [DefaultExecutionOrder(-10000)]
    public partial class Global : MonoBehaviour
    {
        private static Global s_instance;
        public static Global Instance => s_instance;
        public bool LoadData = false;
        public bool UseRemoteResource = true;
        public bool UseDebugLog = true;
        private ManagerHost host;
        private bool paused;
        public static ManagerState State => s_instance?.host?.State ?? ManagerState.Created;
        public static bool IsReady => State == ManagerState.Ready;

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
            ErrorReporter.SetSinks(new FileErrorSink(Path.Combine(Application.persistentDataPath, "ErrorReports")));

            DontDestroyOnLoad(gameObject);

            host = new ManagerHost();
            RegisterManagers(host);

            InitializeAsync().Forget(LogException);
        }

        private async UniTask InitializeAsync()
        {
            try
            {
                await host.InitializeAsync();
                if (host.State != ManagerState.Ready)
                    return;

                host.Pause(paused);
            }
            // UniTask는 취소 예외로 끝난 초기화 실패(Failed)도 취소로 전달하므로 정상 종료로 인한 취소만 삼킨다.
            catch (OperationCanceledException) when (host.State == ManagerState.Stopping || host.State == ManagerState.Stopped)
            {
            }
            finally
            {
                // 초기화 실패로 호스트가 스스로 종료한 경우만 여기서 보고한다. 그 외 종료 오류는 Shutdown에서 보고한다.
                if (host.State == ManagerState.Failed)
                {
                    foreach (var error in host.ShutdownErrors)
                        LogException(error);
                }
            }
        }

        public static async UniTask WhenReadyAsync(CancellationToken cancellationToken = default)
        {
            // Global보다 먼저 활성화된 호출자는 Global이 Awake할 때까지 기다린다.
            if (s_instance == null)
                await UniTask.WaitUntil(() => s_instance != null, cancellationToken: cancellationToken);

            await s_instance.host.WhenReady.AttachExternalCancellation(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (!IsReady)
                throw new OperationCanceledException("Global stopped before the caller resumed.");
        }

        public static T GetManager<T>() where T : ManagerBase
        {
            if (s_instance == null || s_instance.host == null)
                throw new InvalidOperationException("Global is not available.");

            return s_instance.host.GetReadyManager<T>();
        }

        public static bool TryGetReady<T>(out T manager) where T : ManagerBase
        {
            manager = null;
            return s_instance != null && s_instance.host != null && s_instance.host.TryGetReady(out manager);
        }

        private void Update()
        {
            if (!ReferenceEquals(s_instance, this))
                return;

            try
            {
                host?.Update(Time.deltaTime);
            }
            finally
            {
                ErrorReporter.Flush();
            }
        }

        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (!ReferenceEquals(s_instance, this))
                return;

            host?.Pause(value);
            // 모바일은 백그라운드에서 종료 콜백 없이 프로세스가 끝날 수 있다.
            if (value)
                ErrorReporter.Flush();
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
                // 초기화 실패로 이미 Failed인 호스트의 종료 오류는 InitializeAsync에서 보고했다.
                if (host != null && host.State != ManagerState.Failed)
                {
                    host.Shutdown(reason);
                    foreach (var error in host.ShutdownErrors)
                        LogException(error);
                }
            }
            finally
            {
                // 다음 Global이 sink를 새로 지정하므로 종료 오류까지 기록한 뒤 이 인스턴스의 sink를 해제한다.
                ErrorReporter.Flush();
                ErrorReporter.SetSinks();
                s_instance = null;
            }
        }

        #region Log Methods
        // 소켓 스레드와 인스턴스가 없는 종료 중에도 호출되므로 static이며, 인스턴스가 없으면 UseDebugLog 설정 없이 출력한다.
        public static void Log(string msg, string colorHex = default)
        {
            if (IsDebugLogEnabledInternal())
                Debug.Log(MakeTimeStampLog(UtilFunc.MakeColorRichText($"[Global] {msg}", colorHex), "INFO"));
        }

        public static void LogWarning(string msg)
        {
            if (IsDebugLogEnabledInternal())
                Debug.LogWarning(MakeTimeStampLog($"[Global] {msg}", "WARNING"));
        }

        // Unity의 == 비교는 메인 스레드 밖에서 안전하지 않으므로 참조 비교로 한 번만 읽는다.
        private static bool IsDebugLogEnabledInternal()
        {
            var instance = s_instance;
            return instance is null || instance.UseDebugLog;
        }

        public static void LogError(string msg)
        {
            Debug.LogError(MakeTimeStampLog($"[Global] {msg}", "ERROR"));
        }

        public static void LogException(Exception error)
        {
            ErrorReporter.Report(error);
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

