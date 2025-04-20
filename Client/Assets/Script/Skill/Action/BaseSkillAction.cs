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

        //스킬 발동 요청
        public void TryActivate()
        {
            if (!CanActivate()) return;

            IsActive = true;
            Activate(); //실제 발동(애니메이션 판정 등)
            Commit();   //쿨타임 적용, 자원 차감 등
        }

        protected virtual bool CanActivate()
        {
            return !Spec.IsOnCoolDown;
        }

        // 발동 시 애니메이션 등 실행
        protected abstract void Activate();

        //자원 차감, 쿨다운 등록
        protected virtual void Commit()
        {
            Spec.StartCooldown();
        }

        //데미지, 버프, 힐 등 적용
        public virtual void ApplyEffect()
        {

        }

        //스킬 종료
        public virtual void End()
        {
            IsActive = false;
        }

        //중도 취소 (피격 등)
        public virtual void Cancel()
        {
            IsActive = false;
        }

        //Update Function
        public virtual void OnUpdate(float deltaTime) { }
    }
}
