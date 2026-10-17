using System;
using System.Collections.Generic;
using DesignEnum;
using DesignTable;
using ProjectT;
using ProjectT.Skill;
using ProjectT.SkillAdapter;

static partial class Program
{
    static void SkillRegistersOnFirstUse()
    {
        AddSkill(1, SkillType.Melee, 0f);
        var agent = NewAgent();

        agent.GiveSkill(SkillDefinitions.GetSkill(1));

        Check(GetAction(agent, 1) is Action_Melee, "스킬 액션을 생성하지 않았다");
    }

    static void ReactivatedSkillKeepsUpdating()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));

        ProjectT.Skill.Tests.TestSkillAction.EndOnUpdate = true;
        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);
        agent.TickInternal(0.1f);

        ProjectT.Skill.Tests.TestSkillAction.EndOnUpdate = false;
        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);
        agent.TickInternal(0.1f);

        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2, "재발동되지 않았다");
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 3, $"갱신 횟수 기대 3, 실제 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
    }

    static void ActivationDuringUpdateKeepsLoop()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));
        agent.GiveSkill(Skill(11));

        ProjectT.Skill.Tests.TestSkillAction.UpdateHook = action =>
        {
            if (action.Spec.Definition.Id == 10)
                agent.ActivateSkill(11);
        };
        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);
        agent.TickInternal(0.1f);

        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2, "두 번째 스킬이 발동되지 않았다");
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 3, $"갱신 횟수 기대 3, 실제 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
    }

    static void CanceledSkillIsNotUpdated()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));

        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);
        agent.CancelSkill(10);
        agent.TickInternal(0.1f);

        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 1, $"갱신 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
    }

    static void InstantSkillActivatesAgainAfterCooldown()
    {
        AddSkill(20, SkillType.Melee, 1f);
        var agent = NewAgent();
        agent.GiveSkill(SkillDefinitions.GetSkill(20));
        var spec = GetAction(agent, 20).Spec;

        agent.ActivateSkill(20);
        Check(spec.CoolDownRemaining == 1f, "첫 발동에서 쿨다운이 시작되지 않았다");

        agent.TickInternal(1.5f);
        agent.ActivateSkill(20);

        Check(spec.CoolDownRemaining == 1f, $"재발동되지 않았다. 남은 쿨다운 {spec.CoolDownRemaining}");
    }

    static void DuplicateSkillKeepsOneInstance()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));
        var first = GetAction(agent, 10);

        bool threw = false;
        try
        {
            agent.GiveSkill(Skill(10));
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Check(threw, "중복 등록이 거부되지 않았다");
        Check(GetActions(agent).Count == 1 && ReferenceEquals(GetAction(agent, 10), first), "기존 인스턴스가 유지되지 않았다");
    }

    static void ApplyFailureDoesNotRegister()
    {
        var effect = TestEffect(1, 1f);
        var agent = NewAgent();
        ProjectT.Skill.Tests.TestBehavior.ThrowOnApply = true;

        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(effect, null), "OnApply 예외 전파");

        Check(EffectCount(agent) == 0, "실패한 효과가 등록되었다");
        agent.TickInternal(1f);
        CheckExecutes(1, 0, "실패 후 Tick");

        ProjectT.Skill.Tests.TestBehavior.ThrowOnApply = false;
        agent.ApplyEffect(effect, null);
        Check(EffectCount(agent) == 1, "실패한 id를 다시 적용할 수 없다");
        agent.ReleaseInternal();
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, $"제거 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestBehavior.Removes}");
    }

    static void EffectExpiryRemovesOnce()
    {
        var effect = TestEffect(1, 1f);
        var agent = NewAgent();

        agent.ApplyEffect(effect, null);
        agent.TickInternal(1f);
        agent.TickInternal(1f);
        agent.ReleaseInternal();

        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, $"제거 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestBehavior.Removes}");
        Check(EffectCount(agent) == 0, "만료 후 효과가 남았다");

        agent.OpenInternal();
        agent.ApplyEffect(effect, null);
        Check(EffectCount(agent) == 1, "만료된 효과를 다시 적용할 수 없다");
    }

    static void ReleaseClearsEffectsBeforeSkills()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));
        agent.ActivateSkill(10);
        agent.ApplyEffect(TestEffect(1, 1f), null);
        var log = new List<string>();
        ProjectT.Skill.Tests.TestSkillAction.Log = log;
        ProjectT.Skill.Tests.TestBehavior.Log = log;

        agent.ReleaseInternal();

        Check(string.Join(",", log) == "effect,skill", "정리 순서: " + string.Join(",", log));
        Check(EffectCount(agent) == 0, "효과가 남았다");
        Check(GetActions(agent).Count == 0, "스킬이 남았다");
        Check(GetActiveActions(agent).Count == 0, "활성 스킬이 남았다");
    }

    static void ClosedAgentRejectsRegistration()
    {
        var effect = TestEffect(1, 1f);
        var agent = new SkillAgent(new ProjectT.Skill.Tests.TestActor());

        CheckThrows<InvalidOperationException>(() => agent.GiveSkill(Skill(10)), "Open 전 GiveSkill");
        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(effect, null), "Open 전 ApplyEffect");

        agent.OpenInternal();
        agent.ReleaseInternal();

        CheckThrows<InvalidOperationException>(() => agent.GiveSkill(Skill(10)), "Release 후 GiveSkill");
        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(effect, null), "Release 후 ApplyEffect");
        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(Effect(2, EffectDurationPolicy.Instant, 0f, 0f, () => new ProjectT.Skill.Tests.TestBehavior()), null), "Release 후 Instant");
        Check(GetActions(agent).Count == 0 && EffectCount(agent) == 0 && ProjectT.Skill.Tests.TestBehavior.Applies == 0, "닫힌 상태에서 등록되었다");
    }

    static void NoPeriodEffectExpiresAtDuration()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f), null);

        agent.TickInternal(0.5f);
        agent.TickInternal(0.25f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "duration 전에 만료되었다");

        agent.TickInternal(0.25f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "duration에 만료되지 않았다");
        Check(ProjectT.Skill.Tests.TestBehavior.ExecuteCount(1) == 0, "주기 없는 효과가 실행되었다");
    }

    static void PeriodicEffectTicksAndExpires()
    {
        var agent = NewAgent();

        agent.ApplyEffect(TestEffect(1, 1f, 0.25f), null);
        CheckExecutes(1, 1, "적용 직후");
        agent.TickInternal(0.125f);
        CheckExecutes(1, 1, "0.125초");
        agent.TickInternal(0.125f);
        CheckExecutes(1, 2, "0.25초");
        agent.TickInternal(0.25f);
        agent.TickInternal(0.25f);
        CheckExecutes(1, 4, "0.75초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0, "ticks*period 전에 만료되었다");

        agent.TickInternal(0.25f);
        CheckExecutes(1, 4, "1초");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "ticks*period에 만료되지 않았다");
    }

    static void LargeDeltaCatchesUp()
    {
        var agent = NewAgent();

        agent.ApplyEffect(TestEffect(1, 1f, 0.25f), null);
        agent.TickInternal(10f);

        CheckExecutes(1, 4, "큰 delta");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "큰 delta에서 만료되지 않았다");
        Check(EffectCount(agent) == 0, "만료 후 효과가 남았다");
    }

    static void ReleaseDuringApplyRemovesOnce()
    {
        var agent = NewAgent();
        var events = new List<string>();
        agent.OnEffectApplied += (handle, definition) => events.Add("applied");
        agent.OnEffectRemoved += (handle, definition, reason) => events.Add("removed");
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = () => agent.ReleaseInternal();

        var handle = agent.ApplyEffect(TestEffect(1, 1f, 0.5f), null);

        Check(EffectCount(agent) == 0, "정리 후 효과가 등록되었다");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, $"제거 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestBehavior.Removes}");
        Check(ProjectT.Skill.Tests.TestBehavior.Reasons[0] == EffectRemoveReason.Released, "OnApply 중 정리된 효과의 사유가 Released가 아니다");
        CheckExecutes(1, 0, "정리된 효과의 첫 실행");
        Check(handle.Id == 0 && events.Count == 0, $"등록되지 않은 효과가 핸들 {handle.Id}이나 이벤트 {events.Count}개를 냈다");
    }

    static void TicksToleratesDivisionError()
    {
        var cases = new (float Duration, float Period, int Ticks)[] { (0.9f, 0.3f, 3), (1.3f, 0.1f, 13), (1.8f, 0.6f, 3), (1.0f, 0.3f, 3), (0.25f, 0.1f, 2) };
        var agent = NewAgent();
        for (int i = 0; i < cases.Length; i++)
            agent.ApplyEffect(TestEffect(i + 1, cases[i].Duration, cases[i].Period), null);

        agent.TickInternal(100f);

        for (int i = 0; i < cases.Length; i++)
            CheckExecutes(i + 1, cases[i].Ticks, $"{cases[i].Duration}/{cases[i].Period}");
    }

    static void ZeroTicksExpiresNextTick()
    {
        var agent = NewAgent();

        agent.ApplyEffect(TestEffect(1, 0.5f, 1f), null);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 0 && EffectCount(agent) == 1, "적용 중에 만료되었다");

        agent.TickInternal(0.01f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1, "다음 Tick에 만료되지 않았다");
        CheckExecutes(1, 0, "ticks 0");
    }

    static void EffectAppliedDuringTickStartsNextTick()
    {
        var second = TestEffect(2, 1f, 0.25f);
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f, 0.25f), null);
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = behavior =>
        {
            if (behavior.EffectId == 1)
                agent.ApplyEffect(second, null);
        };

        agent.TickInternal(0.25f);
        CheckExecutes(2, 1, "추가된 Tick");

        agent.TickInternal(0.25f);
        CheckExecutes(2, 2, "다음 Tick");
        CheckExecutes(1, 3, "기존 효과");
    }

    static void CancelActiveKeepsTickWorking()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));
        agent.GiveSkill(Skill(11));
        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        agent.TickInternal(0.1f);

        agent.CancelActiveSkillsInternal();
        Check(ProjectT.Skill.Tests.TestSkillAction.Cancels == 2, $"취소 횟수 기대 2, 실제 {ProjectT.Skill.Tests.TestSkillAction.Cancels}");
        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 2, $"취소 후 갱신되었다. 갱신 횟수 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
        Check(GetActiveActions(agent).Count == 0, "취소된 스킬이 활성 목록에 남았다");

        agent.ActivateSkill(10);
        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 3, $"재발동 후 갱신 횟수 기대 3, 실제 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
        Check(GetActions(agent).Count == 2, "취소가 부여된 스킬을 제거했다");
    }

    static void SourceIsStored()
    {
        var agent = NewAgent();
        var source = new ProjectT.Skill.Tests.TestActor();

        agent.ApplyEffect(TestEffect(1, 1f), source);

        Check(ReferenceEquals(GetBehaviors(agent)[0].Source, source), "source가 저장되지 않았다");
    }

    static void ReleaseDuringTickKeepsLoop()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f, 0.25f), null);
        agent.ApplyEffect(TestEffect(2, 0.25f), null);
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = _ => agent.ReleaseInternal();

        agent.TickInternal(0.25f);
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 2, $"제거 횟수 기대 2, 실제 {ProjectT.Skill.Tests.TestBehavior.Removes}");
        Check(EffectCount(agent) == 0, "효과가 남았다");

        agent.OpenInternal();
        agent.GiveSkill(Skill(10));
        agent.GiveSkill(Skill(11));
        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        ProjectT.Skill.Tests.TestSkillAction.UpdateHook = _ => agent.ReleaseInternal();

        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 1, $"갱신 횟수 기대 1, 실제 {ProjectT.Skill.Tests.TestSkillAction.Updates}");
        Check(GetActions(agent).Count == 0 && GetActiveActions(agent).Count == 0, "스킬이 남았다");
    }

    static void RegistrationDuringReleaseThrows()
    {
        var second = TestEffect(2, 1f);
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f), null);
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (_, _) => agent.ApplyEffect(second, null);

        CheckThrows<InvalidOperationException>(() => agent.ReleaseInternal(), "정리 중 ApplyEffect");

        Check(EffectCount(agent) == 0, "정리 중 등록한 효과가 남았다");
    }

    static void ReleaseDuringCancelActiveKeepsLoop()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10));
        agent.GiveSkill(Skill(11));
        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
        };

        agent.CancelActiveSkillsInternal();

        Check(GetActions(agent).Count == 0 && GetActiveActions(agent).Count == 0, "정리 후 스킬이 남았다");
    }

    static void ConverterMapsTableRows()
    {
        var skills = new (SkillType Type, Type Action)[] { (SkillType.Normal, typeof(Action_Normal)), (SkillType.Melee, typeof(Action_Melee)), (SkillType.Range, typeof(Action_Range)) };
        for (int i = 0; i < skills.Length; i++)
        {
            AddSkill(i + 1, skills[i].Type, i + 0.5f);
            var skill = SkillDefinitions.GetSkill(i + 1);
            Type action = skill.ActionFactory().GetType();
            Check(skill.Id == i + 1 && skill.CoolTime == i + 0.5f && action == skills[i].Action, $"스킬 {skills[i].Type} 변환: {skill.Id}, {skill.CoolTime}, {action.Name}");
        }

        var types = new[] { BuffType.AddATK, BuffType.AddDEF, BuffType.LowATK, BuffType.LowDEF, BuffType.Strun, BuffType.Dot };
        for (int i = 0; i < types.Length; i++)
        {
            AddBuff(i + 1, types[i], i + 1f, i + 0.25f);
            var effect = SkillDefinitions.GetEffect(i + 1);
            bool mapped = effect.Id == i + 1 && effect.Duration == i + 1f && effect.Period == i + 0.25f && effect.DurationPolicy == EffectDurationPolicy.Duration && effect.StackPolicy == EffectStackPolicy.Refresh && effect.MaxStack == 1 && effect.BehaviorFactory == null;
            Check(mapped, $"효과 {types[i]} 변환: {effect.Id}, {effect.Duration}, {effect.Period}, {effect.DurationPolicy}, {effect.StackPolicy}, {effect.MaxStack}");
        }
    }

    static void ConverterCachesById()
    {
        AddSkill(1, SkillType.Melee, 1f);
        AddBuff(1, BuffType.AddATK, 1f);

        Check(ReferenceEquals(SkillDefinitions.GetSkill(1), SkillDefinitions.GetSkill(1)), "스킬 정의가 캐시되지 않았다");
        Check(ReferenceEquals(SkillDefinitions.GetEffect(1), SkillDefinitions.GetEffect(1)), "효과 정의가 캐시되지 않았다");
    }

    static void ConverterInvalidatesOnTableChange()
    {
        AddSkill(1, SkillType.Melee, 1f);
        AddBuff(1, BuffType.AddATK, 1f);
        var oldSkill = SkillDefinitions.GetSkill(1);
        var oldEffect = SkillDefinitions.GetEffect(1);

        Global.Table = new DataMgr();
        AddSkill(1, SkillType.Range, 2f);
        AddBuff(1, BuffType.Dot, 3f, 1f);
        var skill = SkillDefinitions.GetSkill(1);
        var effect = SkillDefinitions.GetEffect(1);

        Check(!ReferenceEquals(skill, oldSkill) && skill.CoolTime == 2f && skill.ActionFactory() is Action_Range, "이전 테이블의 스킬 정의가 남았다");
        Check(!ReferenceEquals(effect, oldEffect) && effect.Duration == 3f && effect.DamageExecution != null && effect.Modifiers.Count == 0, "이전 테이블의 효과 정의가 남았다");
    }

    static void ConverterThrowsOnUnknownEnum()
    {
        AddSkill(1, (SkillType)99, 1f);
        AddBuff(1, (BuffType)99, 1f);

        CheckThrows<ArgumentOutOfRangeException>(() => SkillDefinitions.GetSkill(1), "알 수 없는 스킬 enum");
        CheckThrows<ArgumentOutOfRangeException>(() => SkillDefinitions.GetEffect(1), "알 수 없는 효과 enum");
    }
}
