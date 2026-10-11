using System.Collections.Generic;
using ProjectT.Skill;
using ProjectT.Stat;

namespace ProjectT.Sample.TurnBattle
{
    // 턴제 샘플 유닛이다. 효과·쿨다운은 프레임 시간 대신 자기 턴이 끝날 때 1씩 진행한다.
    public class TurnUnit : ComBaseActor, ITurnUnit
    {
        private readonly HashSet<SkillTag> tags = new HashSet<SkillTag>();

        public TurnUnitSpec Spec { get; private set; }
        public TurnUnit Target { get; set; }
        public SkillAgent TargetAgent => Target == null ? null : Target.SkillAgent;
        public bool Alive { get; private set; }
        public EffectHandle GuardHandle { get; private set; }
        public bool Channeling { get; private set; }
        public bool ResourceOutOfRange { get; private set; }

        // 스폰 단위 집계다. 정리 중 나가는 Released·취소 알림까지 이번 스폰에 포함하고 다음 스폰에서 비운다.
        public int Activated { get; private set; }
        public int Completed { get; private set; }
        public int Cancelled { get; private set; }
        public int EffectsApplied { get; private set; }
        public readonly List<EffectRemoveReason> Removed = new List<EffectRemoveReason>();
        public readonly List<string> Events = new List<string>();

        public float Hp => Status.GetResource(TurnIds.Hp);
        public float Mp => Status.GetResource(TurnIds.Mp);
        public bool Stunned => SkillAgent.HasTag(TurnIds.Stun);
        public int ActiveEffects => EffectsApplied - Removed.Count;

        protected override bool AutoTickSkills => false;

        public void Setup(TurnUnitSpec spec)
        {
            Spec = spec;
            name = spec.Name;
        }

        public bool HasTagEvent(SkillTag tag)
        {
            return tags.Contains(tag);
        }

        public void EndTurn()
        {
            TickSkills(1f);
        }

        protected override void OnInit()
        {
            AddSkillAgent();
            Status status = AddStatus();
            status.DefineResource(TurnIds.Hp, TurnIds.MaxHp);
            status.DefineResource(TurnIds.Mp, TurnIds.MaxMp);

            SkillAgent.OnSkillActivated += OnSkillActivatedInternal;
            SkillAgent.OnSkillEnded += OnSkillEndedInternal;
            SkillAgent.OnEffectApplied += OnEffectAppliedInternal;
            SkillAgent.OnEffectRemoved += OnEffectRemovedInternal;
            SkillAgent.OnTagChanged += OnTagChangedInternal;
            status.OnResourceChanged += OnResourceChangedInternal;
        }

        protected override void OnEnter()
        {
            Activated = Completed = Cancelled = EffectsApplied = 0;
            Removed.Clear();
            Events.Clear();
            ResourceOutOfRange = false;

            Status.SetBase(TurnIds.MaxHp, Spec.Hp);
            Status.SetBase(TurnIds.MaxMp, Spec.Mp);
            Status.SetBase(TurnIds.Atk, Spec.Atk);
            Status.SetBase(TurnIds.Def, Spec.Def);
            Status.SetResource(TurnIds.Hp, Spec.Hp);
            Status.SetResource(TurnIds.Mp, Spec.Mp);
            foreach (var skill in Spec.Skills)
                SkillAgent.GiveSkill(skill);

            if (Spec.ManaRegen)
                SkillAgent.ApplyEffect(TurnDefs.ManaRegen, this);

            Alive = true;
        }

        protected override void OnRelease()
        {
            Alive = false;
            Target = null;
            // 정리는 태그 해제를 알리지 않으므로 구독자가 직접 비운다.
            tags.Clear();
        }

        private void OnSkillActivatedInternal(SkillDefinition definition)
        {
            Activated++;
            if (definition.Id == TurnIds.Meditate)
                Channeling = true;

            Events.Add($"activate {definition.Id}");
        }

        private void OnSkillEndedInternal(SkillDefinition definition, SkillEndReason reason)
        {
            if (reason == SkillEndReason.Completed)
                Completed++;
            else
                Cancelled++;

            if (definition.Id == TurnIds.Meditate)
                Channeling = false;

            Events.Add($"end {definition.Id} {reason}");
        }

        private void OnEffectAppliedInternal(EffectHandle handle, EffectDefinition definition)
        {
            EffectsApplied++;
            if (definition.Id == TurnIds.GuardEffect)
                GuardHandle = handle;

            Events.Add($"effect+ {definition.Id}");
        }

        private void OnEffectRemovedInternal(EffectHandle handle, EffectDefinition definition, EffectRemoveReason reason)
        {
            Removed.Add(reason);
            if (definition.Id == TurnIds.GuardEffect)
                GuardHandle = default;

            Events.Add($"effect- {definition.Id} {reason}");
        }

        private void OnTagChangedInternal(SkillTag tag, bool added)
        {
            if (added)
                tags.Add(tag);
            else
                tags.Remove(tag);

            Events.Add($"tag {tag.Name} {added}");
        }

        private void OnResourceChangedInternal(StatId resource, float previous, float current)
        {
            float max = Status.Get(resource == TurnIds.Hp ? TurnIds.MaxHp : TurnIds.MaxMp);
            if (current < 0f || current > max)
                ResourceOutOfRange = true;
        }
    }
}
