using System;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void ChangedSourceSerialDropsSourceStatus()
    {
        var ownerStatus = NewStatus();
        ownerStatus.SetResource(Hp, 10f);
        var casterStatus = new Status();
        var caster = new ProjectT.Skill.Tests.TestActor { Status = casterStatus, SpawnSerial = 1 };
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = 1f };
        var agent = NewAgent(ownerStatus);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 2f, 0.5f, null, power: 5f, execution: execution), caster);
        caster.SpawnSerial = 2;
        agent.TickInternal(0.5f);
        caster.SpawnSerial = 3;
        agent.TickInternal(0.5f);
        Check(execution.Calls.Count == 3 && ReferenceEquals(execution.Calls[0].Source, casterStatus) && execution.Calls[1].Source == null && execution.Calls[2].Source == null, "반납·재스폰된 시전자의 Status가 source로 넘어갔다");
        Check(ownerStatus.GetResource(Hp) == 7f, $"source가 없어도 피해는 계속되어야 한다: HP {ownerStatus.GetResource(Hp)}");

        agent.ApplyEffect(Effect(2, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 5f, execution: execution), caster);
        Check(execution.Calls.Count == 4 && ReferenceEquals(execution.Calls[3].Source, casterStatus), "다시 스폰된 시전자가 새로 건 효과의 source가 아니다");
    }

    static void AbilityOwnerDoesNotNeedActor()
    {
        var owner = new ProjectT.Skill.Tests.PlainOwner { Status = NewStatus() };
        owner.Status.SetResource(Hp, 10f);
        var agent = new SkillAgent(owner);
        owner.SkillAgent = agent;
        WatchEffects(agent);
        agent.OpenInternal();
        var source = new ProjectT.Skill.Tests.PlainOwner { Status = new Status() };
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = 2f };

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, () => new ProjectT.Skill.Tests.TestBehavior(), new[] { new EffectModifier(Atk, StatModOp.Add, 5f) }, power: 3f, execution: execution), source);
        Check(owner.Status.Get(Atk) == 15f && owner.Status.GetResource(Hp) == 8f, $"소유자 Status에 수정자·피해가 반영되지 않았다: Atk {owner.Status.Get(Atk)}, HP {owner.Status.GetResource(Hp)}");
        Check(ReferenceEquals(GetBehaviors(agent)[0].Source, source) && ReferenceEquals(execution.Calls[0].Source, source.Status), "시전자나 시전자 Status가 전달되지 않았다");

        agent.GiveSkill(Skill(10, effects: new[] { TestEffect(2, 1f) }));
        var action = (ProjectT.Skill.Tests.TestSkillAction)GetAction(agent, 10);
        action.ApplyEffectsTo(agent);
        Check(ReferenceEquals(action.Owner, owner) && ReferenceEquals(GetBehaviors(agent)[1].Source, owner), "스킬 소유자나 스킬 효과의 출처가 소유자가 아니다");
    }

    static void FactoryCreatesOneActionPerGiveSkill()
    {
        int created = 0;
        var definition = Skill(10, factory: () =>
        {
            created++;
            return new ExternalGame.ExternalSkillAction();
        });
        var first = NewAgent();
        var second = NewAgent();

        first.GiveSkill(definition);
        second.GiveSkill(definition);
        CheckThrows<ArgumentException>(() => first.GiveSkill(definition), "같은 정의의 중복 GiveSkill");

        Check(created == 2, $"팩토리 호출 횟수 기대 2, 실제 {created}");
        Check(GetAction(first, 10) is ExternalGame.ExternalSkillAction && !ReferenceEquals(GetAction(first, 10), GetAction(second, 10)), "Agent마다 따로 만든 인스턴스가 아니다");
    }

    static void ApplyEffectsAppliesDefinitionEffectsWithOwnerSource()
    {
        var self = NewAgent();
        var other = NewAgent();
        self.GiveSkill(Skill(10, effects: new[] { TestEffect(1, 1f), TestEffect(2, 1f) }));
        var action = (ProjectT.Skill.Tests.TestSkillAction)GetAction(self, 10);

        action.ApplyEffectsTo(other);
        Check(string.Join(",", GetEffectIds(other)) == "1,2" && EffectCount(self) == 0, "다른 대상에 정의의 효과가 모두 걸리지 않았거나 소유자에게 걸렸다");
        Check(GetBehaviors(other).TrueForAll(behavior => ReferenceEquals(behavior.Source, action.Owner)), "다른 대상 효과의 출처가 소유자가 아니다");

        action.ApplyEffectsTo(action.Owner.SkillAgent);
        Check(string.Join(",", GetEffectIds(self)) == "1,2" && EffectCount(other) == 2, "자신에게 정의의 효과가 모두 걸리지 않았다");
        Check(GetBehaviors(self).TrueForAll(behavior => ReferenceEquals(behavior.Source, action.Owner)), "자신 효과의 출처가 소유자가 아니다");
    }

    static void InsufficientCostBlocksActivation()
    {
        var status = ManaStatus(3f);
        var agent = NewAgent(status);
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10, coolTime: 1f, costs: new[] { new SkillCost(Mp, 4f) }));

        agent.ActivateSkill(10);

        var action = GetAction(agent, 10);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 0 && !action.IsActive && !action.Spec.IsOnCoolDown, "자원이 부족한데 발동했거나 쿨다운이 시작되었다");
        Check(status.GetResource(Mp) == 3f && events.Count == 0, $"자원 {status.GetResource(Mp)}, 이벤트 {string.Join(",", events)}");
    }

    static void CostIsDeductedOnCommit()
    {
        var status = ManaStatus(10f);
        var agent = NewAgent(status);
        agent.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 4f) }));
        agent.GiveSkill(Skill(11, costs: new[] { new SkillCost(Mp, 6f) }));
        float atActivate = -1f;
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => atActivate = status.GetResource(Mp);

        agent.ActivateSkill(10);
        Check(atActivate == 10f && status.GetResource(Mp) == 6f, $"Activate 시점 {atActivate}, 차감 뒤 {status.GetResource(Mp)}");

        agent.ActivateSkill(11);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 2 && status.GetResource(Mp) == 0f, "자원이 비용과 같은데 발동하지 않았거나 차감되지 않았다");
    }

    static void MultipleCostsRequireAllAndDeductAll()
    {
        var status = ManaStatus(5f);
        status.SetResource(Hp, 10f);
        var agent = NewAgent(status);
        agent.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 2f), new SkillCost(Hp, 11f) }));
        agent.GiveSkill(Skill(11, costs: new[] { new SkillCost(Mp, 2f), new SkillCost(Hp, 3f) }));

        agent.ActivateSkill(10);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 0 && status.GetResource(Mp) == 5f && status.GetResource(Hp) == 10f, "비용 하나가 부족한데 발동했거나 일부를 차감했다");

        agent.ActivateSkill(11);
        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 1 && status.GetResource(Mp) == 3f && status.GetResource(Hp) == 7f, $"Mp {status.GetResource(Mp)}, Hp {status.GetResource(Hp)}");
    }

    static void CostRequiresStatusAndDefinedResource()
    {
        var withoutStatus = NewAgent();
        withoutStatus.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 1f) }));
        var withoutResource = NewAgent(NewStatus());
        withoutResource.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 1f) }));

        CheckThrows<InvalidOperationException>(() => withoutStatus.ActivateSkill(10), "Status 없는 소유자의 비용");
        CheckThrows<InvalidOperationException>(() => withoutResource.ActivateSkill(10), "정의되지 않은 비용 자원");

        Check(ProjectT.Skill.Tests.TestSkillAction.Activates == 0 && !GetAction(withoutStatus, 10).IsActive && !GetAction(withoutResource, 10).IsActive, "실패한 발동이 상태를 남겼다");
    }

    static void DefinitionRejectsInvalidCosts()
    {
        CheckThrows<ArgumentException>(() => Skill(10, costs: new[] { new SkillCost(default, 1f) }), "default 비용 자원");
        CheckThrows<ArgumentOutOfRangeException>(() => Skill(10, costs: new[] { new SkillCost(Mp, -1f) }), "음수 비용");
        Check(Skill(10, costs: new[] { new SkillCost(Mp, 0f) }).Costs.Count == 1, "0 비용이 거부되었다");
    }

    static void SkillEventsFollowActivationAndEnd()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10, coolTime: 1f));
        agent.OnSkillActivated += definition => events.Add($"cooldown:{GetAction(agent, definition.Id).Spec.IsOnCoolDown}");
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => events.Add("activate");

        agent.ActivateSkill(10);
        Check(string.Join(",", events) == "activate,activated:10,cooldown:True", "발동 이벤트: " + string.Join(",", events));

        ProjectT.Skill.Tests.TestSkillAction.EndOnUpdate = true;
        agent.TickInternal(0.1f);
        agent.TickInternal(0.1f);
        Check(string.Join(",", events) == "activate,activated:10,cooldown:True,ended:10:False", "종료 이벤트: " + string.Join(",", events));
    }

    static void EndDuringActivateIsReportedAfterActivated()
    {
        AddSkill(1, SkillType.Normal, 0f);
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(SkillDefinitions.GetSkill(1));
        agent.GiveSkill(Skill(10));

        agent.ActivateSkill(1);
        Check(string.Join(",", events) == "activated:1,ended:1:False", "즉시형 스킬 이벤트: " + string.Join(",", events));

        events.Clear();
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.CancelSkill(10);
        agent.ActivateSkill(10);
        Check(string.Join(",", events) == "activated:10,ended:10:True" && !GetAction(agent, 10).IsActive, "Activate 중 취소 이벤트: " + string.Join(",", events));
    }

    static void CancelPathsRaiseEndedOnce()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        GiveAndActivate(agent, Skill(10, cancelledBy: new[] { TagA }), Skill(11), Skill(12), Skill(13));
        events.Clear();

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, tags: new[] { TagA }), null);
        agent.CancelSkill(11);
        agent.CancelSkill(11);
        GetAction(agent, 11).Cancel();
        agent.CancelActiveSkillsInternal();
        Check(string.Join(",", events) == "ended:10:True,ended:11:True,ended:12:True,ended:13:True", "취소 이벤트: " + string.Join(",", events));

        events.Clear();
        agent.ActivateSkill(12);
        agent.ReleaseInternal();
        Check(string.Join(",", events) == "activated:12,ended:12:True", "정리 이벤트: " + string.Join(",", events));
    }

    static void BlockedActivationRaisesNoEvents()
    {
        var agent = NewAgent(ManaStatus(0f));
        var events = RecordSkillEvents(agent);
        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, tags: new[] { TagA }), null);
        agent.GiveSkill(Skill(10, blocked: new[] { TagA }));
        agent.GiveSkill(Skill(12, costs: new[] { new SkillCost(Mp, 1f) }));
        GiveAndActivate(agent, Skill(11, coolTime: 1f), Skill(13));
        agent.CancelSkill(11);
        events.Clear();

        agent.ActivateSkill(10);
        agent.ActivateSkill(11);
        agent.ActivateSkill(12);
        agent.ActivateSkill(13);
        agent.ActivateSkill(99);

        Check(events.Count == 0 && ProjectT.Skill.Tests.TestSkillAction.Activates == 2, $"막힌 발동이 알려졌다: {string.Join(",", events)}, 발동 {ProjectT.Skill.Tests.TestSkillAction.Activates}");
    }

    static void CancelledRightAfterActivationRaisesNoEvents()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        var handle = agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, tags: new[] { TagA }), null);
        agent.GiveSkill(Skill(10, coolTime: 1f, cancelledBy: new[] { TagA }));

        agent.ActivateSkill(10);
        var action = GetAction(agent, 10);
        Check(events.Count == 0 && ProjectT.Skill.Tests.TestSkillAction.Activates == 1 && ProjectT.Skill.Tests.TestSkillAction.Cancels == 1 && !action.IsActive && action.Spec.IsOnCoolDown, "발동 직후 취소: " + string.Join(",", events));

        agent.RemoveEffect(handle);
        agent.TickInternal(1.5f);
        agent.ActivateSkill(10);
        agent.CancelSkill(10);
        Check(string.Join(",", events) == "activated:10,ended:10:True", "다음 발동 이벤트: " + string.Join(",", events));
    }

    static void TagGainedInActivatedHandlerCancelsSkill()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.OnSkillActivated += _ => agent.ApplyEffect(Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, tags: new[] { TagA }), null);

        agent.ActivateSkill(10);

        Check(string.Join(",", events) == "activated:10,ended:10:True" && !GetAction(agent, 10).IsActive, "발동 알림 중 태그 취소: " + string.Join(",", events));
    }

    static void ReactivationInActivatedHandlerIsIgnoredForEndedSkill()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () =>
        {
            if (ProjectT.Skill.Tests.TestSkillAction.Activates == 1)
                GetAction(agent, 10).End();
        };
        bool reactivated = false;
        agent.OnSkillActivated += _ =>
        {
            if (reactivated)
                return;

            reactivated = true;
            agent.ActivateSkill(10);
        };

        agent.ActivateSkill(10);
        Check(string.Join(",", events) == "activated:10,ended:10:False" && ProjectT.Skill.Tests.TestSkillAction.Activates == 1 && !GetAction(agent, 10).IsActive && ActiveCount(agent) == 0, $"발동 알림 중 재발동: {string.Join(",", events)}, 발동 {ProjectT.Skill.Tests.TestSkillAction.Activates}");

        var cancelling = NewAgent();
        var cancellingEvents = RecordSkillEvents(cancelling);
        cancelling.GiveSkill(Skill(10));
        int activations = 0;
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () =>
        {
            if (++activations == 1)
                GetAction(cancelling, 10).End();
            else
                GetAction(cancelling, 10).Cancel();
        };
        bool cancellingReactivated = false;
        cancelling.OnSkillActivated += _ =>
        {
            if (cancellingReactivated)
                return;

            cancellingReactivated = true;
            cancelling.ActivateSkill(10);
        };

        cancelling.ActivateSkill(10);
        Check(string.Join(",", cancellingEvents) == "activated:10,ended:10:False" && activations == 1, $"안쪽 발동의 Cancel이 바깥 종료 종류를 바꿨다: {string.Join(",", cancellingEvents)}, 발동 {activations}");

        var fromEnded = NewAgent();
        var fromEndedEvents = RecordSkillEvents(fromEnded);
        fromEnded.GiveSkill(Skill(10));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => GetAction(fromEnded, 10).End();
        bool endedReactivated = false;
        fromEnded.OnSkillEnded += (definition, reason) =>
        {
            if (endedReactivated)
                return;

            endedReactivated = true;
            fromEnded.ActivateSkill(10);
        };

        fromEnded.ActivateSkill(10);
        Check(string.Join(",", fromEndedEvents) == "activated:10,ended:10:False,activated:10,ended:10:False", "종료 알림에서 재발동: " + string.Join(",", fromEndedEvents));
    }

    static void ReactivationDuringCommitIsIgnored()
    {
        var status = ManaStatus(10f);
        var agent = NewAgent(status);
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 1f) }));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () =>
        {
            if (ProjectT.Skill.Tests.TestSkillAction.Activates == 1)
                GetAction(agent, 10).End();
        };
        bool reactivated = false;
        status.OnResourceChanged += (resource, previous, current) =>
        {
            if (reactivated)
                return;

            reactivated = true;
            agent.ActivateSkill(10);
        };

        agent.ActivateSkill(10);

        Check(string.Join(",", events) == "activated:10,ended:10:False" && ProjectT.Skill.Tests.TestSkillAction.Activates == 1, $"비용 알림 중 재발동: {string.Join(",", events)}, 발동 {ProjectT.Skill.Tests.TestSkillAction.Activates}");
        Check(status.GetResource(Mp) == 9f && !GetAction(agent, 10).IsActive && ActiveCount(agent) == 0, $"Mp {status.GetResource(Mp)}, 활성 {GetAction(agent, 10).IsActive}, 목록 {ActiveCount(agent)}");
    }

    static void DeactivationDuringActivateCancelsActivatingSkill()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.CancelActiveSkillsInternal();

        agent.ActivateSkill(10);
        Check(events.Count == 0 && !GetAction(agent, 10).IsActive && ActiveCount(agent) == 0, $"첫 발동: 이벤트 {string.Join(",", events)}, 활성 {GetAction(agent, 10).IsActive}, 목록 {ActiveCount(agent)}");

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = null;
        agent.ActivateSkill(10);
        agent.CancelSkill(10);
        events.Clear();

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.CancelActiveSkillsInternal();
        agent.ActivateSkill(10);
        Check(events.Count == 0 && !GetAction(agent, 10).IsActive, $"목록에 남은 이전 항목: 이벤트 {string.Join(",", events)}, 활성 {GetAction(agent, 10).IsActive}");
    }

    static void TagGainedDuringActivateCancelsWithoutEvents()
    {
        var tagEffect = Effect(1, EffectDurationPolicy.Infinite, 0f, 0f, null, tags: new[] { TagA });
        var fresh = NewAgent();
        var freshEvents = RecordSkillEvents(fresh);
        fresh.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => fresh.ApplyEffect(tagEffect, null);

        fresh.ActivateSkill(10);
        Check(freshEvents.Count == 0 && !GetAction(fresh, 10).IsActive, "첫 발동: " + string.Join(",", freshEvents));

        var stale = NewAgent();
        var staleEvents = RecordSkillEvents(stale);
        stale.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = null;
        stale.ActivateSkill(10);
        stale.CancelSkill(10);
        staleEvents.Clear();
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => stale.ApplyEffect(tagEffect, null);

        stale.ActivateSkill(10);
        Check(staleEvents.Count == 0 && !GetAction(stale, 10).IsActive, "목록에 남은 이전 항목: " + string.Join(",", staleEvents));
    }

    static void DefinitionRejectsDuplicateAndNaNCosts()
    {
        CheckThrows<ArgumentException>(() => Skill(10, costs: new[] { new SkillCost(Mp, 1f), new SkillCost(Hp, 1f), new SkillCost(Mp, 2f) }), "같은 자원 중복 비용");
        CheckThrows<ArgumentOutOfRangeException>(() => Skill(10, costs: new[] { new SkillCost(Mp, float.NaN) }), "NaN 비용");
    }

    static void ActivationDuringReleaseIsIgnored()
    {
        var status = ManaStatus(10f);
        var agent = NewAgent(status);
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10, costs: new[] { new SkillCost(Mp, 4f) }));
        agent.ApplyEffect(TestEffect(1, 10f), null);
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (reason == EffectRemoveReason.Released)
                agent.ActivateSkill(10);
        };

        agent.ReleaseInternal();

        Check(events.Count == 0 && ProjectT.Skill.Tests.TestSkillAction.Activates == 0 && status.GetResource(Mp) == 10f, $"정리 중 발동: 이벤트 {string.Join(",", events)}, 발동 {ProjectT.Skill.Tests.TestSkillAction.Activates}, Mp {status.GetResource(Mp)}");
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1 && EffectCount(agent) == 0, "정리가 끝나지 않았다");
        agent.OpenInternal();
        agent.GiveSkill(Skill(10));
    }

    static void SharedActionInstanceIsRejected()
    {
        var shared = new ProjectT.Skill.Tests.TestSkillAction();
        var definition = Skill(10, factory: () => shared);
        var first = NewAgent();
        var second = NewAgent();
        first.GiveSkill(definition);
        var firstOwner = shared.Owner;

        CheckThrows<InvalidOperationException>(() => second.GiveSkill(definition), "이미 초기화된 실행 인스턴스");

        Check(GivenCount(second) == 0 && ReferenceEquals(shared.Owner, firstOwner) && ReferenceEquals(GetAction(first, 10), shared), "거부된 GiveSkill이 상태를 바꿨다");
    }

    static void AdapterMapsFactoriesAndEffects()
    {
        AddBuff(5, BuffType.AddATK, 1f);
        AddSkill(1, SkillType.Normal, 0f);
        AddSkill(2, SkillType.Melee, 0f, 5);
        AddSkill(3, SkillType.Range, 0f);
        AddSkill(4, SkillType.Range, 0f, 99);

        var normal = SkillDefinitions.GetSkill(1);
        var melee = SkillDefinitions.GetSkill(2);
        var range = SkillDefinitions.GetSkill(3);
        Check(normal.ActionFactory() is Action_Normal && melee.ActionFactory() is Action_Melee && range.ActionFactory() is Action_Range && !ReferenceEquals(normal.ActionFactory(), normal.ActionFactory()), "skill_type이 팩토리로 바뀌지 않았거나 팩토리가 같은 인스턴스를 돌려준다");
        Check(normal.Effects.Count == 0 && range.Effects.Count == 0 && melee.Effects.Count == 1 && ReferenceEquals(melee.Effects[0], SkillDefinitions.GetEffect(5)), "skill_buffId가 효과 목록으로 바뀌지 않았다");
        Check(normal.Costs.Count == 0 && melee.Costs.Count == 0 && range.Costs.Count == 0, "테이블 스킬에 비용이 있다");
        CheckThrows<KeyNotFoundException>(() => SkillDefinitions.GetSkill(4), "테이블에 없는 효과를 가리키는 스킬");
    }

    static void ActivationInsideOnEndIsIgnored()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, factory: () => new ProjectT.Skill.Tests.ReactivateOnEndAction()));
        var events = new List<string>();
        agent.OnSkillActivated += definition => events.Add($"act:{definition.Id}");
        agent.OnSkillEnded += (definition, reason) => events.Add($"end:{definition.Id}:{reason == SkillEndReason.Cancelled}");
        ProjectT.Skill.Tests.ReactivateOnEndAction.OnEndHook = () => agent.ActivateSkill(10);
        agent.ActivateSkill(10);

        agent.CancelSkill(10);

        Check(string.Join(",", events) == "act:10,end:10:True" && !GetAction(agent, 10).IsActive, "OnEnd 중 재발동: " + string.Join(",", events));
        ProjectT.Skill.Tests.ReactivateOnEndAction.OnEndHook = null;
    }

    static void ReleaseDuringAgentCallIsRejected()
    {
        var agent = NewAgent();
        var target = NewAgent();
        var rejected = new List<string>();
        void Release(string name)
        {
            try
            {
                agent.ReleaseInternal();
            }
            catch (InvalidOperationException)
            {
                rejected.Add(name);
            }
        }

        agent.GiveSkill(Skill(10, effects: new[] { TestEffect(2, 1f) }));
        agent.GiveSkill(Skill(11, factory: () => new ProjectT.Skill.Tests.ReleasingCanActivateAction()));
        var action = (ProjectT.Skill.Tests.TestSkillAction)GetAction(agent, 10);
        agent.OnSkillActivated += definition => Release($"activated:{definition.Id}");
        ProjectT.Skill.Tests.ReleasingCanActivateAction.CanActivateHook = () => Release("can activate");
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => Release("activate");
        ProjectT.Skill.Tests.TestSkillAction.UpdateHook = _ => Release("update");
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () => Release("cancel");
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = () => Release("apply");
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = _ => Release("execute");
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) => Release("remove");

        agent.ActivateSkill(11);
        agent.ActivateSkill(10);
        var handle = agent.ApplyEffect(TestEffect(1, 5f, 1f), null);
        agent.TickInternal(1f);
        agent.CancelSkill(10);
        agent.RemoveEffect(handle);
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = () => Release("target apply");
        action.ApplyEffectsTo(target);

        string expected = "can activate,activated:11,activate,activated:10,apply,execute,execute,update,cancel,remove,target apply";
        Check(string.Join(",", rejected) == expected, $"거부된 정리: {string.Join(",", rejected)}");
        ProjectT.Skill.Tests.ReleasingCanActivateAction.CanActivateHook = null;
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = null;
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
        agent.ReleaseInternal();
        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(TestEffect(3, 1f), null), "호출이 끝난 뒤 정리된 Agent에 적용");
    }

    static void ReleaseDuringStatusNotificationIsRejected()
    {
        var status = NewStatus();
        var rejected = new List<string>();
        void Release(string name)
        {
            try
            {
                status.ReleaseInternal();
            }
            catch (InvalidOperationException)
            {
                rejected.Add(name);
            }
        }

        status.OnStatChanged += (stat, previous, current) => Release($"stat:{stat.Name}");
        status.OnResourceChanged += (resource, previous, current) => Release($"res:{resource.Name}");
        var source = new object();

        status.SetBase(Atk, 12f);
        status.AddModifier(MaxHp, StatModOp.Add, 2f, source);
        status.SetResource(Hp, 5f);
        status.RemoveModifiers(source);

        Check(string.Join(",", rejected) == $"stat:{Atk.Name},stat:{MaxHp.Name},res:{Hp.Name},stat:{MaxHp.Name}", $"거부된 정리: {string.Join(",", rejected)}");
        status.ReleaseInternal();
        Check(status.GetResource(Hp) == 0f && status.Get(Atk) == 0f, "알림이 끝난 뒤 정리가 값을 비우지 않았다");
    }
}
