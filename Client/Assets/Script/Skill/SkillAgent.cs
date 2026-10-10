using System;
using System.Collections.Generic;
namespace ProjectT.Skill
{
    public class SkillAgent
    {
        private EffectController effectController;
        private SkillActionController actionController;
        // 겹친 효과 중 하나가 먼저 끝나도 태그가 유지되도록 부여 횟수를 센다. 가진 태그만 남긴다.
        private readonly Dictionary<GameplayTag, int> tagCounts = new Dictionary<GameplayTag, int>();
        private bool isOpen;
        private int releaseCount;

        public event Action<GameplayTag, bool> OnTagChanged;
        public event Action<EffectHandle, EffectDefinition> OnEffectApplied;
        public event Action<EffectHandle, EffectDefinition, EffectRemoveReason> OnEffectRemoved;
        // 발동 직후 취소 태그나 Activate 중 비활성화로 취소된 발동은 쿨다운·비용이 이미 적용되었어도 알리지 않는다. 종료는 알린 발동마다 한 번, 발동 알림 뒤에 나간다.
        // Activate 중 끝난 스킬은 이 알림 안에서 다시 발동되지 않으므로 OnSkillEnded에서 다시 발동한다.
        public event Action<SkillDefinition> OnSkillActivated;
        // 두 번째 인자는 Cancel(태그, 정리, 직접 취소)로 끝났는지다.
        public event Action<SkillDefinition, bool> OnSkillEnded;

        // 콜백을 실행하는 루프가 그 사이 정리(+같은 호출의 재스폰)되었는지 판단하는 정리 횟수다.
        internal int ReleaseCount => releaseCount;

        internal SkillAgent(ComBaseActor owner)
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

            actionController.ActivateSkill(skillId);
        }

        public void CancelSkill(int skillId)
        {
            actionController.CancelSkill(skillId);
        }

        public EffectHandle ApplyEffect(EffectDefinition definition, ComBaseActor source)
        {
            if (!isOpen)
                throw new InvalidOperationException($"[SkillAgent] Registration is closed. EffectID {definition.Id}");

            return effectController.Apply(definition, source);
        }

        public void RemoveEffect(EffectHandle handle)
        {
            effectController.Remove(handle);
        }

        public bool HasTag(GameplayTag tag)
        {
            return tagCounts.ContainsKey(tag);
        }

        internal void AddTagInternal(GameplayTag tag)
        {
            tagCounts.TryGetValue(tag, out int count);
            tagCounts[tag] = count + 1;
            if (count > 0)
                return;

            // 취소 콜백도 태그를 보도록 카운트를 올린 뒤 취소하고, 구독자에게는 취소까지 끝난 뒤 알린다.
            int releases = releaseCount;
            actionController.CancelSkillsByTag(tag);

            // 취소 콜백에서 Actor가 반납되면 정리가 카운트를 비우고 짝이 되는 해제 알림도 없으므로 알리지 않는다.
            // 같은 호출에서 다시 스폰되어 이 태그를 얻었으면 그 획득이 이미 알렸으므로 태그 보유 여부로 판단하지 않는다.
            if (releases != releaseCount)
                return;

            OnTagChanged?.Invoke(tag, true);
        }

        internal void RemoveTagInternal(GameplayTag tag)
        {
            // 정리 중 제거 콜백에서 다시 정리되면 카운트가 먼저 비워진 뒤 바깥 정리가 남은 효과의 태그를 회수한다.
            if (!tagCounts.TryGetValue(tag, out int count))
                return;

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

        internal void NotifySkillEndedInternal(SkillDefinition definition, bool cancelled)
        {
            OnSkillEnded?.Invoke(definition, cancelled);
        }

        internal void OpenInternal()
        {
            isOpen = true;
        }

        internal void TickInternal(float deltaTime)
        {
            effectController.Tick(deltaTime);
            actionController.OnUpdate(deltaTime);
        }

        internal void CancelActiveSkillsInternal()
        {
            actionController.CancelActiveSkills();
        }

        internal void ReleaseInternal()
        {
            // 정리 콜백(OnRemove, Cancel)에서 등록하면 다음 스폰으로 넘어가거나 조용히 버려지므로 정리 전에 닫는다.
            isOpen = false;
            releaseCount++;
            effectController.ReleaseAll();
            actionController.ReleaseAll();
            // 실패한 적용이 남긴 태그도 다음 스폰으로 넘기지 않는다. 구독자는 Disable에서 이미 해제했으므로 알리지 않는다.
            tagCounts.Clear();
        }
    }
}
