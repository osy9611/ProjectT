using System;

namespace ProjectT.Stat
{
    // 게임이 이름으로 정의하고, 판정은 등록 시 받은 정수 id로 한다. default(id 0)는 등록되지 않은 스탯이다.
    public readonly struct StatId : IEquatable<StatId>
    {
        private static readonly NameRegistry registry = new NameRegistry(nameof(StatId));

        private readonly int id;

        public string Name { get; }

        private StatId(int id, string name)
        {
            this.id = id;
            Name = name;
        }

        public static StatId Register(string name) => new StatId(registry.Register(name), name);

        public bool Equals(StatId other) => id == other.id;

        public override bool Equals(object obj) => obj is StatId other && Equals(other);

        public override int GetHashCode() => id;

        public static bool operator ==(StatId left, StatId right) => left.Equals(right);

        public static bool operator !=(StatId left, StatId right) => !left.Equals(right);
    }
}
