using System;
using System.Collections.Generic;
using System.Text;
using ProjectT;
using ProjectT.Costume;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

internal class CommandScript : IRunCommand
{
    readonly List<Object> objects = new List<Object>();
    readonly StringBuilder trace = new StringBuilder();
    readonly CostumeManager mgr = new CostumeManager();

    public void Execute(ExecutionResult result)
    {
        try
        {
            Constructors();
            Detach();
            Attach();
            ColorAndMaterial();
            MeshBranch();
            SkinBranch();
        }
        catch (Exception e)
        {
            trace.AppendLine("EXCEPTION " + e.GetType().Name + " " + e.Message + " " + e.StackTrace);
        }
        finally
        {
            foreach (var o in objects)
                if (o != null)
                    Object.DestroyImmediate(o);
        }
        result.Log("R4TRACE\n" + trace);
    }

    void T(string s) { trace.AppendLine(s); }

    GameObject Go(string name, params Type[] c) { var g = new GameObject(name, c); g.hideFlags = HideFlags.HideAndDontSave; objects.Add(g); return g; }

    static string D(PartAssetData? d)
    {
        if (!d.HasValue) return "null";
        var v = d.Value;
        return $"{v.PartIndex}|{v.SameBoneOrder}|{v.AssetType}|{(v.Renderer ? v.Renderer.name : "-")}|{(v.GameObject ? v.GameObject.name : "-")}|{v.Color}|{(v.Material ? v.Material.name : "-")}|{v.Args}";
    }

    ComCostumeAgent Agent(GameObject root, Transform[] slots, Renderer[] renderers, bool[] skinned)
    {
        var agent = root.AddComponent<ComCostumeAgent>();
        var so = new SerializedObject(agent);
        var p = so.FindProperty("slots");
        p.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; ++i)
        {
            var e = p.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("Index").intValue = i;
            e.FindPropertyRelative("Slot").objectReferenceValue = slots[i];
            e.FindPropertyRelative("Renderer").objectReferenceValue = renderers[i];
            e.FindPropertyRelative("IsSkinnedMesh").boolValue = skinned[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return agent;
    }

    ComCostumeAgent Agent1(GameObject root, Transform slot, Renderer r = null, bool skinned = false)
    {
        return Agent(root, new[] { slot }, new[] { r }, new[] { skinned });
    }

    void Run(string label, Func<bool> action)
    {
        try { T(label + " ret=" + action()); }
        catch (Exception e) { T(label + " throw " + e.GetType().Name); }
    }

    Material Mat(string name) { var m = new Material(Shader.Find("Sprites/Default")) { name = name }; objects.Add(m); return m; }

    void Constructors()
    {
        var mesh = Go("MeshR", typeof(MeshRenderer)).GetComponent<MeshRenderer>();
        var skin = Go("SkinR", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
        var go = Go("GoAsset");
        var mat = Mat("MatAsset");
        IArgs args = new Args<int>(2);
        T("ctor " + D(PartAssetData.Create(1, mesh)));
        T("ctor " + D(PartAssetData.Create(2, mesh, false, args)));
        T("ctor " + D(PartAssetData.Create(3, skin)));
        T("ctor " + D(PartAssetData.Create(4, skin, false, args)));
        T("ctor " + D(PartAssetData.Create(5, go, args)));
        T("ctor " + D(PartAssetData.Create(6, Color.green, args)));
        T("ctor " + D(PartAssetData.Create(7, mat, args)));
        T("ctor " + D(PartAssetData.Create(8, (MeshRenderer)null)));
        T("ctor " + D(PartAssetData.Create(9, (SkinnedMeshRenderer)null, false)));
        T("ctor " + D(default(PartAssetData)));
    }

    void Detach()
    {
        var root = Go("DetachAgent");
        var slot = Go("Slot").transform; slot.SetParent(root.transform, false); slot.position = new Vector3(1, 2, 3);
        var rslot = Go("RSlot", typeof(MeshRenderer)).transform; rslot.SetParent(root.transform, false);
        var rc = Go("RC").transform; rc.SetParent(rslot, false);
        var empty = Go("Empty").transform; empty.SetParent(root.transform, false);
        var agent = Agent(root, new[] { slot, rslot, empty }, new Renderer[] { null, null, null }, new[] { false, false, false });
        Run("presetGo", () => mgr.ChangeGameObject(agent, 0, null));
        var a = Go("A").transform; a.SetParent(slot, false); a.localPosition = new Vector3(1, 0, 0);
        var b = Go("B").transform; b.SetParent(slot, false);
        Vector3 wp = a.position;
        var info0 = agent.Slots[0];
        bool r = agent.TryDetach(0, out var pre);
        T($"detach0 {r} pre={D(pre)} slot={D(info0.AssetData)} aParent={(a.parent ? a.parent.name : "null")} aPosKept={a.position == wp} bParent={b.parent.name} count={slot.childCount}");
        r = agent.TryDetach(0, out pre);
        T($"detach0again {r} pre={D(pre)} count={slot.childCount} bParent={(b.parent ? b.parent.name : "null")}");
        r = agent.TryDetach(1, out pre);
        T($"detach1 {r} pre={D(pre)} rcParent={(rc.parent ? rc.parent.name : "null")}");
        r = agent.TryDetach(2, out pre);
        T($"detach2 {r} pre={D(pre)} slot={D(agent.Slots[2].AssetData)}");
        try { agent.TryDetach(5, out pre); T("detach5 ok"); } catch (Exception e) { T("detach5 throw " + e.GetType().Name); }
        Run("mgrNullAgent", () => mgr.ChangeGameObject(null, 0, null));
    }

    void Attach()
    {
        var root = Go("AttachAgent");
        var slot = Go("Slot").transform; slot.SetParent(root.transform, false); slot.position = new Vector3(4, 5, 6); slot.localScale = new Vector3(2, 2, 2);
        var old = Go("Old").transform; old.SetParent(slot, false); old.localPosition = new Vector3(1, 1, 1);
        var other = Go("Other").transform; other.SetParent(slot, false);
        var agent = Agent1(root, slot);
        var info = agent.Slots[0];
        var repl = Go("Repl"); repl.transform.position = new Vector3(9, 9, 9); repl.transform.localScale = new Vector3(3, 3, 3); repl.transform.rotation = Quaternion.Euler(10, 20, 30);
        Vector3 wp = old.position;
        Run("attach", () => mgr.ChangeGameObject(agent, 0, repl));
        T($"attach slot={D(info.AssetData)} oldParent={(old.parent ? old.parent.name : "null")} oldKept={old.position == wp} otherParent={other.parent.name} replParent={repl.transform.parent.name} lp={repl.transform.localPosition} lr={repl.transform.localRotation} ls={repl.transform.localScale} order={slot.GetChild(0).name},{slot.GetChild(1).name}");
        Run("attachNull", () => mgr.ChangeGameObject(agent, 0, null));
        T($"attachNull slot={D(info.AssetData)} count={slot.childCount} first={slot.GetChild(0).name} replParent={(repl.transform.parent ? repl.transform.parent.name : "null")}");
        Run("attachArgs", () => mgr.ChangeGameObject(agent, 0, repl, new Args<string>("x")));
        T($"attachArgs slot={D(info.AssetData)} replParent={repl.transform.parent.name}");
        var empty = Go("Empty").transform;
        var agent2 = Agent1(Go("Agent2"), empty);
        Run("attachEmpty", () => mgr.ChangeGameObject(agent2, 0, null));
        T($"attachEmpty count={empty.childCount} slot={D(agent2.Slots[0].AssetData)}");
    }

    void ColorAndMaterial()
    {
        var root = Go("ColorAgent", typeof(MeshRenderer), typeof(MeshFilter));
        var r = root.GetComponent<MeshRenderer>();
        var m0 = Mat("M0"); var m1 = Mat("M1");
        r.sharedMaterials = new[] { m0, m1 };
        var agent = Agent1(root, root.transform, r);
        var info = agent.Slots[0];
        Run("colorIdx", () => mgr.ChangeColor(agent, 0, Color.red, new Args<int>(1)));
        T($"colorIdx slot={D(info.AssetData)} shared={Names(r.sharedMaterials)} c0={r.sharedMaterials[0].color} c1={r.sharedMaterials[1].color} m1orig={m1.color}");
        Track(r);
        Run("colorOOR", () => mgr.ChangeColor(agent, 0, Color.blue, new Args<int>(2)));
        T($"colorOOR slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        Track(r);
        Run("colorNeg", () => mgr.ChangeColor(agent, 0, Color.blue, new Args<int>(-1)));
        T($"colorNeg slot={D(info.AssetData)}");
        Track(r);
        Run("colorType", () => mgr.ChangeColor(agent, 0, Color.blue, new Args<string>("1")));
        T($"colorType slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        Run("colorNoArgs", () => mgr.ChangeColor(agent, 0, Color.yellow));
        T($"colorNoArgs slot={D(info.AssetData)} shared={Names(r.sharedMaterials)} c0={r.sharedMaterials[0].color}");
        Track(r);
        var nm = Mat("NM");
        r.sharedMaterials = new[] { m0, m1 };
        Run("matIdx", () => mgr.ChangeMaterial(agent, 0, nm, new Args<int>(1)));
        T($"matIdx slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        Track(r);
        r.sharedMaterials = new[] { m0, m1 };
        Run("matOOR", () => mgr.ChangeMaterial(agent, 0, nm, new Args<int>(5)));
        T($"matOOR slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        Track(r);
        r.sharedMaterials = new[] { m0, m1 };
        Run("matType", () => mgr.ChangeMaterial(agent, 0, nm, new Args<float>(1)));
        T($"matType slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        r.sharedMaterials = new[] { m0, m1 };
        Run("matNoArgs", () => mgr.ChangeMaterial(agent, 0, nm));
        T($"matNoArgs slot={D(info.AssetData)} shared={Names(r.sharedMaterials)}");
        var noRenderer = Agent1(Go("NoR"), root.transform);
        Run("noRendererColor", () => mgr.ChangeColor(noRenderer, 0, Color.red));
        Run("noRendererMat", () => mgr.ChangeMaterial(noRenderer, 0, nm, new Args<int>(0)));
        T($"noRenderer slot={D(noRenderer.Slots[0].AssetData)}");
    }

    void Track(Renderer r) { foreach (var m in r.sharedMaterials) if (m) objects.Add(m); }

    static string Names(Material[] ms) { var s = new List<string>(); foreach (var m in ms) s.Add(m ? m.name : "null"); return string.Join(",", s); }

    void MeshBranch()
    {
        var root = Go("MeshAgent", typeof(MeshRenderer), typeof(MeshFilter));
        var existing = root.GetComponent<MeshRenderer>();
        var oldMesh = new UnityEngine.Mesh { name = "OldMesh" }; var newMesh = new UnityEngine.Mesh { name = "NewMesh" }; objects.Add(oldMesh); objects.Add(newMesh);
        root.GetComponent<MeshFilter>().sharedMesh = oldMesh;
        existing.sharedMaterial = Mat("ExistingMat");
        var incoming = Go("Incoming", typeof(MeshRenderer), typeof(MeshFilter)).GetComponent<MeshRenderer>();
        incoming.GetComponent<MeshFilter>().sharedMesh = newMesh;
        incoming.sharedMaterial = Mat("IncomingMat");
        var agent = Agent1(root, root.transform, existing);
        Run("mesh", () => mgr.ChangeMeshRenderer(agent, 0, incoming, false));
        T($"mesh slot={D(agent.Slots[0].AssetData)} mesh={root.GetComponent<MeshFilter>().sharedMesh.name} mat={existing.sharedMaterial.name}");
    }

    void SkinBranch()
    {
        var root = Go("SkinAgent");
        var b1 = Go("Hip").transform; b1.SetParent(root.transform, false);
        var b2 = Go("Spine").transform; b2.SetParent(b1, false);
        var hidden = Go("Hand"); hidden.transform.SetParent(b2, false); hidden.SetActive(false);
        var body = Go("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(root.transform, false);
        var src = body.GetComponent<SkinnedMeshRenderer>();
        src.bones = new[] { b1, b2 };
        var srcMesh = new UnityEngine.Mesh { name = "SrcMesh" }; objects.Add(srcMesh); src.sharedMesh = srcMesh; src.sharedMaterial = Mat("SrcMat");
        var other = Go("OtherRig");
        var o1 = Go("Hand").transform; o1.SetParent(other.transform, false);
        var o2 = Go("Hip").transform; o2.SetParent(other.transform, false);
        var o3 = Go("Missing").transform; o3.SetParent(other.transform, false);
        var inc = Go("IncSkin", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
        inc.bones = new[] { o1, o2, o3 };
        var incMesh = new UnityEngine.Mesh { name = "IncMesh" }; objects.Add(incMesh); inc.sharedMesh = incMesh; inc.sharedMaterial = Mat("IncMat");
        var agent = Agent1(root, body.transform, src, true);
        Run("skinSame", () => mgr.SkinnedMeshRenderer(agent, 0, inc));
        T($"skinSame slot={D(agent.Slots[0].AssetData)} mesh={src.sharedMesh.name} mat={src.sharedMaterial.name} bones={Bones(src)}");
        Run("skinRemap", () => mgr.SkinnedMeshRenderer(agent, 0, inc, false));
        T($"skinRemap slot={D(agent.Slots[0].AssetData)} bones={Bones(src)} b0parent={(src.bones[0] ? src.bones[0].parent.name : "-")} hipIsRoot={(src.bones[1] == b1)}");
        Run("skinIdxOOR", () => mgr.SkinnedMeshRenderer(agent, 3, inc));
    }

    static string Bones(SkinnedMeshRenderer r) { var s = new List<string>(); foreach (var b in r.bones) s.Add(b ? b.name : "null"); return string.Join(",", s); }
}
