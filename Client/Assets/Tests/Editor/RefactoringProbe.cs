using System;
using System.Collections.Generic;
using ProjectT;
using ProjectT.Sound;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RefactoringProbe
{
    private const string Folder = "Assets/Tests/RefactoringFixtures";
    private static readonly List<Object> objects = new List<Object>();
    private static int checks;

    internal static int Checks => checks;

    internal static void CreateFixtures()
    {
        checks = 0;
        AssetDatabase.CreateFolder("Assets/Tests", "RefactoringFixtures");
    }

    internal static void DeleteFixtures()
    {
        foreach (var item in objects)
        {
            if (item != null)
                Object.DestroyImmediate(item);
        }
        objects.Clear();
        AssetDatabase.DeleteAsset(Folder);
    }

    private static GameObject Create(string name, params Type[] components)
    {
        var instance = new GameObject(name, components);
        objects.Add(instance);
        return instance;
    }

    internal static void PrefabSoundEdits()
    {
        var root = Create("SoundFixture");
        var first = Create("SameName", typeof(RectTransform), typeof(Button), typeof(UIClickSound));
        first.transform.SetParent(root.transform, false);
        first.GetComponent<UIClickSound>().ClickSoundType = "existing";
        var second = Create("SameName", typeof(RectTransform), typeof(Button));
        second.transform.SetParent(root.transform, false);
        second.SetActive(false);
        string path = Folder + "/SoundFixture.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        var tool = ScriptableObject.CreateInstance<UISoundTool>();
        objects.Add(tool);
        int previewScenes = EditorSceneManager.previewSceneCount;
        tool.prefabs = new List<GameObject> { null, root, prefab };
        tool.setSoundType = "added";
        tool.AddClickSound();
        var sounds = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true);
        Check(sounds.Length == 2, "Add includes inactive children and skips invalid assets");
        Check(sounds[0].ClickSoundType == "existing" && sounds[1].ClickSoundType == "added", "Add preserves existing components");
        tool.AddClickSound();
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true).Length == 2, "Add is idempotent");
        tool.GetPrefabComponentList(2);
        var entries = tool.prefabSounds;
        GameObject firstKey = null;
        foreach (var key in entries.Keys)
        {
            firstKey = key;
            break;
        }
        var savedEntry = entries[firstKey];
        entries[firstKey] = null;
        bool failed = false;
        try
        {
            tool.ChangeClickSound();
        }
        catch (NullReferenceException)
        {
            failed = true;
        }
        Check(failed, "Edit failure propagates to caller");
        Check(EditorSceneManager.previewSceneCount == previewScenes, "Edit failure unloads prefab contents");
        Check(entries[firstKey] == null, "Failed Change does not refresh component selection");
        sounds = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true);
        Check(sounds[0].ClickSoundType == "existing" && sounds[1].ClickSoundType == "added", "Failed Change does not save the prefab");
        entries[firstKey] = savedEntry;
        int index = 0;
        foreach (var info in entries.Values)
            info.ClickSoundType = "changed" + index++;
        tool.ChangeClickSound();
        sounds = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true);
        Check(sounds[0].ClickSoundType != sounds[1].ClickSoundType && sounds[0].ClickSoundType.StartsWith("changed") && sounds[1].ClickSoundType.StartsWith("changed"), "Change preserves sibling addressing for duplicate names");
        Check(!ReferenceEquals(savedEntry, entries[firstKey]), "Successful Change refreshes component selection");
        tool.selectPrefabIndex = -1;
        tool.ChangeClickSound();
        tool.selectPrefabIndex = 1;
        tool.ChangeClickSound();
        Check(ReferenceEquals(entries, tool.prefabSounds) && entries.Count == 2, "Invalid selection or asset path leaves component list unchanged");
        tool.GetPrefabComponentList(2);
        tool.RemovePrefabIndex(0);
        Check(tool.selectPrefabIndex == 1 && entries.Count == 2, "Removing an earlier prefab adjusts selection");
        tool.RemoveClickSound();
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true).Length == 0, "Remove includes inactive children");
        tool.RemovePrefabIndex(1);
        Check(tool.selectPrefabIndex == -1 && entries.Count == 0, "Removing selected prefab clears selection");
        Check(EditorSceneManager.previewSceneCount == previewScenes, "Successful edits unload prefab contents");
        Debug.Log("REFACTOR_CASE prefab sound add/change/remove passed");
    }

    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value)
            throw new InvalidOperationException(message);
    }
}
