using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ProjectT;
using ProjectT.Costume;
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
        RunInternal(false);
    }

    public static void RunPrefabSound()
    {
        RunInternal(true);
    }

    private static void RunInternal(bool prefabOnly)
    {
        int exitCode = 0;
        try
        {
            AssetDatabase.CreateFolder("Assets/ProjectTRefactoringHarness", "Fixtures");
            PrefabSoundEdits();
            if (!prefabOnly)
            {
                ConstructorFields();
                AttachmentAndEarlyReturns();
                RendererApplication();
            }
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

    private static void ConstructorFields()
    {
        var mesh = Create("Mesh", typeof(MeshRenderer)).GetComponent<MeshRenderer>();
        var skin = Create("Skin", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
        IArgs args = new Args<int>(2);
        var values = new[] { PartAssetData.Create(3, mesh, false, args), PartAssetData.Create(4, skin, true, args) };
        for (int i = 0; i < values.Length; ++i)
        {
            var value = values[i];
            Check(value.PartIndex == i + 3 && value.SameBoneOrder == (i == 1), "Renderer constructor indices and bone policy");
            Check(value.Renderer == (i == 0 ? (Renderer)mesh : skin) && ReferenceEquals(value.Args, args), "Renderer constructor references");
            Check(value.AssetType == PartAssetType.Renderer_MeshOrSkin && value.GameObject == null && value.Color == null && value.Material == null, "Renderer constructor inactive fields");
        }
        Check(PartAssetData.Create(0, mesh).SameBoneOrder && PartAssetData.Create(0, skin).SameBoneOrder, "Factory default bone policy");
        Debug.Log("REFACTOR_CASE costume constructor fields passed");
    }

    private static void AttachmentAndEarlyReturns()
    {
        var root = Create("Agent", typeof(ComCostumeAgent));
        var agent = root.GetComponent<ComCostumeAgent>();
        var slot = Create("Slot").transform;
        slot.SetParent(root.transform, false);
        slot.position = new Vector3(4, 5, 6);
        var info = new PartSlotInfo { Slot = slot };
        Field(agent, "slots").SetValue(agent, new List<PartSlotInfo> { info });
        var old = Create("Old").transform;
        old.SetParent(slot, false);
        var other = Create("Other").transform;
        other.SetParent(slot, false);
        Vector3 oldPosition = old.position;
        var replacement = Create("Replacement");
        Apply(agent, PartAssetData.Create(0, replacement));
        Check(old.parent == null && old.position == oldPosition, "Detach preserves world position");
        Check(other.parent == slot, "Only first child is detached");
        Check(replacement.transform.parent == slot && replacement.transform.localPosition == Vector3.zero && replacement.transform.localRotation == Quaternion.identity && replacement.transform.localScale == Vector3.one, "Attach resets local transform");
        Check(info.AssetData.Value.GameObject == replacement, "Attach updates slot data");
        Apply(agent, PartAssetData.Create(0, Color.red, new Args<string>("invalid")));
        Check(info.AssetData.Value.GameObject == replacement, "Invalid color args leave slot data unchanged");
        Apply(agent, PartAssetData.Create(0, (Material)null, new Args<string>("invalid")));
        Check(info.AssetData.Value.GameObject == replacement, "Invalid material args leave slot data unchanged");
        Debug.Log("REFACTOR_CASE costume attachment and early return passed");
    }

    private static void RendererApplication()
    {
        var root = Create("RendererAgent", typeof(ComCostumeAgent), typeof(MeshRenderer), typeof(MeshFilter));
        var agent = root.GetComponent<ComCostumeAgent>();
        var existing = root.GetComponent<MeshRenderer>();
        var incoming = Create("Incoming", typeof(MeshRenderer), typeof(MeshFilter)).GetComponent<MeshRenderer>();
        var oldMesh = new Mesh();
        var newMesh = new Mesh();
        objects.Add(oldMesh);
        objects.Add(newMesh);
        root.GetComponent<MeshFilter>().sharedMesh = oldMesh;
        incoming.GetComponent<MeshFilter>().sharedMesh = newMesh;
        var info = new PartSlotInfo { Slot = root.transform, Renderer = existing };
        Field(agent, "slots").SetValue(agent, new List<PartSlotInfo> { info });
        Apply(agent, PartAssetData.Create(0, incoming));
        Check(root.GetComponent<MeshFilter>().sharedMesh == oldMesh, "Existing mesh branch behavior remains unchanged");
        Check(info.AssetData.Value.Renderer == incoming, "Renderer slot data is recorded");

        var shader = Shader.Find("Sprites/Default");
        Check(shader != null, "Fixture shader exists");
        var material = new Material(shader);
        objects.Add(material);
        Apply(agent, PartAssetData.Create(0, material));
        Check(existing.sharedMaterial == material, "Material application");
        Apply(agent, PartAssetData.Create(0, Color.red));
        Check(existing.material.color == Color.red && info.AssetData.Value.Color == Color.red, "Color application");
        objects.Add(existing.material);
        var prior = info.AssetData;
        Apply(agent, PartAssetData.Create(0, Color.blue, new Args<int>(99)));
        Check(info.AssetData.Value.Color == prior.Value.Color, "Out-of-range material slot does not update data");
        Debug.Log("REFACTOR_CASE costume renderer/material/color passed");
    }

    private static void Apply(ComCostumeAgent agent, PartAssetData data)
    {
        var method = typeof(ComCostumeAgent).GetMethod("ChangeOrAttach", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(PartAssetData) }, null);
        method.Invoke(agent, new object[] { data });
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
