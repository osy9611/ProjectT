using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
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
        private ResourceScope resourceScope;

        private AudioSource[] audioSources = new AudioSource[System.Enum.GetNames(typeof(eSound)).Length];
        private Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();

        private CancellationTokenSource soundFadeCancel;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("SoundManager");

            string[] soundNames = System.Enum.GetNames(typeof(eSound));

            for (int i = 0; i < soundNames.Length; ++i)
            {
                GameObject go = new GameObject { name = soundNames[i] };
                audioSources[i] = go.AddComponent<AudioSource>();
                go.transform.parent = RootObject.transform;
            }

            audioSources[(int)eSound.Bgm].loop = true;
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            Clear();
        }


        private AudioClip GetOrAddAudioClip(string path, eSound type = eSound.FX)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (!audioClips.TryGetValue(path, out var clip))
            {
                resourceScope = resourceScope ?? Global.Resource.CreateScope();
                clip = Global.Resource.LoadAndGet<AudioClip>(path, scope: resourceScope);
                if (clip != null)
                    audioClips.Add(path, clip);
            }

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
            soundFadeCancel?.Dispose();
            soundFadeCancel = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
            var clip = GetOrAddAudioClip(path, type);
            ExecuteSoundFade(clip, type, fadeTime, pitch, soundFadeCancel.Token).Forget();
        }

        private async UniTask ExecuteSoundFade(AudioClip clip, eSound type, float fadeTime, float pitch, CancellationToken token)
        {
            var source = audioSources[(int)type];

            if (source == null || clip == null)
                return;

            float volume = source.volume;

            try
            {
                if (fadeTime <= 0f)
                {
                    token.ThrowIfCancellationRequested();
                    Play(clip, type, pitch);

                    return;
                }

                for (float elapsed = 0; elapsed < fadeTime; elapsed += Time.deltaTime)
                {
                    token.ThrowIfCancellationRequested();
                    source.volume = Mathf.Lerp(volume, 0f, elapsed / fadeTime);

                    await UniTask.Yield(cancellationToken: token);
                }

                token.ThrowIfCancellationRequested();

                Play(clip, type, pitch);

                for (float elapsed = 0; elapsed < fadeTime; elapsed += Time.deltaTime)
                {
                    token.ThrowIfCancellationRequested();
                    source.volume = Mathf.Lerp(0f, volume, elapsed / fadeTime);

                    await UniTask.Yield(cancellationToken: token);
                }
            }
            catch (System.OperationCanceledException) { }
            finally
            {
                if (source != null && !token.IsCancellationRequested)
                    source.volume = volume;
            }
        }

        public void Clear()
        {
            soundFadeCancel?.Cancel();
            soundFadeCancel?.Dispose();
            soundFadeCancel = null;

            foreach (var source in audioSources)
            {
                if (source == null)
                    continue;

                source.Stop();
                source.clip = null;
            }

            audioClips.Clear();
            resourceScope?.Dispose();
            resourceScope = null;
        }
    }
}
