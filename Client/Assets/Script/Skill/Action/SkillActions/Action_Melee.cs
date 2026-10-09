using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace ProjectT.Skill
{
    [Preserve]
    [SkillAction(SkillActionKind.Melee)]
    public class Action_Melee : BaseSkillAction
    {
        protected override void Activate()
        {
            End();
        }
    }
}
