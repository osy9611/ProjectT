using System;
using System.Collections.Generic;
using ProjectT.Stat;

namespace ProjectT.Skill
{
    public readonly struct SkillCost
    {
        public readonly StatId Resource;
        public readonly float Amount;

        public SkillCost(StatId resource, float amount)
        {
            Resource = resource;
            Amount = amount;
        }
    }

    public sealed class SkillDefinition
    {
        public int Id { get; }
        public float CoolTime { get; }
        // GiveSkill마다 호출된다. 소유자마다 실행 상태를 따로 가지므로 매번 새 인스턴스를 반환해야 한다.
        public Func<BaseSkillAction> ActionFactory { get; }
        // 소유자가 이 중 하나라도 가지면 발동하지 않는다.
        public IReadOnlyList<GameplayTag> BlockedTags { get; }
        // 활성 중 소유자가 이 중 하나를 새로 얻거나, 발동 직후 가지고 있으면 취소된다.
        public IReadOnlyList<GameplayTag> CancelledByTags { get; }
        // 쿨다운 감소율로 읽을 스탯이다. default면 감소하지 않는다.
        public StatId CooldownReductionStat { get; }
        // 대상은 실행 클래스가 정해 ApplyEffects로 건다.
        public IReadOnlyList<EffectDefinition> Effects { get; }
        // 소유자의 자원이 모두 양 이상이어야 발동하고 Commit에서 차감된다.
        public IReadOnlyList<SkillCost> Costs { get; }

        public SkillDefinition(int id, float coolTime, Func<BaseSkillAction> actionFactory, IReadOnlyList<GameplayTag> blockedTags, IReadOnlyList<GameplayTag> cancelledByTags, StatId cooldownReductionStat, IReadOnlyList<EffectDefinition> effects, IReadOnlyList<SkillCost> costs)
        {
            ValidateTagsInternal(blockedTags, nameof(blockedTags), id);
            ValidateTagsInternal(cancelledByTags, nameof(cancelledByTags), id);
            // 발동 판정은 비용마다 따로 하고 차감은 합산되므로 같은 자원이 두 번 있으면 보유량보다 많이 빠진다.
            var resources = new HashSet<StatId>();
            foreach (SkillCost cost in costs)
            {
                if (cost.Resource == default)
                    throw new ArgumentException($"[SkillDefinition] Costs cannot use an unregistered resource. SkillID {id}", nameof(costs));

                if (!(cost.Amount >= 0f))
                    throw new ArgumentOutOfRangeException(nameof(costs), cost.Amount, $"[SkillDefinition] Cost amount must be zero or positive. SkillID {id}");

                if (!resources.Add(cost.Resource))
                    throw new ArgumentException($"[SkillDefinition] Costs cannot list the same resource twice. SkillID {id}", nameof(costs));
            }

            Id = id;
            CoolTime = coolTime;
            ActionFactory = actionFactory;
            BlockedTags = blockedTags;
            CancelledByTags = cancelledByTags;
            CooldownReductionStat = cooldownReductionStat;
            Effects = effects;
            Costs = costs;
        }

        private static void ValidateTagsInternal(IReadOnlyList<GameplayTag> tags, string paramName, int id)
        {
            foreach (GameplayTag tag in tags)
            {
                if (tag == default)
                    throw new ArgumentException($"[SkillDefinition] Tags cannot contain an unregistered tag. SkillID {id}", paramName);
            }
        }
    }
}
