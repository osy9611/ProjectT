using System;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void RemoveReceivesReason()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f), null);
        agent.ApplyEffect(TestEffect(2, 5f), null);

        agent.TickInternal(1f);
        agent.ReleaseInternal();

        string reasons = string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons);
        Check(reasons == "Expired,Released", "제거 사유: " + reasons);
    }

    static void ChainEffectAppliesNextOnlyOnExpiry()
    {
        var first = TestEffect(1, 1f);
        var next = TestEffect(2, 1f, 0.25f);
        var agent = NewAgent();
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (behavior.EffectId == 1 && reason == EffectRemoveReason.Expired)
                agent.ApplyEffect(next, null);
        };

        agent.ApplyEffect(first, null);
        agent.TickInternal(1f);
        var ids = GetEffectIds(agent);
        Check(ids.Count == 1 && ids[0] == 2, "만료 시 다음 효과가 적용되지 않았다");
        CheckExecutes(2, 1, "연쇄 적용한 Tick");
        agent.TickInternal(0.25f);
        CheckExecutes(2, 2, "다음 Tick");

        agent.ApplyEffect(first, null);
        agent.ReleaseInternal();
        Check(EffectCount(agent) == 0, "정리 중 다음 효과가 적용되었다");
        string reasons = string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons);
        Check(reasons == "Expired,Released,Released", "제거 사유: " + reasons);
    }

    static void ChainEffectCanReapplySameId()
    {
        var effect = TestEffect(1, 1f);
        var agent = NewAgent();
        int chains = 0;
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (reason == EffectRemoveReason.Expired && chains++ == 0)
                agent.ApplyEffect(effect, null);
        };
        agent.ApplyEffect(effect, null);
        var first = GetBehaviors(agent)[0];

        agent.TickInternal(1f);
        var behaviors = GetBehaviors(agent);
        Check(behaviors.Count == 1 && !ReferenceEquals(behaviors[0], first) && ProjectT.Skill.Tests.TestBehavior.Applies == 2, "제거 콜백에서 같은 id를 새로 적용하지 못했다");

        agent.TickInternal(1f);
        Check(EffectCount(agent) == 0 && ProjectT.Skill.Tests.TestBehavior.Removes == 2, "다시 적용한 효과가 만료되지 않았다");
    }

    static void RefreshRestartsTimer()
    {
        var effect = TestEffect(1, 1f);
        var agent = NewAgent();
        var source = new ProjectT.Skill.Tests.TestActor();
        agent.ApplyEffect(effect, source);
        var first = GetBehaviors(agent)[0];

        agent.TickInternal(0.75f);
        agent.ApplyEffect(effect, new ProjectT.Skill.Tests.TestActor());
        agent.TickInternal(0.5f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "갱신 전 시간으로 만료되었다");
        var behaviors = GetBehaviors(agent);
        Check(behaviors.Count == 1 && ReferenceEquals(behaviors[0], first) && ReferenceEquals(first.Source, source), "갱신이 인스턴스나 시전자를 바꿨다");
        Check(ProjectT.Skill.Tests.TestBehavior.Applies == 1, $"OnApply 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestBehavior.Applies}");

        agent.TickInternal(0.5f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "갱신 시점부터 duration에 만료되지 않았다");
    }

    static void RefreshKeepsModifiers()
    {
        var status = NewStatus();
        AddBuff(1, BuffType.AddATK, 1f, 0f, 20f);
        var agent = NewAgent(status);
        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);

        agent.TickInternal(0.75f);
        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        CheckStatNear(status, Atk, 12f, "갱신 직후");
        agent.TickInternal(0.75f);
        CheckStatNear(status, Atk, 12f, "처음 만료 시점");
        agent.TickInternal(0.25f);
        CheckStat(status, Atk, 10f, "갱신 후 만료");
    }

    static void ReapplyDuringExpiryKeepsEffect()
    {
        var first = TestEffect(1, 1f);
        var second = TestEffect(2, 1f);
        foreach (var order in new[] { new[] { first, second }, new[] { second, first } })
        {
            var agent = NewAgent();
            ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
            {
                if (behavior.EffectId == 2 && reason == EffectRemoveReason.Expired)
                    agent.ApplyEffect(first, null);
            };
            foreach (var effect in order)
                agent.ApplyEffect(effect, null);

            agent.TickInternal(1f);
            var ids = GetEffectIds(agent);
            Check(ids.Count == 1 && ids[0] == 1, $"순서 {order[0].Id},{order[1].Id}: 다시 적용한 효과가 남지 않았다");
            agent.TickInternal(0.5f);
            Check(EffectCount(agent) == 1, $"순서 {order[0].Id},{order[1].Id}: 다시 적용한 효과가 1초 전에 만료되었다");
            agent.TickInternal(0.5f);
            Check(EffectCount(agent) == 0, $"순서 {order[0].Id},{order[1].Id}: 다시 적용한 효과가 1초에 만료되지 않았다");
        }
    }

    static void OverlappingStunsKeepStateUntilLast()
    {
        AddBuff(1, BuffType.Strun, 1f);
        AddBuff(2, BuffType.Strun, 2f);
        var agent = NewAgent();
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}:{agent.HasTag(tag)}");

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);
        Check(agent.HasTag(Stun) && string.Join(",", events) == "State.Stun:True:True", "스턴 획득: " + string.Join(",", events));

        agent.TickInternal(1f);
        Check(agent.HasTag(Stun) && events.Count == 1, "첫 스턴 만료로 태그가 해제되었다");

        agent.TickInternal(1f);
        Check(!agent.HasTag(Stun) && string.Join(",", events) == "State.Stun:True:True,State.Stun:False:False", "스턴 해제: " + string.Join(",", events));
    }

    static void StunBlocksActivationAndCancelsActiveSkills()
    {
        AddBuff(1, BuffType.Strun, 1f);
        var agent = NewAgent();
        agent.GiveSkill(StunSkill(10));
        agent.GiveSkill(StunSkill(11));
        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        var log = new List<string>();
        ProjectT.Skill.Tests.TestSkillAction.Log = log;
        agent.OnTagChanged += (tag, active) => log.Add($"{tag.Name}:{active}");

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        Check(string.Join(",", log) == "skill,skill,State.Stun:True", "스턴 획득 순서: " + string.Join(",", log));

        agent.ActivateSkill(10);
        agent.TickInternal(0.5f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2 && ProjectT.Skill.Tests.TestSkillAction.Updates == 0 && GetActiveActions(agent).Count == 0, "스턴 중 스킬이 발동되거나 갱신되었다");

        agent.TickInternal(0.5f);
        agent.ActivateSkill(10);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 3, "스턴 해제 후 발동되지 않았다");
    }

    static void ReleaseClearsStatesSilently()
    {
        AddBuff(1, BuffType.Strun, 5f);
        AddBuff(2, BuffType.Strun, 5f);
        var agent = NewAgent();
        Action<SkillTag, bool> failing = (tag, active) => throw new InvalidOperationException("handler");
        agent.OnTagChanged += failing;
        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(SkillDefinitions.GetEffect(1), null), "구독자 실패");
        agent.OnTagChanged -= failing;
        Check(EffectCount(agent) == 0 && agent.HasTag(Stun), "실패한 적용이 태그를 남기지 않았다");
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);
        int events = 0;
        agent.OnTagChanged += (tag, active) => events++;

        agent.ReleaseInternal();

        Check(!agent.HasTag(Stun) && events == 0, $"정리 후 태그 {agent.HasTag(Stun)}, 이벤트 {events}");
        agent.OpenInternal();
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);
        agent.TickInternal(5f);
        Check(!agent.HasTag(Stun) && events == 2, $"정리 후 스턴 획득·해제 이벤트 기대 2, 실제 {events}");
    }

    static void SelfStunDuringActivateCancelsSkill()
    {
        AddBuff(1, BuffType.Strun, 5f);
        var agent = NewAgent();
        agent.GiveSkill(StunSkill(10));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);

        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);

        Check(agent.HasTag(Stun), "스턴이 적용되지 않았다");
        Check(ProjectT.Skill.Tests.TestSkillAction.Cancels == 1 && ProjectT.Skill.Tests.TestSkillAction.Updates == 0 && GetActiveActions(agent).Count == 0, $"스턴 중 스킬이 남았다 Cancels={ProjectT.Skill.Tests.TestSkillAction.Cancels} Updates={ProjectT.Skill.Tests.TestSkillAction.Updates}");
    }

    static void CooldownReductionShortensCooldown()
    {
        AddSkill(20, SkillType.Melee, 2f);
        var cases = new (float Reduction, float Expected)[] { (0f, 2f), (0.25f, 1.5f), (1f, 0f), (1.5f, 0f) };
        foreach (var testCase in cases)
        {
            var status = new Status();
            status.SetBase(CooldownReduction, testCase.Reduction);
            var agent = NewAgent(status);
            agent.GiveSkill(SkillDefinitions.GetSkill(20));
            agent.ActivateSkill(20);
            var spec = GetAction(agent, 20).Spec;
            Check(spec.CoolDownRemaining == testCase.Expected, $"감소율 {testCase.Reduction}: 쿨다운 기대 {testCase.Expected}, 실제 {spec.CoolDownRemaining}");
        }

        var plain = NewAgent();
        plain.GiveSkill(SkillDefinitions.GetSkill(20));
        plain.ActivateSkill(20);
        Check(GetAction(plain, 20).Spec.CoolDownRemaining == 2f, "Status 없는 Actor의 쿨다운이 바뀌었다");
    }
}
