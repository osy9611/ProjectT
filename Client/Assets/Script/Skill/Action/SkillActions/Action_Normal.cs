using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace ProjectT.Skill
{
    [Preserve]
    [SkillAction(SkillActionKind.Normal)]
    public class Action_Normal : BaseSkillAction
    {
        protected override void Activate()
        {
            End();
        }
    }
}