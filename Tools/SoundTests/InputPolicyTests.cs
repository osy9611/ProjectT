using System;
using ProjectT;
using UnityEngine;

internal static partial class Program
{
    private static readonly float[] NonFiniteInputs = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
    private static readonly float[] InvalidPitches = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f, 3.01f };

    private static void PitchPolicyMatchesAcrossPaths()
    {
        var setup = Setup(1);
        var oneShot = Source(setup.manager, eSound.FX);
        var bgm = Source(setup.manager, eSound.Bgm);
        foreach (var pitch in InvalidPitches)
        {
            var played = oneShot.OneShots.Count;
            var loads = setup.resources.LoadCount;
            ExpectOutOfRange(() => setup.manager.Play(new AudioClip("2d"), eSound.FX, pitch), $"2D one-shot accepted pitch {pitch}.");
            ExpectOutOfRange(() => setup.manager.Play(new AudioClip("bgm"), eSound.Bgm, pitch), $"BGM accepted pitch {pitch}.");
            ExpectOutOfRange(() => setup.manager.Play("A", eSound.FX, pitch), $"2D path accepted pitch {pitch}.");
            ExpectOutOfRange(() => setup.manager.Play("A", eSound.Bgm, pitch), $"BGM path accepted pitch {pitch}.");
            ExpectOutOfRange(() => setup.manager.PlayFade("A", eSound.FX, 1f, pitch), $"Fade accepted pitch {pitch}.");
            ExpectOutOfRange(() => setup.manager.PlayAt(new AudioClip("3d"), new Vector3(), pitch: pitch), $"3D accepted pitch {pitch}.");
            Check(oneShot.OneShots.Count == played && bgm.clip == null, $"Pitch {pitch} played before it was rejected.");
            Check(setup.resources.LoadCount == loads && setup.manager.ActiveSpatialVoiceCount == 0, $"Pitch {pitch} loaded or acquired a voice.");
        }

        foreach (var pitch in new[] { 0.5f, 3f })
        {
            setup.manager.Play(new AudioClip("2d"), eSound.FX, pitch);
            Check(Approximately(oneShot.pitch, pitch), $"2D one-shot did not apply pitch {pitch}.");
            Check(setup.manager.PlayAt(new AudioClip("3d"), new Vector3(), pitch: pitch), $"3D rejected pitch {pitch}.");
            Check(Approximately(ActiveSpatialSource(setup.manager).pitch, pitch), $"3D did not apply pitch {pitch}.");
            setup.manager.StopAllSpatialSounds();
        }
    }

    private static void VolumePolicyMatchesAcrossPaths()
    {
        var setup = Setup(1);
        setup.manager.SetMasterVolume(0.5f);
        setup.manager.SetCategoryVolume(eSound.FX, 0.5f);
        foreach (var volume in NonFiniteInputs)
        {
            ExpectOutOfRange(() => setup.manager.SetMasterVolume(volume), $"Master volume accepted {volume}.");
            ExpectOutOfRange(() => setup.manager.SetCategoryVolume(eSound.FX, volume), $"Category volume accepted {volume}.");
            ExpectOutOfRange(() => setup.manager.PlayAt(new AudioClip("3d"), new Vector3(), volume: volume), $"3D accepted volume {volume}.");
            Check(Approximately(setup.manager.MasterVolume, 0.5f) && Approximately(setup.manager.GetCategoryVolume(eSound.FX), 0.5f),
                $"Volume {volume} changed a setting before it was rejected.");
            Check(setup.manager.ActiveSpatialVoiceCount == 0, $"Volume {volume} acquired a voice.");
        }

        setup.manager.SetMasterVolume(2f);
        setup.manager.SetCategoryVolume(eSound.FX, -1f);
        Check(Approximately(setup.manager.MasterVolume, 1f) && Approximately(setup.manager.GetCategoryVolume(eSound.FX), 0f),
            "Finite volume settings outside zero to one were not clamped.");
    }

    private static void StoppedManagerIgnoresInvalidArguments()
    {
        var setup = Setup(1);
        setup.lifetime.Cancel();
        setup.manager.Play(new AudioClip("2d"), eSound.FX, float.NaN);
        setup.manager.Play(new AudioClip("bgm"), eSound.Bgm, 0f);
        setup.manager.PlayFade("A", eSound.FX, float.NaN);
        Check(!setup.manager.PlayAt(new AudioClip("3d"), new Vector3(float.NaN, 0f, 0f)), "Stopped manager accepted a spatial request.");
    }

    private static void ExpectOutOfRange(Action action, string message)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new Exception(message);
    }
}
