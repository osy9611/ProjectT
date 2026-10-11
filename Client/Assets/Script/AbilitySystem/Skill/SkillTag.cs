using System;
using ProjectT.Stat;

namespace ProjectT.Skill
{
    // 게임이 이름으로 정의하고, 판정은 등록 시 받은 정수 id로 한다. default(id 0)는 등록되지 않은 태그다.
    public readonly struct SkillTag : IEquatable<SkillTag>
    {
        private static readonly NameRegistry registry = new NameRegistry(nameof(SkillTag));

        private readonly int id;

        public string Name { get; }

        private SkillTag(int id, string name)
        {
            this.id = id;
            Name = name;
        }

        public static SkillTag Register(string name) => new SkillTag(registry.Register(name), name);

        public bool Equals(SkillTag other) => id == other.id;

        public override bool Equals(object obj) => obj is SkillTag other && Equals(other);

        public override int GetHashCode() => id;

        public static bool operator ==(SkillTag left, SkillTag right) => left.Equals(right);

        public static bool operator !=(SkillTag left, SkillTag right) => !left.Equals(right);
    }
}
