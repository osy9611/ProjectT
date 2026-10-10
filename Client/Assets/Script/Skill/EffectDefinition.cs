using System;
using System.Collections.Generic;
using ProjectT.Stat;

namespace ProjectT.Skill
{
    public enum EffectDurationPolicy
    {
        Instant,
        Duration,
        Infinite,
    }

    public enum EffectStackPolicy
    {
        Refresh,
        Ignore,
        Stack,
    }

    public readonly struct EffectModifier
    {
        public readonly StatId Stat;
        public readonly StatModOp Op;
        public readonly float Value;

        public EffectModifier(StatId stat, StatModOp op, float value)
        {
            Stat = stat;
            Op = op;
            Value = value;
        }
    }

    public sealed class EffectDefinition
    {
        public int Id { get; }
        public EffectDurationPolicy DurationPolicy { get; }
        public float Duration { get; }
        public float Period { get; }
        public IReadOnlyList<EffectModifier> Modifiers { get; }
        public IReadOnlyList<GameplayTag> GrantedTags { get; }
        public float Power { get; }
        // 피해를 주지 않는 효과는 null이다.
        public DamageExecution DamageExecution { get; }
        // 피해로 깎을 자원이다. 피해를 주지 않는 효과에서는 쓰지 않는다.
        public StatId DamageResource { get; }
        public EffectStackPolicy StackPolicy { get; }
        public int MaxStack { get; }
        // 특수 동작이 없으면 null이다. 적용마다 새 인스턴스를 반환해야 한다.
        public Func<EffectBehavior> BehaviorFactory { get; }

        public EffectDefinition(int id, EffectDurationPolicy durationPolicy, float duration, float period, IReadOnlyList<EffectModifier> modifiers, IReadOnlyList<GameplayTag> grantedTags, float power, DamageExecution damageExecution, StatId damageResource, EffectStackPolicy stackPolicy, int maxStack, Func<EffectBehavior> behaviorFactory)
        {
            // Instant는 등록되지 않아 되돌리거나 반복할 시점이 없다.
            if (durationPolicy == EffectDurationPolicy.Instant && (modifiers.Count > 0 || grantedTags.Count > 0 || period > 0f))
                throw new ArgumentException($"[EffectDefinition] Instant effect cannot have modifiers, granted tags or a period. EffectID {id}");

            // 피해는 주기 실행과 Instant에서만 일어나므로 주기 없는 지속 효과의 피해 계산은 한 번도 쓰이지 않는다.
            if (damageExecution != null && durationPolicy != EffectDurationPolicy.Instant && period <= 0f)
                throw new ArgumentException($"[EffectDefinition] A non-instant damage effect needs a period. EffectID {id}");

            if (damageExecution != null && damageResource == default)
                throw new ArgumentException($"[EffectDefinition] A damage effect needs a damage resource. EffectID {id}", nameof(damageResource));

            if (maxStack < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStack), maxStack, $"[EffectDefinition] MaxStack must be at least 1. EffectID {id}");

            foreach (EffectModifier modifier in modifiers)
            {
                if (modifier.Stat == default)
                    throw new ArgumentException($"[EffectDefinition] Modifiers cannot target an unregistered stat. EffectID {id}", nameof(modifiers));
            }

            foreach (GameplayTag tag in grantedTags)
            {
                if (tag == default)
                    throw new ArgumentException($"[EffectDefinition] Granted tags cannot contain an unregistered tag. EffectID {id}", nameof(grantedTags));
            }

            Id = id;
            DurationPolicy = durationPolicy;
            Duration = duration;
            Period = period;
            Modifiers = modifiers;
            GrantedTags = grantedTags;
            Power = power;
            DamageExecution = damageExecution;
            DamageResource = damageResource;
            StackPolicy = stackPolicy;
            MaxStack = maxStack;
            BehaviorFactory = behaviorFactory;
        }
    }
}
