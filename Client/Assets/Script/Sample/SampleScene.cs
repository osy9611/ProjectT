using Cysharp.Threading.Tasks;
using ProjectT.Scene;
using System;
using System.Threading;

namespace ProjectT.Sample
{
    // SceneManager는 씬을 타입으로 등록하므로 Additive로 함께 올릴 A·B를 별도 타입으로 두고 동작은 여기서 공유한다.
    public abstract class SampleScene : SceneBase
    {
        public override void OnInitialize()
        {
            FeatureSampleRunner.Write($"{GetType().Name} OnInitialize");
        }

        // data[0]: 진입 지연(초), data[1]: true이면 진입 실패
        public override async UniTask OnEnter(CancellationToken token, params object[] data)
        {
            float delay = data.Length > 0 ? (float)data[0] : 0f;
            bool fail = data.Length > 1 && (bool)data[1];
            FeatureSampleRunner.Write($"{GetType().Name} OnEnter (delay {delay}s, fail {fail})");

            await UniTask.WaitForSeconds(delay, cancellationToken: token);
            if (fail)
                throw new InvalidOperationException($"[Sample] {GetType().Name} OnEnter failure test");
        }

        public override void OnLoadingAnimEnd()
        {
            FeatureSampleRunner.Write($"{GetType().Name} Active");
        }

        public override void OnFinalize()
        {
            FeatureSampleRunner.Write($"{GetType().Name} OnFinalize");
        }

        public override void OnExit()
        {
            FeatureSampleRunner.Write($"{GetType().Name} OnExit");
        }
    }
}
