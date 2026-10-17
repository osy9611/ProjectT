using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Pool;
using ProjectT.Sound;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class Program
{
    private static int count;
    private static int failures;
    private static readonly List<TestFixture> activeFixtures = new List<TestFixture>();

    private sealed class TestFixture
    {
        public SoundManager manager;
        public PoolManager pool;
        public FakeResourceManager resources;
        public CancellationTokenSource lifetime;
    }

    private static int Main()
    {
        Run("Latest direct play wins over an in-flight fade", LatestPlayWins);
        Run("Fade replacement is channel-local", FadeReplacementIsChannelLocal);
        Run("Same-channel fade replacement preserves current gain", FadeReplacementPreservesGain);
        Run("Volume and mute changes survive fades and clear", VolumeAndMuteSurvive);
        Run("Fade advances with unscaled time", FadeUsesUnscaledTime);
        Run("Stop and clear prevent pending fades from resuming", StopAndClearCancelPendingFade);
        Run("Clear stops playback before releasing its scope", ClearOrderAndOwnership);
        Run("Clear detaches a scope even when release throws", ClearDetachesFailingScope);
        Run("Invalid requests preserve an existing fade", InvalidRequestsPreserveFade);
        Run("Canceled lifetime rejects cached direct and path playback", CanceledLifetimeRejectsPlayback);
        Run("Same BGM respects the restart flag", SameBgmRestartPolicy);
        Run("Spatial voices are prewarmed reset and reused", SpatialVoicesAreReused);
        Run("Spatial capacity rejects before resource loading", SpatialCapacityRejectsBeforeLoad);
        Run("Spatial completion respects frame load and pause state", SpatialCompletionPolicy);
        Run("Spatial polling uses unscaled intervals and skips inactive periods", SpatialPollingCadence);
        Run("Full spatial pool reclaims completed voices before loading", SpatialCapacityReclaimsCompleted);
        Run("Capacity reclamation honors listener and app pause", SpatialCapacityRespectsPause);
        Run("Stopping all voices resets the next polling interval", SpatialPollingReset);
        Run("A long frame performs one spatial inspection without catch-up scans", SpatialPollingLongFrame);
        Run("Spatial volume follows master and FX without fade gain", SpatialVolumePolicy);
        Run("Spatial clear stops before scope release", SpatialClearOrder);
        Run("Stopped manager rejects spatial playback", StoppedManagerRejectsSpatialPlayback);
        Run("Spatial validation rejects invalid requests before loading", SpatialValidation);
        Run("Sound pool scope release preserves unrelated pools", SoundPoolScopeIsolation);
        Run("Spatial return clears metadata and tolerates repeated stop", SpatialReturnClearsSlot);
        Run("A failed return stays borrowed until shutdown without stopping remaining sound cleanup", FailedReturnCompletesCleanup);
        Run("Canceled lifetime permits returns and rejects new pool gets", CanceledLifetimePoolPolicy);
        Run("External pool clear is detected and playback recovers", ExternalPoolClearRecovery);
        Run("Shutdown destroys pooled copies before the sound template", SpatialShutdownDestroyOrder);
        Run("Spawn path and clip handles follow default auto release", SpawnHandleAutoRelease);
        Run("Retained completion and stop preserve capacity until restart or release", RetainedHandleLifecycle);
        Run("Individual pause and resume preserve playback across global guards", HandlePauseResumeGuards);
        Run("Pausing completed playback cannot retain an automatic voice", PauseCompletedHandle);
        Run("Loop changes affect completion and source reuse resets the setting", HandleLoopPolicy);
        Run("Released reused cleared and shutdown handles are stale", HandleInvalidation);
        Run("Handles cannot control another sound manager", CrossManagerHandle);
        Run("Handle validity checks do not mutate broken leases", HandleValidityIsPure);
        Run("External pool clear recovers a handle slot in the same frame", HandleExternalPoolClear);
        Run("Deferred pool destruction cannot retain or corrupt handle ownership", HandleDeferredPoolClear);
        Run("Failed and lost clips clean up paused and retained voices", HandleBrokenClipCleanup);
        Run("Canceled lifetime allows stop and release but blocks new handle work", HandleCanceledLifetime);
        Run("Individual controls leave other voices and channel behavior intact", IndependentHandleControls);
        Run("BGM clips use the sound scope while other channels use the scene scope", ClipScopesFollowChannelLifetime);
        Run("A scene clip released during fade-out is not played", ReleasedSceneClipSkipsFadeIn);
        Run("Scene replacement cancels pending FX and UI fades with a shared live clip", SceneReplacementCancelsPendingFades);
        Run("Scene replacement restores a started fade without restarting playback", SceneReplacementRestoresStartedFade);
        Run("A new scene fade survives the old fade completion", NewSceneFadeSurvivesOldCompletion);
        Run("BGM fade continues across scene replacement", BgmFadeSurvivesSceneReplacement);
        Run("UI registration is idempotent and disable preserves outsiders", UiRegistrationLifecycle);
        Run("UI disabled by an earlier listener is silent", UiDisabledDuringEventIsSilent);
        Run("Toggle mode has one route and supports on/off clips", ToggleRouteAndVariants);
        Run("EventTrigger cleanup removes only the sound callback", EventTriggerPreservesOtherListeners);

        Console.WriteLine($"{count - failures}/{count} sound tests passed");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        count++;
        Exception failure = null;
        try
        {
            ManualFrameScheduler.Reset();
            TestTrace.Events.Clear();
            UnityEngine.Object.ResetDestroyed();
            Global.Exceptions.Clear();
            AudioListener.pause = false;
            Time.frameCount = 0;
            Time.deltaTime = 0.02f;
            Time.unscaledDeltaTime = 0.02f;
            test();
            if (Global.Exceptions.Count > 0)
                throw new Exception("A forgotten async operation reported: " + Global.Exceptions[0]);
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            foreach (var fixture in activeFixtures)
            {
                Global.Resource = fixture.resources;
                Global.Pool = fixture.pool;
                Global.Sound = fixture.manager;

                try
                {
                    fixture.lifetime.Cancel();
                }
                catch (Exception error)
                {
                    failure = failure == null ? error : new AggregateException(failure, error);
                }

                failure = ShutdownManager(fixture.manager, failure);
                failure = ShutdownManager(fixture.pool, failure);

                try
                {
                    fixture.resources.Shutdown();
                }
                catch (Exception error)
                {
                    failure = failure == null ? error : new AggregateException(failure, error);
                }

                fixture.lifetime.Dispose();
            }

            activeFixtures.Clear();
            try
            {
                for (var frame = 0; frame < 8 && ManualFrameScheduler.PendingCount > 0; frame++)
                    ManualFrameScheduler.Step();
                if (ManualFrameScheduler.PendingCount > 0)
                    throw new Exception("Cleanup left pending async sound work.");
                if (Global.Exceptions.Count > 0)
                    throw new Exception("Cleanup reported a forgotten async failure: " + Global.Exceptions[0]);
            }
            catch (Exception error)
            {
                failure = failure == null ? error : new AggregateException(failure, error);
            }

            ManualFrameScheduler.Reset();
        }

        if (failure == null)
        {
            Console.WriteLine("PASS " + name);
        }
        else
        {
            failures++;
            Console.WriteLine("FAIL " + name + " :: " + failure);
        }
    }

    private static Exception ShutdownManager(ManagerBase manager, Exception failure)
    {
        if (manager.State == ManagerState.Stopped || manager.State == ManagerState.Created)
            return failure;

        try
        {
            manager.Shutdown(ShutdownReason.Normal);
        }
        catch (Exception error)
        {
            return failure == null ? error : new AggregateException(failure, error);
        }

        return failure;
    }

    private static TestFixture Setup(int spatialVoiceCapacity = 32)
    {
        var resources = new FakeResourceManager();
        resources.Add("A", new AudioClip("A"));
        resources.Add("B", new AudioClip("B"));
        resources.Add("C", new AudioClip("C"));
        resources.Add("FX1", new AudioClip("FX1"));
        resources.Add("FX2", new AudioClip("FX2"));
        resources.Add("UI", new AudioClip("UI"));
        resources.Add("On", new AudioClip("On"));
        resources.Add("Off", new AudioClip("Off"));
        var lifetime = new CancellationTokenSource();
        resources.Initialize(lifetime.Token);
        Global.Resource = resources;
        var pool = new PoolManager();
        Global.Pool = pool;
        pool.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        var manager = new SoundManager(spatialVoiceCapacity);
        manager.InitializeAsync(lifetime.Token).GetAwaiter().GetResult();
        Global.Sound = manager;
        var fixture = new TestFixture { manager = manager, pool = pool, resources = resources, lifetime = lifetime };
        activeFixtures.Add(fixture);
        return fixture;
    }

    private static void LatestPlayWins()
    {
        var setup = Setup();
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        setup.manager.Play("C", eSound.Bgm);
        Drain(0.25f);

        var source = Source(setup.manager, eSound.Bgm);
        Check(source.clip?.name == "C" && source.isPlaying, "A canceled fade replaced the latest BGM.");
        Check(Approximately(source.volume, 1f), "Canceled fade left a stale gain on the channel.");
    }

    private static void FadeReplacementIsChannelLocal()
    {
        var setup = Setup();
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        setup.manager.PlayOneShot(new AudioClip("existing-fx"), eSound.FX);
        setup.manager.PlayFade("FX1", eSound.FX, 1f);
        Step(0.25f);
        setup.manager.PlayFade("FX2", eSound.FX, 0.5f);
        Drain(0.25f);

        var bgm = Source(setup.manager, eSound.Bgm);
        var fx = Source(setup.manager, eSound.FX);
        Check(bgm.clip?.name == "B" && bgm.isPlaying, "Replacing an FX fade canceled the BGM fade.");
        Check(fx.OneShots.Count == 2 && fx.OneShots[0].name == "existing-fx" && fx.OneShots[1].name == "FX2", "The replaced FX fade played its obsolete clip or lost the latest clip.");
    }

    private static void FadeReplacementPreservesGain()
    {
        var setup = Setup();
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        var source = Source(setup.manager, eSound.Bgm);
        var before = source.volume;

        setup.manager.PlayFade("C", eSound.Bgm, 1f);

        Check(Approximately(source.volume, before), "Replacing a fade caused an immediate volume jump.");
        Drain(0.25f);
        Check(source.clip?.name == "C" && Approximately(source.volume, 1f), "Replacement fade did not settle on the latest clip and gain.");
    }

    private static void VolumeAndMuteSurvive()
    {
        var setup = Setup();
        setup.manager.SetMasterVolume(0.8f);
        setup.manager.SetCategoryVolume(eSound.Bgm, 0.5f);
        setup.manager.PlayBgm("A");
        Check(Approximately(Source(setup.manager, eSound.Bgm).volume, 0.4f), "Initial effective volume is incorrect.");

        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        setup.manager.SetMasterVolume(0.5f);
        setup.manager.SetCategoryMuted(eSound.Bgm, true);
        Check(Approximately(Source(setup.manager, eSound.Bgm).volume, 0f), "Mute was overwritten by an active fade.");
        setup.manager.SetCategoryMuted(eSound.Bgm, false);
        Drain(0.25f);
        Check(Approximately(Source(setup.manager, eSound.Bgm).volume, 0.25f), "Fade completion restored stale volume settings.");

        setup.manager.Clear();
        Check(Approximately(setup.manager.MasterVolume, 0.5f), "Clear reset master volume.");
        Check(Approximately(setup.manager.GetCategoryVolume(eSound.Bgm), 0.5f), "Clear reset category volume.");
        Check(!setup.manager.GetCategoryMuted(eSound.Bgm), "Clear changed category mute.");
    }

    private static void FadeUsesUnscaledTime()
    {
        var setup = Setup();
        Time.deltaTime = 0f;
        Time.unscaledDeltaTime = 0.5f;
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Drain(0.5f, 8);

        Check(ManualFrameScheduler.PendingCount == 0, "Fade stalled while scaled delta time was zero.");
        Check(Source(setup.manager, eSound.Bgm).clip?.name == "B", "Unscaled fade did not reach the replacement clip.");
    }

    private static void StopAndClearCancelPendingFade()
    {
        var stopped = Setup();
        stopped.manager.PlayBgm("A");
        stopped.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        stopped.manager.StopBgm();
        Drain(0.25f);
        var stoppedSource = Source(stopped.manager, eSound.Bgm);
        Check(!stoppedSource.isPlaying && stoppedSource.clip == null, "A pending fade resumed after StopBgm.");

        var cleared = Setup();
        cleared.manager.PlayBgm("A");
        cleared.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        cleared.manager.Clear();
        Drain(0.25f);
        var clearedSource = Source(cleared.manager, eSound.Bgm);
        Check(!clearedSource.isPlaying && clearedSource.clip == null, "A pending fade resumed after Clear.");
    }

    private static void ClearOrderAndOwnership()
    {
        var setup = Setup();
        var scopes = setup.resources.ScopeCount;
        setup.manager.Play("A", eSound.Bgm);
        setup.manager.Play("FX1", eSound.FX);
        Check(setup.resources.LoadCount == 2 && setup.resources.ScopeCount == scopes + 1, "Test did not establish a shared sound scope and cache.");
        TestTrace.Events.Clear();

        setup.manager.Clear();

        Check(setup.resources.ScopeReleaseCount == 1, "Clear did not release the sound scope exactly once.");
        var release = TestTrace.Events.IndexOf("scope-release");
        Check(release >= 0, "Scope release was not observed.");
        var stops = 0;
        for (var index = 0; index < release; index++)
            if (TestTrace.Events[index].StartsWith("stop:", StringComparison.Ordinal))
                stops++;
        Check(stops == Enum.GetValues(typeof(eSound)).Length, "Scope was released before every channel stopped.");

        foreach (eSound channel in Enum.GetValues(typeof(eSound)))
        {
            var source = Source(setup.manager, channel);
            Check(!source.isPlaying && source.clip == null, "Clear left playback or a retained clip on " + channel + ".");
        }

        var before = setup.resources.LoadCount;
        setup.manager.Play("A", eSound.Bgm);
        Check(setup.resources.LoadCount == before + 1, "Clear did not clear the clip cache.");
    }

    private static void ClearDetachesFailingScope()
    {
        var setup = Setup();
        setup.manager.Play("A", eSound.Bgm);
        setup.resources.ThrowOnNextScopeRelease = true;
        var threw = false;
        try
        {
            setup.manager.Clear();
        }
        catch (Exception error) when (ContainsMessage(error, "scope release"))
        {
            threw = true;
        }

        Check(threw, "The fake release failure did not reach the cleanup boundary.");
        var scopes = setup.resources.ScopeCount;
        setup.manager.Play("B", eSound.Bgm);
        Check(setup.resources.ScopeCount == scopes + 1, "Clear retained a disposed scope after release failed.");
        Check(Source(setup.manager, eSound.Bgm).clip?.name == "B", "Playback could not recover after a scope release failure.");
    }

    private static void InvalidRequestsPreserveFade()
    {
        var setup = Setup();
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);

        var loads = setup.resources.LoadCount;
        setup.manager.PlayFade("C", eSound.Bgm, float.NaN);
        setup.manager.PlayFade("C", eSound.Bgm, float.PositiveInfinity);
        Check(setup.resources.LoadCount == loads, "A non-finite fade duration loaded a resource before validation.");
        setup.manager.PlayFade("missing", eSound.Bgm, 1f);
        setup.manager.PlayFade("C", (eSound)999, 1f);
        setup.manager.Play((AudioClip)null, eSound.Bgm);
        Drain(0.25f);

        Check(Source(setup.manager, eSound.Bgm).clip?.name == "B", "An invalid request canceled or replaced the valid fade.");
    }

    private static void CanceledLifetimeRejectsPlayback()
    {
        var setup = Setup();
        setup.manager.Play("A", eSound.FX);
        var source = Source(setup.manager, eSound.FX);
        var played = source.OneShots.Count;
        var loads = setup.resources.LoadCount;
        setup.lifetime.Cancel();

        setup.manager.Play("A", eSound.FX);
        setup.manager.Play("B", eSound.FX);
        setup.manager.Play(new AudioClip("direct"), eSound.FX);
        setup.manager.PlayOneShot("A", eSound.FX);
        setup.manager.PlayBgm(new AudioClip("direct-bgm"));

        Check(setup.resources.LoadCount == loads, "Canceled lifetime started a cached or new resource request.");
        Check(source.OneShots.Count == played, "Canceled lifetime accepted direct or cached playback.");
        Check(Source(setup.manager, eSound.Bgm).PlayCount == 0, "Canceled lifetime accepted direct BGM playback.");
    }

    private static void SameBgmRestartPolicy()
    {
        var setup = Setup();
        var clip = new AudioClip("same");
        setup.manager.PlayBgm(clip, 1f, false);
        var source = Source(setup.manager, eSound.Bgm);
        var starts = source.PlayCount;
        setup.manager.PlayBgm(clip, 0.75f, false);
        Check(source.PlayCount == starts && Approximately(source.pitch, 0.75f), "Same BGM restarted or failed to update pitch.");
        setup.manager.PlayBgm(clip, 1f, true);
        Check(source.PlayCount == starts + 1, "Explicit BGM restart was ignored.");
    }

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

    private static void AdvanceSpatialPolling(SoundManager manager)
    {
        Time.frameCount++;
        manager.OnUpdate(Time.deltaTime);
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

    private static void AssertEmptySpatialSlot(SoundManager manager)
    {
        var voice = SpatialVoiceAt(manager, 0);
        Check(manager.ActiveSpatialVoiceCount == 0 && !SpatialVoiceField<bool>(voice, "InUse")
            && ReferenceEquals(SpatialVoiceField<GameObject>(voice, "Instance"), null)
            && ReferenceEquals(SpatialVoiceField<AudioSource>(voice, "Source"), null)
            && SpatialVoiceField<float>(voice, "Volume") == 0f, "Slot metadata was not cleared on return.");
        Check(SpatialVoiceField<long>(voice, "Generation") > 0, "Return discarded the generation that rejects stale handles.");
    }

    public sealed class FailingReturn : MonoBehaviour, IPoolable
    {
        public readonly Exception Failure = new InvalidOperationException("return callback failure");

        public void OnGet()
        {
        }

        public void OnReturn()
        {
            throw Failure;
        }
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

    private static void ClipScopesFollowChannelLifetime()
    {
        var setup = Setup(1);
        var sceneScope = setup.resources.SceneScope;
        setup.manager.PlayBgm("A");
        var bgmScope = setup.resources.LastLoadScope;
        Check(bgmScope != null && !ReferenceEquals(bgmScope, sceneScope), "BGM was loaded into the scene scope.");
        setup.manager.Play("FX1", eSound.FX);
        Check(ReferenceEquals(setup.resources.LastLoadScope, sceneScope), "FX path was not loaded into the scene scope.");
        setup.manager.PlayOneShot("UI", eSound.UI);
        Check(ReferenceEquals(setup.resources.LastLoadScope, sceneScope), "UI path was not loaded into the scene scope.");
        Check(setup.manager.PlayAt("B", new Vector3()) && ReferenceEquals(setup.resources.LastLoadScope, sceneScope),
            "Spatial path was not loaded into the scene scope.");
        setup.manager.PlayFade("FX2", eSound.FX, 0f);
        Check(ReferenceEquals(setup.resources.LastLoadScope, sceneScope), "FX fade path was not loaded into the scene scope.");
        setup.manager.PlayFade("C", eSound.Bgm, 0f);
        Check(ReferenceEquals(setup.resources.LastLoadScope, bgmScope), "BGM fade did not reuse the sound scope.");

        setup.manager.StopAllSpatialSounds();
        var releases = setup.resources.ScopeReleaseCount;
        setup.resources.ReplaceSceneScope();
        var bgm = Source(setup.manager, eSound.Bgm);
        Check(setup.resources.ScopeReleaseCount == releases + 1 && bgm.clip?.name == "C" && bgm.isPlaying,
            "Scene scope release affected BGM playback or released the sound scope.");
        setup.manager.Play("FX1", eSound.FX);
        Check(ReferenceEquals(setup.resources.LastLoadScope, setup.resources.SceneScope) && !ReferenceEquals(sceneScope, setup.resources.SceneScope),
            "FX playback after a scene change did not load into the new scene scope.");
    }

    private static void ReleasedSceneClipSkipsFadeIn()
    {
        var setup = Setup();
        var clip = new AudioClip("scene-fx");
        setup.resources.Add("scene-fx", clip);
        var fx = Source(setup.manager, eSound.FX);
        setup.manager.PlayOneShot(new AudioClip("existing-fx"), eSound.FX);
        setup.manager.PlayFade("scene-fx", eSound.FX, 1f);
        Step(0.25f);
        UnityEngine.Object.Destroy(clip);
        Drain(0.25f);

        Check(fx.OneShots.Count == 1 && fx.OneShots[0].name == "existing-fx", "A released scene clip was played after fade-out.");
        Check(Approximately(fx.volume, 1f), "Skipping a released clip left the channel faded out.");
    }

    private static void SceneReplacementCancelsPendingFades()
    {
        var setup = Setup();
        var shared = new AudioClip("shared");
        setup.resources.Add("shared", shared);
        setup.manager.PlayBgm("shared");
        var bgmScope = setup.resources.LastLoadScope;
        setup.manager.PlayOneShot(new AudioClip("existing-fx"), eSound.FX);
        setup.manager.PlayOneShot(new AudioClip("existing-ui"), eSound.UI);
        setup.manager.PlayFade("shared", eSound.FX, 1f);
        setup.manager.PlayFade("shared", eSound.UI, 1f);
        Step(0.25f);

        setup.resources.ReplaceSceneScope();
        Check(!ReferenceEquals(setup.resources.SceneScope, bgmScope) && shared != null,
            "The shared clip was not kept alive by the BGM scope.");
        Drain(0.25f);

        var fx = Source(setup.manager, eSound.FX);
        var ui = Source(setup.manager, eSound.UI);
        Check(fx.OneShots.Count == 1 && fx.OneShots[0].name == "existing-fx", "The ended scene played pending FX.");
        Check(ui.OneShots.Count == 1 && ui.OneShots[0].name == "existing-ui", "The ended scene played pending UI.");
        Check(Approximately(fx.volume, 1f) && Approximately(ui.volume, 1f), "Canceled scene fades left a reduced gain.");
        Check(Source(setup.manager, eSound.Bgm).clip == shared, "Scene replacement affected the BGM owner.");
    }

    private static void SceneReplacementRestoresStartedFade()
    {
        var setup = Setup();
        var shared = new AudioClip("shared");
        setup.resources.Add("shared", shared);
        setup.manager.PlayBgm("shared");
        setup.manager.SetMasterVolume(0.8f);
        setup.manager.SetCategoryVolume(eSound.FX, 0.5f);
        setup.manager.PlayFade("shared", eSound.FX, 1f);
        var fx = Source(setup.manager, eSound.FX);
        Step(0.25f);
        Step(0.25f);
        Check(fx.OneShots.Count == 1 && fx.volume > 0f && fx.volume < 0.4f,
            "The FX fade had not advanced before scene replacement.");

        setup.manager.SetCategoryMuted(eSound.FX, true);
        setup.resources.ReplaceSceneScope();
        Step(0.25f);
        Check(ManualFrameScheduler.PendingCount == 0, "A started scene fade continued after scope disposal.");
        Drain(0.25f);
        Check(fx.OneShots.Count == 1 && Approximately(fx.volume, 0f),
            "Scene cancellation restarted playback or ignored mute.");
        setup.manager.SetCategoryMuted(eSound.FX, false);
        Check(Approximately(fx.volume, 0.4f) && Approximately(setup.manager.MasterVolume, 0.8f)
            && Approximately(setup.manager.GetCategoryVolume(eSound.FX), 0.5f),
            "Scene cancellation did not restore gain with the current volume settings.");
    }

    private static void NewSceneFadeSurvivesOldCompletion()
    {
        var setup = Setup();
        setup.manager.PlayOneShot(new AudioClip("existing-fx"), eSound.FX);
        setup.manager.PlayFade("FX1", eSound.FX, 1f);
        Step(0.25f);
        var fx = Source(setup.manager, eSound.FX);
        var before = fx.volume;

        setup.resources.ReplaceSceneScope();
        setup.manager.PlayFade("FX2", eSound.FX, 1f);
        Check(ReferenceEquals(setup.resources.LastLoadScope, setup.resources.SceneScope),
            "The replacement fade did not load in the new scene scope.");
        Step(0.25f);
        Check(fx.volume < before, "The old fade completion overwrote the new fade gain.");
        Drain(0.25f);
        Check(fx.OneShots.Count == 2 && fx.OneShots[1].name == "FX2" && Approximately(fx.volume, 1f),
            "The replacement fade did not finish on the new scene clip.");
    }

    private static void BgmFadeSurvivesSceneReplacement()
    {
        var setup = Setup();
        setup.manager.PlayBgm("A");
        setup.manager.PlayFade("B", eSound.Bgm, 1f);
        Step(0.25f);
        setup.resources.ReplaceSceneScope();
        Drain(0.25f);

        var bgm = Source(setup.manager, eSound.Bgm);
        Check(bgm.clip?.name == "B" && bgm.isPlaying && Approximately(bgm.volume, 1f),
            "Scene replacement canceled or changed the BGM fade.");
    }

    private static void UiRegistrationLifecycle()
    {
        var setup = Setup();
        var go = new GameObject("Button");
        var button = go.AddComponent<Button>();
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";
        var outside = 0;
        button.onClick.AddListener(() => outside++);

        Invoke(click, "OnEnable");
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "UI sound played before Start.");
        Invoke(click, "Start");
        click.GetUIComponent(true);
        click.GetUIComponent(true);
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 1, "Re-registration duplicated or lost the click callback.");
        Check(outside == 2, "Sound registration disturbed an existing button listener.");

        Invoke(click, "OnDisable");
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 1, "Disabled UI retained its sound callback.");
        Check(outside == 3 && button.onClick.ListenerCount == 1, "Disable removed or duplicated an outside listener.");
    }

    private static void UiDisabledDuringEventIsSilent()
    {
        var setup = Setup();
        var go = new GameObject("DisableDuringClick");
        var button = go.AddComponent<Button>();
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";
        var outside = 0;
        button.onClick.AddListener(() =>
        {
            outside++;
            click.enabled = false;
            Invoke(click, "OnDisable");
        });
        Invoke(click, "OnEnable");
        Invoke(click, "Start");

        button.onClick.Invoke();

        Check(outside == 1, "The preceding listener did not run.");
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "An invocation snapshot played sound after the component was disabled.");
        Check(button.onClick.ListenerCount == 1, "Disable disturbed the preceding listener.");
    }

    private static void ToggleRouteAndVariants()
    {
        var setup = Setup();
        var go = new GameObject("Toggle");
        var button = go.AddComponent<Button>();
        var toggle = go.AddComponent<Toggle>();
        var click = go.AddComponent<UIClickSound>();
        click.isToggle = true;
        SetField(click, "clickSound", new AudioClip("On"));
        SetField(click, "toggleOffSound", new AudioClip("Off"));
        Invoke(click, "OnEnable");
        Invoke(click, "Start");

        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "Toggle mode also registered the button route.");
        toggle.onValueChanged.Invoke(true);
        toggle.onValueChanged.Invoke(false);
        var shots = Source(setup.manager, eSound.UI).OneShots;
        Check(shots.Count == 2 && shots[0].name == "On" && shots[1].name == "Off", "Toggle on/off sounds did not use their configured variants.");
    }

    private static void EventTriggerPreservesOtherListeners()
    {
        var setup = Setup();
        var go = new GameObject("Trigger");
        var trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        var outside = 0;
        entry.callback.AddListener(_ => outside++);
        trigger.triggers.Add(entry);
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";

        Invoke(click, "OnEnable");
        Invoke(click, "Start");
        entry.callback.Invoke(new BaseEventData());
        Check(outside == 1 && Source(setup.manager, eSound.UI).OneShots.Count == 1, "EventTrigger did not invoke both listeners.");
        Invoke(click, "OnDisable");
        entry.callback.Invoke(new BaseEventData());
        Check(outside == 2 && entry.callback.ListenerCount == 1, "Disable removed the EventTrigger entry or another owner's listener.");
    }

    private static AudioSource Source(SoundManager manager, eSound channel)
    {
        var field = typeof(SoundManager).GetField("audioSources", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(manager) is AudioSource[] sources)
            return sources[(int)channel];

        throw new Exception("SoundManager audioSources field is unavailable.");
    }

    private static AudioSource[] PooledSpatialSources(TestFixture fixture)
    {
        var result = new HashSet<AudioSource>();
        foreach (var source in fixture.pool.RootObject.gameObject.GetComponentsInChildren<AudioSource>(true))
            result.Add(source);
        foreach (var source in SpatialSlotSources(fixture.manager))
            if (source != null)
                result.Add(source);

        var array = new AudioSource[result.Count];
        result.CopyTo(array);
        return array;
    }

    private static AudioSource ActiveSpatialSource(SoundManager manager)
    {
        foreach (var source in SpatialSlotSources(manager))
            if (source != null && source.clip != null)
                return source;

        throw new Exception("No active spatial source was found.");
    }

    private static AudioSource[] SpatialSlotSources(SoundManager manager)
    {
        var sources = new AudioSource[manager.SpatialVoiceCapacity];
        for (var i = 0; i < sources.Length; i++)
            sources[i] = SpatialVoiceField<AudioSource>(SpatialVoiceAt(manager, i), "Source");

        return sources;
    }

    private static object SpatialVoiceAt(SoundManager manager, int index)
    {
        var field = typeof(SoundManager).GetField("spatialSounds", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(manager) is not Array voices)
            throw new Exception("SoundManager spatialSounds field is unavailable.");

        return voices.GetValue(index);
    }

    private static T SpatialVoiceField<T>(object voice, string name)
    {
        var info = voice.GetType().GetField(name);
        if (info == null)
            throw new Exception("Missing spatial voice field " + name + ".");

        return (T)info.GetValue(voice);
    }

    private static bool PositionEquals(Vector3 value, float x, float y, float z)
    {
        return Approximately(value.x, x) && Approximately(value.y, y) && Approximately(value.z, z);
    }

    private static void Step(float unscaledDelta)
    {
        Time.unscaledDeltaTime = unscaledDelta;
        ManualFrameScheduler.Step();
    }

    private static void Drain(float unscaledDelta, int maximumFrames = 32)
    {
        for (var frame = 0; frame < maximumFrames && ManualFrameScheduler.PendingCount > 0; frame++)
            Step(unscaledDelta);

        Check(ManualFrameScheduler.PendingCount == 0, "Async sound work did not settle within the frame budget.");
    }

    private static void Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new Exception("Missing lifecycle method " + method + ".");

        info.Invoke(target, null);
    }

    private static void SetField(object target, string field, object value)
    {
        var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new Exception("Missing field " + field + ".");

        info.SetValue(target, value);
    }

    private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;

    private static bool ContainsMessage(Exception error, string message)
    {
        if (error.Message == message)
            return true;
        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (ContainsMessage(inner, message))
                    return true;
        }

        return error.InnerException != null && ContainsMessage(error.InnerException, message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
