using System;
using Cysharp.Threading.Tasks;
using ProjectT;
using UnityEngine;

internal static partial class Program
{
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
}
