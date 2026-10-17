using Cysharp.Threading.Tasks;
using ProjectT.Addressable;
using ProjectT.Pool;
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

    public readonly struct SpatialSoundHandle
    {
        internal readonly long ManagerId;
        internal readonly int Slot;
        internal readonly long Generation;

        internal SpatialSoundHandle(long managerId, int slot, long generation)
        {
            ManagerId = managerId;
            Slot = slot;
            Generation = generation;
        }
    }

    public class SoundManager : ManagerBase
    {
        private struct SpatialSound
        {
            public GameObject Instance;
            public AudioSource Source;
            public PooledObject PooledObject;
            public bool InUse;
            public bool AutoRelease;
            public bool Paused;
            public bool Finished;
            public long Generation;
            public float Volume;
            public int StartFrame;
        }

        private const float SpatialVoiceCheckInterval = 0.1f;
        private const float MaxPitch = 3f;
        private static long nextSpatialManagerId;
        private static readonly int SoundTypeCount = Enum.GetNames(typeof(eSound)).Length;

        private ResourceScope bgmScope;
        private ResourceScope spatialPoolScope;
        private GameObject spatialOriginal;

        private readonly AudioSource[] audioSources = new AudioSource[SoundTypeCount];
        private readonly CancellationTokenSource[] fadeCancels = new CancellationTokenSource[SoundTypeCount];
        private readonly float[] categoryVolumes = new float[SoundTypeCount];
        private readonly bool[] categoryMuted = new bool[SoundTypeCount];
        private readonly float[] fadeGains = new float[SoundTypeCount];
        private readonly SpatialSound[] spatialSounds;
        private readonly long spatialManagerId = Interlocked.Increment(ref nextSpatialManagerId);

        private float masterVolume = 1f;
        private bool masterMuted;
        private bool appPaused;
        private float spatialVoiceCheckElapsed;

        public int SpatialVoiceCapacity => spatialSounds.Length;
        public int ActiveSpatialVoiceCount { get; private set; }

        public SoundManager(int spatialVoiceCapacity = 32)
        {
            if (spatialVoiceCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(spatialVoiceCapacity));

            spatialSounds = new SpatialSound[spatialVoiceCapacity];
        }

        public AudioClip CurrentBgm
        {
            get
            {
                return audioSources[(int)eSound.Bgm].clip;
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
            ErrorCollector.Run(ref errors, this, manager => manager.Clear());

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
            Global.Pool.CreatePool(spatialOriginal, spatialSounds.Length, spatialPoolScope);
        }

        public override void OnUpdate(float dt)
        {
            if (ActiveSpatialVoiceCount == 0 || appPaused || AudioListener.pause)
                return;

            spatialVoiceCheckElapsed += Time.unscaledDeltaTime;
            if (spatialVoiceCheckElapsed < SpatialVoiceCheckInterval)
                return;

            spatialVoiceCheckElapsed = 0f;
            ReleaseCompletedSpatialVoicesInternal();
        }

        private void ReleaseCompletedSpatialVoicesInternal()
        {
            if (appPaused || AudioListener.pause)
                return;

            for (int i = 0; i < spatialSounds.Length; ++i)
            {
                ref SpatialSound voice = ref spatialSounds[i];
                if (!voice.InUse || voice.StartFrame == Time.frameCount)
                    continue;

                if (!IsSpatialVoiceUsableInternal(i))
                    ReturnSpatialInternal(i);
                else if (!voice.Paused && !voice.Finished && IsSpatialPlaybackEndedInternal(i))
                    FinishSpatialVoiceInternal(i);
            }
        }

        private void FinishSpatialVoiceInternal(int index)
        {
            if (spatialSounds[index].AutoRelease)
                ReturnSpatialInternal(index);
            else
                spatialSounds[index].Finished = true;
        }

        public override void OnAppPause(bool paused)
        {
            appPaused = paused;
        }

        public bool PlayAt(string path, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f)
        {
            return SpawnAt(path, position, minDistance, maxDistance, volume, pitch).Generation != 0;
        }

        public bool PlayAt(AudioClip clip, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f)
        {
            return SpawnAt(clip, position, minDistance, maxDistance, volume, pitch).Generation != 0;
        }

        public SpatialSoundHandle SpawnAt(string path, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f, bool loop = false, bool autoRelease = true)
        {
            if (!CanAcceptWorkInternal())
                return default;

            ValidateSpatialInternal(position, minDistance, maxDistance, pitch);
            volume = ToVolumeInternal(volume);
            ReleaseSpatialVoicesBeforePlaybackInternal();
            if (ActiveSpatialVoiceCount >= spatialSounds.Length)
                return default;

            AudioClip clip = LoadClipInternal(path, eSound.FX);
            return SpawnSpatialInternal(clip, position, minDistance, maxDistance, volume, pitch, loop, autoRelease);
        }

        public SpatialSoundHandle SpawnAt(AudioClip clip, Vector3 position, float minDistance = 1f, float maxDistance = 30f, float volume = 1f, float pitch = 1f, bool loop = false, bool autoRelease = true)
        {
            if (!CanAcceptWorkInternal())
                return default;

            ValidateSpatialInternal(position, minDistance, maxDistance, pitch);
            volume = ToVolumeInternal(volume);
            if (clip == null)
                return default;

            ReleaseSpatialVoicesBeforePlaybackInternal();
            return SpawnSpatialInternal(clip, position, minDistance, maxDistance, volume, pitch, loop, autoRelease);
        }

        private SpatialSoundHandle SpawnSpatialInternal(AudioClip clip, Vector3 position, float minDistance, float maxDistance, float volume, float pitch, bool loop, bool autoRelease)
        {
            if (!CanAcceptWorkInternal() || clip == null || clip.loadState == AudioDataLoadState.Failed)
                return default;

            int index = -1;
            for (int i = 0; i < spatialSounds.Length; ++i)
            {
                if (!spatialSounds[i].InUse)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                return default;

            GameObject instance = Global.Pool.Get(spatialOriginal, RootObject, spatialPoolScope);
            AudioSource source = instance.GetComponent<AudioSource>();
            source.transform.position = position;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.pitch = pitch;
            source.loop = loop;
            source.clip = clip;

            long generation = spatialSounds[index].Generation + 1;
            spatialSounds[index] = new SpatialSound
            {
                Instance = instance,
                Source = source,
                PooledObject = instance.GetComponent<PooledObject>(),
                InUse = true,
                AutoRelease = autoRelease,
                Generation = generation,
                Volume = volume,
                StartFrame = Time.frameCount
            };

            ActiveSpatialVoiceCount++;
            ApplySpatialVolumeInternal(index);
            source.Play();
            return new SpatialSoundHandle(spatialManagerId, index, generation);
        }

        private void ReleaseSpatialVoicesBeforePlaybackInternal()
        {
            for (int i = 0; i < spatialSounds.Length; ++i)
            {
                ref SpatialSound voice = ref spatialSounds[i];
                if (!voice.InUse)
                    continue;

                bool broken = !IsSpatialObjectIntactInternal(i) || voice.Source == null;
                if (broken || (voice.Source.clip == null && voice.StartFrame != Time.frameCount))
                    ReturnSpatialInternal(i);
            }

            if (ActiveSpatialVoiceCount >= spatialSounds.Length)
                ReleaseCompletedSpatialVoicesInternal();
        }

        private bool IsSpatialVoiceUsableInternal(int index)
        {
            AudioSource source = spatialSounds[index].Source;
            return IsSpatialObjectIntactInternal(index) && source != null && source.clip != null
                && source.clip.loadState != AudioDataLoadState.Failed;
        }

        private bool IsSpatialObjectIntactInternal(int index)
        {
            GameObject instance = spatialSounds[index].Instance;
            if (instance == null || !instance.activeSelf)
                return false;

            PooledObject item = spatialSounds[index].PooledObject;
            return item != null && item.Owner != null;
        }

        private bool IsSpatialPlaybackEndedInternal(int index)
        {
            ref SpatialSound voice = ref spatialSounds[index];
            if (appPaused || AudioListener.pause || voice.StartFrame == Time.frameCount)
                return false;

            AudioDataLoadState loadState = voice.Source.clip.loadState;
            if (loadState == AudioDataLoadState.Loading || loadState == AudioDataLoadState.Unloaded)
                return false;

            return !voice.Source.isPlaying;
        }

        private bool IsSpatialHandleCurrentInternal(SpatialSoundHandle handle)
        {
            return State == ManagerState.Ready && handle.ManagerId == spatialManagerId
                && handle.Generation != 0 && handle.Slot >= 0 && handle.Slot < spatialSounds.Length
                && spatialSounds[handle.Slot].InUse && spatialSounds[handle.Slot].Generation == handle.Generation;
        }

        private bool TryGetSpatialSlotInternal(SpatialSoundHandle handle, out int index)
        {
            index = -1;
            if (!IsSpatialHandleCurrentInternal(handle))
                return false;

            index = handle.Slot;
            if (IsSpatialVoiceUsableInternal(index))
                return true;

            ReturnSpatialInternal(index);
            index = -1;
            return false;
        }

        public bool IsValid(SpatialSoundHandle handle)
        {
            return IsSpatialHandleCurrentInternal(handle) && IsSpatialVoiceUsableInternal(handle.Slot);
        }

        public bool Pause(SpatialSoundHandle handle)
        {
            if (!CanAcceptWorkInternal() || !TryGetSpatialSlotInternal(handle, out int index))
                return false;

            ref SpatialSound voice = ref spatialSounds[index];
            if (voice.Paused || voice.Finished)
                return false;

            if (IsSpatialPlaybackEndedInternal(index))
            {
                FinishSpatialVoiceInternal(index);
                return false;
            }

            voice.Paused = true;
            voice.Source.Pause();
            return true;
        }

        public bool Resume(SpatialSoundHandle handle)
        {
            if (!CanAcceptWorkInternal() || !TryGetSpatialSlotInternal(handle, out int index) || !spatialSounds[index].Paused)
                return false;

            ref SpatialSound voice = ref spatialSounds[index];
            voice.Paused = false;
            voice.StartFrame = Time.frameCount;
            voice.Source.UnPause();
            return true;
        }

        public bool SetLoop(SpatialSoundHandle handle, bool loop)
        {
            if (!CanAcceptWorkInternal() || !TryGetSpatialSlotInternal(handle, out int index))
                return false;

            spatialSounds[index].Source.loop = loop;
            return true;
        }

        public bool Stop(SpatialSoundHandle handle)
        {
            if (!TryGetSpatialSlotInternal(handle, out int index))
                return false;

            ref SpatialSound voice = ref spatialSounds[index];
            if (voice.AutoRelease)
                ReturnSpatialInternal(index);
            else if (!voice.Finished)
            {
                voice.Paused = false;
                voice.Finished = true;
                voice.Source.Stop();
            }

            return true;
        }

        public bool Release(SpatialSoundHandle handle)
        {
            if (!TryGetSpatialSlotInternal(handle, out int index))
                return false;

            ReturnSpatialInternal(index);
            return true;
        }

        public bool Restart(SpatialSoundHandle handle)
        {
            if (!CanAcceptWorkInternal() || !TryGetSpatialSlotInternal(handle, out int index))
                return false;

            ref SpatialSound voice = ref spatialSounds[index];
            voice.Paused = false;
            voice.Finished = false;
            voice.StartFrame = Time.frameCount;
            voice.Source.Stop();
            voice.Source.Play();
            return true;
        }

        private static void ValidateSpatialInternal(Vector3 position, float minDistance, float maxDistance, float pitch)
        {
            ValidateFiniteInternal(position.x, nameof(position));
            ValidateFiniteInternal(position.y, nameof(position));
            ValidateFiniteInternal(position.z, nameof(position));
            ValidateFiniteInternal(minDistance, nameof(minDistance));
            ValidateFiniteInternal(maxDistance, nameof(maxDistance));
            if (minDistance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(minDistance), minDistance, "Minimum distance must be positive.");
            if (maxDistance <= minDistance)
                throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "Maximum distance must be greater than the minimum distance.");

            ValidatePitchInternal(pitch);
        }

        public void StopAllSpatialSounds()
        {
            List<Exception> errors = null;
            for (int i = 0; i < spatialSounds.Length; ++i)
                ErrorCollector.Run(ref errors, i, ReturnSpatialInternal);

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ReturnSpatialInternal(int index)
        {
            SpatialSound voice = spatialSounds[index];
            if (!voice.InUse)
                return;

            spatialSounds[index] = new SpatialSound { Generation = voice.Generation };
            ActiveSpatialVoiceCount--;
            if (ActiveSpatialVoiceCount == 0)
                spatialVoiceCheckElapsed = 0f;

            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
                voice.Source.pitch = 1f;
                voice.Source.loop = false;
                voice.Source.volume = 0f;
            }

            // 외부에서 풀을 비우면 Owner가 해제된 객체가 파괴 전까지 남아 있으므로 반환하지 않는다.
            if (voice.PooledObject != null && voice.PooledObject.Owner != null)
                Global.Pool.Return(voice.Instance);
        }

        private void ApplySpatialVolumeInternal(int index)
        {
            AudioSource source = spatialSounds[index].Source;
            if (source == null)
                return;

            source.volume = GetCategoryGainInternal(eSound.FX) * spatialSounds[index].Volume;
        }

        // BGM은 씬 전환 뒤에도 이어서 재생되므로 사운드 전용 Scope에 두고, 나머지 채널은 현재 씬 Scope에 둔다.
        private AudioClip LoadClipInternal(string path, eSound type, ResourceScope scope = null)
        {
            if (!CanAcceptWorkInternal() || string.IsNullOrWhiteSpace(path))
                return null;

            if (type != eSound.Bgm)
                return Global.Resource.LoadAndGet<AudioClip>(path, scope: scope);

            bgmScope = bgmScope ?? Global.Resource.CreateScope();
            return Global.Resource.LoadAndGet<AudioClip>(path, scope: bgmScope);
        }

        public void Play(string path, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (!CanAcceptWorkInternal())
                return;

            AudioClip clip = LoadClipInternal(path, type);
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
            if (!CanAcceptWorkInternal())
                return;

            AudioClip clip = LoadClipInternal(path, eSound.Bgm);
            PlayBgm(clip, pitch, restart);
        }

        public void PlayBgm(AudioClip clip, float pitch = 1.0f, bool restart = false)
        {
            if (!CanAcceptWorkInternal())
                return;

            ValidatePitchInternal(pitch);
            if (clip == null)
                return;

            CancelFadeInternal(eSound.Bgm, true);
            PlayBgmInternal(audioSources[(int)eSound.Bgm], clip, pitch, restart);
        }

        public void StopBgm()
        {
            AudioSource source = audioSources[(int)eSound.Bgm];
            CancelFadeInternal(eSound.Bgm, true);
            source.Stop();
            source.clip = null;
        }

        public void PlayOneShot(string path, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (!CanAcceptWorkInternal() || type == eSound.Bgm)
                return;

            AudioClip clip = LoadClipInternal(path, type);
            PlayOneShot(clip, type, pitch);
        }

        public void PlayOneShot(AudioClip clip, eSound type = eSound.FX, float pitch = 1.0f)
        {
            if (!CanAcceptWorkInternal())
                return;

            ValidatePitchInternal(pitch);
            if (clip == null || type == eSound.Bgm)
                return;

            CancelFadeInternal(type, true);
            PlayOneShotInternal(audioSources[(int)type], clip, pitch);
        }

        private void PlayBgmInternal(AudioSource source, AudioClip clip, float pitch, bool restart)
        {
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
            source.pitch = pitch;
            source.PlayOneShot(clip);
        }

        public void PlayFade(string path, eSound type = eSound.FX, float fadeTime = 1.0f, float pitch = 1.0f)
        {
            if (!CanAcceptWorkInternal())
                return;

            ValidateFiniteInternal(fadeTime, nameof(fadeTime));
            ValidatePitchInternal(pitch);

            ResourceScope sceneScope = type == eSound.Bgm ? null : Global.Resource.SceneScope;
            AudioClip clip = LoadClipInternal(path, type, sceneScope);
            if (!CanAcceptWorkInternal() || clip == null || sceneScope != null && sceneScope.Token.IsCancellationRequested)
                return;

            if (fadeTime <= 0f)
            {
                CancelFadeInternal(type, true);
                PlayInternal(clip, type, pitch, false);
                return;
            }

            CancelFadeInternal(type, false);
            var cancel = sceneScope == null
                ? CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken)
                : CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, sceneScope.Token);
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

                // BGM 외 채널의 클립은 씬 Scope 소유이므로 페이드아웃 중 씬 전환으로 해제될 수 있다.
                if (clip == null)
                    return;

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

        private static void ValidateFiniteInternal(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name, value, "Value must be finite.");
        }

        private static void ValidatePitchInternal(float pitch)
        {
            if (!(pitch > 0f && pitch <= MaxPitch))
                throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "Pitch must be greater than 0 and at most 3.");
        }

        private static float ToVolumeInternal(float volume)
        {
            ValidateFiniteInternal(volume, nameof(volume));
            return Mathf.Clamp01(volume);
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
            masterVolume = ToVolumeInternal(volume);
            ApplyAllVolumesInternal();
        }

        public void SetMasterMuted(bool muted)
        {
            masterMuted = muted;
            ApplyAllVolumesInternal();
        }

        public float GetCategoryVolume(eSound type)
        {
            return categoryVolumes[(int)type];
        }

        public bool GetCategoryMuted(eSound type)
        {
            return categoryMuted[(int)type];
        }

        public void SetCategoryVolume(eSound type, float volume)
        {
            categoryVolumes[(int)type] = ToVolumeInternal(volume);
            ApplyVolumeInternal(type);
            if (type == eSound.FX)
                ApplyAllSpatialVolumesInternal();
        }

        public void SetCategoryMuted(eSound type, bool muted)
        {
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
            for (int i = 0; i < spatialSounds.Length; ++i)
            {
                if (spatialSounds[i].InUse)
                    ApplySpatialVolumeInternal(i);
            }
        }

        private void ApplyVolumeInternal(eSound type)
        {
            int index = (int)type;
            audioSources[index].volume = GetCategoryGainInternal(type) * fadeGains[index];
        }

        private float GetCategoryGainInternal(eSound type)
        {
            int index = (int)type;
            return masterMuted || categoryMuted[index] ? 0f : masterVolume * categoryVolumes[index];
        }

        public void Clear()
        {
            List<Exception> errors = null;
            ErrorCollector.Run(ref errors, this, manager => manager.StopAllSpatialSounds());

            for (int i = 0; i < audioSources.Length; ++i)
                ErrorCollector.Run(ref errors, i, ClearSourceInternal);

            ResourceScope scope = bgmScope;
            bgmScope = null;
            if (scope != null)
                ErrorCollector.Run(ref errors, scope, target => target.Dispose());

            ErrorCollector.ThrowIfAny(errors);
        }

        private void ClearSourceInternal(int index)
        {
            CancelFadeInternal((eSound)index, true);

            AudioSource source = audioSources[index];
            source.Stop();
            source.clip = null;
        }
    }
}
