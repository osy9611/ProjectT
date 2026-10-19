using System;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void ConverterThrowsForMissingRow()
    {
        CheckThrows<KeyNotFoundException>(() => SkillDefinitions.GetSkill(1), "없는 스킬 행");
        CheckThrows<KeyNotFoundException>(() => SkillDefinitions.GetEffect(1), "없는 효과 행");

        AddSkill(1, SkillType.Melee, 1f);
        AddBuff(1, BuffType.AddATK, 1f);

        Check(SkillDefinitions.GetSkill(1) != null && SkillDefinitions.GetEffect(1) != null, "실패가 캐시되었다");
    }

    static void RefreshKeepsFirstDefinitionValues()
    {
        var first = TestEffect(1, 2f, 0.5f);
        var other = TestEffect(1, 3f, 1f);
        var agent = NewAgent();
        agent.ApplyEffect(first, null);
        agent.TickInternal(0.75f);

        agent.ApplyEffect(other, null);
        int expiredAt = 0;
        for (int step = 1; step <= 20 && expiredAt == 0; step++)
        {
            agent.TickInternal(0.25f);
            if (EffectCount(agent) == 0)
                expiredAt = step;
        }

        // 처음 정의(주기 0.5, 4회) 기준: 0.75초 시점 실행 2회 → 한도 2 + 4 = 6, 마지막 실행 2.5초, 만료 3초(0.75 + 9 × 0.25).
        CheckExecutes(1, 6, "Refresh 후 실행 횟수");
        Check(expiredAt == 9, $"만료 시점 기대 9번째 0.25초, 실제 {expiredAt}");
    }

    static void ReapplyAfterExpiryStartsNewApplication()
    {
        var periodic = TestEffect(1, 0.5f, 0.25f);
        var trigger = TestEffect(2, 1f);
        var agent = NewAgent();
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (behavior.EffectId == 2)
                agent.ApplyEffect(periodic, null);
        };
        agent.ApplyEffect(trigger, null);
        agent.ApplyEffect(periodic, null);

        agent.TickInternal(1f);

        // 처음 적용분 2회(0, 0.25초) 뒤 만료되고, 다시 적용한 효과가 경과 0에서 1회 실행한다.
        CheckExecutes(1, 3, "다시 적용한 Tick");
        string reasons = string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons);
        Check(GetEffectIds(agent).Count == 1 && ProjectT.Skill.Tests.TestBehavior.Applies == 3 && reasons == "Expired,Expired", $"다시 적용이 새 적용이 아니다: 효과 {EffectCount(agent)}, 적용 {ProjectT.Skill.Tests.TestBehavior.Applies}, 사유 {reasons}");
    }

    static void GiveSkillUsesDefinitionAndRejectsSameId()
    {
        var agent = NewAgent();
        var definition = Skill(10, coolTime: 1f, factory: () => new ExternalGame.ExternalSkillAction());
        agent.GiveSkill(definition);
        var first = GetAction(agent, 10);
        Check(first is ExternalGame.ExternalSkillAction && ReferenceEquals(first.Spec.Definition, definition), "넘긴 정의로 스킬을 만들지 않았다");

        CheckThrows<ArgumentException>(() => agent.GiveSkill(Skill(10, coolTime: 2f, factory: () => new ExternalGame.ExternalSkillAction())), "같은 Id의 다른 정의");

        Check(GivenCount(agent) == 1 && ReferenceEquals(GetAction(agent, 10), first), "기존 인스턴스가 유지되지 않았다");
    }

    static void ApplyEffectRefreshesBySameId()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f), null);
        var first = GetBehaviors(agent)[0];

        agent.TickInternal(0.75f);
        agent.ApplyEffect(TestEffect(1, 1f), null);
        agent.TickInternal(0.5f);
        var behaviors = GetBehaviors(agent);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0 && behaviors.Count == 1 && ReferenceEquals(behaviors[0], first), "같은 Id가 갱신되지 않았다");
        Check(ProjectT.Skill.Tests.TestBehavior.Applies == 1, $"OnApply 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestBehavior.Applies}");

        agent.TickInternal(0.5f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "갱신 시점부터 duration에 만료되지 않았다");
    }

    static void InstantExecutesOnceWithoutRegistration()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        var caster = new ProjectT.Skill.Tests.TestActor { Status = new Status() };
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = 3f };
        var instant = Effect(1, EffectDurationPolicy.Instant, 0f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), power: 3f, execution: execution);
        var agent = NewAgent(status);
        int events = 0;
        agent.OnEffectApplied += (handle, definition) => events++;
        agent.OnEffectRemoved += (handle, definition, reason) => events++;

        var first = agent.ApplyEffect(instant, caster);
        Check(status.GetResource(Hp) == 7f && execution.Calls.Count == 1 && ReferenceEquals(execution.Calls[0].Source, caster.Status) && execution.Calls[0].Power == 3f, $"Instant 피해: HP {status.GetResource(Hp)}, 호출 {execution.Calls.Count}");
        Check(first.Id == 0 && EffectCount(agent) == 0 && ProjectT.Skill.Tests.TestBehavior.Applies == 1 && ProjectT.Skill.Tests.TestBehavior.ExecuteCount(1) == 1, "Instant가 등록되었거나 OnApply·OnExecute를 한 번씩 부르지 않았다");

        var second = agent.ApplyEffect(instant, caster);
        Check(second.Id == 0 && status.GetResource(Hp) == 4f && ProjectT.Skill.Tests.TestBehavior.Applies == 2, "같은 Instant를 다시 적용하면 다시 실행되어야 한다");

        agent.TickInternal(10f);
        agent.ReleaseInternal();
        Check(execution.Calls.Count == 2 && status.GetResource(Hp) == 4f, "Tick이나 정리가 Instant를 다시 실행했다");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0 && events == 0, $"Instant 제거 콜백 {ProjectT.Skill.Tests.TestBehavior.Removes}, 이벤트 {events}");
    }

    static void InvalidDefinitionsThrow()
    {
        var modifiers = new[] { new EffectModifier(Atk, StatModOp.Add, 1f) };
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Instant, 0f, 0f, null, modifiers: modifiers), "수정자가 있는 Instant");
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Instant, 0f, 0f, null, tags: new[] { Stun }), "태그를 부여하는 Instant");
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Instant, 0f, 0f, null, tags: new[] { TagA, TagB }), "여러 태그를 부여하는 Instant");
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Instant, 0f, 0.5f, null), "주기가 있는 Instant");
        CheckThrows<ArgumentOutOfRangeException>(() => Effect(1, EffectDurationPolicy.Duration, 1f, 0f, null, stack: EffectStackPolicy.Stack, maxStack: 0), "MaxStack 0");

        Effect(1, EffectDurationPolicy.Instant, 0f, 0f, null, power: 1f, execution: new FlatDamageExecution());
    }

    static void InfiniteNeverExpires()
    {
        var agent = NewAgent();
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, () => new ProjectT.Skill.Tests.TestBehavior()), null);

        for (int i = 0; i < 10; i++)
            agent.TickInternal(3600f);

        Check(EffectCount(agent) == 1 && ProjectT.Skill.Tests.TestBehavior.Removes == 0, "Infinite가 시간으로 만료되었다");
    }

    static void PeriodicInfiniteKeepsExecuting()
    {
        var effect = Effect(1, EffectDurationPolicy.Infinite, 1f, 0.5f, () => new ProjectT.Skill.Tests.TestBehavior());
        var agent = NewAgent();
        var handle = agent.ApplyEffect(effect, null);
        CheckExecutes(1, 1, "적용 직후");

        for (int i = 0; i < 100; i++)
            agent.TickInternal(0.5f);
        CheckExecutes(1, 101, "50초");

        Check(agent.ApplyEffect(effect, null).Id == handle.Id, "Refresh가 다른 핸들을 돌려줬다");
        agent.TickInternal(100f);
        CheckExecutes(1, 301, "Refresh 뒤 큰 delta");
        Check(EffectCount(agent) == 1 && ProjectT.Skill.Tests.TestBehavior.Removes == 0, "주기 Infinite가 만료되었다");
    }

    static void InfiniteRemovedByHandle()
    {
        var agent = NewAgent();
        var handle = agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0.5f, () => new ProjectT.Skill.Tests.TestBehavior()), null);
        agent.TickInternal(1f);

        agent.RemoveEffect(handle);

        Check(EffectCount(agent) == 0 && string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Removed", "Infinite가 핸들로 제거되지 않았다");
        agent.TickInternal(1f);
        CheckExecutes(1, 3, "제거 뒤");
    }

    static void RemoveEffectRevertsWithRemovedReason()
    {
        var status = NewStatus();
        var agent = NewAgent(status);
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), modifiers: new[] { new EffectModifier(Atk, StatModOp.Add, 5f) }, tags: new[] { Stun });
        var handle = agent.ApplyEffect(effect, null);
        Check(handle.Id != 0 && status.Get(Atk) == 15f && agent.HasTag(Stun), "효과가 적용되지 않았다");

        agent.RemoveEffect(handle);

        Check(status.Get(Atk) == 10f && !agent.HasTag(Stun) && EffectCount(agent) == 0, "제거가 수정자나 태그를 되돌리지 않았다");
        Check(string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Removed", "제거 사유: " + string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons));
    }

    static void StaleHandleIsNoOp()
    {
        var agent = NewAgent();
        var removed = agent.ApplyEffect(TestEffect(1, 5f), null);
        agent.ApplyEffect(TestEffect(2, 5f), null);
        agent.RemoveEffect(removed);

        agent.RemoveEffect(removed);
        agent.RemoveEffect(default);

        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1 && GetEffectIds(agent).Count == 1 && GetEffectIds(agent)[0] == 2, "이전 핸들이나 기본 핸들이 다른 효과를 제거했다");
    }

    static void HandleIsNotReusedAfterExpiry()
    {
        var effect = TestEffect(1, 1f);
        var agent = NewAgent();
        var expired = agent.ApplyEffect(effect, null);
        agent.TickInternal(1f);

        var current = agent.ApplyEffect(effect, null);
        agent.RemoveEffect(expired);

        Check(current.Id != 0 && current.Id != expired.Id, $"만료 뒤 같은 핸들 {current.Id}을 다시 썼다");
        Check(EffectCount(agent) == 1 && ProjectT.Skill.Tests.TestBehavior.Removes == 1, "만료된 핸들이 새 효과를 제거했다");
        agent.RemoveEffect(current);
        Check(EffectCount(agent) == 0, "새 핸들로 제거되지 않았다");
    }

    static void EffectEventsCarryHandleAndReason()
    {
        var agent = NewAgent();
        var log = new List<string>();
        agent.OnEffectApplied += (handle, definition) => log.Add($"applied:{definition.Id}:{handle.Id}");
        agent.OnEffectRemoved += (handle, definition, reason) => log.Add($"removed:{definition.Id}:{handle.Id}:{reason}");

        var expired = agent.ApplyEffect(TestEffect(1, 1f), null);
        var removed = agent.ApplyEffect(TestEffect(2, 5f), null);
        var released = agent.ApplyEffect(TestEffect(3, 5f), null);
        agent.TickInternal(1f);
        agent.RemoveEffect(removed);
        agent.ReleaseInternal();

        string expected = $"applied:1:{expired.Id},applied:2:{removed.Id},applied:3:{released.Id},removed:1:{expired.Id}:Expired,removed:2:{removed.Id}:Removed,removed:3:{released.Id}:Released";
        Check(string.Join(",", log) == expected, "이벤트: " + string.Join(",", log));
    }

    static void ReapplyRaisesNoEvents()
    {
        var refresh = TestEffect(1, 5f);
        var ignore = Effect(2, EffectDurationPolicy.Duration, 5f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), stack: EffectStackPolicy.Ignore);
        var stack = Effect(3, EffectDurationPolicy.Duration, 5f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent();
        var handles = new[] { agent.ApplyEffect(refresh, null), agent.ApplyEffect(ignore, null), agent.ApplyEffect(stack, null) };
        int events = 0;
        agent.OnEffectApplied += (handle, definition) => events++;
        agent.OnEffectRemoved += (handle, definition, reason) => events++;

        var again = new[] { agent.ApplyEffect(refresh, null), agent.ApplyEffect(ignore, null), agent.ApplyEffect(stack, null) };

        for (int i = 0; i < handles.Length; i++)
            Check(again[i].Id == handles[i].Id, $"효과 {i + 1}의 재적용이 다른 핸들을 돌려줬다");
        Check(events == 0 && ProjectT.Skill.Tests.TestBehavior.Applies == 3, $"재적용 이벤트 {events}, OnApply {ProjectT.Skill.Tests.TestBehavior.Applies}");
    }

    static void IgnoreKeepsTimer()
    {
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0.25f, () => new ProjectT.Skill.Tests.TestBehavior(), stack: EffectStackPolicy.Ignore);
        var agent = NewAgent();
        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.75f);

        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.25f);

        CheckExecutes(1, 4, "Ignore 후");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1 && ProjectT.Skill.Tests.TestBehavior.Applies == 1, "Ignore가 만료 시점을 바꿨다");
    }

    static void StackIsCappedAtMaxStack()
    {
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent();
        var handle = agent.ApplyEffect(effect, null);

        for (int i = 0; i < 4; i++)
            Check(agent.ApplyEffect(effect, null).Id == handle.Id, "중첩이 다른 핸들을 돌려줬다");

        Check(GetStackCount(agent, 0) == 3 && EffectCount(agent) == 1 && ProjectT.Skill.Tests.TestBehavior.Applies == 1, $"중첩 수 기대 3, 실제 {GetStackCount(agent, 0)}");
    }

    static void StackScalesModifiers()
    {
        var status = NewStatus();
        var other = new object();
        status.AddModifier(Atk, StatModOp.Add, 1f, other);
        var modifiers = new[] { new EffectModifier(Atk, StatModOp.Add, 2f), new EffectModifier(Def, StatModOp.Multiply, 1.2f), new EffectModifier(MoveSpeed, StatModOp.Override, 50f) };
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, modifiers: modifiers, stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent(status);

        var expected = new (float Atk, float Def)[] { (13f, 12f), (15f, 14f), (17f, 16f), (17f, 16f) };
        for (int i = 0; i < expected.Length; i++)
        {
            agent.ApplyEffect(effect, null);
            CheckStatNear(status, Atk, expected[i].Atk, $"{i + 1}번째 적용");
            CheckStatNear(status, Def, expected[i].Def, $"{i + 1}번째 적용");
            CheckStat(status, MoveSpeed, 50f, $"{i + 1}번째 적용");
        }

        agent.TickInternal(5f);
        CheckStat(status, Atk, 11f, "만료 후 다른 출처");
        CheckStat(status, Def, 10f, "만료 후");
        CheckStat(status, MoveSpeed, 0f, "만료 후");
    }

    static void StackKeepsHpAndOverrideOrder()
    {
        var status = NewStatus();
        status.SetBase(MaxHp, 100f);
        var modifiers = new[] { new EffectModifier(MaxHp, StatModOp.Add, 50f), new EffectModifier(Atk, StatModOp.Override, 50f) };
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, modifiers: modifiers, stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent(status);
        agent.ApplyEffect(effect, null);
        status.SetResource(Hp, 150f);
        var other = new object();
        status.AddModifier(Atk, StatModOp.Override, 30f, other);
        var hpEvents = new List<string>();
        status.OnResourceChanged += (resource, previous, current) => hpEvents.Add($"{previous}->{current}");

        agent.ApplyEffect(effect, null);

        Check(status.Get(MaxHp) == 200f && status.GetResource(Hp) == 150f && hpEvents.Count == 0, $"중첩이 Hp를 깎았다 MaxHp={status.Get(MaxHp)} Hp={status.GetResource(Hp)} 이벤트={string.Join(",", hpEvents)}");
        CheckStat(status, Atk, 30f, "더 최근 Override");
    }

    static void RemoveDuringStackDoesNotLeakModifiers()
    {
        var status = NewStatus();
        var modifiers = new[] { new EffectModifier(Atk, StatModOp.Add, 2f), new EffectModifier(Def, StatModOp.Add, 2f) };
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, modifiers: modifiers, stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent(status);
        EffectHandle handle = agent.ApplyEffect(effect, null);
        bool removed = false;
        status.OnStatChanged += (type, previous, current) =>
        {
            if (removed)
                return;

            removed = true;
            agent.RemoveEffect(handle);
        };

        agent.ApplyEffect(effect, null);

        Check(GetBehaviors(agent).Count == 0 && status.Get(Atk) == 10f && status.Get(Def) == 10f, $"중첩 중 제거 후 수정자가 남았다 Atk={status.Get(Atk)} Def={status.Get(Def)}");
    }

    static void PeriodlessDamageEffectIsInvalid()
    {
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Duration, 1f, 0f, null, power: 1f, execution: new FlatDamageExecution()), "주기 없는 지속 피해");
        CheckThrows<ArgumentException>(() => Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, power: 1f, execution: new FlatDamageExecution()), "주기 없는 무한 피해");
    }

    static void StackScalesPeriodicDamage()
    {
        var status = NewStatus();
        status.SetBase(MaxHp, 100f);
        status.SetResource(Hp, 100f);
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 2f, execution: new FlatDamageExecution(), stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent(status);

        agent.ApplyEffect(effect, null);
        Check(status.GetResource(Hp) == 98f, $"1중첩 피해 후 HP 기대 98, 실제 {status.GetResource(Hp)}");
        agent.ApplyEffect(effect, null);
        Check(status.GetResource(Hp) == 98f, "중첩이 즉시 실행했다");
        agent.TickInternal(0.5f);
        Check(status.GetResource(Hp) == 94f, $"2중첩 피해 후 HP 기대 94, 실제 {status.GetResource(Hp)}");
        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.5f);
        Check(status.GetResource(Hp) == 88f, $"3중첩 피해 후 HP 기대 88, 실제 {status.GetResource(Hp)}");
    }

    static void StackRefreshesAndExpiresOnce()
    {
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent();
        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.75f);

        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.75f);
        Check(EffectCount(agent) == 1 && ProjectT.Skill.Tests.TestBehavior.Removes == 0, "중첩이 지속 시간을 갱신하지 않았다");

        agent.TickInternal(0.25f);
        Check(EffectCount(agent) == 0 && string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Expired", "모든 중첩이 한 번에 만료되지 않았다: " + string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons));
    }

    static void StackGrantsStateOnce()
    {
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0f, null, tags: new[] { Stun }, stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent();
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}");

        for (int i = 0; i < 3; i++)
            agent.ApplyEffect(effect, null);
        agent.TickInternal(1f);

        Check(!agent.HasTag(Stun) && string.Join(",", events) == "State.Stun:True,State.Stun:False", "중첩이 태그를 여러 번 부여했다: " + string.Join(",", events));
    }

    static void ExpiryOrderIsListIndependent()
    {
        var first = TestEffect(1, 1f);
        var second = TestEffect(2, 1f);
        var results = new List<string>();
        foreach (var order in new[] { new[] { first, second }, new[] { second, first } })
        {
            ProjectT.Skill.Tests.TestBehavior.Applies = 0;
            var agent = NewAgent();
            var removed = new List<string>();
            ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
            {
                removed.Add($"{behavior.EffectId}:{reason}");
                if (behavior.EffectId == 1 && reason == EffectRemoveReason.Expired)
                    agent.ApplyEffect(second, null);
            };
            foreach (var effect in order)
                agent.ApplyEffect(effect, null);
            var original = GetBehaviors(agent).Find(behavior => behavior.Definition.Id == 2);

            agent.TickInternal(1f);

            removed.Sort();
            var behaviors = GetBehaviors(agent);
            bool newApplication = behaviors.Count == 1 && behaviors[0].Definition.Id == 2 && !ReferenceEquals(behaviors[0], original);
            results.Add($"{string.Join(",", removed)}/{newApplication}/{ProjectT.Skill.Tests.TestBehavior.Applies}");
        }

        Check(results[0] == results[1] && results[0] == "1:Expired,2:Expired/True/3", "순서에 따라 결과가 다르거나 재적용이 새 적용이 아니다: " + string.Join(" vs ", results));
    }

    static void FailedExpiryCallbackLeavesPendingForRelease()
    {
        var agent = NewAgent();
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (behavior.EffectId == 1 && reason == EffectRemoveReason.Expired)
                throw new InvalidOperationException("remove");
        };
        agent.ApplyEffect(TestEffect(1, 1f), null);
        agent.ApplyEffect(TestEffect(2, 1f), null);

        CheckThrows<InvalidOperationException>(() => agent.TickInternal(1f), "만료 제거 콜백 실패");
        agent.ReleaseInternal();

        string reasons = string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons);
        Check(reasons == "Expired,Released" && EffectCount(agent) == 0, $"남은 만료 효과의 사유 {reasons}, 효과 {EffectCount(agent)}");
    }

    static void RemoveDuringTickKeepsLoop()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 5f, 1f), null);
        var removed = agent.ApplyEffect(TestEffect(2, 5f, 1f), null);
        agent.ApplyEffect(TestEffect(3, 5f, 1f), null);
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = behavior =>
        {
            if (behavior.EffectId == 1)
                agent.RemoveEffect(removed);
        };

        agent.TickInternal(1f);

        CheckExecutes(2, 1, "제거된 효과");
        CheckExecutes(3, 2, "제거 뒤 순서의 효과");
        Check(EffectCount(agent) == 2 && string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Removed", "Tick 중 제거가 순회를 깨뜨렸다");
    }

    static void RemoveDuringDamageSkipsExecuteHook()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        var agent = NewAgent(status);
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, () => new ProjectT.Skill.Tests.TestBehavior(), power: 1f, execution: new FlatDamageExecution());
        var handle = agent.ApplyEffect(effect, null);
        status.OnResourceChanged += (resource, previous, current) => agent.RemoveEffect(handle);

        agent.TickInternal(0.5f);

        CheckExecutes(1, 1, "피해 알림에서 제거된 실행");
        Check(status.GetResource(Hp) == 8f && ProjectT.Skill.Tests.TestBehavior.Removes == 1 && EffectCount(agent) == 0, $"HP {status.GetResource(Hp)}, 제거 {ProjectT.Skill.Tests.TestBehavior.Removes}");
    }

    static void BehaviorHooksRunInOrderPerApplication()
    {
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, () => new ProjectT.Skill.Tests.OrderBehavior(), tags: new[] { Stun });
        var instant = Effect(2, EffectDurationPolicy.Instant, 0f, 0f, () => new ProjectT.Skill.Tests.OrderBehavior());
        var agent = NewAgent();

        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.5f);
        agent.ApplyEffect(effect, null);
        agent.TickInternal(1.5f);
        var handle = agent.ApplyEffect(effect, null);
        agent.RemoveEffect(handle);
        agent.ApplyEffect(instant, null);
        agent.ApplyEffect(effect, null);
        agent.ReleaseInternal();

        string expected = "apply#1:True,execute#1,execute#1,execute#1,execute#1,remove#1:Expired:True,apply#2:True,execute#2,remove#2:Removed:True,apply#3:False,execute#3,apply#4:True,execute#4,remove#4:Released:True";
        Check(string.Join(",", ProjectT.Skill.Tests.OrderBehavior.Log) == expected, "훅 순서: " + string.Join(",", ProjectT.Skill.Tests.OrderBehavior.Log));
    }
}
