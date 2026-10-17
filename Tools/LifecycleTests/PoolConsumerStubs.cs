using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace DesignEnum
{
    public enum SkillType : sbyte { Normal = 0, Melee = 1, Range = 2 }
    public enum BuffType : sbyte { AddATK = 0, AddDEF = 1, LowATK = 2, LowDEF = 3, Strun = 4, Dot = 5 }
}
namespace DesignTable
{
    public class skillInfo { public int skill_Id; public sbyte skill_type; public float skill_coolTime; public int skill_buffId; }
    public class buffInfo { public int buff_Id; public sbyte buff_type; public float buff_interval, buff_duration, buff_usePercent; }
    public class FakeTable<T> where T : class
    {
        public readonly Dictionary<int, T> Rows = new();
        public T Get(int id) => Rows.TryGetValue(id, out var row) ? row : null;
    }
}
namespace Cysharp.Threading.Tasks
{
    public partial struct UniTask
    {
        public static UniTask Delay(TimeSpan time, CancellationToken cancellationToken = default) => FakeFrames.Wait(cancellationToken);
    }
}
