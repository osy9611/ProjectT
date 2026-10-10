using System;
using System.Collections.Generic;
using DesignTable;
using ProjectT.Skill;
using ProjectT.Stat;

namespace ProjectT.SkillAdapter
{
    public static class SkillDefinitions
    {
        public static readonly GameplayTag StunTag = GameplayTag.Register("State.Stun");
        public static readonly StatId MaxHpStat = StatId.Register("MaxHp");
        public static readonly StatId AtkStat = StatId.Register("Atk");
        public static readonly StatId DefStat = StatId.Register("Def");
        public static readonly StatId MoveSpeedStat = StatId.Register("MoveSpeed");
        public static readonly StatId CooldownReductionStat = StatId.Register("CooldownReduction");
        // MaxHpStat을 상한으로 갖는 자원이다. Status를 쓰는 Actor가 DefineResource(HpStat, MaxHpStat)로 정의한다.
        public static readonly StatId HpStat = StatId.Register("Hp");
        // 테이블에 태그 열이 없어 모든 스킬이 스턴에 막히고 취소되는 기존 규칙을 기본값으로 둔다.
        private static readonly GameplayTag[] stunTags = { StunTag };
        private static readonly Dictionary<int, SkillDefinition> skills = new Dictionary<int, SkillDefinition>();
        private static readonly Dictionary<int, EffectDefinition> effects = new Dictionary<int, EffectDefinition>();
        private static readonly DamageExecution flatDamageExecution = new FlatDamageExecution();
        private static DataMgr source;

        public static SkillDefinition GetSkill(int id)
        {
            DataMgr table = GetTableInternal();
            if (skills.TryGetValue(id, out var definition))
                return definition;

            skillInfo info = table.SkillInfos.Get(id);
            // 테이블에 없는 id는 데이터 오류이므로 코어에서 위치 없는 null 참조로 터지지 않게 이 경계에서 드러낸다.
            if (info == null)
                throw new KeyNotFoundException($"[SkillDefinitions] Skill {id} is not in the table");

            // skill_buffId가 -1이면 효과가 없다. 테이블에 비용 열이 없어 비용은 없다.
            IReadOnlyList<EffectDefinition> skillEffects = info.skill_buffId < 0 ? Array.Empty<EffectDefinition>() : new[] { GetEffect(info.skill_buffId) };
            definition = new SkillDefinition(info.skill_Id, info.skill_coolTime, ToActionFactoryInternal((DesignEnum.SkillType)info.skill_type), stunTags, stunTags, CooldownReductionStat, skillEffects, Array.Empty<SkillCost>());
            skills.Add(id, definition);
            return definition;
        }

        public static EffectDefinition GetEffect(int id)
        {
            DataMgr table = GetTableInternal();
            if (effects.TryGetValue(id, out var definition))
                return definition;

            buffInfo info = table.BuffInfos.Get(id);
            if (info == null)
                throw new KeyNotFoundException($"[SkillDefinitions] Effect {id} is not in the table");

            definition = ToEffectDefinitionInternal(info);
            effects.Add(id, definition);
            return definition;
        }

        private static DataMgr GetTableInternal()
        {
            DataMgr table = Global.Table;
            // DataManager는 재시작할 때 새 DataMgr를 만들므로 이전 테이블에서 변환한 정의를 버린다.
            if (!ReferenceEquals(table, source))
            {
                source = table;
                skills.Clear();
                effects.Clear();
            }

            return table;
        }

        private static Func<BaseSkillAction> ToActionFactoryInternal(DesignEnum.SkillType type)
        {
            switch (type)
            {
                case DesignEnum.SkillType.Normal:
                    return () => new Action_Normal();
                case DesignEnum.SkillType.Melee:
                    return () => new Action_Melee();
                case DesignEnum.SkillType.Range:
                    return () => new Action_Range();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        // buff_usePercent는 스탯 변화율(%)이다. 도트 피해량 열이 아직 없어 도트는 buff_usePercent를 1회 피해량으로 임시 해석한다.
        private static EffectDefinition ToEffectDefinitionInternal(buffInfo info)
        {
            IReadOnlyList<EffectModifier> modifiers = Array.Empty<EffectModifier>();
            IReadOnlyList<GameplayTag> grantedTags = Array.Empty<GameplayTag>();
            float power = 0f;
            DamageExecution damageExecution = null;
            StatId damageResource = default;
            var type = (DesignEnum.BuffType)info.buff_type;
            switch (type)
            {
                case DesignEnum.BuffType.AddATK:
                    modifiers = new[] { new EffectModifier(AtkStat, StatModOp.Multiply, 1f + info.buff_usePercent / 100f) };
                    break;
                case DesignEnum.BuffType.AddDEF:
                    modifiers = new[] { new EffectModifier(DefStat, StatModOp.Multiply, 1f + info.buff_usePercent / 100f) };
                    break;
                case DesignEnum.BuffType.LowATK:
                    modifiers = new[] { new EffectModifier(AtkStat, StatModOp.Multiply, 1f - info.buff_usePercent / 100f) };
                    break;
                case DesignEnum.BuffType.LowDEF:
                    modifiers = new[] { new EffectModifier(DefStat, StatModOp.Multiply, 1f - info.buff_usePercent / 100f) };
                    break;
                case DesignEnum.BuffType.Strun:
                    grantedTags = stunTags;
                    break;
                case DesignEnum.BuffType.Dot:
                    power = info.buff_usePercent;
                    damageExecution = flatDamageExecution;
                    damageResource = HpStat;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            return new EffectDefinition(info.buff_Id, EffectDurationPolicy.Duration, info.buff_duration, info.buff_interval, modifiers, grantedTags, power, damageExecution, damageResource, EffectStackPolicy.Refresh, 1, null);
        }
    }
}
