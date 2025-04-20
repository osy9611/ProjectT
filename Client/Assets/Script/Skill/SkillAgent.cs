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

            if(buffController == null)
            {
                buffController = new BuffController();
                buffController.Init(actor);
            }


        }

        public virtual void AddBuff(int buffId)
        {
            if(buffController != null)
            {
                buffController.Register(buffId);
            }
        }
    }
}