namespace ProjectT.Skill
{
    public enum BuffKind
    {
        AddATK,
        AddDEF,
        LowATK,
        LowDEF,
        Stun,
        Dot,
    }

    public sealed class BuffDefinition
    {
        public int Id { get; }
        public float Duration { get; }
        public float Interval { get; }
        public BuffKind Kind { get; }

        public BuffDefinition(int id, float duration, float interval, BuffKind kind)
        {
            Id = id;
            Duration = duration;
            Interval = interval;
            Kind = kind;
        }
    }
}
