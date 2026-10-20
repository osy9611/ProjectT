using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ProjectT;
using ProjectT.Sound;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RefactoringProbe
{
    private const string Folder = "Assets/ProjectTRefactoringHarness/Fixtures";
    private static readonly List<Object> objects = new List<Object>();
    private static int checks;

    public static void Run()
    {
        int exitCode = 0;
        try
        {
            AssetDatabase.CreateFolder("Assets/ProjectTRefactoringHarness", "Fixtures");
            PrefabSoundEdits();
            Debug.Log("PROJECTT_REFACTORING_PASS " + checks + " checks");
        }
        catch (Exception error)
        {
            exitCode = 1;
            Debug.LogException(error);
        }
        finally
        {
            foreach (var item in objects)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }
            AssetDatabase.DeleteAsset(Folder);
        }
        EditorApplication.Exit(exitCode);
    }

    private static GameObject Create(string name, params Type[] components)
    {
        var instance = new GameObject(name, components);
        objects.Add(instance);
        return instance;
    }

    private static void PrefabSoundEdits()
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
        Field(tool, "prefabs").SetValue(tool, new List<GameObject> { null, root, prefab });
        Field(tool, "setSoundType").SetValue(tool, "added");
        Call(tool, "AddClickSound");
        var sounds = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true);
        Check(sounds.Length == 2, "Add includes inactive children and skips invalid assets");
        Check(sounds[0].ClickSoundType == "existing" && sounds[1].ClickSoundType == "added", "Add preserves existing components");
        Call(tool, "AddClickSound");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true).Length == 2, "Add is idempotent");
        Call(tool, "GetPrefabComponentList", 2);
        var entries = (IDictionary)Field(tool, "prefabSounds").GetValue(tool);
        object firstKey = null;
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
            Call(tool, "ChangeClickSound");
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
            info.GetType().GetField("ClickSoundType").SetValue(info, "changed" + index++);
        Call(tool, "ChangeClickSound");
        sounds = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true);
        Check(sounds[0].ClickSoundType != sounds[1].ClickSoundType && sounds[0].ClickSoundType.StartsWith("changed") && sounds[1].ClickSoundType.StartsWith("changed"), "Change preserves sibling addressing for duplicate names");
        Check(!ReferenceEquals(savedEntry, entries[firstKey]), "Successful Change refreshes component selection");
        Field(tool, "selectPrefabIndex").SetValue(tool, -1);
        Call(tool, "ChangeClickSound");
        Field(tool, "selectPrefabIndex").SetValue(tool, 1);
        Call(tool, "ChangeClickSound");
        Check(ReferenceEquals(entries, Field(tool, "prefabSounds").GetValue(tool)) && entries.Count == 2, "Invalid selection or asset path leaves component list unchanged");
        Call(tool, "GetPrefabComponentList", 2);
        Call(tool, "RemovePrefabIndex", 0);
        Check((int)Field(tool, "selectPrefabIndex").GetValue(tool) == 1 && entries.Count == 2, "Removing an earlier prefab adjusts selection");
        Call(tool, "RemoveClickSound");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<UIClickSound>(true).Length == 0, "Remove includes inactive children");
        Call(tool, "RemovePrefabIndex", 1);
        Check((int)Field(tool, "selectPrefabIndex").GetValue(tool) == -1 && entries.Count == 0, "Removing selected prefab clears selection");
        Check(EditorSceneManager.previewSceneCount == previewScenes, "Successful edits unload prefab contents");
        Debug.Log("REFACTOR_CASE prefab sound add/change/remove passed");
    }

    private static FieldInfo Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private static void Call(object target, string name, params object[] args)
    {
        try
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }
        catch (TargetInvocationException error)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static void Check(bool value, string message)
    {
        ++checks;
        if (!value)
            throw new InvalidOperationException(message);
    }
}
