using System.Collections.Generic;

namespace ProjectT.Skill
{
    internal class SkillActionController
    {
        private ComBaseActor owner;
        private SkillAgent agent;
        
        private Dictionary<int, BaseSkillAction> actions = new Dictionary<int, BaseSkillAction>();
        private List<BaseSkillAction> activeActions = new List<BaseSkillAction>();
        // 발동 중인 스킬은 비활성화 순회에서 빠지므로 발동 경로가 Activate 중 비활성화되었는지 판단하는 횟수다.
        private int deactivationCount;

        public SkillActionController(ComBaseActor owner, SkillAgent agent)
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
                if (skillAction.IsActive || skillAction.IsActivating)
                    return;

                SkillDefinition definition = skillAction.Spec.Definition;
                if (HasAnyTagInternal(definition.BlockedTags))
                    return;

                int releases = agent.ReleaseCount;
                int deactivations = deactivationCount;
                if (!skillAction.TryActivateInternal())
                    return;

                // Activate·Commit 콜백에서 Actor가 반납되면(같은 호출에서 재스폰 포함) 정리된 스킬이므로 발동을 알리지 않는다.
                if (releases != agent.ReleaseCount)
                    return;

                // 발동 중인 스킬은 태그 획득·비활성화 취소에서 빠지므로, 이미 가졌거나 Activate 중에 얻은 취소 태그와 Activate 중 비활성화는 여기서 처리한다.
                if (skillAction.IsActive && (deactivations != deactivationCount || HasAnyTagInternal(definition.CancelledByTags)))
                {
                    skillAction.CancelActivationInternal();
                    return;
                }

                if (skillAction.IsActive && !activeActions.Contains(skillAction))
                    activeActions.Add(skillAction);

                // 발동 알림 핸들러가 얻은 취소 태그로 이 스킬이 취소되도록 활성 목록에 넣은 뒤 알린다.
                skillAction.CompleteActivationInternal();
            }
        }

        public void OnUpdate(float deletaTime)
        {
            int count = activeActions.Count;
            int releases = agent.ReleaseCount;
            // OnUpdate 중 Actor가 반납되면 목록이 비워지고, 같은 호출에서 다시 스폰되면 새 스킬이 들어오므로 정리가 일어나면 멈춘다.
            for (int i = 0; i < count && releases == agent.ReleaseCount; i++)
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
            deactivationCount++;
            int count = activeActions.Count;
            int releases = agent.ReleaseCount;
            // Cancel 콜백에서 Actor가 반납되면 목록이 비워지고, 같은 호출에서 다시 스폰되면 새 스킬이 들어오므로 정리가 일어나면 멈춘다.
            for (int i = 0; i < count && releases == agent.ReleaseCount; i++)
            {
                var action = activeActions[i];
                if (action.IsActive && !action.IsActivating)
                    action.Cancel();
            }
        }

        public void CancelSkillsByTag(GameplayTag tag)
        {
            int count = activeActions.Count;
            int releases = agent.ReleaseCount;
            // Cancel 콜백에서 Actor가 반납되면 목록이 비워지고, 같은 호출에서 다시 스폰되면 새 스킬이 들어오므로 정리가 일어나면 멈춘다.
            for (int i = 0; i < count && releases == agent.ReleaseCount; i++)
            {
                var action = activeActions[i];
                if (action.IsActive && !action.IsActivating && ContainsInternal(action.Spec.Definition.CancelledByTags, tag))
                    action.Cancel();
            }
        }

        // 발동마다 호출되므로 IReadOnlyList의 foreach 열거자 할당을 피한다.
        private bool HasAnyTagInternal(IReadOnlyList<GameplayTag> tags)
        {
            for (int i = 0; i < tags.Count; i++)
            {
                if (agent.HasTag(tags[i]))
                    return true;
            }

            return false;
        }

        private static bool ContainsInternal(IReadOnlyList<GameplayTag> tags, GameplayTag tag)
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
