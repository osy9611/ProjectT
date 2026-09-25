using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using System;
using System.Collections.Generic;
using System.Threading;
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
        private static readonly int SoundTypeCount = Enum.GetNames(typeof(eSound)).Length;
        private static readonly Action<AudioSource> resetSpatialSource = ResetSpatialSourceInternal;
        private static readonly Action<GameObject> returnSpatialObject = ReturnSpatialObjectInternal;

        private ResourceScope resourceScope;
        private ResourceScope spatialPoolScope;
        private GameObject spatialOriginal;

        private readonly AudioSource[] audioSources = new AudioSource[SoundTypeCount];
        private readonly Dictionary<string, AudioClip> audioClips = new Dictionary<string, AudioClip>();
        private readonly CancellationTokenSource[] fadeCancels = new CancellationTokenSource[SoundTypeCount];
        private readonly float[] categoryVolumes = new float[SoundTypeCount];
        private readonly bool[] categoryMuted = new bool[SoundTypeCount];
        private readonly float[] fadeGains = new float[SoundTypeCount];
        private readonly GameObject[] spatialObjects;
        private readonly AudioSource[] spatialSources;
        private readonly bool[] spatialInUse;
        private readonly float[] spatialVolumes;
        private readonly int[] spatialStartFrames;

        private float masterVolume = 1f;
        private bool masterMuted;
        private bool appPaused;

        public int SpatialVoiceCapacity => spatialObjects.Length;
        public int ActiveSpatialVoiceCount { get; private set; }

        public SoundManager(int spatialVoiceCapacity = 32)
        {
            if (spatialVoiceCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(spatialVoiceCapacity));

            spatialObjects = new GameObject[spatialVoiceCapacity];
            spatialSources = new AudioSource[spatialVoiceCapacity];
            spatialInUse = new bool[spatialVoiceCapacity];
            spatialVolumes = new float[spatialVoiceCapacity];
            spatialStartFrames = new int[spatialVoiceCapacity];
        }

        public AudioClip CurrentBgm
        {
            get
            {
                return TryGetSource(eSound.Bgm, out var source) ? source.clip : null;
            }
        }

        public float MasterVolume => masterVolume;
        public bool MasterMuted => masterMuted;

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            CreateRootObject("SoundManager");

            string[] soundNames = Enum.GetNames(typeof(eSound));

            for (int i = 0; i < SoundTypeCount; ++i)
            {
                GameObject go = new GameObject { name = soundNames[i] };
                audioSources[i] = go.AddComponent<AudioSource>();
                go.transform.parent = RootObject.transform;
                categoryVolumes[i] = 1f;
                fadeGains[i] = 1f;
            }

            audioSources[(int)eSound.Bgm].loop = true;

            CreateSpatialPoolInternal();

            ApplyAllVolumesInternal();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, this, manager => manager.ClearInternal());

            ResourceScope scope = spatialPoolScope;
            spatialPoolScope = null;
            if (scope != null)
                ErrorCollector.Run(ref errors, scope, target => target.Dispose());

            ErrorCollector.ThrowIfAny(errors);
        }

        private void CreateSpatialPoolInternal()
        {
            spatialOriginal = new GameObject("SpatialSound");
            spatialOriginal.transform.SetParent(RootObject, false);

            AudioSource source = spatialOriginal.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            source.loop = false;
            spatialOriginal.SetActive(false);

            spatialPoolScope = Global.Resource.CreateScope();
            Global.Pool.CreatePool(spatialOriginal, spatialObjects.Length, spatialPoolScope);
        }

        public override void OnUpdate(float dt)
        {
            if (appPaused || AudioListener.pause)
                return;

            for (int i = 0; i < spatialSources.Length; ++i)
            {
                AudioSource source = spatialSources[i];
                if (!spatialInUse[i] || spatialStartFrames[i] == Time.frameCount)
                    continue;

                if (source == null || source.clip == null)
                {
                    ReturnSpatialInternal(i);
                    continue;
                }

                AudioDataLoadState loadState = source.clip.loadState;
                if (loadState == AudioDataLoadState.Loading || loadState == AudioDataLoadState.Unloaded)
                    continue;

                if (!source.isPlaying || loadState == AudioDataLoadState.Failed)
                    ReturnSpatialInternal(i);
            }
        }

        public override void OnAppPause(bool paused)
        {
            appPaused = paused;
        }

        public bool PlayAt(string path, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f)
        {
            if (!CanPlaySpatialInternal(position, minDistance, maxDistance, volume, pitch))
                return false;

            ReleaseInvalidSpatialVoicesInternal();
            if (ActiveSpatialVoiceCount >= spatialObjects.Length)
                return false;

            AudioClip clip = GetOrAddAudioClip(path);
            return PlaySpatialInternal(clip, position, minDistance, maxDistance, volume, pitch);
        }

        public bool PlayAt(AudioClip clip, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f)
        {
            if (!CanPlaySpatialInternal(position, minDistance, maxDistance, volume, pitch) || clip == null)
                return false;

            ReleaseInvalidSpatialVoicesInternal();
            return PlaySpatialInternal(clip, position, minDistance, maxDistance, volume, pitch);
        }

        private bool PlaySpatialInternal(AudioClip clip, Vector3 position, float minDistance, float maxDistance, float volume, float pitch)
        {
            if (!CanStartPlayback() || clip == null || clip.loadState == AudioDataLoadState.Failed || ActiveSpatialVoiceCount >= spatialObjects.Length)
                return false;

            int index = -1;
            for (int i = 0; i < spatialInUse.Length; ++i)
            {
                if (!spatialInUse[i])
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                return false;

            GameObject instance = Global.Pool.Get(spatialOriginal, RootObject, spatialPoolScope);
            AudioSource source = null;
            try
            {
                source = instance.GetComponent<AudioSource>();
                if (source == null)
                    throw new InvalidOperationException("Spatial sound pool object has no AudioSource.");

                source.transform.position = position;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.pitch = pitch;
                source.loop = false;
                source.clip = clip;
                spatialObjects[index] = instance;
                spatialSources[index] = source;
                spatialVolumes[index] = Mathf.Clamp01(volume);
                spatialStartFrames[index] = Time.frameCount;
                spatialInUse[index] = true;
                ActiveSpatialVoiceCount++;
                ApplySpatialVolumeInternal(index);
                source.Play();
                return true;
            }
            catch (Exception error)
            {
                if (spatialInUse[index])
                {
                    spatialInUse[index] = false;
                    spatialObjects[index] = null;
                    spatialSources[index] = null;
                    spatialVolumes[index] = 0f;
                    ActiveSpatialVoiceCount--;
                }

                List<Exception> rollbackErrors = null;
                if (source != null)
                    ErrorCollector.Run(ref rollbackErrors, source, resetSpatialSource);

                if (instance != null)
                    ErrorCollector.Run(ref rollbackErrors, instance, returnSpatialObject);

                if (rollbackErrors != null)
                {
                    rollbackErrors.Insert(0, error);
                    throw new AggregateException(rollbackErrors);
                }

                throw;
            }
        }

        private void ReleaseInvalidSpatialVoicesInternal()
        {
            List<Exception> errors = null;
            for (int i = 0; i < spatialInUse.Length; ++i)
            {
                if (!spatialInUse[i])
                    continue;

                bool objectMissing = spatialObjects[i] == null;
                bool sourceMissing = spatialSources[i] == null;
                if (!objectMissing && !sourceMissing && spatialSources[i].clip != null)
                    continue;

                if (!objectMissing && !sourceMissing && spatialStartFrames[i] == Time.frameCount)
                    continue;

                ErrorCollector.Run(ref errors, i, ReturnSpatialInternal);
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private bool CanPlaySpatialInternal(Vector3 position, float minDistance, float maxDistance, float volume, float pitch)
        {
            return CanStartPlayback()
                && IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z)
                && IsFinite(minDistance) && IsFinite(maxDistance) && minDistance > 0f && maxDistance > minDistance
                && IsFinite(volume) && IsFinite(pitch) && pitch > 0f && pitch <= 3f;
        }

        public void StopAllSpatialSounds()
        {
            List<Exception> errors = null;
            for (int i = 0; i < spatialInUse.Length; ++i)
                ErrorCollector.Run(ref errors, i, ReturnSpatialInternal);

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ReturnSpatialInternal(int index)
        {
            if (!spatialInUse[index])
                return;

            GameObject instance = spatialObjects[index];
            AudioSource source = spatialSources[index];

            spatialInUse[index] = false;
            spatialObjects[index] = null;
            spatialSources[index] = null;
            spatialVolumes[index] = 0f;
            ActiveSpatialVoiceCount--;

            List<Exception> errors = null;
            if (source != null)
                ErrorCollector.Run(ref errors, source, resetSpatialSource);

            if (instance != null)
                ErrorCollector.Run(ref errors, instance, returnSpatialObject);

            ErrorCollector.ThrowIfAny(errors);
        }

        private static void ResetSpatialSourceInternal(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.pitch = 1f;
            source.volume = 0f;
        }

        private static void ReturnSpatialObjectInternal(GameObject instance)
        {
            if (!Global.Pool.Return(instance))
                throw new InvalidOperationException("Spatial sound object is not owned by PoolManager.");
        }

        private void ApplySpatialVolumeInternal(int index)
        {
            AudioSource source = spatialSources[index];
            if (source == null)
                return;

            source.volume = masterMuted || categoryMuted[(int)eSound.FX]
                ? 0f
                : masterVolume * categoryVolumes[(int)eSound.FX] * spatialVolumes[index];
        }

        private bool CanStartPlayback()
        {
            return State == ManagerState.Ready && !LifetimeToken.IsCancellationRequested;
        }

        private bool TryGetSource(eSound type, out AudioSource source)
        {
            int index = (int)type;
            if (index < 0 || index >= audioSources.Length)
            {
                source = null;
                return false;
            }

            source = audioSources[index];
            return source != null;
        }

        private AudioClip GetOrAddAudioClip(string path)
        {
            if (!CanStartPlayback() || string.IsNullOrWhiteSpace(path))
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
            if (!CanStartPlayback() || !TryGetSource(type, out _))
                return;

            AudioClip clip = GetOrAddAudioClip(path);
            Play(clip, type, pitch);
        }

        public void Play(AudioClip clip, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (type == eSound.Bgm)
                PlayBgm(clip, pitch);
            else
                PlayOneShot(clip, type, pitch);
        }

        public void PlayBgm(string path, float pitch = 1.0f, bool restart = false)
        {
            if (!CanStartPlayback())
                return;

            AudioClip clip = GetOrAddAudioClip(path);
            PlayBgm(clip, pitch, restart);
        }

        public void PlayBgm(AudioClip clip, float pitch = 1.0f, bool restart = false)
        {
            if (!CanStartPlayback() || clip == null || !TryGetSource(eSound.Bgm, out var source))
                return;

            CancelFadeInternal(eSound.Bgm, true);
            PlayBgmInternal(source, clip, pitch, restart);
        }

        public void StopBgm()
        {
            if (!TryGetSource(eSound.Bgm, out var source))
                return;

            CancelFadeInternal(eSound.Bgm, true);
            source.Stop();
            source.clip = null;
        }

        public void PlayOneShot(string path, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (!CanStartPlayback() || !TryGetOneShotSource(type, out _))
                return;

            AudioClip clip = GetOrAddAudioClip(path);
            PlayOneShot(clip, type, pitch);
        }

        public void PlayOneShot(AudioClip clip, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (!CanStartPlayback() || clip == null || !TryGetOneShotSource(type, out var source))
                return;

            CancelFadeInternal(type, true);
            PlayOneShotInternal(source, clip, pitch);
        }

        private bool TryGetOneShotSource(eSound type, out AudioSource source)
        {
            if (type == eSound.Bgm)
            {
                source = null;
                return false;
            }

            return TryGetSource(type, out source);
        }

        private static float NormalizePitch(float pitch)
        {
            return float.IsNaN(pitch) || float.IsInfinity(pitch) ? 1f : pitch;
        }

        private void PlayBgmInternal(AudioSource source, AudioClip clip, float pitch, bool restart)
        {
            pitch = NormalizePitch(pitch);

            if (!restart && source.clip == clip && source.isPlaying)
            {
                source.pitch = pitch;
                return;
            }

            source.Stop();
            source.pitch = pitch;
            source.clip = clip;
            source.Play();
        }

        private static void PlayOneShotInternal(AudioSource source, AudioClip clip, float pitch)
        {
            // FX와 UI는 채널별 AudioSource를 공유하므로 겹치는 재생의 pitch를 개별 제어할 수 없다.
            source.pitch = NormalizePitch(pitch);
            source.PlayOneShot(clip);
        }

        public void PlayFade(string path, eSound type = eSound.FX, float fadeTime = 1.0f, float pitch = 1.0f)
        {
            if (!CanStartPlayback() || !TryGetSource(type, out _) || !IsFinite(fadeTime))
                return;

            AudioClip clip = GetOrAddAudioClip(path);
            if (!CanStartPlayback() || clip == null)
                return;

            if (fadeTime <= 0f)
            {
                CancelFadeInternal(type, true);
                PlayInternal(clip, type, pitch, false);
                return;
            }

            CancelFadeInternal(type, false);
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken);
            fadeCancels[(int)type] = cancel;
            ExecuteSoundFadeAsync(clip, type, fadeTime, pitch, cancel).Forget(Global.LogException);
        }

        private async UniTask ExecuteSoundFadeAsync(AudioClip clip, eSound type, float fadeTime, float pitch, CancellationTokenSource owner)
        {
            AudioSource source = audioSources[(int)type];
            CancellationToken token = owner.Token;

            try
            {
                float startGain = fadeGains[(int)type];

                if (source.isPlaying)
                    await FadeGainInternalAsync(type, startGain, 0f, fadeTime, token);

                token.ThrowIfCancellationRequested();
                SetFadeGainInternal(type, 0f);
                PlayInternal(clip, type, pitch, true);
                await FadeGainInternalAsync(type, 0f, 1f, fadeTime, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            finally
            {
                int index = (int)type;
                if (ReferenceEquals(fadeCancels[index], owner))
                {
                    fadeCancels[index] = null;
                    SetFadeGainInternal(type, 1f);
                }

                owner.Dispose();
            }
        }

        private async UniTask FadeGainInternalAsync(eSound type, float from, float to, float duration, CancellationToken token)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                token.ThrowIfCancellationRequested();

                SetFadeGainInternal(type, Mathf.Lerp(from, to, elapsed / duration));

                await UniTask.Yield(PlayerLoopTiming.Update, token);

                elapsed += Time.unscaledDeltaTime;
            }

            token.ThrowIfCancellationRequested();
            SetFadeGainInternal(type, to);
        }

        private void PlayInternal(AudioClip clip, eSound type, float pitch, bool restartBgm)
        {
            AudioSource source = audioSources[(int)type];
            if (type == eSound.Bgm)
                PlayBgmInternal(source, clip, pitch, restartBgm);
            else
                PlayOneShotInternal(source, clip, pitch);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void CancelFadeInternal(eSound type, bool restoreGain)
        {
            int index = (int)type;
            CancellationTokenSource cancel = fadeCancels[index];
            fadeCancels[index] = null;

            if (cancel != null)
                cancel.Cancel();

            if (restoreGain)
                SetFadeGainInternal(type, 1f);
        }

        public void SetMasterVolume(float volume)
        {
            if (float.IsNaN(volume))
                return;

            masterVolume = Mathf.Clamp01(volume);
            ApplyAllVolumesInternal();
        }

        public void SetMasterMuted(bool muted)
        {
            masterMuted = muted;
            ApplyAllVolumesInternal();
        }

        public float GetCategoryVolume(eSound type)
        {
            return TryGetSource(type, out _) ? categoryVolumes[(int)type] : 0f;
        }

        public bool GetCategoryMuted(eSound type)
        {
            return TryGetSource(type, out _) && categoryMuted[(int)type];
        }

        public void SetCategoryVolume(eSound type, float volume)
        {
            if (!TryGetSource(type, out _) || float.IsNaN(volume))
                return;

            categoryVolumes[(int)type] = Mathf.Clamp01(volume);
            ApplyVolumeInternal(type);
            if (type == eSound.FX)
                ApplyAllSpatialVolumesInternal();
        }

        public void SetCategoryMuted(eSound type, bool muted)
        {
            if (!TryGetSource(type, out _))
                return;

            categoryMuted[(int)type] = muted;
            ApplyVolumeInternal(type);
            if (type == eSound.FX)
                ApplyAllSpatialVolumesInternal();
        }

        private void SetFadeGainInternal(eSound type, float gain)
        {
            fadeGains[(int)type] = Mathf.Clamp01(gain);
            ApplyVolumeInternal(type);
        }

        private void ApplyAllVolumesInternal()
        {
            for (int i = 0; i < audioSources.Length; ++i)
                ApplyVolumeInternal((eSound)i);

            ApplyAllSpatialVolumesInternal();
        }

        private void ApplyAllSpatialVolumesInternal()
        {
            for (int i = 0; i < spatialSources.Length; ++i)
            {
                if (spatialInUse[i])
                    ApplySpatialVolumeInternal(i);
            }
        }

        private void ApplyVolumeInternal(eSound type)
        {
            int index = (int)type;
            AudioSource source = audioSources[index];
            if (source == null)
                return;

            bool muted = masterMuted || categoryMuted[index];
            source.volume = muted ? 0f : masterVolume * categoryVolumes[index] * fadeGains[index];
        }

        public void Clear()
        {
            ClearInternal();
        }

        private void ClearInternal()
        {
            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, this, manager => manager.StopAllSpatialSounds());

            for (int i = 0; i < audioSources.Length; ++i)
                ErrorCollector.Run(ref errors, i, ClearSourceInternal);

            audioClips.Clear();
            ResourceScope scope = resourceScope;
            resourceScope = null;
            if (scope != null)
                ErrorCollector.Run(ref errors, scope, target => target.Dispose());

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ClearSourceInternal(int index)
        {
            CancelFadeInternal((eSound)index, true);

            AudioSource source = audioSources[index];
            if (source == null)
                return;

            source.Stop();
            source.clip = null;
        }
    }
}
