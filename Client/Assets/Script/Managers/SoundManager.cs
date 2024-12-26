using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
namespace ProjectT
{
    public enum eSound
    {
        Bgm,
        FX,
        UI
    }

    public class SoundManager : ManagerBase
    {
        private AudioSource[] audioSources = new AudioSource[System.Enum.GetNames(typeof(eSound)).Length];
        private Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();

        private CancellationTokenSource soundFadeCancel = new CancellationTokenSource();

        #region ManagerBase
        public override void OnAppEnd()
        {
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }

        public override void OnAppStart()
        {
        }

        public override void OnEnter()
        {
            CreateRootObject(Global.Instance.transform, "SoundManager");

            string[] soundNames = System.Enum.GetNames(typeof(eSound));

            for (int i = 0; i < soundNames.Length; ++i)
            {
                GameObject go = new GameObject { name = soundNames[i] };
                audioSources[i] = go.AddComponent<AudioSource>();
                go.transform.parent = RootObject.transform;
            }

            audioSources[(int)eSound.Bgm].loop = true;
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnLeave()
        {
            Clear();
        }

        public override void OnUpdate(float dt)
        {
        }
        #endregion

        private AudioClip GetOrAddAudioClip(string path, eSound type = eSound.FX)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            AudioClip clip = null;

            if (type == eSound.Bgm)
                clip = Global.Resource.LoadAndGet<AudioClip>(path);
            else
            {
                if (!audioClips.ContainsKey(path))
                {
                    clip = Global.Resource.LoadAndGet<AudioClip>(path);
                    audioClips.Add(clip.name, clip);
                }
            }

            if (clip == null)
                Global.Instance.LogError($"AudioClip Missing!! {path}");

            return clip;
        }

        public void Play(string path, eSound type = eSound.FX, float pitch = 1.0f)
        {
            AudioClip clip = GetOrAddAudioClip(path);
            Play(clip, type, pitch);
        }

        public void Play(AudioClip clip, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (clip == null)
                return;

            if (type == eSound.Bgm)
            {
                AudioSource audioSource = audioSources[(int)eSound.Bgm];
                if (audioSource.isPlaying)
                    audioSource.Stop();

                audioSource.pitch = pitch;
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                AudioSource audioSource = audioSources[(int)type];
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(clip);
            }
        }

        public void PlayFade(string path, eSound type = eSound.FX, float fadeTime = 1.0f, float pitch = 1.0f)
        {
            soundFadeCancel?.Cancel();

            AudioClip clip = GetOrAddAudioClip(path);
            ExecuteSoudnFade(clip, type, fadeTime, pitch).Forget();
        }

        private async UniTask ExecuteSoudnFade(AudioClip clip, eSound type, float fadeTime, float pitch)
        {
            await SoundStopFade(type, fadeTime);
            Play(clip, type, pitch);
            await SoundPlayFade(type, fadeTime);
        }

        private async UniTask SoundStopFade(eSound type, float fadeTime)
        {
            if (audioSources[(int)type] == null)
                return;

            AudioSource source = audioSources[(int)type];

            var startVolume = 0.2f;
            float reachVolume = source.volume;

            source.volume = 0;
            while (source.volume < reachVolume)
            {
                source.volume += startVolume * Time.deltaTime / fadeTime;
                await UniTask.Yield(cancellationToken: soundFadeCancel.Token);
            }

            source.volume = reachVolume;
        }

        private async UniTask SoundPlayFade(eSound type, float fadeTime)
        {
            if (audioSources[(int)type] == null)
                return;

            AudioSource source = audioSources[(int)type];

            var startVolume = source.volume;

            while (startVolume > 0)
            {
                source.volume -= startVolume * Time.deltaTime / fadeTime;
                await UniTask.Yield(cancellationToken: soundFadeCancel.Token);
            }

            source.Stop();
        }

        public void Clear()
        {
            foreach (AudioSource audioSource in audioSources)
            {
                audioSource.clip = null;
                audioSource.Stop();
            }

            foreach (var clip in audioClips)
            {
                var path = Global.Resource.GetPathFromData(clip.Key);
                Global.Resource.Release(path);
            }
        }
    }
}