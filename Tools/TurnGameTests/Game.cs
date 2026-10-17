using System;
using System.Collections.Generic;
using ProjectT.Skill;
using ProjectT.Stat;
using ProjectT.Sample.TurnBattle;

namespace TurnGame
{
    // ComBaseActor 대신 IAbilityOwner를 구현한 턴제 유닛이다. 스폰·사망 순서는 ComBaseActor와 같게 둔다.
    public sealed class Unit : IAbilityOwner, ITurnUnit
    {
        private readonly HashSet<SkillTag> tags = new HashSet<SkillTag>();
        private int spawnSerial;

        public string Name { get; }
        public bool IsPlayer { get; }
        public SkillAgent SkillAgent { get; }
        public Status Status { get; }
        public int SpawnSerial => spawnSerial;
        public bool Alive { get; private set; }
        public Unit Target { get; set; }
        public SkillAgent TargetAgent => Target?.SkillAgent;
        public EffectHandle GuardHandle { get; private set; }

        public int Activated { get; private set; }
        public int Completed { get; private set; }
        public int Cancelled { get; private set; }
        public int EffectsApplied { get; private set; }
        public readonly List<EffectRemoveReason> Removed = new List<EffectRemoveReason>();
        public readonly List<string> Events = new List<string>();
        public bool ResourceOutOfRange { get; private set; }
        public bool Channeling { get; private set; }
        public float Hp => Status.GetResource(TurnIds.Hp);
        public float Mp => Status.GetResource(TurnIds.Mp);
        public bool Stunned => SkillAgent.HasTag(TurnIds.Stun);
        public int ActiveEffects => EffectsApplied - Removed.Count;

        public Unit(string name, bool isPlayer)
        {
            Name = name;
            IsPlayer = isPlayer;
            SkillAgent = new SkillAgent(this);
            Status = new Status();
            Status.DefineResource(TurnIds.Hp, TurnIds.MaxHp);
            Status.DefineResource(TurnIds.Mp, TurnIds.MaxMp);

            SkillAgent.OnSkillActivated += definition =>
            {
                Activated++;
                if (definition.Id == TurnIds.Meditate)
                    Channeling = true;

                Events.Add($"activate {definition.Id}");
            };
            SkillAgent.OnSkillEnded += (definition, reason) =>
            {
                if (reason == SkillEndReason.Completed)
                    Completed++;
                else
                    Cancelled++;

                if (definition.Id == TurnIds.Meditate)
                    Channeling = false;

                Events.Add($"end {definition.Id} {reason}");
            };
            SkillAgent.OnEffectApplied += (handle, definition) =>
            {
                EffectsApplied++;
                if (definition.Id == TurnIds.GuardEffect)
                    GuardHandle = handle;

                Events.Add($"effect+ {definition.Id}");
            };
            SkillAgent.OnEffectRemoved += (handle, definition, reason) =>
            {
                Removed.Add(reason);
                if (definition.Id == TurnIds.GuardEffect)
                    GuardHandle = default;

                Events.Add($"effect- {definition.Id} {reason}");
            };
            SkillAgent.OnTagChanged += (tag, added) =>
            {
                if (added)
                    tags.Add(tag);
                else
                    tags.Remove(tag);

                Events.Add($"tag {tag.Name} {added}");
            };
            Status.OnResourceChanged += (resource, previous, current) =>
            {
                float max = Status.Get(resource == TurnIds.Hp ? TurnIds.MaxHp : TurnIds.MaxMp);
                if (current < 0f || current > max)
                    ResourceOutOfRange = true;
            };
        }

        public bool HasTagEvent(SkillTag tag) => tags.Contains(tag);

        public void Spawn(float hp, float mp, float atk, float def, bool manaRegen, params SkillDefinition[] skills)
        {
            spawnSerial++;
            SkillAgent.OpenInternal();
            Status.SetBase(TurnIds.MaxHp, hp);
            Status.SetBase(TurnIds.MaxMp, mp);
            Status.SetBase(TurnIds.Atk, atk);
            Status.SetBase(TurnIds.Def, def);
            Status.SetResource(TurnIds.Hp, hp);
            Status.SetResource(TurnIds.Mp, mp);
            foreach (var skill in skills)
                SkillAgent.GiveSkill(skill);

            if (manaRegen)
                SkillAgent.ApplyEffect(TurnDefs.ManaRegen, this);

            Alive = true;
        }

        // ComBaseActor의 반납·파괴 순서: 스폰 번호 변경 → 활성 스킬 취소(Disable) → SkillAgent 정리 → Status 정리.
        public void Die()
        {
            spawnSerial++;
            Alive = false;
            SkillAgent.CancelActiveSkillsInternal();
            SkillAgent.ReleaseInternal();
            Status.ReleaseInternal();
            // 정리는 태그 해제를 알리지 않으므로 구독자가 직접 비운다.
            tags.Clear();
        }
    }

    public sealed class Battle
    {
        public readonly List<Unit> Units = new List<Unit>();
        public readonly List<string> Log = new List<string>();
        public int Round { get; private set; }

        public Battle(params Unit[] units)
        {
            Units.AddRange(units);
        }

        // 가드는 시전자의 다음 턴 시작까지 유지되는 게임 규칙이라 핸들로 직접 제거한다.
        public void BeginTurn(Unit unit)
        {
            if (unit.GuardHandle.Id != 0)
                unit.SkillAgent.RemoveEffect(unit.GuardHandle);
        }

        // 발동 여부는 OnSkillActivated 알림으로 판단한다. 막힌 발동(쿨다운·비용·스턴)은 SkillAgent가 조용히 무시한다.
        public bool Act(Unit unit, int skillId, Unit target)
        {
            unit.Target = target;
            int before = unit.Activated;
            unit.SkillAgent.ActivateSkill(skillId);
            bool activated = unit.Activated != before;
            Log.Add($"  {unit.Name} skill {skillId} -> {target?.Name ?? "self"}: {(activated ? "ok" : unit.Stunned ? "blocked (stun)" : "ignored")}");
            RemoveDead();
            return activated;
        }

        public void EndTurn(Unit unit)
        {
            if (!unit.Alive)
                return;

            unit.SkillAgent.TickInternal(1f);
            RemoveDead();
        }

        public void Turn(Unit unit, int skillId, Unit target)
        {
            BeginTurn(unit);
            Act(unit, skillId, target);
            EndTurn(unit);
        }

        public bool IsOver => !Units.Exists(unit => unit.Alive && unit.IsPlayer) || !Units.Exists(unit => unit.Alive && !unit.IsPlayer);

        // 우선순위대로 시도해 처음 발동된 스킬로 턴을 쓴다. 스턴이면 모든 시도가 막힌다.
        public void RunAI(int maxRounds, Func<Unit, IEnumerable<int>> priorities)
        {
            while (!IsOver && Round < maxRounds)
            {
                Round++;
                Log.Add($"Round {Round}");
                foreach (var unit in Units)
                {
                    if (!unit.Alive || IsOver)
                        continue;

                    BeginTurn(unit);
                    var enemy = LowestHpEnemyInternal(unit);
                    bool acted = false;
                    foreach (var skillId in priorities(unit))
                    {
                        if (Act(unit, skillId, TurnIds.IsSelfSkill(skillId) ? null : enemy))
                        {
                            acted = true;
                            break;
                        }

                        if (unit.Stunned)
                            break;
                    }

                    if (!acted)
                        Log.Add($"  {unit.Name} {(unit.Channeling ? "keeps meditating" : "loses the turn")}");

                    EndTurn(unit);
                    Log.Add($"  [{StateInternal()}]");
                }
            }
        }

        private Unit LowestHpEnemyInternal(Unit unit)
        {
            Unit best = null;
            foreach (var other in Units)
            {
                if (other.Alive && other.IsPlayer != unit.IsPlayer && (best == null || other.Hp < best.Hp))
                    best = other;
            }

            return best;
        }

        private string StateInternal()
        {
            var parts = new List<string>();
            foreach (var unit in Units)
                parts.Add(unit.Alive ? $"{unit.Name} {unit.Hp:0.#}hp {unit.Mp:0.#}mp{(unit.Stunned ? " STUN" : "")}" : $"{unit.Name} dead");

            return string.Join(", ", parts);
        }

        public void RemoveDead()
        {
            foreach (var unit in Units)
            {
                if (unit.Alive && unit.Hp <= 0f)
                {
                    unit.Die();
                    Log.Add($"  {unit.Name} dies");
                }
            }
        }
    }
}
