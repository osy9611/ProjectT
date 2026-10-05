using Cysharp.Threading.Tasks;
using ProjectT.UGUI;
using System.Text;
using UnityEngine;

namespace ProjectT.Sample
{
    // FeatureSampleRunner와 화면 영역을 나눠 UIManager 수명 정책(Static 스택·포커스, Dynamic, System, 전환 정리)을 확인한다.
    public class UISampleRunner : MonoBehaviour
    {
        private const float Width = 330f;

        private readonly StringBuilder builder = new StringBuilder();
        private bool ready;
        private bool uiInputAllowed = true;

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

            ready = true;
        }

        private void OnGUI()
        {
            if (!ready)
                return;

            GUILayout.BeginArea(new Rect(Screen.width - Width - 10, 10, Width, 340), GUI.skin.box);
            GUILayout.Label("[UI]");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Static A"))
                OpenInternal(UIDefine.eUIType.SampleStaticA);

            if (GUILayout.Button("Static B (Async)"))
                OpenInternalAsync(UIDefine.eUIType.SampleStaticB).Forget(Global.LogException);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Dynamic"))
                OpenInternal(UIDefine.eUIType.SampleDynamic);

            if (GUILayout.Button("System"))
                OpenInternal(UIDefine.eUIType.SampleSystem);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Async x2 Same Type (Dynamic)"))
                OpenTwiceInternalAsync().Forget(Global.LogException);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Hide Top"))
                HideTopInternal(true);

            if (GUILayout.Button("Hide Top (no restore)"))
                HideTopInternal(false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove Static A"))
            {
                Global.UI.RemoveWidget(UIDefine.eUIType.SampleStaticA);
                FeatureSampleRunner.Write("RemoveWidget(SampleStaticA)");
            }

            if (GUILayout.Button("Clear Transient"))
            {
                Global.UI.ClearTransientWidgets();
                FeatureSampleRunner.Write("ClearTransientWidgets");
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button(uiInputAllowed ? "Block UI Input" : "Allow UI Input"))
            {
                uiInputAllowed = !uiInputAllowed;
                Global.UI.SetInputAllowed(uiInputAllowed);
                FeatureSampleRunner.Write($"UI.SetInputAllowed({uiInputAllowed})");
            }

            GUILayout.Label(BuildStateInternal());
            GUILayout.EndArea();
        }

        private string BuildStateInternal()
        {
            builder.Clear();
            var top = Global.UI.GetCurrentStackUI();
            builder.Append("Top: ").Append(top == null ? "None" : top.name).Append('\n');
            builder.Append("Stack: ");
            foreach (var widget in Global.UI.UIStack)
                builder.Append(widget == null ? "(destroyed)" : widget.name).Append(widget != null && widget.gameObject.activeSelf ? "" : "(hidden)").Append(" > ");

            builder.Append('\n');
            AppendWidgetInternal(UIDefine.eUIType.SampleStaticA);
            AppendWidgetInternal(UIDefine.eUIType.SampleStaticB);
            AppendWidgetInternal(UIDefine.eUIType.SampleDynamic);
            AppendWidgetInternal(UIDefine.eUIType.SampleSystem);
            return builder.ToString();
        }

        private void AppendWidgetInternal(UIDefine.eUIType type)
        {
            var widget = Global.UI.FindWidget<SampleUI>(type);
            builder.Append(type).Append(": ").Append(widget == null ? "-" : widget.IsActive ? "Shown" : "Hidden").Append('\n');
        }

        private void OpenInternal(UIDefine.eUIType type)
        {
            Global.UI.CreateWidget<SampleUI>(type).Show();
        }

        private async UniTask OpenInternalAsync(UIDefine.eUIType type)
        {
            var token = this.GetCancellationTokenOnDestroy();
            var (canceled, widget) = await Global.UI.CreateWidgetAsync<SampleUI>(type, token).SuppressCancellationThrow();
            if (canceled || widget == null)
                return;

            widget.Show();
        }

        // 같은 타입을 동시에 요청하면 생성은 한 번만 일어나고 두 호출자가 같은 인스턴스를 받아야 한다.
        private async UniTask OpenTwiceInternalAsync()
        {
            var type = UIDefine.eUIType.SampleDynamic;
            Global.UI.RemoveWidget(type);

            var token = this.GetCancellationTokenOnDestroy();
            var (first, second) = await UniTask.WhenAll(
                Global.UI.CreateWidgetAsync<SampleUI>(type, token),
                Global.UI.CreateWidgetAsync<SampleUI>(type, token));

            FeatureSampleRunner.Write($"Async x2 same instance: {ReferenceEquals(first, second)}");
            first.Show();
        }

        private void HideTopInternal(bool restorePrevious)
        {
            var top = Global.UI.GetCurrentStackUI();
            if (top != null)
                top.Hide(restorePrevious);
        }
    }
}
