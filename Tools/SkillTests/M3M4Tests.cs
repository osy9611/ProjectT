using System;
using System.Collections;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void ConverterMapsGrantedTags()
    {
        var types = new[] { BuffType.AddATK, BuffType.AddDEF, BuffType.LowATK, BuffType.LowDEF, BuffType.Strun, BuffType.Dot };
        for (int i = 0; i < types.Length; i++)
        {
            AddBuff(i + 1, types[i], 1f, 0.5f);
            IReadOnlyList<SkillTag> tags = SkillDefinitions.GetEffect(i + 1).GrantedTags;
            bool mapped = types[i] == BuffType.Strun ? tags.Count == 1 && tags[0] == Stun : tags.Count == 0;
            Check(mapped, $"{types[i]} 부여 태그 수: {tags.Count}");
        }
    }

    static void TagRegisterIsIdempotent()
    {
        var first = SkillTag.Register("Test.Idempotent");
        var second = SkillTag.Register("Test.Idempotent");

        Check(first == second && !(first != second) && first.Equals(second) && first.Equals((object)second) && first.GetHashCode() == second.GetHashCode(), "같은 이름이 다른 태그가 되었다");
        Check(SkillTag.Register("Test.A") == TagA, "이미 등록한 이름이 새 태그가 되었다");
        Check(first.Name == "Test.Idempotent" && TagA.Name == "Test.A", $"Name: {first.Name}, {TagA.Name}");
    }

    static void DistinctNamesAreDistinctTags()
    {
        Check(TagA != TagB && !(TagA == TagB) && !TagA.Equals(TagB) && !TagA.Equals((object)TagB) && TagA.GetHashCode() != TagB.GetHashCode(), "다른 이름이 같은 태그가 되었다");
        Check(!TagA.Equals((object)"Test.A"), "태그가 이름 문자열과 같다고 판정되었다");

        var agent = NewAgent();
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);
        Check(agent.HasTag(SkillTag.Register("Test.A")) && !agent.HasTag(TagB), "같은 이름으로 다시 얻은 태그로 판정하지 못했거나 다른 태그를 가졌다");
    }

    static void DefaultTagIsInvalid()
    {
        SkillTag invalid = default;

        Check(invalid == new SkillTag() && invalid != TagA && invalid != Stun && invalid.Name == null, $"default 태그: {invalid.Name}");
        Check(SkillTag.Register("Test.Empty") != invalid, "등록한 태그가 default와 같다");
        Check(!NewAgent().HasTag(invalid), "새 Agent가 default 태그를 가졌다");
    }

    static void EffectGrantsMultipleTags()
    {
        var agent = NewAgent();
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}:{agent.HasTag(tag)}");

        var handle = agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA, TagB }), null);
        Check(agent.HasTag(TagA) && agent.HasTag(TagB) && !agent.HasTag(TagC), "부여한 태그를 모두 갖지 않았다");
        Check(string.Join(",", events) == "Test.A:True:True,Test.B:True:True", "획득 이벤트: " + string.Join(",", events));

        agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);
        agent.RemoveEffect(handle);
        Check(agent.HasTag(TagA) && !agent.HasTag(TagB), "태그 카운트가 서로 영향을 주었다");
        Check(string.Join(",", events) == "Test.A:True:True,Test.B:True:True,Test.B:False:False", "해제 이벤트: " + string.Join(",", events));
    }

    static void OverlappingTagCountsAcrossEffects()
    {
        var agent = NewAgent();
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}");
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0f, null, tags: new[] { TagA, TagB }), null);
        agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 2f, 0f, null, tags: new[] { TagB, TagC }), null);
        Check(string.Join(",", events) == "Test.A:True,Test.B:True,Test.C:True", "겹친 획득: " + string.Join(",", events));

        agent.TickInternal(1f);
        Check(!agent.HasTag(TagA) && agent.HasTag(TagB) && agent.HasTag(TagC) && events.Count == 4, "첫 효과 만료: " + string.Join(",", events));

        agent.TickInternal(1f);
        Check(!agent.HasTag(TagB) && !agent.HasTag(TagC) && string.Join(",", events) == "Test.A:True,Test.B:True,Test.C:True,Test.A:False,Test.B:False,Test.C:False", "모든 효과 만료: " + string.Join(",", events));
    }

    static void SkillIsBlockedOnlyByOwnBlockedTags()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, blocked: new[] { Stun }));
        agent.GiveSkill(Skill(11, blocked: new[] { TagB, Stun }));
        agent.GiveSkill(Skill(12));
        agent.GiveSkill(Skill(13, blocked: new[] { TagA }));
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { Stun }), null);

        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 0 && !GetAction(agent, 10).IsActive && !GetAction(agent, 11).IsActive, "BlockedTags 중 하나를 가졌는데 발동되었다");

        agent.ActivateSkill(12);
        agent.ActivateSkill(13);
        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2 && ProjectT.Skill.Tests.TestSkillAction.Updates == 2 && GetActiveActions(agent).Count == 2, "자기 BlockedTags가 없는 스킬이 막혔다");
    }

    static void GainingTagCancelsOnlyMatchingSkills()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.GiveSkill(Skill(11, cancelledBy: new[] { TagB, TagC }));
        agent.GiveSkill(Skill(12));
        for (int id = 10; id <= 12; id++)
            agent.ActivateSkill(id);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);
        Check(!GetAction(agent, 10).IsActive && GetAction(agent, 11).IsActive && GetAction(agent, 12).IsActive && ProjectT.Skill.Tests.TestSkillAction.Cancels == 1, "TagA 획득이 다른 스킬을 취소했거나 대상 스킬을 취소하지 않았다");

        agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagC }), null);
        Check(!GetAction(agent, 11).IsActive && GetAction(agent, 12).IsActive && ProjectT.Skill.Tests.TestSkillAction.Cancels == 2, "CancelledByTags의 두 번째 태그로 취소되지 않았다");

        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 1 && GetActiveActions(agent).Count == 1, "태그 없는 스킬이 갱신되지 않았다");
    }

    static void ActivationCancelsOnAnyCancelledByTag()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.GiveSkill(Skill(11, cancelledBy: new[] { TagC, TagB }));
        agent.GiveSkill(Skill(12));
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);

        agent.ActivateSkill(10);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 1 && ProjectT.Skill.Tests.TestSkillAction.Cancels == 1 && !GetAction(agent, 10).IsActive && GetActiveActions(agent).Count == 0, "이미 가진 취소 태그로 발동 직후 취소되지 않았다");

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagB }), null);
        agent.ActivateSkill(11);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2 && ProjectT.Skill.Tests.TestSkillAction.Cancels == 2 && !GetAction(agent, 11).IsActive && GetActiveActions(agent).Count == 0, "Activate 중에 얻은 취소 태그로 취소되지 않았다");

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.ApplyEffect(Effect(3, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagC }), null);
        agent.ActivateSkill(12);
        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Cancels == 2 && GetAction(agent, 12).IsActive && ProjectT.Skill.Tests.TestSkillAction.Updates == 1, "취소 태그가 없는 스킬이 Activate 중 태그 획득으로 취소되었다");
    }

    static void AdapterSkillsUseStunTag()
    {
        AddSkill(1, SkillType.Normal, 0f);
        AddSkill(2, SkillType.Melee, 1f);
        AddSkill(3, SkillType.Range, 2f);
        AddBuff(4, BuffType.Strun, 1f);
        for (int id = 1; id <= 3; id++)
        {
            var definition = SkillDefinitions.GetSkill(id);
            Check(definition.BlockedTags.Count == 1 && definition.BlockedTags[0] == Stun && definition.CancelledByTags.Count == 1 && definition.CancelledByTags[0] == Stun, $"스킬 {id}의 기본 태그 규칙이 스턴이 아니다");
        }

        Check(Stun.Name == "State.Stun" && SkillTag.Register("State.Stun") == Stun && SkillDefinitions.GetEffect(4).GrantedTags[0] == SkillDefinitions.GetSkill(1).BlockedTags[0], "스킬과 효과가 같은 스턴 태그를 쓰지 않는다");
    }

    static void TagRegisterRejectsEmptyName()
    {
        var nullError = CaptureThrows<ArgumentException>(() => SkillTag.Register(null), "null 이름");
        var emptyError = CaptureThrows<ArgumentException>(() => SkillTag.Register(""), "빈 이름");
        Check(nullError.GetType() == typeof(ArgumentException) && emptyError.GetType() == typeof(ArgumentException), $"예외 형식: {nullError.GetType().Name}, {emptyError.GetType().Name}");
        Check(SkillTag.Register("test.a") != TagA, "이름의 대소문자를 구별하지 않았다");
    }

    static void DefinitionsRejectDefaultTag()
    {
        SkillTag[] withDefault = { TagA, default };
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Duration, 1f, 0f, null, tags: withDefault), "GrantedTags의 default 태그");
        CheckThrows<ArgumentException>(() => Skill(10, blocked: withDefault), "BlockedTags의 default 태그");
        CheckThrows<ArgumentException>(() => Skill(10, cancelledBy: withDefault), "CancelledByTags의 default 태그");
    }

    static void StatIdRegisterIsIdempotent()
    {
        var first = StatId.Register("Test.Idempotent");
        var second = StatId.Register("Test.Idempotent");

        Check(first == second && !(first != second) && first.Equals(second) && first.Equals((object)second) && first.GetHashCode() == second.GetHashCode(), "같은 이름이 다른 스탯이 되었다");
        Check(StatId.Register("Atk") == Atk, "이미 등록한 이름이 새 스탯이 되었다");
        Check(first.Name == "Test.Idempotent" && Atk.Name == "Atk", $"Name: {first.Name}, {Atk.Name}");
    }

    static void DistinctNamesAreDistinctStats()
    {
        Check(Atk != Def && !(Atk == Def) && !Atk.Equals(Def) && !Atk.Equals((object)Def) && Atk.GetHashCode() != Def.GetHashCode(), "다른 이름이 같은 스탯이 되었다");
        Check(!Atk.Equals((object)"Atk") && StatId.Register("atk") != Atk, "이름 문자열과 같다고 판정했거나 대소문자를 구별하지 않았다");

        var status = new Status();
        status.SetBase(Atk, 10f);
        Check(status.Get(StatId.Register("Atk")) == 10f && status.Get(Def) == 0f, "같은 이름으로 다시 얻은 스탯으로 읽지 못했거나 다른 스탯에 값이 있다");
    }

    static void DefaultStatIdIsInvalid()
    {
        StatId invalid = default;

        Check(invalid == new StatId() && invalid != Atk && invalid != Hp && invalid.Name == null, $"default 스탯: {invalid.Name}");
        Check(StatId.Register("Test.EmptyStat") != invalid, "등록한 스탯이 default와 같다");
    }

    static void StatIdRegisterRejectsEmptyName()
    {
        var nullError = CaptureThrows<ArgumentException>(() => StatId.Register(null), "null 이름");
        var emptyError = CaptureThrows<ArgumentException>(() => StatId.Register(""), "빈 이름");
        Check(nullError.GetType() == typeof(ArgumentException) && emptyError.GetType() == typeof(ArgumentException), $"예외 형식: {nullError.GetType().Name}, {emptyError.GetType().Name}");
    }

    static void StatRegistryIsSeparateFromTags()
    {
        var tag = SkillTag.Register("Test.Shared");
        var stat = StatId.Register("Test.Shared");
        Check(tag.Name == "Test.Shared" && stat.Name == "Test.Shared" && stat != default, "같은 이름의 태그와 스탯을 함께 등록하지 못했다");

        int before = (int)GetField(SkillTag.Register("Test.SeparateA"), "id");
        StatId.Register("Test.SeparateStat");
        int after = (int)GetField(SkillTag.Register("Test.SeparateB"), "id");
        Check(after == before + 1, $"스탯 등록이 태그 id를 소모했다: {before} -> {after}");
    }

    static void UnknownStatReadsZero()
    {
        var status = new Status();
        var unknown = StatId.Register("Test.Unknown");
        Check(status.Get(unknown) == 0f && status.GetBase(unknown) == 0f, "설정하지 않은 스탯이 0이 아니다");

        status.AddModifier(unknown, StatModOp.Add, 3f, new object());
        Check(status.Get(unknown) == 3f && status.GetBase(unknown) == 0f, $"수정자만 건 스탯: {status.Get(unknown)}");
    }

    static void ResourcesClampIndependently()
    {
        var status = NewStatus();
        status.SetBase(MaxMp, 20f);
        status.DefineResource(Mp, MaxMp);
        status.SetResource(Hp, 10f);
        status.SetResource(Mp, 30f);
        Check(status.GetResource(Hp) == 10f && status.GetResource(Mp) == 20f, $"자기 상한으로 제한되지 않았다 Hp={status.GetResource(Hp)} Mp={status.GetResource(Mp)}");
        var events = new List<string>();
        status.OnResourceChanged += (resource, previous, current) => events.Add($"{resource.Name}:{previous}>{current}");

        status.SetBase(MaxMp, 5f);
        Check(status.GetResource(Hp) == 10f && status.GetResource(Mp) == 5f, "MaxMp 감소가 Hp를 바꿨다");
        status.SetBase(MaxHp, 4f);
        Check(status.GetResource(Hp) == 4f && status.GetResource(Mp) == 5f, "MaxHp 감소가 Mp를 바꿨다");
        status.SetResource(Mp, 3f);

        Check(string.Join(",", events) == "Test.Mp:20>5,Hp:10>4,Test.Mp:5>3", "자원 이벤트: " + string.Join(",", events));
    }

    static void MaxDecreaseClampsResourcesAfterStatEvent()
    {
        var status = new Status();
        var shield = StatId.Register("Test.Shield");
        var source = new object();
        status.SetBase(MaxMp, 10f);
        status.AddModifier(MaxMp, StatModOp.Add, 10f, source);
        status.DefineResource(Mp, MaxMp);
        status.DefineResource(shield, MaxMp);
        status.SetResource(Mp, 20f);
        status.SetResource(shield, 15f);
        var events = new List<string>();
        status.OnStatChanged += (stat, previous, current) => events.Add($"{stat.Name}:{previous}>{current}:mp{status.GetResource(Mp)}");
        status.OnResourceChanged += (resource, previous, current) => events.Add($"{resource.Name}:{previous}>{current}");

        status.RemoveModifiers(source);
        status.AddModifier(MaxMp, StatModOp.Multiply, 0.5f, source);
        status.SetBase(MaxMp, 6f);
        status.SetBase(MaxMp, 10f);

        Check(status.GetResource(Mp) == 3f && status.GetResource(shield) == 3f, $"Mp={status.GetResource(Mp)} Shield={status.GetResource(shield)}");
        Check(string.Join(",", events) == "Test.MaxMp:20>10:mp10,Test.Mp:20>10,Test.Shield:15>10,Test.MaxMp:10>5:mp5,Test.Mp:10>5,Test.Shield:10>5,Test.MaxMp:5>3:mp3,Test.Mp:5>3,Test.Shield:5>3,Test.MaxMp:3>5:mp3", "이벤트: " + string.Join(",", events));
    }

    static void UndefinedResourceThrows()
    {
        var status = NewStatus();

        CheckThrows<InvalidOperationException>(() => status.GetResource(Mp), "정의하지 않은 자원 읽기");
        CheckThrows<InvalidOperationException>(() => status.SetResource(Mp, 1f), "정의하지 않은 자원 쓰기");
        CheckThrows<InvalidOperationException>(() => status.DefineResource(Hp, MaxMp), "같은 자원 다시 정의");

        status.SetResource(Hp, 10f);
        Check(status.GetResource(Hp) == 10f, "다시 정의하려다 기존 상한이 바뀌었다");
        Check(status.Get(Hp) == 0f, "자원이 스탯으로 읽혔다");
    }

    static void ReleaseKeepsResourceDefinitions()
    {
        var status = NewStatus();
        status.SetBase(MaxMp, 10f);
        status.DefineResource(Mp, MaxMp);
        status.SetResource(Hp, 10f);
        status.SetResource(Mp, 10f);
        int events = 0;
        status.OnResourceChanged += (resource, previous, current) => events++;

        status.ReleaseInternal();

        Check(events == 0 && status.GetResource(Hp) == 0f && status.GetResource(Mp) == 0f, $"정리 후 Hp={status.GetResource(Hp)} Mp={status.GetResource(Mp)} 이벤트={events}");
        CheckThrows<InvalidOperationException>(() => status.DefineResource(Mp, MaxMp), "정리 뒤 같은 자원 다시 정의");
        status.SetBase(MaxMp, 5f);
        status.SetResource(Mp, 8f);
        Check(status.GetResource(Mp) == 5f && events == 1, $"정리 후 다시 설정한 Mp {status.GetResource(Mp)}, 이벤트 {events}");
    }

    static void DamageEffectDamagesConfiguredResource()
    {
        var status = NewStatus();
        status.SetBase(MaxMp, 10f);
        status.DefineResource(Mp, MaxMp);
        status.SetResource(Hp, 10f);
        status.SetResource(Mp, 10f);
        var agent = NewAgent(status);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Instant, 0f, 0f, null, power: 3f, execution: new FlatDamageExecution(), damageResource: Mp), null);
        agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 2f, execution: new FlatDamageExecution(), damageResource: Mp), null);
        agent.TickInternal(0.5f);

        Check(status.GetResource(Mp) == 3f && status.GetResource(Hp) == 10f, $"Mp={status.GetResource(Mp)} Hp={status.GetResource(Hp)}");
    }

    static void DamageEffectRequiresResource()
    {
        var error = CaptureThrows<ArgumentException>(() => new EffectDefinition(1, EffectDurationPolicy.Instant, 0f, 0f, Array.Empty<EffectModifier>(), Array.Empty<SkillTag>(), 1f, new FlatDamageExecution(), default, EffectStackPolicy.Refresh, 1, null), "피해 자원 없는 피해 효과");
        Check(error.ParamName == "damageResource", $"예외 인자: {error.ParamName}");

        var withoutDamage = Effect(2, EffectDurationPolicy.Duration, 1f, 0f, null, damageResource: default(StatId));
        Check(withoutDamage.DamageResource == default, "피해 없는 효과가 피해 자원을 요구했다");
    }

    static void DamageEffectWithUndefinedResourceThrowsBeforeApply()
    {
        var status = NewStatus();
        var agent = NewAgent(status);
        var modifiers = new[] { new EffectModifier(Atk, StatModOp.Add, 5f) };
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, () => new ProjectT.Skill.Tests.TestBehavior(), modifiers: modifiers, tags: new[] { TagA }, power: 3f, execution: new FlatDamageExecution(), damageResource: Mp);

        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(effect, null), "정의되지 않은 피해 자원");

        Check(EffectCount(agent) == 0 && ProjectT.Skill.Tests.TestBehavior.Applies == 0 && status.Get(Atk) == 10f && !agent.HasTag(TagA), "실패한 효과가 상태를 남겼다");
    }

    static void CooldownReductionUsesSkillStat()
    {
        var haste = StatId.Register("Test.Haste");
        var status = new Status();
        status.SetBase(CooldownReduction, 0.5f);
        status.SetBase(haste, 0.25f);
        var agent = NewAgent(status);

        GiveAndActivate(agent, Skill(10, coolTime: 2f), Skill(11, coolTime: 2f, cooldownStat: CooldownReduction), Skill(12, coolTime: 2f, cooldownStat: haste));

        float plain = GetAction(agent, 10).Spec.CoolDownRemaining, reduced = GetAction(agent, 11).Spec.CoolDownRemaining, hasted = GetAction(agent, 12).Spec.CoolDownRemaining;
        Check(plain == 2f && reduced == 1f && hasted == 1.5f, $"쿨다운: 없음 {plain}, CooldownReduction {reduced}, Haste {hasted}");
    }

    static void AdapterMapsStatIds()
    {
        AddSkill(1, SkillType.Melee, 2f);
        AddBuff(2, BuffType.Dot, 1f, 0.5f, 3f);
        AddBuff(3, BuffType.AddATK, 1f, 0f, 20f);

        Check(SkillDefinitions.GetSkill(1).CooldownReductionStat == CooldownReduction, "테이블 스킬의 쿨다운 감소 스탯이 다르다");
        Check(SkillDefinitions.GetEffect(2).DamageResource == Hp && SkillDefinitions.GetEffect(3).DamageResource == default, "도트의 피해 자원이 Hp가 아니거나 피해 없는 효과에 자원이 있다");
        var ids = new[] { MaxHp, Atk, Def, MoveSpeed, CooldownReduction, Hp };
        Check(string.Join(",", Array.ConvertAll(ids, id => id.Name)) == "MaxHp,Atk,Def,MoveSpeed,CooldownReduction,Hp" && new HashSet<StatId>(ids).Count == ids.Length, "어댑터 스탯 이름이 다르거나 겹친다");
    }

    static void UnregisteredStatsAreRejected()
    {
        CheckThrows<ArgumentException>(() => new EffectDefinition(1, EffectDurationPolicy.Duration, 1f, 0f, new[] { new EffectModifier(default, StatModOp.Add, 1f) }, Array.Empty<SkillTag>(), 0f, null, default, EffectStackPolicy.Refresh, 1, null), "수정자의 default 스탯");
        var status = new Status();
        CheckThrows<ArgumentException>(() => status.DefineResource(default, MaxHp), "default 자원");
        CheckThrows<ArgumentException>(() => status.DefineResource(Hp, default), "default 상한");
    }
}
