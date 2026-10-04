using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    // 초기화 실패·종료 중 오류도 받아야 하므로 Ready일 때만 조회되는 매니저가 아닌 정적 클래스로 둔다.
    // Report와 로그 훅은 어느 스레드에서나 호출되며 큐에 넣기만 한다. Flush·SetSinks와 집계 상태는 메인 스레드 전용이다.
    public static class ErrorReporter
    {
        private const int MaxDeliveriesPerFingerprint = 5;
        private const int MaxQueuedEvents = 1000;
        private const int MaxFingerprints = 1000;
        private const string OverflowFingerprint = "Overflow";

        // 자신이 콘솔에 쓴 로그를 Unity 로그 훅이 다시 수집하지 않도록 구분한다.
        [ThreadStatic]
        private static bool s_writing;
        private static readonly ConcurrentQueue<ErrorEvent> s_queue = new ConcurrentQueue<ErrorEvent>();
        private static int s_queued;
        private static int s_dropped;
        private static readonly Dictionary<string, int> s_counts = new Dictionary<string, int>();
        private static readonly List<ErrorEvent> s_batch = new List<ErrorEvent>();
        private static IErrorSink[] s_sinks = Array.Empty<IErrorSink>();

        // 시스템 등록 시점. 도메인 리로드 없이 Play Mode에 들어와도 상태와 구독이 중복되지 않게 한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            s_queue.Clear();
            s_queued = 0;
            s_dropped = 0;
            s_counts.Clear();
            s_batch.Clear();
            s_sinks = Array.Empty<IErrorSink>();
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            UniTaskScheduler.UnobservedTaskException -= Report;
            UniTaskScheduler.UnobservedTaskException += Report;
        }

        internal static void Report(Exception error)
        {
            if (error == null || error is OperationCanceledException)
                return;

            var id = Guid.NewGuid();
            var inners = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : null;    
            if (inners == null || inners.Count == 0)
            {
                WriteExceptionInternal(error);
                EnqueueInternal(new ErrorEvent(id, error.GetType().FullName, error.Message, error.StackTrace, error));
                return;
            }

            if (AllCanceledInternal(inners))
                return;

            // ErrorCollector가 묶은 오류는 내부 예외별 지문으로 나누고 같은 사건 id로 묶는다.
            WriteExceptionInternal(error);
            foreach (var inner in inners)
            {
                if (!(inner is OperationCanceledException))
                    EnqueueInternal(new ErrorEvent(id, inner.GetType().FullName, inner.Message, inner.StackTrace, inner));
            }
        }

        internal static void SetSinks(params IErrorSink[] sinks)
        {
            s_sinks = sinks ?? Array.Empty<IErrorSink>();
        }

        internal static void Flush()
        {
            if (s_queue.IsEmpty && Volatile.Read(ref s_dropped) == 0)
                return;

            int dropped = Interlocked.Exchange(ref s_dropped, 0);
            if (dropped > 0)
                AddToBatchInternal(new ErrorEvent(Guid.NewGuid(), ErrorEvent.LogKind, $"{dropped} error events dropped", null, null));

            // 다른 스레드가 계속 넣어도 한 번의 Flush가 끝나도록 시작 시점의 개수만 꺼낸다.
            int pending = Volatile.Read(ref s_queued);
            while (pending-- > 0 && s_queue.TryDequeue(out var error))
            {
                Interlocked.Decrement(ref s_queued);
                AddToBatchInternal(error);
            }

            if (s_batch.Count == 0)
                return;

            var sinks = s_sinks;
            bool writing = s_writing;
            s_writing = true;
            try
            {
                foreach (var sink in sinks)
                {
                    try
                    {
                        sink.Write(s_batch);
                    }
                    catch (Exception sinkError)
                    {
                        // 실패한 sink는 이번 세션에서 제외한다. 남겨 두면 자신의 실패를 다시 받아 매 프레임 실패를 반복한다.
                        RemoveSinkInternal(sink);
                        Report(sinkError);
                    }
                }
            }
            finally
            {
                s_writing = writing;
                s_batch.Clear();
            }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (s_writing || (type != LogType.Exception && type != LogType.Error && type != LogType.Assert))
                return;

            string kind = ErrorEvent.LogKind;
            if (type == LogType.Exception && condition != null)
            {
                // Unity는 예외 로그를 "타입 이름: 메시지" 형식으로 넘긴다.
                int separator = condition.IndexOf(':');
                if (separator > 0)
                    kind = condition.Substring(0, separator);
            }

            EnqueueInternal(new ErrorEvent(Guid.NewGuid(), kind, condition, stackTrace, null));
        }

        private static void WriteExceptionInternal(Exception error)
        {
            bool writing = s_writing;
            s_writing = true;
            try
            {
                Debug.LogException(error);
            }
            finally
            {
                s_writing = writing;
            }
        }

        // 메인 스레드가 Flush하지 못하는 동안에도 메모리가 무한히 늘지 않도록 넘친 이벤트는 개수만 남긴다.
        private static void EnqueueInternal(in ErrorEvent error)
        {
            if (Interlocked.Increment(ref s_queued) > MaxQueuedEvents)
            {
                Interlocked.Decrement(ref s_queued);
                Interlocked.Increment(ref s_dropped);
                return;
            }

            s_queue.Enqueue(error);
        }

        private static void AddToBatchInternal(in ErrorEvent error)
        {
            string fingerprint = error.MakeFingerprint();
            // 지문 종류가 계속 늘어나는 오류가 집계 사전을 키우지 않도록 상한 이후의 새 지문은 한 칸에서 센다.
            string key = s_counts.Count < MaxFingerprints || s_counts.ContainsKey(fingerprint) ? fingerprint : OverflowFingerprint;
            s_counts.TryGetValue(key, out int count);
            s_counts[key] = ++count;
            // 매 프레임 터지는 오류가 sink를 채우지 않도록 처음 몇 번과 10의 거듭제곱 번째만 전달한다.
            if (count <= MaxDeliveriesPerFingerprint || IsPowerOfTenInternal(count))
                s_batch.Add(error.WithFingerprint(fingerprint, count));
        }

        private static bool AllCanceledInternal(IReadOnlyList<Exception> errors)
        {
            foreach (var error in errors)
            {
                if (!(error is OperationCanceledException))
                    return false;
            }

            return true;
        }

        private static bool IsPowerOfTenInternal(int value)
        {
            while (value % 10 == 0)
                value /= 10;

            return value == 1;
        }

        private static void RemoveSinkInternal(IErrorSink sink)
        {
            int index = Array.IndexOf(s_sinks, sink);
            if (index < 0)
                return;

            var sinks = new IErrorSink[s_sinks.Length - 1];
            Array.Copy(s_sinks, 0, sinks, 0, index);
            Array.Copy(s_sinks, index + 1, sinks, index, sinks.Length - index);
            s_sinks = sinks;
        }
    }
}
