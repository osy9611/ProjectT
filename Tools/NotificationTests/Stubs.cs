using System;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    public enum PlayerLoopTiming
    {
        Update
    }

    public readonly partial struct UniTask
    {
        // Global.cs를 컴파일하기 위한 자리다. 알림 테스트는 Global 준비 대기를 거치지 않는다.
        public static UniTask WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            throw new NotSupportedException();
        }
    }
}
