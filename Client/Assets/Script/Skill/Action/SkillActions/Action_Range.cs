using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace ProjectT.Skill
{
    [Preserve]
    [SkillAction(SkillActionKind.Range)]
    public class Action_Range : BaseSkillAction
    {
        protected override void Activate()
        {
            End();
        }       
    }
}
