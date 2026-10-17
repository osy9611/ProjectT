using System;
using System.Linq;
using ProjectT.Concurrent;
using ProjectT.Server.Byte;
using ProjectT.Server.Messages;
using ProjectT.Server.Stream;

// IPoolable has an unused UnityEngine import; no engine behavior is substituted.
namespace UnityEngine { }

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        var tests = new Action[]
        {
            PoolInitializationMasks,
            PoolConstructorErrors,
            PoolSizeBoundariesAndReturn,
            PoolNegativeRequest,
            EmptyPipePreservesPayload,
            FrameBytesAndReuse,
            PendingFramePrecedesQueuedFrame,
            ClearReturnsPendingAndQueuedStreams,
            ExistingCapacityBoundary,
            SerializationFailureKeepsStreamBorrowed,
            ReturnFailurePreservesPayload,
            ExistingSegmentEnqueueBehavior
        };

        try
        {
            foreach (var test in tests)
            {
                test();
                Console.WriteLine("PASS " + test.Method.Name);
            }

            Console.WriteLine($"ALL PASSED: {tests.Length} cases, {assertions} assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void PoolInitializationMasks()
    {
        foreach (int mask in Enumerable.Range(0, 32).Concat(new[] { (int)InitAllocMask.Everything, -1 }))
        {
            var pool = new BytesPool((InitAllocMask)mask, true, 2, 4);
            var buckets = Buckets(pool);
            for (int i = 0; i < buckets.Length; ++i)
            {
                Equal((mask & (1 << i)) != 0 ? 4 : 2, buckets[i].InactiveCount, "initial count");
                Equal(0, buckets[i].ActiveCount, "initial active count");
            }
        }
    }

    private static void PoolConstructorErrors()
    {
        var pool = new BytesPool(InitAllocMask.Everything, true, -1, 2);
        Check(Buckets(pool).All(bucket => bucket.Count == 2), "masked-out generation count stays ignored");
        Equal("initGenerateCount", Throws<ArgumentOutOfRangeException>(() => new BytesPool(InitAllocMask.Notthing, true, -1, 2)).ParamName, "generation argument");
        Equal("initialCapacity", Throws<ArgumentOutOfRangeException>(() => new BytesPool(InitAllocMask.Notthing, true, 0, -1)).ParamName, "capacity argument");
        Equal("initGenerateCount", Throws<ArgumentOutOfRangeException>(() => new BytesPool(InitAllocMask.Everything, true, 0, -1)).ParamName, "selected capacity is generation argument");
    }

    private static void PoolSizeBoundariesAndReturn()
    {
        var pool = new BytesPool(InitAllocMask.Notthing, true, 0, 1);
        int[] sizes = { 0, 1, 128, 129, 512, 513, 1024, 1025, 2048, 2049, 8192 };
        int[] buckets = { 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4 };
        for (int i = 0; i < sizes.Length; ++i)
        {
            var item = pool.Get(sizes[i]);
            Equal((MemSizeType)buckets[i], item.SizeType, "size bucket");
            Equal(sizes[i], item.ArraySegment.Count, "borrowed length");
            Check(item.Enabled, "pooled bytes enabled");
            Equal(1, Buckets(pool)[buckets[i]].ActiveCount, "borrowed count");
            item.Dispose();
            Check(!item.Enabled && item.ArraySegment.Array == null, "returned bytes state");
            Equal(0, Buckets(pool)[buckets[i]].ActiveCount, "returned count");
            var reused = pool.Get(sizes[i]);
            Check(ReferenceEquals(item, reused), "same bucket reuses bytes");
            reused.Dispose();
        }

        var large = pool.Get(8193);
        Equal(MemSizeType._Large, large.SizeType, "large allocation");
        Equal(8193, large.ArraySegment.Count, "large length");
        Check(!large.Enabled, "existing large allocation activation behavior");
    }

    private static void PoolNegativeRequest()
    {
        var pool = new BytesPool(InitAllocMask.Notthing, true, 0, 1);
        Throws<ArgumentOutOfRangeException>(() => pool.Get(-1));
        Equal(1, pool.Pool128.ActiveCount, "existing error occurs after borrowing");
        pool.Clear();
        Equal(0, pool.Pool128.ActiveCount, "clear returns borrowed bytes");
    }

    private static void EmptyPipePreservesPayload()
    {
        var pipe = new SendPipe(32, 16, true, 1);
        var sentinel = new byte[] { 99 };
        var payload = new ArraySegment<byte>(sentinel);
        Check(!pipe.DequeueAndSerializeAll(ref payload), "empty pipe result");
        Check(ReferenceEquals(sentinel, payload.Array), "empty pipe leaves payload unchanged");
    }

    private static void FrameBytesAndReuse()
    {
        var pipe = new SendPipe(32, 16, true, 2);
        var first = Queue(pipe, eMessageBuiltInDataId.ExternalData, 0x11, 0x22, 0x33);
        var second = Queue(pipe, eMessageBuiltInDataId.Heartbeat);
        var payload = default(ArraySegment<byte>);
        Check(pipe.DequeueAndSerializeAll(ref payload), "serialization result");
        Bytes(new byte[] { 0, 3, 0x27, 0x10, 0x11, 0x22, 0x33, 0, 0, 3, 0xf2 }, payload);
        Equal(0, pipe.Count, "all frames consumed");
        Equal(0, first.Count, "first stream reset on return");
        Equal(0, second.Count, "second stream reset on return");
        Check(ReferenceEquals(second, pipe.GetWriteStream()), "last returned stream is reusable");
    }

    private static void PendingFramePrecedesQueuedFrame()
    {
        var pipe = new SendPipe(12, 16, true, 3);
        var first = Queue(pipe, eMessageBuiltInDataId.ExternalData, 1, 2, 3, 4, 5, 6);
        var second = Queue(pipe, eMessageBuiltInDataId.ExternalData, 7, 8, 9);
        var payload = default(ArraySegment<byte>);
        Check(pipe.DequeueAndSerializeAll(ref payload), "first batch");
        Bytes(new byte[] { 0, 6, 0x27, 0x10, 1, 2, 3, 4, 5, 6 }, payload);
        Equal(1, pipe.Count, "pending counted");
        Equal(0, first.Count, "first returned");
        Equal(3, second.Count, "pending still borrowed");
        var third = Queue(pipe, eMessageBuiltInDataId.Heartbeat, 10);
        Check(pipe.DequeueAndSerializeAll(ref payload), "second batch");
        Bytes(new byte[] { 0, 3, 0x27, 0x10, 7, 8, 9, 0, 1, 3, 0xf2, 10 }, payload);
        Equal(0, pipe.Count, "pending and queue consumed");
        Equal(0, second.Count, "pending returned");
        Equal(0, third.Count, "queued stream returned");
    }

    private static void ClearReturnsPendingAndQueuedStreams()
    {
        var pipe = new SendPipe(8, 16, true, 3);
        Queue(pipe, eMessageBuiltInDataId.ExternalData, 1, 2, 3, 4);
        var pending = Queue(pipe, eMessageBuiltInDataId.ExternalData, 5, 6, 7);
        var payload = default(ArraySegment<byte>);
        pipe.DequeueAndSerializeAll(ref payload);
        var queued = Queue(pipe, eMessageBuiltInDataId.ExternalData, 8, 9);
        Equal(2, pipe.Count, "pending plus queued count");
        pipe.Clear();
        Equal(0, pipe.Count, "clear empties pending and queue");
        Equal(0, pending.Count, "pending returned by clear");
        Equal(0, queued.Count, "queued returned by clear");
    }

    private static void ExistingCapacityBoundary()
    {
        var pipe = new SendPipe(6, 8, true, 1);
        Queue(pipe, eMessageBuiltInDataId.ExternalData, 1, 2, 3, 4);
        var payload = default(ArraySegment<byte>);
        Check(pipe.DequeueAndSerializeAll(ref payload), "existing two-byte capacity check");
        Bytes(new byte[] { 0, 4, 0x27, 0x10, 1, 2, 3, 4 }, payload);
    }

    private static void SerializationFailureKeepsStreamBorrowed()
    {
        var pipe = new SendPipe(2, 8, true, 1);
        var stream = Queue(pipe, eMessageBuiltInDataId.Heartbeat);
        var sentinel = new byte[] { 99 };
        var payload = new ArraySegment<byte>(sentinel);
        Throws<IndexOutOfRangeException>(() => pipe.DequeueAndSerializeAll(ref payload));
        Check(ReferenceEquals(sentinel, payload.Array), "serialization failure leaves payload unchanged");
        Check(!ReferenceEquals(stream, pipe.GetWriteStream()), "failure does not return consumed stream");
    }

    private static void ReturnFailurePreservesPayload()
    {
        var pipe = new SendPipe(16, 8, true, 1);
        var foreign = new NetWriteStream();
        foreign.WriteBytes(new byte[] { 42 }, 0, 1);
        pipe.Enqueue(foreign);
        var sentinel = new byte[] { 99 };
        var payload = new ArraySegment<byte>(sentinel);
        Throws<InvalidOperationException>(() => pipe.DequeueAndSerializeAll(ref payload));
        Equal(0, foreign.Count, "reset precedes rejected return");
        Check(ReferenceEquals(sentinel, payload.Array), "return failure precedes payload assignment");
    }

    private static void ExistingSegmentEnqueueBehavior()
    {
        var pipe = new SendPipe(16, 8, true, 1);
        pipe.Enqueue(new ArraySegment<byte>(new byte[] { 1, 2 }));
        Equal(0, pipe.Count, "existing segment overload does not queue the stream");
        var payload = default(ArraySegment<byte>);
        Check(!pipe.DequeueAndSerializeAll(ref payload), "no segment frame available");
    }

    private static ConcurrentPool<PooledBytes>[] Buckets(BytesPool pool)
    {
        return new[] { pool.Pool128, pool.Pool512, pool.Pool1024, pool.Pool2048, pool.Pool8192 };
    }

    private static NetWriteStream Queue(SendPipe pipe, eMessageBuiltInDataId id, params byte[] bytes)
    {
        var stream = pipe.GetWriteStream(id);
        if (bytes.Length > 0)
            stream.WriteBytes(bytes, 0, bytes.Length);
        pipe.Enqueue(stream);
        return stream;
    }

    private static void Bytes(byte[] expected, ArraySegment<byte> actual)
    {
        Check(expected.SequenceEqual(actual), "serialized frame bytes");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        Check(Equals(expected, actual), message + $": expected {expected}, got {actual}");
    }

    private static void Check(bool value, string message)
    {
        ++assertions;
        if (!value)
            throw new Exception(message);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        ++assertions;
        try
        {
            action();
        }
        catch (T error)
        {
            return error;
        }

        throw new Exception("Expected " + typeof(T).Name);
    }
}
