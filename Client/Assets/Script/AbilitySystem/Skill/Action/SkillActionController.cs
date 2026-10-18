using System.Collections.Generic;

namespace ProjectT.Skill
{
    internal class SkillActionController
    {
        private IAbilityOwner owner;
        private SkillAgent agent;
        
        private Dictionary<int, BaseSkillAction> actions = new Dictionary<int, BaseSkillAction>();
        private List<BaseSkillAction> activeActions = new List<BaseSkillAction>();

        public SkillActionController(IAbilityOwner owner, SkillAgent agent)
        {
            this.owner = owner;
            this.agent = agent;
        }

        public void ReleaseAll()
        {
            var snapshot = new List<BaseSkillAction>(actions.Values);
            // 취소가 실패해도 다음 스폰에 스킬이 남지 않도록 목록을 먼저 비운다. 실패한 Actor는 faulted로 격리된다.
            actions.Clear();
            activeActions.Clear();
            foreach (var action in snapshot)
            {
                if (action.IsActive)
                    action.Cancel();
            }
        }


        public void RegisterSkill(SkillDefinition definition)
        {
            if (actions.ContainsKey(definition.Id))
                throw new System.ArgumentException($"[SkillActionController] Already Registered SkillID {definition.Id}");

            BaseSkillAction skillAction = definition.ActionFactory();
            if (skillAction.Spec != null)
                throw new System.InvalidOperationException($"[SkillActionController] ActionFactory returned an action that is already registered. SkillID {definition.Id}");

            skillAction.InitInternal(owner, agent, new SkillSpec(definition));

            actions.Add(definition.Id, skillAction);
        }

        public void ActivateSkill(int skillID)
        {
            if(actions.TryGetValue(skillID,out var skillAction))
            {
                if (skillAction.State != SkillActionState.Idle)
                    return;

                SkillDefinition definition = skillAction.Spec.Definition;
                if (agent.HasAnyTagInternal(definition.BlockedTags))
                    return;

                if (!skillAction.TryActivateInternal())
                    return;

                if (skillAction.IsActive && !activeActions.Contains(skillAction))
                    activeActions.Add(skillAction);

                // 발동 알림 핸들러가 얻은 취소 태그로 이 스킬이 취소되도록 활성 목록에 넣은 뒤 알린다.
                skillAction.CompleteActivationInternal();
            }
        }

        public void OnUpdate(float deletaTime)
        {
            int count = activeActions.Count;
            for (int i = 0; i < count; i++)
            {
                var action = activeActions[i];
                if (action.IsActive)
                    action.OnUpdate(deletaTime);
            }

            //쿨다운 업데이트
            foreach(var pair in actions)
            {
                pair.Value.Spec.OnUpdateInternal(deletaTime);
            }

            activeActions.RemoveAll(x => !x.IsActive);
        }
        
        public void CancelSkill(int skillID)
        {
            if (actions.TryGetValue(skillID, out var skillAction))
            {
                if(skillAction.IsActive)
                {
                    skillAction.Cancel();
                }
            }
        }

        public void CancelActiveSkills()
        {
            int count = activeActions.Count;
            for (int i = 0; i < count; i++)
            {
                var action = activeActions[i];
                if (action.State == SkillActionState.Active)
                    action.Cancel();
            }
        }

        public void CancelSkillsByTag(SkillTag tag)
        {
            int count = activeActions.Count;
            for (int i = 0; i < count; i++)
            {
                var action = activeActions[i];
                if (action.State == SkillActionState.Active && ContainsInternal(action.Spec.Definition.CancelledByTags, tag))
                    action.Cancel();
            }
        }

        private static bool ContainsInternal(IReadOnlyList<SkillTag> tags, SkillTag tag)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }
    }
}
