using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.Skill
{
    public class SkillAgent
    {
        protected BaseActor actor;
        private BuffController buffController;
        private SkillEffectController effectController;
        private SkillActionController actionController;
        public SkillActionController ActionController { get => actionController; }

        public virtual void Init(BaseActor actor)
        {
            this.actor = actor;
            buffController = new BuffController();

        }
    }
}