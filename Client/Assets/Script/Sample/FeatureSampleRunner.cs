using Cysharp.Threading.Tasks;
using ProjectT.Controller;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProjectT.Sample
{
    // 빌드·TCP를 제외한 리팩토링 기능(Global 생명주기, Actor·Pool, Input, ErrorReporter)을 Play Mode에서 직접 확인하는 샘플이다.
    public class FeatureSampleRunner : MonoBehaviour
    {
        private const int MaxLines = 16;

        private static readonly List<string> s_lines = new List<string>();

        [SerializeField] private GameObject actorPrefab;

        private readonly List<SampleActor> spawned = new List<SampleActor>();
        private InputContext blocker;
        private bool inputAllowed = true;
        private bool ready;
        private Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_lines.Clear();
        }

        public static void Write(string message)
        {
            Debug.Log($"[Sample] {message}");
            s_lines.Add($"{Time.frameCount}: {message}");
            if (s_lines.Count > MaxLines)
                s_lines.RemoveAt(0);
        }

        private void Awake()
        {
            // 샘플 패널이 Single 전환으로 언로드되지 않도록 Global처럼 씬 밖에 둔다.
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeInternalAsync().Forget(Global.LogException);
        }

        private async UniTask InitializeInternalAsync()
        {
            var token = this.GetCancellationTokenOnDestroy();
            if (await Global.Instance.WhenReadyAsync(token).SuppressCancellationThrow())
                return;

            Global.Notify.Subscribe(NotificationId.InputControlsChanged, _ => Write($"Notify InputControlsChanged: {Global.Input.CurrentDevice?.displayName}"), token);
            Global.Notify.Subscribe(NotificationId.InputDeviceLost, _ => Write("Notify InputDeviceLost"), token);
            Global.Notify.Subscribe(NotificationId.InputDeviceRegained, _ => Write("Notify InputDeviceRegained"), token);

            blocker = new InputContext("SampleBlocker", 100)
                .Bind("Player/Jump", _ => Write("Blocker consumed Jump"), eInputEvent.Performed);

            ready = true;
            Write("Global Ready");
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label($"Global: {(Global.Instance == null ? "None" : Global.Instance.State.ToString())}");
            if (!ready)
            {
                GUILayout.Label("Waiting for WhenReadyAsync...");
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            DrawActorPoolInternal();
            DrawInputInternal();
            DrawErrorInternal();

            GUILayout.Space(8);
            GUILayout.Label("Log");
            foreach (var line in s_lines)
                GUILayout.Label(line);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawActorPoolInternal()
        {
            // 씬 Scope 풀의 인스턴스는 Single 전환에서 파괴되므로 목록에서 뺀다.
            spawned.RemoveAll(component => component == null);
            GUILayout.Label($"[Actor·Pool] spawned {spawned.Count}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spawn"))
                SpawnInternal();

            if (GUILayout.Button("Return Last"))
                ReturnLastInternal();

            if (GUILayout.Button("Return All"))
            {
                while (spawned.Count > 0)
                    ReturnLastInternal();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle Active"))
                ToggleLastInternal();

            if (GUILayout.Button("Respawn"))
                RespawnInternal();
            GUILayout.EndHorizontal();
        }

        private void DrawInputInternal()
        {
            var input = Global.Input;
            GUILayout.Label($"[Input] device {input.CurrentDevice?.displayName ?? "None"}, lost {input.IsDeviceLost}");
            GUILayout.Label("WASD/Stick: Move, Space/South: Jump");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(inputAllowed ? "Block Input" : "Allow Input"))
            {
                inputAllowed = !inputAllowed;
                input.SetInputAllowed(inputAllowed);
                Write($"SetInputAllowed({inputAllowed})");
            }

            bool blocking = blocker.Owner != null;
            if (GUILayout.Button(blocking ? "Remove Jump Blocker" : "Add Jump Blocker"))
            {
                if (blocking)
                    input.Controller.RemoveContext(blocker);
                else
                    input.Controller.AddContext(blocker);

                Write(blocking ? "Blocker removed" : "Blocker added (priority 100, consume)");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawErrorInternal()
        {
            GUILayout.Label("[ErrorReporter]");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("LogError"))
                Debug.LogError("[Sample] Debug.LogError test");

            if (GUILayout.Button("LogException"))
                Global.LogException(new InvalidOperationException("[Sample] Global.LogException test"));

            if (GUILayout.Button("Forget Throw"))
                ThrowNextFrameInternalAsync().Forget(Global.LogException);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Repeat x30"))
            {
                // 같은 지문은 처음 5번과 10의 거듭제곱 번째만 파일에 기록된다.
                for (int i = 0; i < 30; i++)
                    Debug.LogError("[Sample] repeated error");
            }

            if (GUILayout.Button("Canceled (ignored)"))
                Global.LogException(new OperationCanceledException("[Sample] canceled"));

            if (GUILayout.Button("Open Log Folder"))
                Application.OpenURL("file://" + Path.Combine(Application.persistentDataPath, "ErrorReports"));
            GUILayout.EndHorizontal();
        }

        private void SpawnInternal()
        {
            // 활성 씬이 있으면 씬 Scope 풀에서 대여해, 씬 정지 시 풀 정리와 Actor 반납을 함께 확인한다.
            var scene = Global.Scene.CurrentScene;
            var obj = Global.Pool.Get(actorPrefab, null, scene == null ? null : scene.ResourceScope);
            obj.transform.position = new Vector3(UnityEngine.Random.Range(-4f, 4f), 0f, UnityEngine.Random.Range(-2f, 2f));
            var component = obj.GetComponent<SampleActor>();
            component.Spawn();
            spawned.Add(component);
            Write($"Spawn {obj.name} ({(scene == null ? "App" : scene.GetType().Name)} scope)");
        }

        private void ReturnLastInternal()
        {
            if (spawned.Count == 0)
                return;

            var component = spawned[spawned.Count - 1];
            spawned.RemoveAt(spawned.Count - 1);
            Global.Pool.Return(component.gameObject);
            Write("Return to pool");
        }

        private void ToggleLastInternal()
        {
            if (spawned.Count == 0)
                return;

            var obj = spawned[spawned.Count - 1].gameObject;
            obj.SetActive(!obj.activeSelf);
        }

        private void RespawnInternal()
        {
            if (spawned.Count == 0)
                return;

            ReturnLastInternal();
            SpawnInternal();
        }

        private static async UniTask ThrowNextFrameInternalAsync()
        {
            await UniTask.Yield();
            throw new InvalidOperationException("[Sample] Forget exception test");
        }
    }
}
