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

        public void Init(BaseActor owner)
        {
            if (owner == null)
            {
                Global.Instance.LogError($"[SkillActionController] This Actor is null");
                return;
            }
        }

        public void RegisterAbility(int skillID)
        {
            //Global.Table.SkillInfos.Get(skillID);
        }
    }
}
