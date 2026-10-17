using ProjectT;
using ProjectT.Pool;
using UnityEngine;

internal static partial class Program
{
    private static void SpawnHandleAutoRelease()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        var position = new Vector3(3f, 4f, 5f);
        var path = setup.manager.SpawnAt("A", position, 2f, 20f, 0.6f, 1.5f);
        var source = ActiveSpatialSource(setup.manager);
        Check(setup.manager.IsValid(path) && setup.manager.ActiveSpatialVoiceCount == 1, "Path spawn did not return a live handle.");
        Check(source.clip.name == "A" && PositionEquals(source.transform.position, 3f, 4f, 5f)
            && Approximately(source.minDistance, 2f) && Approximately(source.maxDistance, 20f)
            && Approximately(source.volume, 0.6f) && Approximately(source.pitch, 1.5f), "Path spawn omitted playback settings.");
        source.isPlaying = false;
        Time.frameCount++;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(path) && setup.manager.ActiveSpatialVoiceCount == 0, "Default path voice did not auto release.");

        var clip = new AudioClip("clip-spawn");
        var direct = setup.manager.SpawnAt(clip, new Vector3());
        Check(setup.manager.IsValid(direct) && ReferenceEquals(source, ActiveSpatialSource(setup.manager)), "Clip spawn did not reuse the released source.");
        Check(setup.manager.Stop(direct) && !setup.manager.IsValid(direct) && source.clip == null, "Default clip voice did not return on Stop.");
        Check(!setup.manager.IsValid(default) && !setup.manager.Stop(default) && !setup.manager.Release(default), "Default handle gained ownership.");
    }

    private static void RetainedHandleLifecycle()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        var retained = setup.manager.SpawnAt(new AudioClip("retained"), new Vector3(), autoRelease: false);
        var source = ActiveSpatialSource(setup.manager);
        var owner = source.GetComponent<PooledObject>().Owner;
        source.isPlaying = false;
        Time.frameCount++;
        AdvanceSpatialPolling(setup.manager);
        var reads = source.IsPlayingReadCount;
        Check(reads == 1, "Retained completion was not inspected at the polling interval.");
        for (var i = 0; i < 4; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(retained) && setup.manager.ActiveSpatialVoiceCount == 1 && owner.ActiveCount == 1
            && source.IsPlayingReadCount == reads, "Finished retained voice lost ownership or kept polling playback.");
        var loads = setup.resources.LoadCount;
        Check(!setup.manager.IsValid(setup.manager.SpawnAt("B", new Vector3())) && setup.resources.LoadCount == loads,
            "Retained completion did not block capacity before loading.");
        Check(setup.manager.Stop(retained) && setup.manager.Stop(retained), "Stopping a finished retained voice was not idempotent.");
        var starts = source.PlayCount;
        Check(setup.manager.Restart(retained) && source.PlayCount == starts + 1 && source.isPlaying
            && ReferenceEquals(source, ActiveSpatialSource(setup.manager)), "Restart did not reuse the retained source.");
        Check(setup.manager.Stop(retained) && setup.manager.IsValid(retained) && !source.isPlaying, "Stop released a retained voice.");
        reads = source.IsPlayingReadCount;
        for (var i = 0; i < 4; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == reads, "Stopped retained voice kept polling playback.");
        Check(setup.manager.Release(retained) && !setup.manager.IsValid(retained) && owner.InactiveCount == 1,
            "Explicit release did not return the retained pool lease.");
        Check(setup.manager.IsValid(setup.manager.SpawnAt("B", new Vector3())) && setup.resources.LoadCount == loads + 1,
            "Capacity did not recover after retained release.");
    }

    private static void HandlePauseResumeGuards()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(2);
        var own = setup.manager.SpawnAt(new AudioClip("own-pause"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        Check(setup.manager.Pause(own) && !source.isPlaying && !setup.manager.Pause(own) && source.PauseCount == 1,
            "Own pause did not stop visible playback exactly once.");
        Time.frameCount++;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(own), "Own pause was mistaken for completion.");
        var reads = source.IsPlayingReadCount;
        for (var i = 0; i < 3; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == reads, "Own pause kept polling source.isPlaying.");
        AudioListener.pause = true;
        setup.manager.OnAppPause(true);
        Check(setup.manager.Resume(own) && source.isPlaying && !setup.manager.Resume(own) && source.UnPauseCount == 1,
            "Resume did not remove only the own pause.");
        source.isPlaying = false;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(own), "App or listener pause permitted completion cleanup.");
        AudioListener.pause = false;
        setup.manager.OnAppPause(false);
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(own), "Completion was not collected after global pauses ended.");

        var loading = new AudioClip("loading-pause") { loadState = AudioDataLoadState.Loading };
        var waiting = setup.manager.SpawnAt(loading, new Vector3());
        var waitingSource = ActiveSpatialSource(setup.manager);
        waitingSource.isPlaying = false;
        Time.frameCount++;
        Check(setup.manager.Pause(waiting), "Loading voice could not be paused.");
        loading.loadState = AudioDataLoadState.Loaded;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(waiting), "Paused loading voice was reclaimed when loading completed.");
        Check(setup.manager.Resume(waiting), "Paused loading voice could not resume.");
        setup.manager.OnUpdate(Time.deltaTime);
        Check(setup.manager.IsValid(waiting), "Resume lost its creation-frame guard.");
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(waiting), "Stopped loading voice survived its resumed frame guard.");
    }

    private static void PauseCompletedHandle()
    {
        var setup = Setup(1);
        var handle = setup.manager.SpawnAt(new AudioClip("completed-before-pause"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        Time.frameCount++;
        Check(!setup.manager.Pause(handle) && !setup.manager.IsValid(handle) && setup.manager.ActiveSpatialVoiceCount == 0,
            "Pausing a completed voice retained an automatic lease.");
        Check(setup.manager.IsValid(setup.manager.SpawnAt(new AudioClip("after-pause"), new Vector3())),
            "Completed voice blocked the next spawn.");
    }

    private static void HandleLoopPolicy()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        var handle = setup.manager.SpawnAt(new AudioClip("loop"), new Vector3(), loop: true, autoRelease: false);
        var source = ActiveSpatialSource(setup.manager);
        Check(source.loop && setup.manager.IsValid(handle), "Initial loop setting was ignored.");
        Time.frameCount++;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(handle), "Playing loop was reclaimed.");
        Check(setup.manager.SetLoop(handle, false) && !source.loop, "Loop could not be disabled.");
        source.isPlaying = false;
        AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 2, "Loop completion was not inspected after disabling repeat.");
        Check(setup.manager.IsValid(handle) && setup.manager.ActiveSpatialVoiceCount == 1, "Completed loop toggle did not retain its lease.");
        Check(setup.manager.SetLoop(handle, true) && setup.manager.Restart(handle) && source.loop && source.isPlaying,
            "Completed retained voice could not change loop and restart.");
        Check(setup.manager.Release(handle) && !setup.manager.SetLoop(handle, false), "Released voice accepted a loop change.");
        var next = setup.manager.SpawnAt(new AudioClip("next"), new Vector3());
        Check(setup.manager.IsValid(next) && !source.loop, "Reused source inherited the previous loop setting.");
        Check(setup.manager.SetLoop(next, true) && source.loop, "Automatic voice could not enable looping.");
        Check(setup.manager.SetLoop(next, false) && !source.loop, "Automatic voice could not disable looping.");
        source.isPlaying = false;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(next) && setup.manager.ActiveSpatialVoiceCount == 0,
            "Automatic voice was not returned after looping was disabled and playback ended.");
        var stoppedLoop = setup.manager.SpawnAt(new AudioClip("stopped-loop"), new Vector3(), loop: true);
        Check(setup.manager.IsValid(stoppedLoop) && source.loop && setup.manager.Stop(stoppedLoop)
            && !setup.manager.IsValid(stoppedLoop) && !source.loop,
            "Stopping a looping automatic voice did not return and reset it.");
        var interruptedLoop = setup.manager.SpawnAt(new AudioClip("interrupted-loop"), new Vector3(), loop: true);
        source.isPlaying = false;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(interruptedLoop) && setup.manager.ActiveSpatialVoiceCount == 0,
            "A stopped looping source was skipped by completion inspection.");
    }

    private static void HandleInvalidation()
    {
        var setup = Setup(1);
        var old = setup.manager.SpawnAt(new AudioClip("old"), new Vector3(), autoRelease: false);
        Check(setup.manager.Release(old) && !setup.manager.IsValid(old), "Released handle stayed valid.");
        var current = setup.manager.SpawnAt(new AudioClip("current"), new Vector3(), autoRelease: false);
        Check(setup.manager.IsValid(current) && !setup.manager.Pause(old) && !setup.manager.Restart(old)
            && !setup.manager.Stop(old) && !setup.manager.Release(old) && !setup.manager.SetLoop(old, true),
            "Old generation controlled a reused source.");
        setup.manager.Clear();
        Check(!setup.manager.IsValid(current) && !setup.manager.Restart(current) && !setup.manager.Release(current),
            "Clear left a handle active.");
        var afterClear = setup.manager.SpawnAt(new AudioClip("after-clear"), new Vector3());
        Check(setup.manager.IsValid(afterClear), "Clear prevented new leases.");
        setup.manager.Shutdown(ShutdownReason.Normal);
        Check(!setup.manager.IsValid(afterClear) && !setup.manager.Stop(afterClear)
            && !setup.manager.IsValid(setup.manager.SpawnAt(new AudioClip("after-shutdown"), new Vector3())),
            "Shutdown left usable handles or allowed new playback.");
    }

    private static void CrossManagerHandle()
    {
        var first = Setup(1);
        var firstHandle = first.manager.SpawnAt(new AudioClip("first-manager"), new Vector3());
        var second = Setup(1);
        var secondHandle = second.manager.SpawnAt(new AudioClip("second-manager"), new Vector3());
        Check(!first.manager.IsValid(secondHandle) && !second.manager.IsValid(firstHandle)
            && !second.manager.Stop(firstHandle) && !second.manager.Pause(firstHandle)
            && !second.manager.Resume(firstHandle) && !second.manager.Restart(firstHandle)
            && !second.manager.Release(firstHandle) && !second.manager.SetLoop(firstHandle, true),
            "A handle controlled another manager's voice.");
        Check(first.manager.IsValid(firstHandle) && second.manager.IsValid(secondHandle), "Cross-manager operations changed an owned voice.");
    }

    private static void HandleValidityIsPure()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        Check(!setup.manager.IsValid(default) && setup.manager.ActiveSpatialVoiceCount == 0, "Default query changed capacity.");
        var clip = new AudioClip("failed-load");
        var handle = setup.manager.SpawnAt(clip, new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        var stops = source.StopCount;
        clip.loadState = AudioDataLoadState.Failed;
        Check(!setup.manager.IsValid(handle) && !setup.manager.IsValid(handle) && setup.manager.ActiveSpatialVoiceCount == 1
            && source.StopCount == stops, "Invalidity query cleaned a failed clip as a side effect.");
        Time.frameCount++;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.ActiveSpatialVoiceCount == 0, "Central polling did not clean the failed clip.");
    }

    private static void HandleExternalPoolClear()
    {
        var setup = Setup(1);
        var stale = setup.manager.SpawnAt(new AudioClip("before-external-clear"), new Vector3());
        var previous = ActiveSpatialSource(setup.manager);
        setup.pool.Clear();
        Check(!setup.manager.IsValid(stale) && setup.manager.ActiveSpatialVoiceCount == 1,
            "Validity query mutated stale ownership after external pool clear.");
        var recovered = setup.manager.SpawnAt(new AudioClip("same-frame-recovery"), new Vector3());
        Check(setup.manager.IsValid(recovered) && !setup.manager.IsValid(stale)
            && setup.manager.ActiveSpatialVoiceCount == 1 && !ReferenceEquals(previous, ActiveSpatialSource(setup.manager)),
            "Same-frame spawn did not repair stale pool ownership.");
    }

    private static void HandleDeferredPoolClear()
    {
        var setup = Setup(1);
        var stale = setup.manager.SpawnAt(new AudioClip("deferred-old"), new Vector3());
        var previous = ActiveSpatialSource(setup.manager);
        var pooled = previous.GetComponent<PooledObject>();
        SpatialSoundHandle recovered = default;
        AudioSource replacement = null;
        try
        {
            setup.pool.Clear();
            Check(previous != null && pooled != null && pooled.Owner == null && !previous.gameObject.activeSelf,
                "Deferred destruction did not leave a non-null inactive orphaned lease.");
            Check(!setup.manager.IsValid(stale) && setup.manager.ActiveSpatialVoiceCount == 1,
                "Validity query cleaned the orphaned lease before the common entry point.");
            Check(!setup.manager.Resume(stale) && !setup.manager.Restart(stale) && !setup.manager.IsValid(stale),
                "Orphaned handle resumed or restarted playback.");
            recovered = setup.manager.SpawnAt(new AudioClip("deferred-new"), new Vector3());
            replacement = ActiveSpatialSource(setup.manager);
            Check(setup.manager.IsValid(recovered) && !setup.manager.IsValid(stale)
                && setup.manager.ActiveSpatialVoiceCount == 1 && !ReferenceEquals(previous, replacement),
                "Same-frame spawn failed while stale objects awaited Destroy.");
        }
        finally
        {
            UnityEngine.Object.FlushDeferredDestroy();
        }

        Check(previous == null && replacement != null && setup.manager.IsValid(recovered)
            && ReferenceEquals(replacement, ActiveSpatialSource(setup.manager)),
            "Late destruction of the old pool affected the replacement voice.");
    }

    private static void HandleBrokenClipCleanup()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        var failing = new AudioClip("failed-while-paused");
        var paused = setup.manager.SpawnAt(failing, new Vector3());
        Check(setup.manager.Pause(paused), "Paused failure fixture was not paused.");
        failing.loadState = AudioDataLoadState.Failed;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(paused) && setup.manager.ActiveSpatialVoiceCount == 0,
            "Failed clip stayed leased because its voice was paused.");

        var retained = setup.manager.SpawnAt(new AudioClip("lost-after-finish"), new Vector3(), autoRelease: false);
        var source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.IsValid(retained), "Finished retained fixture was unexpectedly returned.");
        source.clip = null;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.IsValid(retained) && setup.manager.ActiveSpatialVoiceCount == 0,
            "Lost clip stayed leased because retained playback was already finished.");
    }

    private static void HandleCanceledLifetime()
    {
        var setup = Setup(2);
        var retained = setup.manager.SpawnAt(new AudioClip("cancel-retained"), new Vector3(), autoRelease: false);
        var automatic = setup.manager.SpawnAt(new AudioClip("cancel-auto"), new Vector3());
        Check(setup.manager.Pause(retained), "Cancellation fixture did not establish an owned pause.");
        setup.lifetime.Cancel();
        var loads = setup.resources.LoadCount;
        Check(!setup.manager.IsValid(setup.manager.SpawnAt("A", new Vector3())) && setup.resources.LoadCount == loads,
            "Canceled lifetime started path loading.");
        Check(!setup.manager.Pause(retained) && !setup.manager.Resume(retained) && !setup.manager.Restart(retained)
            && !setup.manager.SetLoop(retained, true), "Canceled lifetime allowed new playback control.");
        Check(setup.manager.Stop(retained) && setup.manager.IsValid(retained) && setup.manager.Release(retained),
            "Canceled lifetime blocked retained cleanup.");
        Check(setup.manager.Stop(automatic) && !setup.manager.IsValid(automatic), "Canceled lifetime blocked automatic cleanup.");
    }

    private static void IndependentHandleControls()
    {
        var setup = Setup(2);
        var first = setup.manager.SpawnAt(new AudioClip("first-independent"), new Vector3(), volume: 0.8f, autoRelease: false);
        var firstSource = ActiveSpatialSource(setup.manager);
        var second = setup.manager.SpawnAt(new AudioClip("second-independent"), new Vector3(), volume: 0.4f);
        AudioSource secondSource = null;
        foreach (var candidate in PooledSpatialSources(setup))
            if (candidate.clip?.name == "second-independent")
                secondSource = candidate;
        Check(secondSource != null && setup.manager.Pause(first) && !firstSource.isPlaying && secondSource.isPlaying,
            "Pausing one handle changed a second voice.");
        Check(setup.manager.SetLoop(first, true) && firstSource.loop && !secondSource.loop, "Loop change leaked to the second voice.");
        setup.manager.SetMasterVolume(0.5f);
        setup.manager.SetCategoryVolume(eSound.FX, 0.5f);
        Check(Approximately(firstSource.volume, 0.2f) && Approximately(secondSource.volume, 0.1f),
            "Handle control broke shared master and FX volume behavior.");
        setup.manager.PlayOneShot(new AudioClip("ui-independent"), eSound.UI);
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 1 && setup.manager.IsValid(second),
            "Handle controls changed independent UI playback.");
        Check(setup.manager.Stop(first) && !firstSource.isPlaying && secondSource.isPlaying && setup.manager.IsValid(second),
            "Stopping one retained voice changed the second voice.");
        Check(setup.manager.Release(first) && setup.manager.Release(second), "Independent handles could not be released.");
    }
}
