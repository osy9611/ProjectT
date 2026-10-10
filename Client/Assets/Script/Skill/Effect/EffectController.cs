using System;
using System.Collections.Generic;
using ProjectT.Stat;
using UnityEngine;
namespace ProjectT.Skill
{
    public readonly struct EffectHandle
    {
        // 0은 등록되지 않은 효과(Instant, 적용 중 정리)를 뜻한다.
        public int Id { get; }

        internal EffectHandle(int id)
        {
            Id = id;
        }
    }

    internal class EffectController
    {
        private class ActiveEffect
        {
            public EffectDefinition Definition;
            public ComBaseActor Source;
            public EffectBehavior Behavior;
            public EffectHandle Handle;
            public int StackCount;
            public double Elapsed;
            public int ExecuteCount;
            // 주기 효과는 한도만큼 실행하고 한 주기 뒤에 만료된다. Refresh는 한도만 늘려 실행 위상을 유지한다.
            public int ExecuteLimit;
            // 주기 없는 효과는 이 경과 시점부터 duration 뒤에 만료된다.
            public double DurationStart;
            public bool IsRemoved;
        }

        // 다른 Actor나 이전 스폰의 핸들이 지금 효과를 가리키지 않도록 모든 컨트롤러가 같은 번호를 이어서 쓴다.
        private static int nextHandleId;

        private ComBaseActor actor;
        private SkillAgent agent;

        private List<ActiveEffect> activeEffects = new List<ActiveEffect>();
        private Dictionary<int, ActiveEffect> activeEffectById = new Dictionary<int, ActiveEffect>();
        // 콜백에서 효과가 제거·추가되어도 Tick 시작 시점의 효과만 순회하도록 쓰는 재사용 목록이다.
        private List<ActiveEffect> ticking = new List<ActiveEffect>();
        // 만료가 확정되어 목록에서 뗐지만 아직 제거 처리 전인 효과다. 다른 효과의 제거 콜백에서 정리되면 ReleaseAll이 끝낸다.
        private List<ActiveEffect> expiring = new List<ActiveEffect>();
        private int releaseCount;

        public EffectController(ComBaseActor actor, SkillAgent agent)
        {
            this.actor = actor;
            this.agent = agent;
        }

        public EffectHandle Apply(EffectDefinition definition, ComBaseActor source)
        {
            bool instant = definition.DurationPolicy == EffectDurationPolicy.Instant;
            if (!instant && activeEffectById.TryGetValue(definition.Id, out ActiveEffect current))
            {
                ReapplyInternal(current);
                return current.Handle;
            }

            // 수정자와 피해에 필요한 Status와 피해 자원을 아무것도 바꾸기 전에 확인해 실패해도 남는 상태가 없게 한다.
            Status status = actor.Status;
            if ((definition.Modifiers.Count > 0 || definition.DamageExecution != null) && status == null)
                throw new InvalidOperationException($"[EffectController] Owner actor has no Status. EffectID {definition.Id}");

            // 정의되지 않은 피해 자원은 GetResource가 예외로 드러낸다.
            if (definition.DamageExecution != null)
                status.GetResource(definition.DamageResource);

            var active = new ActiveEffect { Definition = definition, Source = source, Behavior = definition.BehaviorFactory?.Invoke(), StackCount = 1 };
            active.Behavior?.InitInternal(actor, source, definition);
            // 적용이 실패하면 등록되지 않도록 성공한 뒤에 목록에 넣는다. 이미 건 수정자와 태그는 정리에서 비운다.
            int releases = releaseCount;
            AddModifiersInternal(active);
            foreach (GameplayTag tag in definition.GrantedTags)
            {
                // 앞선 알림이나 태그의 취소 콜백에서 Actor가 반납되면 정리된 Agent에 남은 태그를 걸어 알리지 않는다.
                if (releases != releaseCount)
                    break;

                agent.AddTagInternal(tag);
            }

            active.Behavior?.OnApply();
            // 적용 중 Actor가 반납되면 이미 정리가 끝났으므로 등록하지 않고 다른 효과와 같이 Released로 되돌린다.
            if (releases != releaseCount)
            {
                if (!instant)
                {
                    active.Behavior?.OnRemove(EffectRemoveReason.Released);
                    RevertInternal(active, releases);
                }

                return default;
            }

            // Instant는 등록하지 않으므로 핸들, 제거 콜백, 적용·제거 이벤트가 없다.
            if (instant)
            {
                ExecuteInternal(active);
                return default;
            }

            active.Handle = new EffectHandle(++nextHandleId);
            active.ExecuteLimit = definition.DurationPolicy == EffectDurationPolicy.Infinite ? int.MaxValue : GetTicksInternal(definition.Duration, definition.Period);
            activeEffectById.Add(definition.Id, active);
            activeEffects.Add(active);
            agent.NotifyEffectAppliedInternal(active.Handle, definition);
            // 경과 0 시점의 주기 실행만 처리한다. 만료는 다음 Tick에서 판정한다.
            AdvanceInternal(active);
            return active.Handle;
        }

        public void Remove(EffectHandle handle)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Handle.Id == handle.Id)
                {
                    RemoveInternal(activeEffects[i], EffectRemoveReason.Removed);
                    return;
                }
            }
        }

        public void Tick(float deltaTime)
        {
            ticking.Clear();
            ticking.AddRange(activeEffects);
            // 콜백에서 Refresh되는 효과가 순회 순서와 관계없이 이번 Tick의 시점을 기준으로 하도록 콜백 전에 시간을 모두 진행한다.
            // 콜백에서 새로 적용된 효과는 스냅샷 밖에 있어 다음 Tick부터 진행한다.
            for (int i = 0; i < ticking.Count; i++)
                ticking[i].Elapsed += deltaTime;

            List<ActiveEffect> expired = null;
            int releases = releaseCount;
            // 실행 콜백에서 Actor가 반납되면 목록이 비워지고, 같은 호출에서 다시 스폰되면 새 효과가 들어오므로 정리가 일어나면 멈춘다.
            for (int i = 0; i < ticking.Count && releases == releaseCount; i++)
            {
                ActiveEffect active = ticking[i];
                if (AdvanceInternal(active))
                    (expired ??= new List<ActiveEffect>()).Add(active);
            }

            ticking.Clear();
            if (expired == null)
                return;

            // 판정 뒤 다른 효과의 콜백에서 Refresh되었으면 늘어난 한도까지 실행하고 만료를 다시 판정한다.
            // 만료가 확정된 효과를 모두 뗀 뒤에 제거 콜백을 호출해 목록 순서와 관계없이 같게 하고, 콜백에서 다시 적용하면 항상 새로 적용되게 한다.
            foreach (ActiveEffect active in expired)
            {
                if (AdvanceInternal(active) && !active.IsRemoved)
                {
                    DetachInternal(active);
                    expiring.Add(active);
                }
            }

            while (expiring.Count > 0)
            {
                ActiveEffect active = expiring[0];
                expiring.RemoveAt(0);
                EndInternal(active, EffectRemoveReason.Expired);
            }
        }

        public void ReleaseAll()
        {
            releaseCount++;
            ActiveEffect[] pending = expiring.ToArray();
            ActiveEffect[] snapshot = activeEffects.ToArray();
            // 콜백이 실패해도 다음 스폰에 효과가 남지 않도록 목록을 먼저 비운다. 실패한 Actor는 faulted로 격리된다.
            expiring.Clear();
            activeEffects.Clear();
            activeEffectById.Clear();
            foreach (ActiveEffect active in pending)
                EndInternal(active, EffectRemoveReason.Released);

            foreach (ActiveEffect active in snapshot)
                RemoveInternal(active, EffectRemoveReason.Released);
        }

        // 같은 효과를 다시 적용하면 OnApply 없이 처음 적용한 정의, 시전자, 동작을 유지하고 그 정의의 중첩 정책을 따른다.
        private void ReapplyInternal(ActiveEffect active)
        {
            EffectDefinition definition = active.Definition;
            if (definition.StackPolicy == EffectStackPolicy.Ignore)
                return;

            if (definition.StackPolicy == EffectStackPolicy.Stack && active.StackCount < definition.MaxStack)
            {
                active.StackCount++;
                // Status는 Add와 Multiply의 (값 - 1)을 합산하므로 중첩마다 한 벌 더 거는 것이 배율을 곱한 것과 같다.
                // 떼었다 다시 걸지 않아야 상한 스탯이 잠시 줄며 자원이 깎이거나 Override의 최근 순서가 바뀌지 않는다. Override는 한 번만 건다.
                Status status = actor.Status;
                foreach (EffectModifier modifier in definition.Modifiers)
                {
                    // 스탯 알림에서 이 효과가 제거·정리되면 남은 수정자를 걸지 않는다.
                    if (active.IsRemoved)
                        break;

                    if (modifier.Op != StatModOp.Override)
                        status.AddModifier(modifier.Stat, modifier.Op, modifier.Value, active);
                }
            }

            if (definition.DurationPolicy == EffectDurationPolicy.Infinite)
                return;

            // 지금부터 지속 시간을 다시 세되 즉시 실행하지 않는다. 아직 처리되지 않은 이번 Tick의 실행까지 포함해 세어야 순회 순서와 관계없이 같은 한도가 된다.
            active.ExecuteLimit = GetDueCountInternal(active) + GetTicksInternal(definition.Duration, definition.Period);
            active.DurationStart = active.Elapsed;
        }

        private void AddModifiersInternal(ActiveEffect active)
        {
            Status status = actor.Status;
            foreach (EffectModifier modifier in active.Definition.Modifiers)
                status.AddModifier(modifier.Stat, modifier.Op, modifier.Value, active);
        }

        private void ExecuteInternal(ActiveEffect active)
        {
            EffectDefinition definition = active.Definition;
            int releases = releaseCount;
            if (definition.DamageExecution != null)
            {
                Status target = actor.Status;
                // 파괴된 시전자는 Unity의 == null로 걸러지지만, 풀에 반납된 뒤 다시 스폰된 시전자는 구별하지 못한다.
                Status source = active.Source == null ? null : active.Source.Status;
                float damage = definition.DamageExecution.Execute(source, target, definition.Power * active.StackCount);
                // 교체 가능한 공식이 NaN을 내면 자원이 NaN으로 고정되므로 음수와 함께 무시한다.
                if (damage > 0f)
                    target.SetResource(definition.DamageResource, target.GetResource(definition.DamageResource) - damage);
            }

            // 피해 알림에서 이 효과가 제거되거나 Actor가 반납되었으면(등록되지 않는 Instant 포함) 실행 콜백을 호출하지 않는다.
            if (!active.IsRemoved && releases == releaseCount)
                active.Behavior?.OnExecute();
        }

        private static int GetDueCountInternal(ActiveEffect active)
        {
            int due = active.ExecuteCount;
            while (IsDueInternal(active, due))
                due++;

            return due;
        }

        // Refresh 한도 계산과 실제 실행이 같은 판정을 쓰도록 한 곳에 둔다.
        private static bool IsDueInternal(ActiveEffect active, int count)
        {
            return count < active.ExecuteLimit && count * active.Definition.Period <= (float)active.Elapsed;
        }

        private static int GetTicksInternal(float duration, float period)
        {
            if (period <= 0)
                return 0;

            // 0.9 / 0.3 = 2.9999...처럼 기획값의 나눗셈 오차로 횟수가 1 줄지 않도록 정수 경계 근처는 올린다.
            return Mathf.FloorToInt(duration / period + 1e-4f);
        }

        // 누적은 double로 해 장시간 오차를 막고, 비교는 float인 기획값과 같은 정밀도로 해 경계 프레임이 밀리지 않게 한다.
        private bool AdvanceInternal(ActiveEffect active)
        {
            EffectDefinition definition = active.Definition;
            float period = definition.Period;
            if (period > 0)
            {
                // 실행 콜백에서 이 효과가 Refresh되면 한도가 늘어나므로 실행할 때마다 현재 한도로 비교한다.
                while (!active.IsRemoved && IsDueInternal(active, active.ExecuteCount))
                {
                    active.ExecuteCount++;
                    ExecuteInternal(active);
                }
            }

            if (definition.DurationPolicy == EffectDurationPolicy.Infinite)
                return false;

            if (period <= 0)
                return (float)(active.Elapsed - active.DurationStart) >= definition.Duration;

            return (float)active.Elapsed >= active.ExecuteLimit * period;
        }

        private void RemoveInternal(ActiveEffect active, EffectRemoveReason reason)
        {
            if (active.IsRemoved)
                return;

            DetachInternal(active);
            EndInternal(active, reason);
        }

        private void DetachInternal(ActiveEffect active)
        {
            active.IsRemoved = true;
            activeEffects.Remove(active);
            activeEffectById.Remove(active.Definition.Id);
        }

        // 적용의 역순으로 동작 콜백, 태그, 수정자 순서로 되돌린 뒤 알린다.
        private void EndInternal(ActiveEffect active, EffectRemoveReason reason)
        {
            int releases = releaseCount;
            active.Behavior?.OnRemove(reason);
            RevertInternal(active, releases);
            agent.NotifyEffectRemovedInternal(active.Handle, active.Definition, reason);
        }

        // releases는 적용이나 제거를 시작할 때의 정리 횟수다.
        private void RevertInternal(ActiveEffect active, int releases)
        {
            EffectDefinition definition = active.Definition;
            foreach (GameplayTag tag in definition.GrantedTags)
            {
                // 그 사이 Actor가 반납되면 정리가 카운트를 비웠고, 같은 호출에서 다시 스폰되면 남은 카운트는 새 스폰의 것이므로 회수하지 않는다.
                // 수정자는 효과별 출처로 떼므로 새 스폰의 수정자에 영향이 없다.
                if (releases != releaseCount)
                    break;

                agent.RemoveTagInternal(tag);
            }

            if (definition.Modifiers.Count > 0)
                actor.Status.RemoveModifiers(active);
        }
    }
}
