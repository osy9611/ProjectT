using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Cysharp.Threading.Tasks
{
    public enum PlayerLoopTiming
    {
        Update
    }

    public readonly partial struct UniTask
    {
        public static UniTask WaitUntil(Func<bool> predicate, PlayerLoopTiming timing = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            if (predicate())
                return CompletedTask;

            return ManualNotificationScheduler.SchedulePredicate(predicate, cancellationToken, cancelImmediately);
        }

        public static UniTask NextFrame(CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            return ManualNotificationScheduler.ScheduleFrame(cancellationToken, cancelImmediately);
        }

        public static UniTask Delay(TimeSpan delayTimeSpan, bool ignoreTimeScale = false, PlayerLoopTiming delayTiming = PlayerLoopTiming.Update,
            CancellationToken cancellationToken = default, bool cancelImmediately = false)
        {
            if (delayTimeSpan < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delayTimeSpan));

            return ManualNotificationScheduler.ScheduleSeconds(delayTimeSpan.TotalSeconds, cancellationToken, cancelImmediately);
        }
    }

    public static class ManualNotificationScheduler
    {
        private sealed class Wait
        {
            public readonly UniTaskCompletionSource Source = new UniTaskCompletionSource();
            public CancellationTokenRegistration Registration;
            public CancellationToken Token;
            public Func<bool> Predicate;
            public int Frames;
            public double Seconds;
        }

        private static readonly List<Wait> pending = new List<Wait>();
        public static int PendingCount => pending.Count;

        internal static UniTask ScheduleFrame(CancellationToken token, bool cancelImmediately)
        {
            return Schedule(1, 0d, token, cancelImmediately);
        }

        internal static UniTask ScheduleSeconds(double seconds, CancellationToken token, bool cancelImmediately)
        {
            return Schedule(0, seconds, token, cancelImmediately);
        }

        internal static UniTask SchedulePredicate(Func<bool> predicate, CancellationToken token, bool cancelImmediately)
        {
            return Schedule(0, 0d, token, cancelImmediately, predicate);
        }

        private static UniTask Schedule(int frames, double seconds, CancellationToken token, bool cancelImmediately, Func<bool> predicate = null)
        {
            var wait = new Wait { Frames = frames, Seconds = seconds, Token = token, Predicate = predicate };
            if (token.IsCancellationRequested)
            {
                wait.Source.TrySetCanceled(token);
                return wait.Source.Task;
            }

            if (cancelImmediately && token.CanBeCanceled)
            {
                wait.Registration = token.Register(state =>
                {
                    var item = (Wait)state;
                    item.Source.TrySetCanceled(item.Token);
                }, wait);
            }

            pending.Add(wait);
            return wait.Source.Task;
        }

        public static void StepFrame()
        {
            Advance(0d, true);
        }

        public static void AdvanceSeconds(double seconds)
        {
            Advance(seconds, false);
        }

        private static void Advance(double seconds, bool frame)
        {
            var snapshot = pending.ToArray();
            foreach (var wait in snapshot)
            {
                if (wait.Token.IsCancellationRequested)
                {
                    Complete(wait, true);
                    continue;
                }

                if (wait.Predicate != null)
                {
                    if (wait.Predicate())
                        Complete(wait, false);
                    continue;
                }

                if (frame && wait.Frames > 0)
                    wait.Frames--;
                if (wait.Seconds > 0d)
                    wait.Seconds -= seconds;

                if (wait.Frames <= 0 && wait.Seconds <= 0d)
                    Complete(wait, false);
            }
        }

        private static void Complete(Wait wait, bool canceled)
        {
            if (!pending.Remove(wait))
                return;

            wait.Registration.Dispose();
            if (canceled)
                wait.Source.TrySetCanceled(wait.Token);
            else
                wait.Source.TrySetResult();
        }

        public static void Reset()
        {
            var snapshot = pending.ToArray();
            pending.Clear();
            foreach (var wait in snapshot)
            {
                wait.Registration.Dispose();
                wait.Source.TrySetCanceled(wait.Token);
            }
        }
    }
}
