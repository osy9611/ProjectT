using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ProjectT.Addressable;

namespace ProjectT
{
    // 원격 번들 부팅과 패치는 Firebase SDK에 의존하므로 에셋 소유권을 담당하는 ResourceManager와 분리한다.
    // ResourceManager보다 먼저 초기화되어야 테이블 등 첫 로드가 준비된 카탈로그를 사용한다.
    public class PatchManager : ManagerBase
    {
        private readonly bool useRemote;

        public PatchManager(bool useRemote = true)
        {
            this.useRemote = useRemote;
        }

        protected override async UniTask OnInitializeAsync(CancellationToken token)
        {
            if (!useRemote)
                return;

            await FirebaseAddressablesManager.InitializeAsync(token);
        }

        public async UniTask<long> GetDownloadSizeAsync(IList<string> labels, CancellationToken cancelToken = default)
        {
            using (var lifetime = CreateLinkedTokenSource(cancelToken))
            {
                await PreWarmAsync(labels, lifetime.Token);

                return await Downloader.GetDownloadSizeAsync(labels, lifetime.Token);
            }
        }

        public async UniTask DownloadAsync(IList<string> labels, Action<DownloadInfo> onProgress = null, CancellationToken cancelToken = default)
        {
            using (var lifetime = CreateLinkedTokenSource(cancelToken))
            {
                await PreWarmAsync(labels, lifetime.Token);

                await Downloader.DownloadAsync(labels, onProgress, lifetime.Token);
            }
        }

        // 다운로드 대상 번들의 gs:// 주소를 실제 다운로드 URL로 바꿔 두어야 크기 조회와 내려받기가 같은 위치를 사용한다.
        private UniTask PreWarmAsync(IList<string> labels, CancellationToken token)
        {
            if (!useRemote)
                return UniTask.CompletedTask;

            return FirebaseAddressablesCache.PreWarmDependenciesAsync(labels, token);
        }

        private CancellationTokenSource CreateLinkedTokenSource(CancellationToken cancelToken)
        {
            LifetimeToken.ThrowIfCancellationRequested();

            if (State != ManagerState.Ready)
                throw new InvalidOperationException($"PatchManager is {State}.");

            return CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, cancelToken);
        }
    }
}
