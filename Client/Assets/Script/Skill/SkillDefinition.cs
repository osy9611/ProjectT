namespace ProjectT.Skill
{
    public enum SkillActionKind
    {
        Normal,
        Melee,
        Range,
    }

    public sealed class SkillDefinition
    {
        public int Id { get; }
        public float CoolTime { get; }
        public SkillActionKind Kind { get; }

        public SkillDefinition(int id, float coolTime, SkillActionKind kind)
        {
            Id = id;
            CoolTime = coolTime;
            Kind = kind;
        }
    }
}
