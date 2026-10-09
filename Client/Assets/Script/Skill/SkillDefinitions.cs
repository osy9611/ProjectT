using System;
using System.Collections.Generic;
using DesignTable;

namespace ProjectT.Skill
{
    public static class SkillDefinitions
    {
        private static readonly Dictionary<int, SkillDefinition> skills = new Dictionary<int, SkillDefinition>();
        private static readonly Dictionary<int, BuffDefinition> buffs = new Dictionary<int, BuffDefinition>();
        private static DataMgr source;

        public static SkillDefinition GetSkill(int id)
        {
            DataMgr table = GetTableInternal();
            if (skills.TryGetValue(id, out var definition))
                return definition;

            skillInfo info = table.SkillInfos.Get(id);
            if (info == null)
                return null;

            definition = new SkillDefinition(info.skill_Id, info.skill_coolTime, ToSkillActionKindInternal((DesignEnum.SkillType)info.skill_type));
            skills.Add(id, definition);
            return definition;
        }

        public static BuffDefinition GetBuff(int id)
        {
            DataMgr table = GetTableInternal();
            if (buffs.TryGetValue(id, out var definition))
                return definition;

            buffInfo info = table.BuffInfos.Get(id);
            if (info == null)
                return null;

            definition = new BuffDefinition(info.buff_Id, info.buff_duration, info.buff_interval, ToBuffKindInternal((DesignEnum.BuffType)info.buff_type));
            buffs.Add(id, definition);
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
                buffs.Clear();
            }

            return table;
        }

        private static SkillActionKind ToSkillActionKindInternal(DesignEnum.SkillType type)
        {
            switch (type)
            {
                case DesignEnum.SkillType.Normal:
                    return SkillActionKind.Normal;
                case DesignEnum.SkillType.Melee:
                    return SkillActionKind.Melee;
                case DesignEnum.SkillType.Range:
                    return SkillActionKind.Range;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static BuffKind ToBuffKindInternal(DesignEnum.BuffType type)
        {
            switch (type)
            {
                case DesignEnum.BuffType.AddATK:
                    return BuffKind.AddATK;
                case DesignEnum.BuffType.AddDEF:
                    return BuffKind.AddDEF;
                case DesignEnum.BuffType.LowATK:
                    return BuffKind.LowATK;
                case DesignEnum.BuffType.LowDEF:
                    return BuffKind.LowDEF;
                case DesignEnum.BuffType.Strun:
                    return BuffKind.Stun;
                case DesignEnum.BuffType.Dot:
                    return BuffKind.Dot;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
