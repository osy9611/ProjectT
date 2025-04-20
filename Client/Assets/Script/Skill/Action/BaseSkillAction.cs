using DesignTable;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ProjectT.Skill
{
    public class SkillSpec
    {
        public skillInfo skillInfo;
        public float CoolDownRemaining;
        public bool IsOnCoolDown => CoolDownRemaining > 0;

        public void Init(skillInfo skillInfo)
        {
            this.skillInfo = skillInfo;
        }

        public void StartCooldown()
        {
            CoolDownRemaining = skillInfo.skill_coolTime;
        }

        public void OnUpdate(float dt)
        {
            if (CoolDownRemaining > 0)
            {
                CoolDownRemaining -= dt;
            }
        }
    }

    public abstract class BaseSkillAction
    {
        private SkillSpec spec;
        public SkillSpec Spec { get => spec; }

        public DesignEnum.SkillType SkillType { get => (DesignEnum.SkillType)spec.skillInfo.skill_type; }

        private BaseActor owner;
        public BaseActor Owner { get => owner; }
        public bool IsActive;

        public virtual void Init(BaseActor owner, SkillSpec spec)
        {
            if (owner == null)
            {
                Global.Instance.LogError($"[BaseSkillAction] owner Actor is null");
                return;
            }

            if (spec == null)
            {
                Global.Instance.LogError($"[BaseSkillAction] SkillSpec is null");
                return;
            }

            this.owner = owner;
            this.spec = spec;
        }

        //��ų �ߵ� ��û
        public void TryActivate()
        {
            if (!CanActivate()) return;

            IsActive = true;
            Activate(); //���� �ߵ�(�ִϸ��̼� ���� ��)
            Commit();   //��Ÿ�� ����, �ڿ� ���� ��
        }

        protected virtual bool CanActivate()
        {
            return !Spec.IsOnCoolDown;
        }

        // �ߵ� �� �ִϸ��̼� �� ����
        protected abstract void Activate();

        //�ڿ� ����, ��ٿ� ���
        protected virtual void Commit()
        {
            Spec.StartCooldown();
        }

        //������, ����, �� �� ����
        public virtual void ApplyEffect()
        {

        }

        //��ų ����
        public virtual void End()
        {
            IsActive = false;
        }

        //�ߵ� ��� (�ǰ� ��)
        public virtual void Cancel()
        {
            IsActive = false;
        }

        //Update Function
        public virtual void OnUpdate(float deltaTime) { }
    }
}
