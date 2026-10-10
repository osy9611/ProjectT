namespace ProjectT.Skill
{
    public enum EffectRemoveReason
    {
        Expired,
        Released,
        Removed,
    }

    // 정의의 데이터(수정자, 부여 태그, 주기 피해)로 표현할 수 없는 동작만 확장한다. 정의의 팩토리가 적용마다 새로 만든다.
    public abstract class EffectBehavior
    {
        protected ComBaseActor Owner { get; private set; }
        public ComBaseActor Source { get; private set; }
        public EffectDefinition Definition { get; private set; }

        internal void InitInternal(ComBaseActor owner, ComBaseActor source, EffectDefinition definition)
        {
            Owner = owner;
            Source = source;
            Definition = definition;
        }

        // 수정자와 태그를 건 뒤, 등록 전에 호출된다. 실패하면 효과는 등록되지 않는다.
        public virtual void OnApply()
        {
        }

        // 주기 피해 뒤에 호출된다. Instant는 OnApply 뒤에 한 번 호출된다.
        public virtual void OnExecute()
        {
        }

        // 태그와 수정자를 되돌리기 전에 호출된다. Instant는 등록되지 않으므로 호출되지 않는다.
        // Released는 Actor 정리 중이라 등록이 닫혀 있으므로 연쇄 효과는 Expired일 때만 다음 효과를 적용한다.
        public virtual void OnRemove(EffectRemoveReason reason)
        {
        }
    }
}
