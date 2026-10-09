namespace ProjectT.Skill
{

    public class BaseBuff
    {
        protected ComBaseActor ownerActor;
        protected BuffDefinition definition;

        private ComBaseActor caster;
        public ComBaseActor Caster { get => caster; }

        public int BuffID { get => definition.Id; }

        public float Interval { get => definition.Interval; }

        public float Duration { get => definition.Duration; }

        public virtual void Init(ComBaseActor ownerActor, ComBaseActor caster, BuffDefinition definition)
        {
            this.ownerActor = ownerActor;
            this.caster = caster;
            this.definition = definition;
        }

        //활성화가 될때
        public virtual void OnApply()
        {
            Global.Instance.Log($"[BaseBuff] OnApply");
        }

        //실행 중일때
        public virtual void OnExecute()
        {
            Global.Instance.Log($"[BaseBuff] OnExecute");
        }

        //버프 제거될때
        public virtual void OnExpire()
        {
            Global.Instance.Log($"[BaseBuff] OnExpire");
        }
    }
}
