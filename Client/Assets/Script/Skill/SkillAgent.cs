using System;
namespace ProjectT.Skill
{
    public class SkillAgent
    {
        private BuffController buffController;
        private SkillActionController actionController;
        private bool isOpen;

        internal SkillAgent(ComBaseActor owner)
        {
            buffController = new BuffController(owner);
            actionController = new SkillActionController(owner);
        }

        public void GiveSkill(int skillId)
        {
            if (!isOpen)
                throw new InvalidOperationException($"[SkillAgent] Registration is closed. SkillID {skillId}");

            actionController.RegisterSkill(skillId);
        }

        public void ActivateSkill(int skillId)
        {
            actionController.ActivateSkill(skillId);
        }

        public void CancelSkill(int skillId)
        {
            actionController.CancelSkill(skillId);
        }

        public void ApplyBuff(int buffId, ComBaseActor caster)
        {
            if (!isOpen)
                throw new InvalidOperationException($"[SkillAgent] Registration is closed. BuffID {buffId}");

            buffController.Register(buffId, caster);
        }

        internal void OpenInternal()
        {
            isOpen = true;
        }

        internal void TickInternal(float deltaTime)
        {
            buffController.Tick(deltaTime);
            actionController.OnUpdate(deltaTime);
        }

        internal void CancelActiveSkillsInternal()
        {
            actionController.CancelActiveSkills();
        }

        internal void ReleaseInternal()
        {
            // 정리 콜백(OnExpire, Cancel)에서 등록하면 다음 스폰으로 넘어가거나 조용히 버려지므로 정리 전에 닫는다.
            isOpen = false;
            try { buffController.ReleaseAll(); }
            finally { actionController.ReleaseAll(); }
        }
    }
}
