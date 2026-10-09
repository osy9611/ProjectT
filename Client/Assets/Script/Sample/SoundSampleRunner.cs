using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectT.Sample
{
    // SoundManager의 채널 재생·페이드, 3D 음원 핸들, 볼륨·뮤트를 확인한다. 씬 전환 시 BGM 유지와 3D 음원 정리는 Scene 패널과 함께 확인한다.
    public class SoundSampleRunner : MonoBehaviour
    {
        private const float Width = 330f;
        private const float Top = 600f;
        private const string TitleBgmPath = "Assets/BundleRes/Sound/Title.ogg";
        private const string ToneBgmPath = "Assets/BundleRes/Sound/Sample/SampleBgm.wav";
        private const string BeepPath = "Assets/BundleRes/Sound/Sample/SampleBeep.wav";
        private const string ClickPath = "Assets/BundleRes/Sound/Sample/SampleClick.wav";
        private const string LoopPath = "Assets/BundleRes/Sound/Sample/SampleLoop.wav";
        private const int BurstCount = 40;

        private SpatialSoundHandle loopHandle;
        private bool ready;

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

            var sound = Global.Sound;
            GUILayout.BeginArea(new Rect(Screen.width - Width - 10, Top, Width, Screen.height - Top - 10), GUI.skin.box);
            GUILayout.Label("[Sound]");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("BGM Title"))
                sound.PlayBgm(TitleBgmPath);

            if (GUILayout.Button("BGM Tone"))
                sound.PlayBgm(ToneBgmPath);

            if (GUILayout.Button("Fade BGM"))
                sound.PlayFade(sound.CurrentBgm != null && sound.CurrentBgm.name == "Title" ? ToneBgmPath : TitleBgmPath, eSound.Bgm, 1.5f);

            if (GUILayout.Button("Stop BGM"))
                sound.StopBgm();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("FX Beep"))
                sound.PlayOneShot(BeepPath);

            if (GUILayout.Button("FX Fade"))
                sound.PlayFade(BeepPath, eSound.FX, 0.5f);

            if (GUILayout.Button("UI Click"))
                sound.PlayOneShot(ClickPath, eSound.UI);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("3D PlayAt"))
                sound.PlayAt(BeepPath, RandomPositionInternal());

            if (GUILayout.Button($"3D Burst x{BurstCount}"))
                BurstInternal();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Loop Spawn"))
            {
                sound.Release(loopHandle);
                loopHandle = sound.SpawnAt(LoopPath, RandomPositionInternal(), loop: true, autoRelease: false);
            }

            if (GUILayout.Button("Pause"))
                sound.Pause(loopHandle);

            if (GUILayout.Button("Resume"))
                sound.Resume(loopHandle);

            if (GUILayout.Button("Stop"))
                sound.Stop(loopHandle);

            if (GUILayout.Button("Release"))
                sound.Release(loopHandle);
            GUILayout.EndHorizontal();

            DrawVolumeInternal(sound);

            GUILayout.Label($"BGM: {(sound.CurrentBgm == null ? "None" : sound.CurrentBgm.name)}");
            GUILayout.Label($"3D voices: {sound.ActiveSpatialVoiceCount}/{sound.SpatialVoiceCapacity}, loop handle valid: {sound.IsValid(loopHandle)}");
            GUILayout.EndArea();
        }

        private static void DrawVolumeInternal(SoundManager sound)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Master {sound.MasterVolume:0.00}", GUILayout.Width(90));
            float master = GUILayout.HorizontalSlider(sound.MasterVolume, 0f, 1f);
            if (master != sound.MasterVolume)
                sound.SetMasterVolume(master);

            bool masterMuted = GUILayout.Toggle(sound.MasterMuted, "Mute", GUILayout.Width(50));
            if (masterMuted != sound.MasterMuted)
                sound.SetMasterMuted(masterMuted);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            float fxVolume = sound.GetCategoryVolume(eSound.FX);
            GUILayout.Label($"FX {fxVolume:0.00}", GUILayout.Width(90));
            float fx = GUILayout.HorizontalSlider(fxVolume, 0f, 1f);
            if (fx != fxVolume)
                sound.SetCategoryVolume(eSound.FX, fx);

            bool fxMuted = GUILayout.Toggle(sound.GetCategoryMuted(eSound.FX), "Mute", GUILayout.Width(50));
            if (fxMuted != sound.GetCategoryMuted(eSound.FX))
                sound.SetCategoryMuted(eSound.FX, fxMuted);
            GUILayout.EndHorizontal();
        }

        // 동시 재생 한도를 넘는 요청은 로드 전에 거절되어야 한다.
        private void BurstInternal()
        {
            int accepted = 0;
            for (int i = 0; i < BurstCount; i++)
            {
                if (Global.Sound.PlayAt(BeepPath, RandomPositionInternal(), volume: 0.2f))
                    accepted++;
            }

            FeatureSampleRunner.Write($"3D Burst accepted {accepted}/{BurstCount} (capacity {Global.Sound.SpatialVoiceCapacity})");
        }

        private static Vector3 RandomPositionInternal()
        {
            return new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(0f, 4f));
        }
    }
}
