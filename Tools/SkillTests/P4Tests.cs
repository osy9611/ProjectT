using System;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void RefreshKeepsPeriodicPhase()
    {
        var effect = TestEffect(1, 1f, 0.25f);
        var agent = NewAgent();
        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.25f);
        agent.TickInternal(0.125f);
        CheckExecutes(1, 2, "갱신 전");

        agent.ApplyEffect(effect, null);
        CheckExecutes(1, 2, "갱신 직후");
        agent.TickInternal(0.125f);
        CheckExecutes(1, 3, "기존 위상 0.5초");
        agent.TickInternal(0.25f);
        agent.TickInternal(0.25f);
        CheckExecutes(1, 5, "1초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "갱신 전 한도로 만료되었다");
        agent.TickInternal(0.25f);
        CheckExecutes(1, 6, "1.25초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "마지막 실행에 만료되었다");
        agent.TickInternal(0.25f);
        CheckExecutes(1, 6, "1.5초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "마지막 실행 한 주기 뒤에 만료되지 않았다");
    }

    static void RefreshDuringExecuteUsesTickTime()
    {
        var periodic = TestEffect(1, 2f, 1f);
        var refreshed = TestEffect(2, 1f);
        foreach (var order in new[] { new[] { periodic, refreshed }, new[] { refreshed, periodic } })
        {
            var agent = NewAgent();
            foreach (var effect in order)
                agent.ApplyEffect(effect, null);
            ProjectT.Skill.Tests.TestBehavior.ExecuteHook = behavior =>
            {
                if (behavior.EffectId == 1)
                    agent.ApplyEffect(refreshed, null);
            };

            agent.TickInternal(1f);
            Check(EffectCount(agent) == 2, $"순서 {order[0].Id},{order[1].Id}: 갱신된 효과가 갱신한 Tick에 만료되었다");
            agent.TickInternal(0.5f);
            Check(EffectCount(agent) == 2, $"순서 {order[0].Id},{order[1].Id}: 갱신된 효과가 1초 전에 만료되었다");
            agent.TickInternal(0.5f);
            Check(EffectCount(agent) == 0, $"순서 {order[0].Id},{order[1].Id}: 갱신된 효과가 1초에 만료되지 않았다");
        }
    }

    static void SelfRefreshDuringExecuteKeepsPhase()
    {
        var effect = TestEffect(1, 1f, 0.25f);
        var agent = NewAgent();
        agent.ApplyEffect(effect, null);
        bool refreshed = false;
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = _ =>
        {
            if (refreshed)
                return;

            refreshed = true;
            agent.ApplyEffect(effect, null);
        };

        agent.TickInternal(0.5f);
        CheckExecutes(1, 3, "자신을 갱신한 Tick");
        agent.TickInternal(0.75f);
        CheckExecutes(1, 6, "1.25초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "갱신 전 한도로 만료되었다");
        agent.TickInternal(0.25f);
        CheckExecutes(1, 7, "1.5초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "마지막 실행에 만료되었다");
        agent.TickInternal(0.25f);
        CheckExecutes(1, 7, "1.75초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "마지막 실행 한 주기 뒤에 만료되지 않았다");
    }

    static void PeriodicRefreshFromOtherEffectIsOrderIndependent()
    {
        var trigger = TestEffect(1, 10f, 1f);
        var refreshed = TestEffect(2, 2f, 1f);
        var results = new List<string>();
        foreach (var order in new[] { new[] { trigger, refreshed }, new[] { refreshed, trigger } })
        {
            ProjectT.Skill.Tests.TestBehavior.Executes.Clear();
            var agent = NewAgent();
            foreach (var effect in order)
                agent.ApplyEffect(effect, null);
            bool done = false;
            ProjectT.Skill.Tests.TestBehavior.ExecuteHook = behavior =>
            {
                if (behavior.EffectId != 1 || done)
                    return;

                done = true;
                agent.ApplyEffect(refreshed, null);
            };

            int expiredAt = 0;
            for (int tick = 1; tick <= 12 && expiredAt == 0; tick++)
            {
                agent.TickInternal(1f);
                if (!GetEffectIds(agent).Contains(2))
                    expiredAt = tick;
            }

            results.Add($"{ProjectT.Skill.Tests.TestBehavior.ExecuteCount(2)}/{expiredAt}");
        }

        Check(results[0] == results[1], "순서에 따라 결과가 다르다: " + string.Join(" vs ", results));
    }

    static void RepeatedRefreshDoesNotStack()
    {
        var effect = TestEffect(1, 1f, 0.5f);
        var agent = NewAgent();
        agent.ApplyEffect(effect, null);
        agent.TickInternal(0.25f);

        for (int i = 0; i < 3; i++)
            agent.ApplyEffect(effect, null);
        CheckExecutes(1, 1, "연속 갱신 직후");
        agent.TickInternal(10f);

        CheckExecutes(1, 3, "연속 갱신 후");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "연속 갱신 후 만료되지 않았다");
    }

    static void SetHpClampsAndRaisesOnChange()
    {
        var status = NewStatus();
        var events = new List<string>();
        status.OnResourceChanged += (resource, previous, current) => events.Add($"{previous}>{current}:{status.GetResource(Hp)}");

        status.SetResource(Hp, 4f);
        status.SetResource(Hp, 4f);
        status.SetResource(Hp, 25f);
        status.SetResource(Hp, 25f);
        status.SetResource(Hp, -3f);
        status.SetResource(Hp, -3f);

        Check(status.GetResource(Hp) == 0f, $"HP 기대 0, 실제 {status.GetResource(Hp)}");
        Check(string.Join(",", events) == "0>4:4,4>10:10,10>0:0", "HP 이벤트: " + string.Join(",", events));
    }

    static void MaxHpDecreaseClampsHp()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        var events = new List<string>();
        status.OnStatChanged += (type, previous, current) => events.Add($"{type.Name}:{previous}>{current}:hp{status.GetResource(Hp)}");
        status.OnResourceChanged += (resource, previous, current) => events.Add($"{resource.Name}:{previous}>{current}");

        status.SetBase(MaxHp, 6f);
        Check(status.GetResource(Hp) == 6f, $"MaxHp 감소 후 HP 기대 6, 실제 {status.GetResource(Hp)}");
        status.SetBase(MaxHp, 8f);
        Check(status.GetResource(Hp) == 6f, $"MaxHp 증가 후 HP 기대 6, 실제 {status.GetResource(Hp)}");
        status.SetBase(MaxHp, 7f);
        Check(status.GetResource(Hp) == 6f, $"HP보다 큰 MaxHp로 감소 후 HP 기대 6, 실제 {status.GetResource(Hp)}");

        Check(string.Join(",", events) == "MaxHp:10>6:hp6,Hp:10>6,MaxHp:6>8:hp6,MaxHp:8>7:hp6", "이벤트: " + string.Join(",", events));
    }

    static void MaxHpModifierDecreaseClampsHp()
    {
        var status = NewStatus();
        var source = new object();
        status.AddModifier(MaxHp, StatModOp.Add, 10f, source);
        status.SetResource(Hp, 20f);
        var events = new List<string>();
        status.OnResourceChanged += (resource, previous, current) => events.Add($"{previous}>{current}");

        status.RemoveModifiers(source);
        Check(status.GetResource(Hp) == 10f, $"수정자 제거 후 HP 기대 10, 실제 {status.GetResource(Hp)}");
        status.AddModifier(MaxHp, StatModOp.Multiply, 0.5f, source);
        Check(status.GetResource(Hp) == 5f, $"감소 수정자 추가 후 HP 기대 5, 실제 {status.GetResource(Hp)}");

        Check(string.Join(",", events) == "20>10,10>5", "HP 이벤트: " + string.Join(",", events));
    }

    static void StatusReleaseResetsHpSilently()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        int events = 0;
        status.OnResourceChanged += (resource, previous, current) => events++;

        status.ReleaseInternal();

        Check(status.GetResource(Hp) == 0f && events == 0, $"정리 후 HP {status.GetResource(Hp)}, 이벤트 {events}");
        status.SetBase(MaxHp, 5f);
        status.SetResource(Hp, 5f);
        Check(status.GetResource(Hp) == 5f && events == 1, $"정리 후 다시 설정한 HP {status.GetResource(Hp)}, 이벤트 {events}");
    }

    static void FlatDamageReturnsPower()
    {
        Check(new FlatDamageExecution().Execute(null, new Status(), 7.5f) == 7.5f, "기본 계산이 power를 바꿨다");
    }

    static void PeriodicDamageUsesDefinitionExecution()
    {
        var ownerStatus = NewStatus();
        ownerStatus.SetResource(Hp, 10f);
        var casterStatus = new Status();
        var caster = new ProjectT.Skill.Tests.TestActor { Status = casterStatus };
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = 3f };
        var agent = NewAgent(ownerStatus);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 5f, execution: execution), caster);
        Check(execution.Calls.Count == 1 && ReferenceEquals(execution.Calls[0].Source, casterStatus) && ReferenceEquals(execution.Calls[0].Target, ownerStatus) && execution.Calls[0].Power == 5f, "계산 클래스 인자가 다르다");
        Check(ownerStatus.GetResource(Hp) == 7f, $"HP 기대 7, 실제 {ownerStatus.GetResource(Hp)}");

        execution.Result = -4f;
        agent.TickInternal(0.5f);
        Check(execution.Calls.Count == 2 && ownerStatus.GetResource(Hp) == 7f, $"음수 피해가 HP를 바꿨다: {ownerStatus.GetResource(Hp)}");
    }

    static void PeriodicDamageSourceIsNullWithoutLiveCaster()
    {
        var withoutStatus = new ProjectT.Skill.Tests.TestActor();
        foreach (var caster in new[] { null, withoutStatus })
        {
            var execution = new ProjectT.Skill.Tests.RecordingDamageExecution();
            var agent = NewAgent(NewStatus());
            agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 5f, execution: execution), caster);
            Check(execution.Calls.Count == 1 && execution.Calls[0].Source == null, "살아 있는 시전자의 Status가 아닌데 source가 null이 아니다");
        }
    }

    static void PeriodicDamageIgnoresNaN()
    {
        var ownerStatus = NewStatus();
        ownerStatus.SetResource(Hp, 10f);
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = float.NaN };
        var agent = NewAgent(ownerStatus);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 5f, execution: execution), null);

        Check(execution.Calls.Count == 1 && ownerStatus.GetResource(Hp) == 10f, $"NaN 피해가 HP를 바꿨다: {ownerStatus.GetResource(Hp)}");
    }

    static void DotReducesHpEachTick()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        AddBuff(1, BuffType.Dot, 1f, 0.25f, 3f);
        var agent = NewAgent(status);
        var hps = new List<float>();
        status.OnResourceChanged += (resource, previous, current) => hps.Add(current);

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        agent.TickInternal(0.25f);
        agent.TickInternal(0.25f);
        agent.TickInternal(0.25f);
        Check(EffectCount(agent) == 1, "마지막 실행에 만료되었다");
        agent.TickInternal(0.25f);

        Check(string.Join(",", hps) == "7,4,1,0", "HP 변화: " + string.Join(",", hps));
        Check(EffectCount(agent) == 0, "ticks*period에 만료되지 않았다");
        agent.ReleaseInternal();
        Check(status.GetResource(Hp) == 0f, "만료나 정리가 피해를 되돌렸다");
    }

    static void DotWithoutStatusThrows()
    {
        AddBuff(1, BuffType.Dot, 1f, 0.25f, 3f);
        var agent = NewAgent();

        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(SkillDefinitions.GetEffect(1), null), "Status 없는 도트");

        Check(EffectCount(agent) == 0, "실패한 효과가 등록되었다");
    }

    static void ConverterMapsDotDamage()
    {
        var types = new[] { BuffType.AddATK, BuffType.AddDEF, BuffType.LowATK, BuffType.LowDEF, BuffType.Strun, BuffType.Dot };
        for (int i = 0; i < types.Length; i++)
        {
            AddBuff(i + 1, types[i], 1f, 0.5f, 30f);
            var effect = SkillDefinitions.GetEffect(i + 1);
            bool mapped = types[i] == BuffType.Dot ? effect.Power == 30f && effect.DamageExecution is FlatDamageExecution : effect.Power == 0f && effect.DamageExecution == null;
            Check(mapped, $"{types[i]} 피해: {effect.Power}, {effect.DamageExecution}");
        }

        AddBuff(10, BuffType.Dot, 1f, 0.5f, 5f);
        Check(ReferenceEquals(SkillDefinitions.GetEffect(10).DamageExecution, SkillDefinitions.GetEffect(6).DamageExecution), "기본 계산을 공용 인스턴스로 쓰지 않는다");
    }
}
