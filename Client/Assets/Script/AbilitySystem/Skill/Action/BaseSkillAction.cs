using System;
using System.Collections.Generic;
using ProjectT.Stat;

namespace ProjectT.Skill
{
    public class SkillSpec
    {
        public SkillDefinition Definition { get; }
        public float CoolDownRemaining { get; private set; }
        public bool IsOnCoolDown => CoolDownRemaining > 0;
        
        internal SkillSpec(SkillDefinition definition)
        {
            Definition = definition;
        }

        // reduction은 쿨다운 감소율이다. Status가 0 미만을 막으므로 1을 넘는 값만 제한한다.
        internal void StartCooldownInternal(float reduction)
        {
            CoolDownRemaining = Definition.CoolTime * (1f - Math.Min(reduction, 1f));
        }
        

        internal void OnUpdateInternal(float dt)
        {
            if (CoolDownRemaining > 0)
            {
                CoolDownRemaining -= dt;
            }
        }
    }

    public enum SkillEndReason
    {
        Completed,
        // 태그, 정리, 직접 취소로 끝난 경우다.
        Cancelled,
    }

    internal enum SkillActionState
    {
        Idle,
        // 발동이 확정되기 전(Activate, Commit, 발동 직후 태그 판정)이다. 이때 끝나면 종료 이벤트를 발동 이벤트 뒤로 미룬다.
        Activating,
        Active,
        // OnEnd 중이거나 발동 확정 전에 끝나 종료 알림을 미룬 상태다.
        // 이때 같은 스킬을 다시 발동하면 새 발동 알림이 이전 종료 알림보다 먼저 나가므로 막는다. 재발동과 연계는 종료 알림에서 한다.
        Ending
    }

    public abstract class BaseSkillAction
    {
        private SkillSpec spec;
        public SkillSpec Spec { get => spec; }

        private IAbilityOwner owner;
        public IAbilityOwner Owner { get => owner; }
        public bool IsActive => State == SkillActionState.Activating || State == SkillActionState.Active;

        private SkillAgent agent;
        private SkillEndReason pendingEndReason;

        internal SkillActionState State { get; private set; }

        internal void InitInternal(IAbilityOwner owner, SkillAgent agent, SkillSpec spec)
        {
            this.owner = owner;
            this.agent = agent;
            this.spec = spec;
        }

        //스킬 발동 요청. 발동을 알려야 하면 true를 반환한다.
        internal bool TryActivateInternal()
        {
            // CanActivate의 부작용으로 반납·비활성화되는 경우도 잡도록 판정 전에 기록한다.
            int releases = agent.ReleaseCount;
            int deactivations = agent.DeactivationCount;
            if (!CanActivate())
                return false;

            State = SkillActionState.Activating;
            Activate(); //실제 발동(애니메이션 판정 등)
            // Activate에서 Actor가 반납되면 다음 스폰의 자원을 차감하지 않도록 Commit하지 않는다.
            if (agent.IsReleasedSinceInternal(releases))
                return false;

            Commit();   //쿨타임 적용, 자원 차감 등
            // Commit 콜백에서 Actor가 반납되면 정리된 스킬이므로 발동을 알리지 않는다.
            if (agent.IsReleasedSinceInternal(releases))
                return false;

            // 발동 중인 스킬은 태그 획득·비활성화 취소에서 빠지므로, 이미 가졌거나 Activate 중에 얻은 취소 태그와 Activate 중 비활성화는 여기서 처리한다.
            // 이렇게 취소된 스킬은 발동하지 않은 것으로 보고 발동·종료 이벤트를 모두 내지 않는다.
            if (State == SkillActionState.Activating && (deactivations != agent.DeactivationCount || agent.HasAnyTagInternal(Spec.Definition.CancelledByTags)))
            {
                Cancel();
                State = SkillActionState.Idle;
                return false;
            }

            return true;
        }

        internal void CompleteActivationInternal()
        {
            // 발동 알림 핸들러가 End·Cancel하면 그 호출이 종료를 알리므로 알림 전 상태로 판단한다.
            bool ended = State == SkillActionState.Ending;
            // 이미 끝난 발동은 알림 중 Ending을 유지해 같은 스킬의 재발동을 막는다. 재발동은 발동·종료 쌍과 미룬 종료 종류를 깨뜨리므로 종료 알림에서 한다.
            if (!ended)
                State = SkillActionState.Active;

            int releases = agent.ReleaseCount;
            agent.NotifySkillActivatedInternal(Spec.Definition);
            if (ended)
                State = SkillActionState.Idle;
            // 발동 알림에서 Actor가 반납되면 미룬 종료를 새 구독자에게 보내지 않는다.
            if (ended && !agent.IsReleasedSinceInternal(releases))
                agent.NotifySkillEndedInternal(Spec.Definition, pendingEndReason);
        }

        // 비용이 있으면 소유자의 Status가 있어야 하고, 정의되지 않은 자원은 GetResource가 예외로 드러낸다.
        protected virtual bool CanActivate()
        {
            return !Spec.IsOnCoolDown && CanPayCostsInternal();
        }

        // 발동 시 애니메이션 등 실행
        protected abstract void Activate();

        //자원 차감, 쿨다운 등록
        protected virtual void Commit()
        {
            StatId stat = Spec.Definition.CooldownReductionStat;
            Status status = Owner.Status;
            Spec.StartCooldownInternal(stat == default || status == null ? 0f : status.Get(stat));
            IReadOnlyList<SkillCost> costs = Spec.Definition.Costs;
            int releases = agent.ReleaseCount;
            // 자원 알림에서 Actor가 반납되면 남은 비용을 다음 스폰에서 차감하지 않는다.
            for (int i = 0; i < costs.Count && !agent.IsReleasedSinceInternal(releases); i++)
                status.SetResource(costs[i].Resource, status.GetResource(costs[i].Resource) - costs[i].Amount);
        }

        // 대상 선택은 게임 코드가 한다. 소유자에게 걸려면 소유자의 SkillAgent를 넘긴다.
        protected void ApplyEffects(SkillAgent target)
        {
            IReadOnlyList<EffectDefinition> effects = Spec.Definition.Effects;
            int releases = target.ReleaseCount;
            int ownerReleases = agent.ReleaseCount;
            // 앞선 효과의 콜백에서 대상이 반납되면 닫힌 Agent나 다시 스폰된 대상에, 시전자가 반납되면 반납된 출처로 남은 효과를 걸지 않는다.
            for (int i = 0; i < effects.Count && !target.IsReleasedSinceInternal(releases) && !agent.IsReleasedSinceInternal(ownerReleases); i++)
                target.ApplyEffect(effects[i], Owner);
        }

        //스킬 종료
        public void End()
        {
            EndInternal(SkillEndReason.Completed);
        }

        //중도 취소 (피격 등)
        public void Cancel()
        {
            EndInternal(SkillEndReason.Cancelled);
        }

        // 활성 스킬이 끝날 때 한 번 불린다. 이미 비활성 상태이므로 여기서 End·Cancel해도 다시 불리지 않는다.
        protected virtual void OnEnd(SkillEndReason reason) { }

        //Update Function
        public virtual void OnUpdate(float deltaTime) { }

        private bool CanPayCostsInternal()
        {
            IReadOnlyList<SkillCost> costs = Spec.Definition.Costs;
            if (costs.Count == 0)
                return true;

            Status status = Owner.Status;
            if (status == null)
                throw new InvalidOperationException($"[BaseSkillAction] Owner has no Status. SkillID {Spec.Definition.Id}");

            for (int i = 0; i < costs.Count; i++)
            {
                if (status.GetResource(costs[i].Resource) < costs[i].Amount)
                    return false;
            }

            return true;
        }

        private void EndInternal(SkillEndReason reason)
        {
            // 정리가 Cancel 콜백 중인 스킬을 다시 취소하는 것처럼 여러 번 불려도 종료는 한 번만 알린다.
            if (!IsActive)
                return;

            bool wasActivating = State == SkillActionState.Activating;
            State = SkillActionState.Ending;
            int releases = agent.ReleaseCount;
            OnEnd(reason);
            if (!wasActivating)
                State = SkillActionState.Idle;
            // OnEnd에서 Actor가 반납되면 정리된 스킬의 종료를 새 구독자에게 보내지 않는다.
            if (agent.IsReleasedSinceInternal(releases))
                return;

            if (wasActivating)
                pendingEndReason = reason;
            else
                agent.NotifySkillEndedInternal(Spec.Definition, reason);
        }
    }
}
