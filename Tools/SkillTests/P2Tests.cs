using System;
using System.Collections.Generic;
using DesignEnum;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

static partial class Program
{
    static void StatusCalculatesModifiers()
    {
        var status = new Status();
        var source = new object();
        status.SetBase(Atk, 10f);
        CheckStat(status, Atk, 10f, "Base");
        status.AddModifier(Atk, StatModOp.Add, 5f, source);
        CheckStat(status, Atk, 15f, "Add");
        status.AddModifier(Atk, StatModOp.Multiply, 1.5f, source);
        CheckStat(status, Atk, 22.5f, "Multiply");
        status.AddModifier(Atk, StatModOp.Multiply, 1.25f, source);
        CheckStat(status, Atk, 26.25f, "Multiply 합산");
        status.AddModifier(Atk, StatModOp.Override, 3f, source);
        CheckStat(status, Atk, 3f, "Override");
        Check(status.GetBase(Atk) == 10f, "Base가 바뀌었다");
        CheckStat(status, Def, 0f, "다른 스탯");
    }

    static void StatusIsOrderIndependent()
    {
        var forward = new Status();
        var backward = new Status();
        var source = new object();
        var modifiers = new (StatModOp Op, float Value)[] { (StatModOp.Add, 5f), (StatModOp.Multiply, 1.5f), (StatModOp.Add, 2.5f), (StatModOp.Multiply, 0.75f) };
        forward.SetBase(Def, 10f);
        for (int i = 0; i < modifiers.Length; i++)
            forward.AddModifier(Def, modifiers[i].Op, modifiers[i].Value, source);
        for (int i = modifiers.Length - 1; i >= 0; i--)
            backward.AddModifier(Def, modifiers[i].Op, modifiers[i].Value, source);
        backward.SetBase(Def, 10f);
        CheckStat(forward, Def, 21.875f, "정순");
        CheckStat(backward, Def, 21.875f, "역순");
    }

    static void StatusRemovesModifiersBySource()
    {
        var status = new Status();
        var effect = new object();
        var other = new object();
        status.SetBase(Atk, 10f);
        status.SetBase(Def, 4f);
        status.AddModifier(Atk, StatModOp.Add, 4f, effect);
        status.AddModifier(Atk, StatModOp.Add, 2f, other);
        status.AddModifier(Atk, StatModOp.Multiply, 1.5f, effect);
        status.AddModifier(Def, StatModOp.Override, 100f, effect);
        CheckStat(status, Atk, 24f, "제거 전 Atk");
        CheckStat(status, Def, 100f, "제거 전 Def");
        status.RemoveModifiers(effect);
        CheckStat(status, Atk, 12f, "제거 후 Atk");
        CheckStat(status, Def, 4f, "제거 후 Def");
        status.RemoveModifiers(other);
        CheckStat(status, Atk, 10f, "모두 제거 후 Atk");
    }

    static void StatusUsesLatestOverride()
    {
        var status = new Status();
        var first = new object();
        var latest = new object();
        var add = new object();
        status.SetBase(MoveSpeed, 10f);
        status.AddModifier(MoveSpeed, StatModOp.Override, 50f, first);
        status.AddModifier(MoveSpeed, StatModOp.Add, 5f, add);
        status.AddModifier(MoveSpeed, StatModOp.Override, 20f, latest);
        CheckStat(status, MoveSpeed, 20f, "최근 Override");
        status.RemoveModifiers(latest);
        CheckStat(status, MoveSpeed, 50f, "이전 Override");
        status.RemoveModifiers(first);
        CheckStat(status, MoveSpeed, 15f, "공식");
    }

    static void StatusRaisesEventOnlyOnChange()
    {
        var status = new Status();
        var source = new object();
        var events = new List<string>();
        status.OnStatChanged += (type, previous, current) => events.Add($"{type.Name}:{previous}>{current}:{status.Get(type)}");
        status.SetBase(Atk, 10f);
        status.SetBase(Atk, 10f);
        status.AddModifier(Atk, StatModOp.Multiply, 1f, source);
        status.AddModifier(Atk, StatModOp.Add, 0f, source);
        status.AddModifier(Atk, StatModOp.Add, 5f, source);
        status.AddModifier(MaxHp, StatModOp.Add, 7f, source);
        status.RemoveModifiers(new object());
        status.RemoveModifiers(source);
        Check(string.Join(",", events) == "Atk:0>10:10,Atk:10>15:15,MaxHp:0>7:7,Atk:15>10:10,MaxHp:7>0:0", "이벤트: " + string.Join(",", events));
    }

    static void StatusHandlerReentrancyStaysConsistent()
    {
        var status = new Status();
        var handler = new object();
        var events = new List<string>();
        status.OnStatChanged += (type, previous, current) =>
        {
            events.Add($"{type.Name}:{previous}>{current}:{status.Get(type)}");
            if (type == Atk && current == 10f)
                status.AddModifier(Atk, StatModOp.Add, 5f, handler);
        };
        status.SetBase(Atk, 10f);
        CheckStat(status, Atk, 15f, "핸들러 추가 후");
        Check(string.Join(",", events) == "Atk:0>10:10,Atk:10>15:15", "이벤트: " + string.Join(",", events));
        events.Clear();
        status.RemoveModifiers(handler);
        CheckStat(status, Atk, 15f, "제거 중 핸들러가 다시 추가한 값");
        Check(string.Join(",", events) == "Atk:15>10:10,Atk:10>15:15", "제거 이벤트: " + string.Join(",", events));
    }

    static void StatusRemoveSurvivesHandlerFailure()
    {
        var status = new Status();
        var source = new object();
        status.SetBase(MaxHp, 100f);
        status.SetBase(Atk, 10f);
        status.AddModifier(MaxHp, StatModOp.Add, 20f, source);
        status.AddModifier(Atk, StatModOp.Add, 5f, source);
        status.OnStatChanged += (type, previous, current) => throw new InvalidOperationException("handler");

        CheckThrows<InvalidOperationException>(() => status.RemoveModifiers(source), "핸들러 예외 전파");

        Check(status.Get(MaxHp) == 100f && status.Get(Atk) == 10f, $"제거 후 값 MaxHp={status.Get(MaxHp)} Atk={status.Get(Atk)}");
    }

    static void StatusReleaseClearsSilently()
    {
        var status = new Status();
        var source = new object();
        status.SetBase(Atk, 10f);
        status.SetBase(MaxHp, 100f);
        status.AddModifier(Atk, StatModOp.Add, 5f, source);
        status.AddModifier(MaxHp, StatModOp.Override, 1f, source);
        int events = 0;
        status.OnStatChanged += (type, previous, current) => events++;
        status.ReleaseInternal();
        Check(events == 0, "정리 중 이벤트가 발생했다");
        foreach (StatId type in new[] { Atk, MaxHp })
        {
            CheckStat(status, type, 0f, "정리 후 " + type.Name);
            Check(status.GetBase(type) == 0f, "정리 후 Base가 남았다: " + type.Name);
        }
        status.SetBase(Atk, 3f);
        status.SetBase(MaxHp, 3f);
        CheckStat(status, Atk, 3f, "정리 후 Add가 남았다");
        CheckStat(status, MaxHp, 3f, "정리 후 Override가 남았다");
    }

    static void ConverterBuildsModifiers()
    {
        var cases = new (BuffType Type, StatId Stat, float Value)[] { (BuffType.AddATK, Atk, 1f + 20f / 100f), (BuffType.AddDEF, Def, 1f + 20f / 100f), (BuffType.LowATK, Atk, 1f - 20f / 100f), (BuffType.LowDEF, Def, 1f - 20f / 100f) };
        for (int i = 0; i < cases.Length; i++)
        {
            AddBuff(i + 1, cases[i].Type, 1f, 0f, 20f);
            var modifiers = SkillDefinitions.GetEffect(i + 1).Modifiers;
            Check(modifiers.Count == 1 && modifiers[0].Stat == cases[i].Stat && modifiers[0].Op == StatModOp.Multiply && modifiers[0].Value == cases[i].Value, $"{cases[i].Type} 수정자: {modifiers.Count}");
        }

        AddBuff(10, BuffType.Strun, 1f, 0f, 20f);
        AddBuff(11, BuffType.Dot, 1f, 0.5f, 20f);
        var stun = SkillDefinitions.GetEffect(10).Modifiers;
        var dot = SkillDefinitions.GetEffect(11).Modifiers;
        Check(stun.Count == 0 && ReferenceEquals(stun, dot), "Stun·Dot 수정자가 공용 빈 배열이 아니다");
    }

    static void ModifierEffectAppliesAndRemovesModifiers()
    {
        var status = NewStatus();
        AddBuff(1, BuffType.AddATK, 1f, 0f, 20f);
        AddBuff(2, BuffType.LowDEF, 5f, 0f, 50f);
        var agent = NewAgent(status);

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);
        CheckStatNear(status, Atk, 12f, "AddATK 적용");
        CheckStatNear(status, Def, 5f, "LowDEF 적용");

        agent.TickInternal(1f);
        CheckStat(status, Atk, 10f, "AddATK 만료");
        CheckStatNear(status, Def, 5f, "LowDEF 유지");

        agent.ReleaseInternal();
        CheckStat(status, Def, 10f, "정리");
    }

    static void ModifierEffectsSumAdditively()
    {
        var status = NewStatus();
        AddBuff(1, BuffType.AddATK, 1f, 0f, 20f);
        AddBuff(2, BuffType.AddATK, 1f, 0f, 10f);
        var agent = NewAgent(status);

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);

        CheckStatNear(status, Atk, 13f, "+20%·+10%");
    }

    static void StatusClampsAtZero()
    {
        var status = NewStatus();
        var source = new object();
        status.AddModifier(Atk, StatModOp.Multiply, 0.4f, source);
        status.AddModifier(Atk, StatModOp.Multiply, 0.5f, source);
        CheckStat(status, Atk, 0f, "Multiply 합계 음수");
        status.AddModifier(Def, StatModOp.Add, -20f, source);
        CheckStat(status, Def, 0f, "Add 음수");
        status.AddModifier(MaxHp, StatModOp.Override, -5f, source);
        CheckStat(status, MaxHp, 0f, "Override 음수");
        status.RemoveModifiers(source);
        CheckStat(status, Atk, 10f, "제거 후");
    }

    static void ModifierEffectClampsAtZero()
    {
        var status = NewStatus();
        AddBuff(1, BuffType.LowATK, 1f, 0f, 60f);
        AddBuff(2, BuffType.LowATK, 1f, 0f, 50f);
        var agent = NewAgent(status);

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        agent.ApplyEffect(SkillDefinitions.GetEffect(2), null);
        CheckStat(status, Atk, 0f, "LowATK 110%");

        agent.ReleaseInternal();
        CheckStat(status, Atk, 10f, "정리");
    }

    static void ModifierEffectWithoutStatusThrows()
    {
        AddBuff(1, BuffType.AddATK, 1f, 0f, 20f);
        var agent = NewAgent();

        CheckThrows<InvalidOperationException>(() => agent.ApplyEffect(SkillDefinitions.GetEffect(1), null), "Status 없는 수정자 효과");

        Check(EffectCount(agent) == 0, "실패한 효과가 등록되었다");
    }

    static void ActionFactoryFailureDoesNotRegister()
    {
        var agent = NewAgent();
        var definition = Skill(1, factory: () => throw new InvalidOperationException("factory"));

        CheckThrows<InvalidOperationException>(() => agent.GiveSkill(definition), "실패하는 팩토리");

        Check(GivenCount(agent) == 0, "스킬이 등록되었다");
    }

    static void BoundaryFramesAreNotDelayed()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 0.3f), null);
        agent.ApplyEffect(TestEffect(2, 0.9f, 0.3f), null);

        for (int frame = 1; frame <= 9; frame++)
        {
            agent.TickInternal(0.1f);
            if (frame == 2)
                Check(EffectCount(agent) == 2, "2프레임에 만료되었다");
            else if (frame == 3)
            {
                Check(EffectCount(agent) == 1, "0.3초 효과가 3프레임에 만료되지 않았다");
                CheckExecutes(2, 2, "3프레임");
            }
            else if (frame == 6)
                CheckExecutes(2, 3, "6프레임");
            else if (frame == 8)
                Check(EffectCount(agent) == 1, "0.9초 효과가 8프레임에 만료되었다");
        }

        Check(EffectCount(agent) == 0, "0.9초 효과가 9프레임에 만료되지 않았다");
    }

    static void StatusClampsNegativeTerms()
    {
        var status = new Status();
        var source = new object();
        status.SetBase(Atk, 10f);
        status.AddModifier(Atk, StatModOp.Add, -20f, source);
        status.AddModifier(Atk, StatModOp.Multiply, 0.4f, source);
        status.AddModifier(Atk, StatModOp.Multiply, 0.4f, source);

        Check(status.Get(Atk) == 0f, $"Atk 기대 0, 실제 {status.Get(Atk)}");
    }

    static void LongRunExpiresOnTime()
    {
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 3600f), null);

        int frames = 0;
        while (ProjectT.Skill.Tests.TestBehavior.Removes == 0 && frames < 300000)
        {
            agent.TickInternal(1f / 60f);
            frames++;
        }

        Check(Math.Abs(frames - 3600 * 60) <= 1, $"만료 프레임 기대 {3600 * 60}, 실제 {frames}");
    }
}
