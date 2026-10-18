using System;
using System.Collections.Generic;
namespace ProjectT.Skill
{
    public class SkillAgent
    {
        private EffectController effectController;
        private SkillActionController actionController;
        // 겹친 효과 중 하나가 먼저 끝나도 태그가 유지되도록 부여 횟수를 센다. 가진 태그만 남긴다.
        private readonly Dictionary<SkillTag, int> tagCounts = new Dictionary<SkillTag, int>();
        private bool isOpen;
        // 콜백에서 Actor가 반납되면 진행 중인 순회가 정리된 상태나 다음 스폰의 상태를 다루므로 호출 중에는 정리를 거부한다.
        private int callDepth;
        // 발동 중인 스킬은 비활성화 순회에서 빠지므로 발동 경로가 Activate 중 비활성화되었는지 판단하는 횟수다.
        private int deactivationCount;

        public event Action<SkillTag, bool> OnTagChanged;
        public event Action<EffectHandle, EffectDefinition> OnEffectApplied;
        public event Action<EffectHandle, EffectDefinition, EffectRemoveReason> OnEffectRemoved;
        // 발동 직후 취소 태그나 Activate 중 비활성화로 취소된 발동은 쿨다운·비용이 이미 적용되었어도 알리지 않는다. 종료는 알린 발동마다 한 번, 발동 알림 뒤에 나간다.
        // Activate 중 끝난 스킬은 이 알림 안에서 다시 발동되지 않으므로 OnSkillEnded에서 다시 발동한다.
        public event Action<SkillDefinition> OnSkillActivated;
        public event Action<SkillDefinition, SkillEndReason> OnSkillEnded;

        internal int DeactivationCount => deactivationCount;
        internal bool IsInCall => callDepth > 0;

        internal readonly struct CallScope : IDisposable
        {
            private readonly SkillAgent agent;

            public CallScope(SkillAgent agent)
            {
                this.agent = agent;
                agent.callDepth++;
            }

            public void Dispose()
            {
                agent.callDepth--;
            }
        }

        internal SkillAgent(IAbilityOwner owner)
        {
            effectController = new EffectController(owner, this);
            actionController = new SkillActionController(owner, this);
        }

        public void GiveSkill(SkillDefinition definition)
        {
            if (!isOpen)
                throw new InvalidOperationException($"[SkillAgent] Registration is closed. SkillID {definition.Id}");

            actionController.RegisterSkill(definition);
        }

        public void ActivateSkill(int skillId)
        {
            // 정리 중(효과 제거 콜백) 발동하면 정리될 스킬이 비용을 쓰고 알림을 내므로 닫힌 동안은 무시한다.
            if (!isOpen)
                return;

            using var scope = EnterCallInternal();
            actionController.ActivateSkill(skillId);
        }

        public void CancelSkill(int skillId)
        {
            using var scope = EnterCallInternal();
            actionController.CancelSkill(skillId);
        }

        public EffectHandle ApplyEffect(EffectDefinition definition, IAbilityOwner source)
        {
            if (!isOpen)
                throw new InvalidOperationException($"[SkillAgent] Registration is closed. EffectID {definition.Id}");

            using var scope = EnterCallInternal();
            return effectController.Apply(definition, source);
        }

        public void RemoveEffect(EffectHandle handle)
        {
            using var scope = EnterCallInternal();
            effectController.Remove(handle);
        }

        public bool HasTag(SkillTag tag)
        {
            return tagCounts.ContainsKey(tag);
        }

        // 발동마다 호출되므로 IReadOnlyList의 foreach 열거자 할당을 피한다.
        internal bool HasAnyTagInternal(IReadOnlyList<SkillTag> tags)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (HasTag(tags[i]))
                    return true;
            }

            return false;
        }

        // 발동 밖(애니메이션 이벤트 등)에서 불리는 End·Cancel·ApplyEffects도 호출 중으로 보도록 스킬 액션이 함께 쓴다.
        internal CallScope EnterCallInternal()
        {
            return new CallScope(this);
        }

        internal void AddTagInternal(SkillTag tag)
        {
            tagCounts.TryGetValue(tag, out int count);
            tagCounts[tag] = count + 1;
            if (count > 0)
                return;

            // 취소 콜백도 태그를 보도록 카운트를 올린 뒤 취소하고, 구독자에게는 취소까지 끝난 뒤 알린다.
            actionController.CancelSkillsByTag(tag);
            OnTagChanged?.Invoke(tag, true);
        }

        internal void RemoveTagInternal(SkillTag tag)
        {
            int count = tagCounts[tag];
            if (count > 1)
            {
                tagCounts[tag] = count - 1;
                return;
            }

            tagCounts.Remove(tag);
            OnTagChanged?.Invoke(tag, false);
        }

        internal void NotifyEffectAppliedInternal(EffectHandle handle, EffectDefinition definition)
        {
            OnEffectApplied?.Invoke(handle, definition);
        }

        internal void NotifyEffectRemovedInternal(EffectHandle handle, EffectDefinition definition, EffectRemoveReason reason)
        {
            OnEffectRemoved?.Invoke(handle, definition, reason);
        }

        internal void NotifySkillActivatedInternal(SkillDefinition definition)
        {
            OnSkillActivated?.Invoke(definition);
        }

        internal void NotifySkillEndedInternal(SkillDefinition definition, SkillEndReason reason)
        {
            OnSkillEnded?.Invoke(definition, reason);
        }

        internal void OpenInternal()
        {
            isOpen = true;
        }

        internal void TickInternal(float deltaTime)
        {
            using var scope = EnterCallInternal();
            effectController.Tick(deltaTime);
            actionController.OnUpdate(deltaTime);
        }

        internal void CancelActiveSkillsInternal()
        {
            using var scope = EnterCallInternal();
            deactivationCount++;
            actionController.CancelActiveSkills();
        }

        internal void ReleaseInternal()
        {
            if (IsInCall)
                throw new InvalidOperationException("[SkillAgent] Cannot release during a skill or effect call.");

            using var scope = EnterCallInternal();
            // 정리 콜백(OnRemove, Cancel)에서 등록하면 다음 스폰으로 넘어가거나 조용히 버려지므로 정리 전에 닫는다.
            isOpen = false;
            effectController.ReleaseAll();
            actionController.ReleaseAll();
            // 실패한 적용이 남긴 태그도 다음 스폰으로 넘기지 않는다. 구독자는 Disable에서 이미 해제했으므로 알리지 않는다.
            tagCounts.Clear();
        }
    }
}
