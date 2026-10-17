using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Pool;
using UnityEngine;

internal static partial class Program
{
    private static void SpatialVoicesAreReused()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(2);
        var sources = PooledSpatialSources(setup);
        Check(setup.manager.SpatialVoiceCapacity == 2 && sources.Length == 2, "Spatial voices were not fully prewarmed.");
        foreach (var source in sources)
        {
            Check(!source.gameObject.activeSelf, "An unused spatial voice started active.");
            Check(Approximately(source.spatialBlend, 1f) && source.rolloffMode == AudioRolloffMode.Linear, "Spatial source defaults are incorrect.");
            Check(Approximately(source.dopplerLevel, 0f) && !source.loop && !source.playOnAwake, "Spatial source playback defaults are incorrect.");
        }

        var destroyed = UnityEngine.Object.Destroyed.Count;
        var created = GameObject.CreatedCount;
        var firstClip = new AudioClip("first");
        Check(setup.manager.PlayAt(firstClip, new Vector3(1f, 2f, 3f), 2f, 40f, 0.4f, 2f), "Valid spatial playback was rejected.");
        var first = ActiveSpatialSource(setup.manager);
        var owner = first.gameObject.GetComponent<PooledObject>()?.Owner;
        Check(owner != null, "Spatial voice is not owned by the production GameObjectPool.");
        Check(PositionEquals(first.transform.position, 1f, 2f, 3f), "Spatial position was not applied.");
        Check(Approximately(first.minDistance, 2f) && Approximately(first.maxDistance, 40f), "Spatial distance range was not applied.");
        Check(Approximately(first.volume, 0.4f) && Approximately(first.pitch, 2f), "Spatial volume or pitch was not applied.");

        first.isPlaying = false;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "A voice was reclaimed in its creation frame.");
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 0 && !first.gameObject.activeSelf && first.clip == null, "Completed voice was not reset and returned.");

        Check(setup.manager.PlayAt(new AudioClip("second"), new Vector3(4f, 5f, 6f)), "Reused spatial playback was rejected.");
        var second = ActiveSpatialSource(setup.manager);
        Check(ReferenceEquals(first, second), "The pool did not reuse the returned AudioSource instance.");
        Check(ReferenceEquals(second.gameObject.GetComponent<PooledObject>()?.Owner, owner), "Reused spatial voice changed pool ownership.");
        Check(PositionEquals(second.transform.position, 4f, 5f, 6f), "Reused source retained its old position.");
        Check(Approximately(second.minDistance, 1f) && Approximately(second.maxDistance, 30f), "Reused source retained its old distance range.");
        Check(Approximately(second.volume, 1f) && Approximately(second.pitch, 1f), "Reused source retained its old volume or pitch.");
        Check(PooledSpatialSources(setup).Length == 2 && GameObject.CreatedCount == created && UnityEngine.Object.Destroyed.Count == destroyed, "Spatial reuse allocated or destroyed scene objects.");
        Check(ManualFrameScheduler.PendingCount == 0, "Spatial playback created asynchronous frame work.");

        setup.manager.Clear();
        Check(setup.manager.ActiveSpatialVoiceCount == 0 && setup.resources.ActiveScopeCount == 3, "Clear released the reusable spatial pool scope.");
        Check(setup.manager.PlayAt(new AudioClip("after-clear"), new Vector3()), "Spatial pool was not reusable after Clear.");
        Check(PooledSpatialSources(setup).Length == 2 && GameObject.CreatedCount == created, "Clear rebuilt or expanded the prewarmed spatial pool.");
    }

    private static void SpatialCapacityRejectsBeforeLoad()
    {
        var setup = Setup(1);
        Check(setup.manager.PlayAt(new AudioClip("occupied"), new Vector3()), "Initial spatial voice was rejected.");
        var loads = setup.resources.LoadCount;
        var created = GameObject.CreatedCount;
        var destroyed = UnityEngine.Object.Destroyed.Count;
        Check(!setup.manager.PlayAt("A", new Vector3()), "Playback beyond spatial capacity was accepted.");
        Check(setup.resources.LoadCount == loads, "Capacity rejection loaded a resource.");
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Capacity rejection changed active ownership.");
        Check(GameObject.CreatedCount == created && UnityEngine.Object.Destroyed.Count == destroyed, "Capacity rejection allocated or destroyed a pooled object.");
    }

    private static void SpatialCompletionPolicy()
    {
        Time.unscaledDeltaTime = 0.1f;
        var setup = Setup(1);
        var clip = new AudioClip("stream") { loadState = AudioDataLoadState.Unloaded };
        Check(setup.manager.PlayAt(clip, new Vector3()), "Unloaded clip playback was rejected.");
        var source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Unloaded clip was reclaimed before load settled.");
        clip.loadState = AudioDataLoadState.Loading;
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Loading clip was reclaimed.");
        clip.loadState = AudioDataLoadState.Failed;
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 0, "Failed clip was not reclaimed.");

        Check(setup.manager.PlayAt(new AudioClip("lost"), new Vector3()), "Clip-loss setup was rejected.");
        source = ActiveSpatialSource(setup.manager);
        source.clip = null;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Clip loss bypassed the creation-frame guard.");
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 0 && !source.gameObject.activeSelf, "Externally lost clip did not release slot ownership.");

        var loaded = new AudioClip("loaded");
        Check(setup.manager.PlayAt(loaded, new Vector3()), "Loaded clip playback was rejected.");
        source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        Time.frameCount++;
        AudioListener.pause = true;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Listener pause reclaimed a voice.");
        AudioListener.pause = false;
        setup.manager.OnAppPause(true);
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "App pause reclaimed a voice.");
        setup.manager.OnAppPause(false);
        Time.frameCount++;
        setup.manager.OnUpdate(0f);
        Check(setup.manager.ActiveSpatialVoiceCount == 0, "Voice was not reclaimed after pause ended.");
    }

    private static void SpatialPollingCadence()
    {
        var setup = Setup(1);
        var source = PooledSpatialSources(setup)[0];
        Time.unscaledDeltaTime = 0.025f;
        for (int i = 0; i < 20; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 0, "Idle manager inspected a pooled AudioSource.");
        setup.manager.PlayAt(new AudioClip("cadence"), new Vector3());
        Time.deltaTime = 0f;
        for (int i = 0; i < 3; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 0, "Spatial audio was inspected before the interval.");
        AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 1, "Unscaled inspection did not run with scaled time stopped.");
        for (int i = 0; i < 36; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 10, "Forty 25ms ticks must perform ten inspections.");
        source.isPlaying = false;
        for (int i = 0; i < 3; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.ActiveSpatialVoiceCount == 1 && source.IsPlayingReadCount == 10, "Completed source returned before the scheduled inspection.");
        AdvanceSpatialPolling(setup.manager);
        Check(setup.manager.ActiveSpatialVoiceCount == 0 && source.IsPlayingReadCount == 11 && source.clip == null, "Scheduled inspection did not return the completed source.");
        Check(ManualFrameScheduler.PendingCount == 0, "Spatial polling introduced per-sound asynchronous work.");
    }

    private static void SpatialCapacityReclaimsCompleted()
    {
        var setup = Setup(1);
        setup.manager.PlayAt(new AudioClip("short"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        int loads = setup.resources.LoadCount;
        int created = GameObject.CreatedCount;
        Check(!setup.manager.PlayAt("A", new Vector3()) && setup.resources.LoadCount == loads,
            "Capacity inspection bypassed the creation-frame guard.");
        Time.frameCount++;
        Check(setup.manager.PlayAt("A", new Vector3()), "Completed voice held capacity until the next periodic scan.");
        Check(ReferenceEquals(source, ActiveSpatialSource(setup.manager)) && source.StopCount == 1
            && setup.resources.LoadCount == loads + 1 && GameObject.CreatedCount == created, "Capacity recovery did not return and reuse the lease before loading.");
    }

    private static void SpatialCapacityRespectsPause()
    {
        var setup = Setup(1);
        setup.manager.PlayAt(new AudioClip("paused"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        source.isPlaying = false;
        Time.unscaledDeltaTime = 1f;
        AudioListener.pause = true;
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.PlayAt("A", new Vector3()), "Listener pause reclaimed capacity.");
        AudioListener.pause = false;
        setup.manager.OnAppPause(true);
        AdvanceSpatialPolling(setup.manager);
        Check(!setup.manager.PlayAt("A", new Vector3()), "App pause reclaimed capacity.");
        Check(source.IsPlayingReadCount == 0 && setup.resources.LoadCount == 0, "Paused polling inspected or loaded audio.");
        setup.manager.OnAppPause(false);
        Check(setup.manager.PlayAt(new AudioClip("resumed"), new Vector3()), "Capacity did not recover after resume.");
    }

    private static void SpatialPollingReset()
    {
        var setup = Setup(1);
        setup.manager.PlayAt(new AudioClip("before"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        Time.unscaledDeltaTime = 0.075f;
        AdvanceSpatialPolling(setup.manager);
        setup.manager.StopAllSpatialSounds();
        setup.manager.PlayAt(new AudioClip("after"), new Vector3());
        Time.unscaledDeltaTime = 0.025f;
        for (int i = 0; i < 3; i++)
            AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 0, "New active period inherited the old polling interval.");
        AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 1, "New active period was not inspected after its interval.");
    }

    private static void SpatialPollingLongFrame()
    {
        var setup = Setup(1);
        setup.manager.PlayAt(new AudioClip("long frame"), new Vector3());
        var source = ActiveSpatialSource(setup.manager);
        Time.unscaledDeltaTime = 2f;
        AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 1, "A long frame performed repeated catch-up scans.");
        Time.unscaledDeltaTime = 0.025f;
        AdvanceSpatialPolling(setup.manager);
        Check(source.IsPlayingReadCount == 1, "A long frame left catch-up scan debt.");
    }

    private static void SpatialVolumePolicy()
    {
        var setup = Setup(1);
        setup.manager.SetMasterVolume(0.5f);
        setup.manager.SetCategoryVolume(eSound.FX, 0.4f);
        Check(setup.manager.PlayAt(new AudioClip("spatial"), new Vector3(), volume: 0.5f), "Spatial playback was rejected.");
        var source = ActiveSpatialSource(setup.manager);
        Check(Approximately(source.volume, 0.1f), "Spatial voice did not apply master, FX, and per-play volume.");

        setup.manager.PlayOneShot(new AudioClip("2d"), eSound.FX);
        setup.manager.PlayFade("FX2", eSound.FX, 1f);
        Step(0.25f);
        Check(Approximately(source.volume, 0.1f), "2D FX fade gain leaked into spatial volume.");
        setup.manager.SetCategoryMuted(eSound.FX, true);
        Check(Approximately(source.volume, 0f), "FX mute did not affect active spatial voices.");
        setup.manager.SetCategoryMuted(eSound.FX, false);
        setup.manager.SetMasterMuted(true);
        Check(Approximately(source.volume, 0f), "Master mute did not affect active spatial voices.");
        setup.manager.SetMasterMuted(false);
        Check(Approximately(source.volume, 0.1f), "Spatial volume did not recover from mute policies.");
    }

    private static void SpatialClearOrder()
    {
        var setup = Setup(2);
        setup.manager.Play("A", eSound.Bgm);
        Check(setup.manager.PlayAt(new AudioClip("spatial"), new Vector3()), "Spatial playback was rejected.");
        var source = ActiveSpatialSource(setup.manager);
        TestTrace.Events.Clear();

        setup.manager.Clear();

        var release = TestTrace.Events.IndexOf("scope-release");
        var spatialStop = TestTrace.Events.IndexOf("stop:" + source.gameObject.name);
        Check(spatialStop >= 0 && release > spatialStop, "Sound scope released before active spatial playback stopped.");
        Check(source.clip == null && !source.gameObject.activeSelf && setup.manager.ActiveSpatialVoiceCount == 0, "Clear did not reset the spatial pool.");
    }

    private static void StoppedManagerRejectsSpatialPlayback()
    {
        var setup = Setup(1);
        setup.manager.Shutdown(ShutdownReason.Normal);
        var loads = setup.resources.LoadCount;
        Check(!setup.manager.PlayAt(new AudioClip("direct"), new Vector3()), "Stopped manager accepted a direct spatial clip.");
        Check(!setup.manager.PlayAt("A", new Vector3()), "Stopped manager accepted a spatial path.");
        Check(setup.resources.LoadCount == loads && setup.manager.ActiveSpatialVoiceCount == 0, "Stopped spatial request loaded or acquired state.");
    }

    private static void SpatialValidation()
    {
        var setup = Setup(1);
        Check(!setup.manager.PlayAt(" ", new Vector3()), "Blank spatial path was accepted.");
        Check(!setup.manager.PlayAt(new AudioClip("failed") { loadState = AudioDataLoadState.Failed }, new Vector3()), "Failed direct clip was accepted.");
        var badPositions = new[]
        {
            new Vector3(float.NaN, 0f, 0f),
            new Vector3(0f, float.PositiveInfinity, 0f),
            new Vector3(0f, 0f, float.NegativeInfinity)
        };
        foreach (var position in badPositions)
            Check(!setup.manager.PlayAt("A", position), "Non-finite spatial position was accepted.");

        Check(!setup.manager.PlayAt("A", new Vector3(), 0f, 30f), "Non-positive minimum distance was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), 2f, 2f), "Maximum distance not greater than minimum was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), 1f, float.PositiveInfinity), "Non-finite maximum distance was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), volume: float.NaN), "Non-finite volume was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), volume: float.PositiveInfinity), "Infinite volume was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), pitch: 0f), "Non-positive pitch was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), pitch: -1f), "Negative pitch was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), pitch: float.NaN), "NaN pitch was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), pitch: float.PositiveInfinity), "Infinite pitch was accepted.");
        Check(!setup.manager.PlayAt("A", new Vector3(), pitch: 3.01f), "Pitch above the supported range was accepted.");
        Check(setup.resources.LoadCount == 0 && setup.manager.ActiveSpatialVoiceCount == 0, "Invalid spatial requests loaded or acquired a voice.");

        Check(setup.manager.PlayAt(new AudioClip("clamped"), new Vector3(), volume: 2f), "Finite volume above one was rejected instead of clamped.");
        Check(Approximately(ActiveSpatialSource(setup.manager).volume, 1f), "Spatial volume above one was not clamped.");
        setup.manager.StopAllSpatialSounds();
        Check(setup.manager.PlayAt(new AudioClip("zero"), new Vector3(), volume: -1f), "Finite negative volume was rejected instead of clamped.");
        Check(Approximately(ActiveSpatialSource(setup.manager).volume, 0f), "Negative spatial volume was not clamped to zero.");
    }

    private static void SoundPoolScopeIsolation()
    {
        var setup = Setup(1);
        var otherScope = setup.resources.CreateScope();
        var otherOriginal = new GameObject("OtherPoolOriginal");
        setup.pool.CreatePool(otherOriginal, 1, otherScope);
        var otherInstance = setup.pool.Get(otherOriginal, scope: otherScope);
        Check(otherInstance.GetComponent<PooledObject>()?.Owner != null, "Unrelated production pool was not established.");
        setup.pool.Return(otherInstance);

        setup.manager.Shutdown(ShutdownReason.Normal);

        Check(setup.pool.GetOriginal("SpatialSound") == null, "Sound pool survived its dedicated scope disposal.");
        Check(ReferenceEquals(setup.pool.GetOriginal("OtherPoolOriginal"), otherOriginal), "Sound scope disposal removed an unrelated pool.");
        var reused = setup.pool.Get(otherOriginal, scope: otherScope);
        Check(ReferenceEquals(reused, otherInstance), "Unrelated pool was rebuilt instead of preserved.");
        setup.pool.Return(reused);
    }

    private static void SpatialReturnClearsSlot()
    {
        var setup = Setup(1);
        Check(setup.manager.PlayAt(new AudioClip("stop"), new Vector3(), volume: 0.7f), "Playback failed.");
        var source = ActiveSpatialSource(setup.manager);
        var owner = source.GetComponent<PooledObject>().Owner;
        setup.manager.StopAllSpatialSounds();
        setup.manager.StopAllSpatialSounds();
        Check(source.StopCount == 1 && owner.ActiveCount == 0 && owner.InactiveCount == 1,
            "Repeated stop duplicated reset/return or changed ownership.");
        AssertEmptySpatialSlot(setup.manager);
        Check(setup.manager.PlayAt(new AudioClip("reuse"), new Vector3()), "Cleared slot could not be reused.");
        Check(ReferenceEquals(ActiveSpatialSource(setup.manager), source), "The original pool lease was lost before return.");
    }

    private static void FailedReturnCompletesCleanup()
    {
        var setup = Setup(2);
        setup.manager.PlayBgm("C");
        setup.manager.PlayAt("A", new Vector3());
        setup.manager.PlayAt("B", new Vector3());
        var sources = PooledSpatialSources(setup);
        var item = sources[0].GetComponent<PooledObject>();
        var owner = item.Owner;
        var callback = item.gameObject.AddComponent<FailingReturn>();
        item.Initialize(owner);
        var releaseCount = setup.resources.ScopeReleaseCount;
        bool failed = false;
        try
        {
            setup.manager.Clear();
        }
        catch (AggregateException error)
        {
            foreach (var cause in error.Flatten().InnerExceptions)
                if (ReferenceEquals(cause, callback.Failure))
                    failed = true;
        }

        Check(failed, "The original return callback failure was not preserved.");
        Check(setup.manager.ActiveSpatialVoiceCount == 0 && owner.ActiveCount == 1 && owner.InactiveCount == 1,
            "A failed return released the voice slot incorrectly or prevented the remaining voice from returning.");
        Check(setup.resources.ScopeReleaseCount == releaseCount + 1 && setup.resources.ActiveScopeCount == 3,
            "A failed return prevented BGM scope release or disposed the reusable pool or scene scope.");
        Check(sources[0].clip == null && sources[0].gameObject.activeSelf, "A failed return did not keep the cleared voice borrowed.");
        Check(sources[1].clip == null && !sources[1].gameObject.activeSelf, "Cleanup retained a clip or active source.");
        Check(setup.manager.PlayAt("C", new Vector3()), "Playback did not recover after aggregated cleanup failure.");

        failed = false;
        try
        {
            setup.manager.Shutdown(ShutdownReason.Normal);
        }
        catch (Exception error)
        {
            IEnumerable<Exception> causes = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { error };
            foreach (var cause in causes)
                if (ReferenceEquals(cause, callback.Failure))
                    failed = true;
        }

        Check(failed, "Shutdown did not report the borrowed voice return failure.");
        UnityEngine.Object.FlushDeferredDestroy();
        Check(sources[0] == null, "Shutdown did not destroy the voice whose return failed.");
    }

    private static void CanceledLifetimePoolPolicy()
    {
        var setup = Setup(1);
        var scope = setup.resources.CreateScope();
        var original = new GameObject("CancellationPoolOriginal");
        setup.pool.CreatePool(original, 1, scope);
        var instance = setup.pool.Get(original, scope: scope);
        setup.lifetime.Cancel();

        Check(setup.pool.Return(instance), "Pool return was rejected after lifetime cancellation.");
        var rejected = false;
        try
        {
            setup.pool.Get(original, scope: scope);
        }
        catch (OperationCanceledException)
        {
            rejected = true;
        }

        Check(rejected, "Pool accepted a new get after lifetime cancellation.");
    }

    private static void ExternalPoolClearRecovery()
    {
        var setup = Setup(2);
        Check(setup.manager.PlayAt(new AudioClip("before-clear"), new Vector3()), "Initial spatial playback was rejected.");
        var previous = ActiveSpatialSource(setup.manager);

        setup.pool.Clear();
        UnityEngine.Object.FlushDeferredDestroy();
        Check(previous == null, "External PoolManager.Clear did not destroy the leased spatial object.");
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Test did not retain stale manager metadata before repair.");

        Check(setup.manager.PlayAt(new AudioClip("after-pool-clear"), new Vector3()), "Spatial playback did not recover after external pool clear.");
        var recovered = ActiveSpatialSource(setup.manager);
        Check(recovered != null && !ReferenceEquals(previous, recovered), "Recovery reused a destroyed spatial source.");
        Check(recovered.gameObject.GetComponent<PooledObject>()?.Owner != null, "Recovered spatial source has no production pool owner.");
        Check(setup.manager.ActiveSpatialVoiceCount == 1, "Stale ownership count was not repaired before reacquiring.");
    }

    private static void SpatialShutdownDestroyOrder()
    {
        var setup = Setup(2);
        var copies = PooledSpatialSources(setup);
        var copyObjects = new GameObject[copies.Length];
        for (var i = 0; i < copies.Length; i++)
            copyObjects[i] = copies[i].gameObject;
        var field = typeof(SoundManager).GetField("spatialOriginal", BindingFlags.Instance | BindingFlags.NonPublic);
        var template = field?.GetValue(setup.manager) as GameObject;
        Check(template != null && copyObjects.Length == 2, "Shutdown order fixture did not find template and prewarmed copies.");
        // 템플릿은 매니저 루트와 함께 파괴되므로 루트의 Destroy 호출 순서로 비교한다.
        var templateRoot = template.transform.parent.gameObject;

        setup.manager.Shutdown(ShutdownReason.Normal);

        UnityEngine.Object.FlushDeferredDestroy();
        var templateIndex = UnityEngine.Object.Destroyed.IndexOf(templateRoot);
        Check(template == null && templateIndex >= 0, "Sound template was not destroyed by manager shutdown.");
        foreach (var copy in copyObjects)
        {
            var copyIndex = UnityEngine.Object.Destroyed.IndexOf(copy);
            Check(copy == null && copyIndex >= 0 && copyIndex < templateIndex, "A pooled copy was not destroyed before the sound template.");
        }
    }
}
