using System.Collections.Generic;

namespace ProjectT.Skill
{
    internal class SkillActionController
    {
        private ComBaseActor owner;
        
        private Dictionary<int, BaseSkillAction> actions = new Dictionary<int, BaseSkillAction>();
        private List<BaseSkillAction> activeActions = new List<BaseSkillAction>();

        public SkillActionController(ComBaseActor owner)
        {
            this.owner = owner;
        }

        public void ReleaseAll()
        {
            try
            {
                foreach (var action in actions.Values)
                {
                    if (action.IsActive)
                        action.Cancel();
                }
            }
            finally
            {
                actions.Clear();
                activeActions.Clear();
            }
        }


        public void RegisterSkill(int skillID)
        {
            SkillDefinition definition = SkillDefinitions.GetSkill(skillID);
            if(definition == null)
            {
                Global.LogError($"[SkillActionController] SkillInfo Not Found SkillID {skillID}");
                return;
            }

            if (actions.ContainsKey(skillID))
                throw new System.ArgumentException($"[SkillActionController] Already Registered SkillID {skillID}");

            SkillSpec spec = new SkillSpec();
            spec.Init(definition);

            BaseSkillAction skillAction = SkillActionContainer.Get(definition.Kind);
            skillAction.Init(owner, spec);

            actions.Add(skillID, skillAction);
        }

        public void ActivateSkill(int skillID)
        {
            if(actions.TryGetValue(skillID,out var skillAction))
            {
                if (skillAction.IsActive)
                    return;

                skillAction.TryActivate();

                if (skillAction.IsActive && !activeActions.Contains(skillAction))
                    activeActions.Add(skillAction);
            }
        }

        public void OnUpdate(float deletaTime)
        {
            int count = activeActions.Count;
            // OnUpdate 중 Actor가 반납되면 순회 도중 목록이 비워진다.
            for (int i = 0; i < count && i < activeActions.Count; i++)
            {
                var action = activeActions[i];
                if (action.IsActive)
                    action.OnUpdate(deletaTime);
            }

            //쿨다운 업데이트
            foreach(var pair in actions)
            {
                pair.Value.Spec.OnUpdate(deletaTime);
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
            // Cancel 콜백에서 Actor가 반납되면 순회 도중 목록이 비워진다.
            for (int i = 0; i < count && i < activeActions.Count; i++)
            {
                var action = activeActions[i];
                if (action.IsActive)
                    action.Cancel();
            }
        }
    }
}
