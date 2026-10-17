using System.Collections.Generic;

namespace Cysharp.Threading.Tasks { internal sealed class SkillTestPlaceholder { } }

namespace UnityEngine { internal sealed class SkillTestPlaceholder { } }

namespace DesignEnum
{
    public enum SkillType : sbyte
    {
        Normal = 0,
        Melee = 1,
        Range = 2,
    }

    public enum BuffType : sbyte
    {
        AddATK = 0,
        AddDEF = 1,
        LowATK = 2,
        LowDEF = 3,
        Strun = 4,
        Dot = 5,
    }
}

namespace DesignTable
{
    public class skillInfo
    {
        public int skill_Id;
        public float skill_coolTime;
        public sbyte skill_type;
        public int skill_buffId;
    }

    public class buffInfo
    {
        public int buff_Id;
        public sbyte buff_type;
        public float buff_duration;
        public float buff_interval;
        public float buff_usePercent;
    }

    public class FakeTable<T> where T : class
    {
        public readonly Dictionary<int, T> Rows = new Dictionary<int, T>();
        public T Get(int id) => Rows.TryGetValue(id, out var row) ? row : null;
    }

    public class DataMgr
    {
        public readonly FakeTable<skillInfo> SkillInfos = new FakeTable<skillInfo>();
        public readonly FakeTable<buffInfo> BuffInfos = new FakeTable<buffInfo>();
    }
}

namespace ProjectT
{
    // Tests change SpawnSerial to simulate the host releasing, respawning or destroying a caster.
    public abstract class ComBaseActor : ProjectT.Skill.IAbilityOwner
    {
        public ProjectT.Stat.Status Status { get; set; }
        public ProjectT.Skill.SkillAgent SkillAgent { get; set; }
        public int SpawnSerial { get; set; }
    }

    public class Global
    {
        public static DesignTable.DataMgr Table { get; set; } = new DesignTable.DataMgr();
    }
}
