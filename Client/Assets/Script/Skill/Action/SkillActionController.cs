using DesignEnum;
using DesignTable;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Skill
{
    public class SkillActionController
    {
        private BaseActor owner;
        
        private Dictionary<int, BaseSkillAction> actions;
        private List<BaseSkillAction> activeActions;
        private List<BaseSkillAction> removeActions;

        public void Init(BaseActor owner)
        {
            if (owner == null)
            {
                Global.Instance.LogError($"[SkillActionController] This Actor is null");
                return;
            }

            this.owner = owner;
            actions = new Dictionary<int, BaseSkillAction>();
            activeActions = new List<BaseSkillAction>();
            removeActions = new List<BaseSkillAction>();
        }

        public void UnRegisterAbilities()
        {
            CancelAllSkill();
            foreach (var action in actions.Values)
            {
                SkillActionContainer.Return(action.SkillType, action);
            }
            actions.Clear();
        }

        public void RegisterAbility(int skillID)
        {
            skillInfo skillInfo =  Global.Table.SkillInfos.Get(skillID);
            if(skillInfo == null)
            {
                Global.Instance.LogError($"[SkillActionController] SkillInfo Not Found SkillID {skillID}");
                return;
            }

            SkillSpec spec = new SkillSpec();
            spec.Init(skillInfo);

            BaseSkillAction skillAction = SkillActionContainer.Get(skillInfo.skill_type);
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

                if (skillAction.IsActive)
                    activeActions.Add(skillAction);
            }
        }

        public void OnUpdate(float deletaTime)
        {
            foreach(var action in activeActions)
            {
                action.OnUpdate(deletaTime);

                if (!action.IsActive)
                    removeActions.Add(action);
            }

            //쿨다운 업데이트
            foreach(var pair in actions)
            {
                pair.Value.Spec.OnUpdate(deletaTime);
            }

            activeActions.RemoveAll(x => removeActions.Contains(x));
        }

        public void CancelAllSkill()
        {
            foreach (var action in activeActions)
                action.Cancel();

            activeActions.Clear();
        }
    }
}
