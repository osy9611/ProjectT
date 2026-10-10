using Cysharp.Threading.Tasks;
using ProjectT.Skill;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.SkillAdapter
{
    public class Action_Range : BaseSkillAction
    {
        protected override void Activate()
        {
            End();
        }       
    }
}
