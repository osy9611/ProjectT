using System;
using System.Collections.Generic;
using System.Linq;
using ProjectT.Sound;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class UISoundToolTests
{
    private static int count;

    private static void Main()
    {
        foreach (string command in new[] { "AddClickSound", "RemoveClickSound", "ChangeClickSound" })
        {
            Run(command + " success", () => Success(command));
            Run(command + " invalid inputs", () => Invalid(command));
            foreach (string failure in new[] { "load", "save", "unload", "save+unload" })
                Run(command + " failure=" + failure, () => Failure(command, failure));
        }
        Run("ChangeClickSound failure=edit", () => Failure("ChangeClickSound", "edit"));
        Run("batch fails before the next prefab", BatchFailure);
        Run("selection indices stay aligned", Selection);
        Console.WriteLine(count + "/" + count + " UISoundTool scenarios passed");
    }

    private static void Run(string name, Action action)
    {
        PrefabUtility.Reset();
        action();
        Check(PrefabUtility.Loaded.Count == 0, "Loaded prefab escaped cleanup.");
        Console.WriteLine("PASS " + name);
        Console.WriteLine("TRACE " + string.Join(" > ", PrefabUtility.Trace));
        count++;
    }

    private static UISoundTool Fixture(out GameObject asset)
    {
        asset = new GameObject("Root");
        var first = new GameObject("SameName");
        first.AddComponent<Button>();
        first.AddComponent<UIClickSound>().ClickSoundType = "existing";
        var second = new GameObject("SameName") { activeSelf = false };
        second.AddComponent<Button>();
        var other = new GameObject("NonButton") { activeSelf = false };
        other.AddComponent<UIClickSound>().ClickSoundType = "non-button";
        first.transform.SetParent(asset.transform, false);
        second.transform.SetParent(asset.transform, false);
        other.transform.SetParent(asset.transform, false);
        AssetDatabase.Assets.Add("fixture.prefab", asset);
        var tool = new UISoundTool();
        tool.prefabs = new List<GameObject> { asset };
        tool.setSoundType = "added";
        tool.GetPrefabComponentList(0);
        int index = 0;
        foreach (var info in tool.prefabSounds.Values)
            info.ClickSoundType = "changed" + index++;
        return tool;
    }

    private static void Success(string command)
    {
        var tool = Fixture(out var asset);
        object oldInfo = tool.prefabSounds[asset.transform.GetChild(0).gameObject];
        Execute(tool, command);
        Equal("load:fixture.prefab > save:fixture.prefab > unload", string.Join(" > ", PrefabUtility.Trace));
        var sounds = asset.GetComponentsInChildren<UIClickSound>(true);
        if (command == "AddClickSound")
        {
            Check(sounds.Length == 3 && sounds[0].ClickSoundType == "existing" && sounds[1].ClickSoundType == "added" && sounds[2].ClickSoundType == "non-button", "Add changed existing data or missed inactive button.");
            Execute(tool, command);
            Check(asset.GetComponentsInChildren<UIClickSound>(true).Length == 3, "Add is not idempotent.");
        }
        else if (command == "RemoveClickSound")
        {
            Check(sounds.Length == 0, "Remove missed a non-button or inactive sound.");
        }
        else
        {
            Check(sounds.Select(x => x.ClickSoundType).SequenceEqual(new[] { "changed0", "changed1" }), "Change failed to apply distinct sibling-path entries.");
            Check(!tool.prefabSounds.Values.Cast<object>().Contains(oldInfo), "Successful Change did not refresh entries.");
        }
    }

    private static void Invalid(string command)
    {
        var tool = Fixture(out var asset);
        var entries = tool.prefabSounds.Values.Cast<object>().ToArray();
        tool.prefabs = new List<GameObject> { null, new GameObject() };
        foreach (int index in new[] { -1, 0, 1, 2 })
        {
            tool.selectPrefabIndex = index;
            Execute(tool, command);
        }
        Check(PrefabUtility.Trace.Count == 0, "Invalid asset scheduled editing.");
        Check(entries.SequenceEqual(tool.prefabSounds.Values.Cast<object>()), "Skipped command refreshed component list.");
    }

    private static void Failure(string command, string stage)
    {
        var tool = Fixture(out var asset);
        var expected = new InvalidOperationException(stage);
        Exception unload = new ArgumentException("unload");
        if (stage == "load")
            PrefabUtility.LoadError = expected;
        if (stage == "edit")
        {
            var key = tool.prefabSounds.Keys.First();
            tool.prefabSounds[key] = null;
        }
        if (stage == "save" || stage == "save+unload")
            PrefabUtility.SaveError = expected;
        if (stage == "unload" || stage == "save+unload")
            PrefabUtility.UnloadError = unload;
        var entries = tool.prefabSounds.Values.Cast<object>().ToArray();
        Exception observed = null;
        try
        {
            Execute(tool, command);
        }
        catch (Exception error)
        {
            observed = error;
        }
        if (stage == "edit")
            Check(observed is NullReferenceException, "Change edit exception did not propagate.");
        else
            Check(ReferenceEquals(observed, stage.Contains("unload") ? unload : expected), "Exception identity/precedence changed.");
        var trace = PrefabUtility.Trace;
        Check(trace.Count(x => x == "unload") == (stage == "load" ? 0 : 1), "Unload must run exactly once after load succeeded.");
        Check(trace.Count(x => x.StartsWith("save:")) == (stage == "load" || stage == "edit" ? 0 : 1), "Save happened after an edit failure or did not happen.");
        Check(entries.SequenceEqual(tool.prefabSounds.Values.Cast<object>()), "Failure refreshed component entries.");
        if (stage != "unload")
            Check(asset.GetComponentsInChildren<UIClickSound>(true).Select(x => x.ClickSoundType).SequenceEqual(new[] { "existing", "non-button" }), "Failed edit/save mutated persistent data.");
    }

    private static void BatchFailure()
    {
        foreach (string command in new[] { "AddClickSound", "RemoveClickSound" })
        {
            PrefabUtility.Reset();
            var tool = Fixture(out var asset);
            tool.prefabs = new List<GameObject> { asset, asset };
            PrefabUtility.SaveError = new InvalidOperationException("stop batch");
            try
            {
                Execute(tool, command);
                throw new Exception("Batch failure was swallowed.");
            }
            catch (InvalidOperationException error)
            {
                Check(ReferenceEquals(error, PrefabUtility.SaveError), "Batch exception changed.");
            }
            Check(PrefabUtility.Trace.Count(x => x.StartsWith("load:")) == 1 && PrefabUtility.Loaded.Count == 0, "Batch continued after failure.");
        }
    }

    private static void Selection()
    {
        var tool = Fixture(out var asset);
        tool.prefabs = new List<GameObject> { null, asset };
        tool.GetPrefabComponentList(1);
        tool.RemovePrefabIndex(0);
        Check(tool.selectPrefabIndex == 0 && tool.prefabSounds.Count == 2, "Selection shifted incorrectly.");
        tool.RemovePrefabIndex(0);
        Check(tool.selectPrefabIndex == -1 && tool.prefabSounds.Count == 0, "Selected removal did not clear entries.");
    }

    private static void Execute(UISoundTool tool, string command)
    {
        switch (command)
        {
            case "AddClickSound": tool.AddClickSound(); break;
            case "RemoveClickSound": tool.RemoveClickSound(); break;
            case "ChangeClickSound": tool.ChangeClickSound(); break;
            default: throw new ArgumentException("Unknown command " + command);
        }
    }

    private static void Equal(string expected, string actual)
    {
        Check(expected == actual, "Expected " + expected + ", got " + actual);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
