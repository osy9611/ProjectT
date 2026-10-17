using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Scene;

static class FakeFrames
{
    private static readonly Queue<UniTaskCompletionSource> pending = new();

    public static UniTask Wait(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = new UniTaskCompletionSource();
        pending.Enqueue(source);
        return source.Task.AttachExternalCancellation(token);
    }

    public static void Pump()
    {
        int count = pending.Count;
        while (count-- > 0)
            pending.Dequeue().TrySetResult();
    }

    public static void Clear() => pending.Clear();
}

namespace ProjectT
{
    public sealed class DataManager : ManagerBase
    {
        public DesignTable.DataMgr Table { get; } = new();
    }

    public partial class UIManager : ManagerBase
    {
        public readonly List<ProjectT.UGUI.UIBase> UIStack = new();
        public UnityEngine.Transform Canvas2D = new UnityEngine.GameObject("Canvas").transform;
        public bool InputAllowed = true;
        public Action<bool> InputAllowedAction;
        public int TransientClears;
        public void SetInputAllowed(bool allowed) { InputAllowed = allowed; InputAllowedAction?.Invoke(allowed); }
        public void ClearTransientWidgets() => TransientClears++;
        public string GetUIPath(ProjectT.UGUI.UIDefine.eUIType type) => "Widget";
    }
    public partial class SoundManager : ManagerBase
    {
        public int SpatialStops;
        public void StopAllSpatialSounds() => SpatialStops++;
    }

    public class FakeTables
    {
        public FakeSceneTable SceneDataInfos { get; } = new();
    }

    public sealed class FakeSceneTable
    {
        public FakeSceneInfo Get(string key) => key == "missing" ? null : new FakeSceneInfo { Path = key };
    }

    public sealed class FakeSceneInfo
    {
        public string Path;
    }
}

public sealed class TitleScene : SceneBase
{
    protected override void OnInitialize()
    {
    }

    protected override void OnFinalize()
    {
    }
}

namespace DesignTable
{
    public sealed class DataMgr : ProjectT.FakeTables
    {
        public FakeTable<skillInfo> SkillInfos = new();
        public FakeTable<buffInfo> BuffInfos = new();
    }
}

namespace ProjectT
{
    public enum eSound { Bgm, FX, UI }
}

namespace ProjectT.UGUI
{
    public abstract class UIBase : UnityEngine.MonoBehaviour
    {
        public virtual void OnEnter() { }
        public virtual void OnHide() { }
    }

    public static class UIDefine
    {
        public enum eUIType { Test }
    }
}

namespace ProjectT.Scene
{
    public sealed class SceneTransitionView : UnityEngine.MonoBehaviour
    {
        public static UniTaskCompletionSource ShowGate, HideGate;
        public static Action ShowAction, HideAction;
        public bool Visible;
        public void Initialize() { }
        public async UniTask ShowAsync(CancellationToken token)
        {
            Visible = true;
            ShowAction?.Invoke();
            if (ShowGate != null) await ShowGate.Task;
        }
        public async UniTask HideAsync(CancellationToken token)
        {
            HideAction?.Invoke();
            if (HideGate != null) await HideGate.Task;
        }
        public void ResetView() => Visible = false;
    }
}
