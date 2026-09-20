using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Addressable;
using ProjectT.Scene;

public class DownloadScene : SceneBase
{
    private static readonly List<string> patchLabels = new List<string> { "default" };

    private int reportedStep = -1;

    public override void OnFinalize()
    {
    }

    public override void OnInitialize()
    {
    }

    public override async UniTask OnEnter(CancellationToken token, params object[] data)
    {
        long size = await Global.Patch.GetDownloadSizeAsync(patchLabels, token);

        Global.Instance.Log($"[DownloadScene] Patch size : {size}");

        if (size <= 0)
            return;

        await Global.Patch.DownloadAsync(patchLabels, OnProgress, token);

        Global.Instance.Log($"[DownloadScene] Patch complete");
    }

    // 프레임마다 호출되므로 10% 단위로만 남긴다.
    private void OnProgress(DownloadInfo info)
    {
        int step = (int)(info.progress * 10f);

        if (step == reportedStep)
            return;

        reportedStep = step;
        Global.Instance.Log($"[DownloadScene] {info.downloadedByte} / {info.size} ({info.progress:P0})");
    }
}
