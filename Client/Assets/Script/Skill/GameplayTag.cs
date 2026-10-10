using System;
using ProjectT.Stat;

namespace ProjectT.Skill
{
    // 게임이 이름으로 정의하고, 판정은 등록 시 받은 정수 id로 한다. default(id 0)는 등록되지 않은 태그다.
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        private static readonly NameRegistry registry = new NameRegistry(nameof(GameplayTag));

        private readonly int id;

        public string Name { get; }

        private GameplayTag(int id, string name)
        {
            this.id = id;
            Name = name;
        }

        public static GameplayTag Register(string name) => new GameplayTag(registry.Register(name), name);

        public bool Equals(GameplayTag other) => id == other.id;

        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);

        public override int GetHashCode() => id;

        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
    }
}
