using System;
using System.Collections.Generic;
using ProjectT.Skill;
using ProjectT.Stat;

// Unity를 참조하지 않는다. Tools/TurnGameTests 콘솔 테스트가 같은 정의를 컴파일해 Unity 샘플과 같은 규칙을 검증한다.
namespace ProjectT.Sample.TurnBattle
{
    public static class TurnIds
    {
        public static readonly StatId Hp = StatId.Register("Hp");
        public static readonly StatId MaxHp = StatId.Register("MaxHp");
        public static readonly StatId Mp = StatId.Register("Mp");
        public static readonly StatId MaxMp = StatId.Register("MaxMp");
        public static readonly StatId Atk = StatId.Register("Atk");
        public static readonly StatId Def = StatId.Register("Def");
        public static readonly SkillTag Stun = SkillTag.Register("State.Stun");

        public const int Strike = 1;
        public const int Fireball = 2;
        public const int Poison = 3;
        public const int Bash = 4;
        public const int WarCry = 5;
        public const int Guard = 7;
        public const int Meditate = 8;
        public const int Burn = 9;

        public const int GuardEffect = 106;

        public static bool IsSelfSkill(int id)
        {
            return id == WarCry || id == Guard || id == Meditate;
        }

        public static string SkillName(int id)
        {
            switch (id)
            {
                case Strike: return "Strike";
                case Fireball: return "Fireball";
                case Poison: return "Poison";
                case Bash: return "Bash";
                case WarCry: return "WarCry";
                case Guard: return "Guard";
                case Meditate: return "Meditate";
                case Burn: return "Burn";
                default: return id.ToString();
            }
        }
    }

    // 대상 선택은 게임 몫이다. 턴제 유닛이 이번 행동의 대상을 알려 준다.
    public interface ITurnUnit
    {
        SkillAgent TargetAgent { get; }
    }

    // 게임 쪽 피해 공식이다. 시전자가 사라지면(source null) Atk 0으로 계산해 최소 피해만 준다.
    public sealed class AttackDamage : DamageExecution
    {
        public override float Execute(Status source, Status target, float power)
        {
            float atk = source == null ? 0f : source.Get(TurnIds.Atk);
            return Math.Max(atk * power - target.Get(TurnIds.Def), 1f);
        }
    }

    public sealed class ManaRegenBehavior : EffectBehavior
    {
        public override void OnExecute()
        {
            Owner.Status.SetResource(TurnIds.Mp, Owner.Status.GetResource(TurnIds.Mp) + 2f);
        }
    }

    public sealed class TargetedAction : BaseSkillAction
    {
        private readonly bool self;

        public TargetedAction(bool self)
        {
            this.self = self;
        }

        protected override void Activate()
        {
            ApplyEffects(self ? Owner.SkillAgent : ((ITurnUnit)Owner).TargetAgent);
            End();
        }
    }

    // 여러 턴 유지되는 스킬이다. 틱마다 Mp를 회복하고 3틱 뒤 끝난다.
    public sealed class MeditateAction : BaseSkillAction
    {
        private int turns;

        protected override void Activate()
        {
            turns = 0;
        }

        public override void OnUpdate(float deltaTime)
        {
            Owner.Status.SetResource(TurnIds.Mp, Owner.Status.GetResource(TurnIds.Mp) + 5f);
            if (++turns >= 3)
                End();
        }
    }

    public sealed class TurnUnitSpec
    {
        public string Name { get; }
        public bool IsPlayer { get; }
        public float Hp { get; }
        public float Mp { get; }
        public float Atk { get; }
        public float Def { get; }
        public bool ManaRegen { get; }
        public SkillDefinition[] Skills { get; }

        public TurnUnitSpec(string name, bool isPlayer, float hp, float mp, float atk, float def, bool manaRegen, params SkillDefinition[] skills)
        {
            Name = name;
            IsPlayer = isPlayer;
            Hp = hp;
            Mp = mp;
            Atk = atk;
            Def = def;
            ManaRegen = manaRegen;
            Skills = skills;
        }
    }

    public static class TurnDefs
    {
        private static readonly SkillTag[] stunTags = { TurnIds.Stun };
        private static readonly AttackDamage attackDamage = new AttackDamage();
        private static readonly FlatDamageExecution flatDamage = new FlatDamageExecution();

        public static readonly EffectDefinition StrikeHit = Instant(101, 1f);
        public static readonly EffectDefinition FireballHit = Instant(102, 2f);
        public static readonly EffectDefinition Poison = new EffectDefinition(103, EffectDurationPolicy.Duration, 3f, 1f, Array.Empty<EffectModifier>(), Array.Empty<SkillTag>(), 5f, flatDamage, TurnIds.Hp, EffectStackPolicy.Stack, 3, null);
        public static readonly EffectDefinition BashHit = Instant(104, 0.5f);
        public static readonly EffectDefinition Stun = new EffectDefinition(105, EffectDurationPolicy.Duration, 1f, 0f, Array.Empty<EffectModifier>(), stunTags, 0f, null, default, EffectStackPolicy.Refresh, 1, null);
        public static readonly EffectDefinition WarCry = new EffectDefinition(107, EffectDurationPolicy.Duration, 2f, 0f, new[] { new EffectModifier(TurnIds.Atk, StatModOp.Multiply, 1.5f) }, Array.Empty<SkillTag>(), 0f, null, default, EffectStackPolicy.Refresh, 1, null);
        public static readonly EffectDefinition Guard = new EffectDefinition(TurnIds.GuardEffect, EffectDurationPolicy.Infinite, 0f, 0f, new[] { new EffectModifier(TurnIds.Def, StatModOp.Add, 10f) }, Array.Empty<SkillTag>(), 0f, null, default, EffectStackPolicy.Ignore, 1, null);
        public static readonly EffectDefinition ManaRegen = new EffectDefinition(108, EffectDurationPolicy.Infinite, 0f, 1f, Array.Empty<EffectModifier>(), Array.Empty<SkillTag>(), 0f, null, default, EffectStackPolicy.Ignore, 1, () => new ManaRegenBehavior());
        public static readonly EffectDefinition Burn = new EffectDefinition(109, EffectDurationPolicy.Duration, 2f, 1f, Array.Empty<EffectModifier>(), Array.Empty<SkillTag>(), 1f, attackDamage, TurnIds.Hp, EffectStackPolicy.Refresh, 1, null);

        public static readonly SkillDefinition StrikeSkill = Skill(TurnIds.Strike, 0f, () => new TargetedAction(false), 0f, StrikeHit);
        public static readonly SkillDefinition FireballSkill = Skill(TurnIds.Fireball, 2f, () => new TargetedAction(false), 10f, FireballHit);
        public static readonly SkillDefinition PoisonSkill = Skill(TurnIds.Poison, 0f, () => new TargetedAction(false), 5f, Poison);
        public static readonly SkillDefinition BashSkill = Skill(TurnIds.Bash, 3f, () => new TargetedAction(false), 0f, BashHit, Stun);
        public static readonly SkillDefinition WarCrySkill = Skill(TurnIds.WarCry, 1f, () => new TargetedAction(true), 0f, WarCry);
        public static readonly SkillDefinition GuardSkill = Skill(TurnIds.Guard, 2f, () => new TargetedAction(true), 0f, Guard);
        public static readonly SkillDefinition MeditateSkill = Skill(TurnIds.Meditate, 4f, () => new MeditateAction(), 0f);
        public static readonly SkillDefinition BurnSkill = Skill(TurnIds.Burn, 0f, () => new TargetedAction(false), 5f, Burn);

        public static readonly TurnUnitSpec Warrior = new TurnUnitSpec("Warrior", true, 100f, 30f, 20f, 5f, false, StrikeSkill, BashSkill, WarCrySkill, GuardSkill);
        public static readonly TurnUnitSpec Mage = new TurnUnitSpec("Mage", true, 70f, 50f, 12f, 2f, true, StrikeSkill, FireballSkill, PoisonSkill, MeditateSkill);
        public static readonly TurnUnitSpec MageNoRegen = new TurnUnitSpec("Mage", true, 70f, 50f, 12f, 2f, false, StrikeSkill, FireballSkill, PoisonSkill, MeditateSkill);
        public static readonly TurnUnitSpec Orc = new TurnUnitSpec("Orc", false, 120f, 0f, 15f, 4f, false, StrikeSkill, BashSkill);
        public static readonly TurnUnitSpec Goblin = new TurnUnitSpec("Goblin", false, 60f, 40f, 8f, 1f, false, StrikeSkill, PoisonSkill, BurnSkill);

        private static EffectDefinition Instant(int id, float power)
        {
            return new EffectDefinition(id, EffectDurationPolicy.Instant, 0f, 0f, Array.Empty<EffectModifier>(), Array.Empty<SkillTag>(), power, attackDamage, TurnIds.Hp, EffectStackPolicy.Refresh, 1, null);
        }

        // 모든 스킬은 스턴에 막히고, 활성 중 스턴을 받으면 취소된다.
        private static SkillDefinition Skill(int id, float coolTime, Func<BaseSkillAction> factory, float mpCost, params EffectDefinition[] effects)
        {
            SkillCost[] costs = mpCost > 0f ? new[] { new SkillCost(TurnIds.Mp, mpCost) } : Array.Empty<SkillCost>();
            return new SkillDefinition(id, coolTime, factory, stunTags, stunTags, default, effects, costs);
        }
    }

    public static class TurnAI
    {
        // 앞에서부터 시도해 처음 발동된 스킬로 턴을 쓴다. 대상은 IsSelfSkill로 정한다.
        public static IEnumerable<int> Priorities(string name, Status status, bool channeling)
        {
            switch (name)
            {
                case "Warrior":
                    if (status.GetResource(TurnIds.Hp) < 50f)
                        yield return TurnIds.Guard;

                    yield return TurnIds.Bash;
                    if (status.Get(TurnIds.Atk) <= 20f)
                        yield return TurnIds.WarCry;

                    yield return TurnIds.Strike;
                    break;
                case "Mage":
                    if (channeling)
                        yield break;

                    if (status.GetResource(TurnIds.Mp) < 15f)
                        yield return TurnIds.Meditate;

                    yield return TurnIds.Fireball;
                    yield return TurnIds.Poison;
                    yield return TurnIds.Strike;
                    break;
                case "Orc":
                    yield return TurnIds.Bash;
                    yield return TurnIds.Strike;
                    break;
                default:
                    yield return TurnIds.Burn;
                    yield return TurnIds.Strike;
                    break;
            }
        }
    }
}
