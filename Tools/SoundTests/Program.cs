using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Pool;
using UnityEngine;

internal static partial class Program
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
        Run("Same BGM keeps playing until StopBgm", SameBgmRestartPolicy);
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
        Run("2D and 3D playback apply one pitch policy", PitchPolicyMatchesAcrossPaths);
        Run("2D settings and 3D playback apply one volume policy", VolumePolicyMatchesAcrossPaths);
        Run("Stopped manager ignores invalid arguments", StoppedManagerIgnoresInvalidArguments);
        Run("Sound pool scope release preserves unrelated pools", SoundPoolScopeIsolation);
        Run("Spatial return clears metadata and tolerates repeated stop", SpatialReturnClearsSlot);
        Run("A failed return stays borrowed until shutdown without stopping remaining sound cleanup", FailedReturnCompletesCleanup);
        Run("Canceled lifetime permits returns and rejects new pool gets", CanceledLifetimePoolPolicy);
        Run("External pool clear is detected and playback recovers", ExternalPoolClearRecovery);
        Run("Deferred pool destruction is repaired by same-frame playback", DeferredPoolClearRecovery);
        Run("Shutdown destroys pooled copies before the sound template", SpatialShutdownDestroyOrder);
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

    private static void AdvanceSpatialPolling(SoundManager manager)
    {
        Time.frameCount++;
        manager.OnUpdate(Time.deltaTime);
    }

    private static void AssertEmptySpatialSlot(SoundManager manager)
    {
        var voice = SpatialVoiceAt(manager, 0);
        Check(manager.ActiveSpatialVoiceCount == 0 && !SpatialVoiceField<bool>(voice, "InUse")
            && ReferenceEquals(SpatialVoiceField<GameObject>(voice, "Instance"), null)
            && ReferenceEquals(SpatialVoiceField<AudioSource>(voice, "Source"), null)
            && SpatialVoiceField<float>(voice, "Volume") == 0f, "Slot metadata was not cleared on return.");
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
