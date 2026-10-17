using System;
using System.Collections.Generic;
using System.Linq;
using ProjectT.Skill;
using ProjectT.Sample.TurnBattle;
using TurnGame;

public static class Program
{
    private static int pass;
    private static readonly List<string> failures = new List<string>();
    private static string current;

    public static int Main()
    {
        Run("기본 공격", BasicAttack);
        Run("Mp 비용·쿨다운", CostAndCooldown);
        Run("독 중첩·갱신", PoisonStack);
        Run("스턴 차단·해제", StunBlocksTurn);
        Run("스턴이 채널링 취소", StunCancelsChannel);
        Run("공격 버프·갱신", WarCryBuff);
        Run("가드 핸들 제거", GuardHandle);
        Run("Mp 재생 동작", ManaRegen);
        Run("사망 정리·출처 상실·재스폰", DeathAndRespawn);
        Run("AI 전투", AIBattle);

        Console.WriteLine();
        foreach (var failure in failures)
            Console.WriteLine("FAIL " + failure);

        Console.WriteLine($"{pass}/{pass + failures.Count} passed");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void BasicAttack()
    {
        var (battle, warrior, orc) = Duel();
        Check(battle.Act(warrior, TurnIds.Strike, orc), "Strike 발동");
        Check(orc.Hp == 104f, $"20 Atk - 4 Def = 16 피해 (Orc {orc.Hp})");
        Check(warrior.Activated == 1 && warrior.Completed == 1, "발동·종료 알림 한 쌍");
    }

    private static void CostAndCooldown()
    {
        var mage = Mage(false);
        var orc = Orc();
        var battle = new Battle(mage, orc);
        Check(battle.Act(mage, TurnIds.Fireball, orc), "1턴 Fireball 발동");
        Check(mage.Mp == 40f && orc.Hp == 100f, $"Mp 10 차감, 12×2-4 = 20 피해 (Mp {mage.Mp}, Orc {orc.Hp})");
        battle.EndTurn(mage);

        Check(!battle.Act(mage, TurnIds.Fireball, orc), "2턴 쿨다운 중 무시");
        Check(mage.Mp == 40f && orc.Hp == 100f && mage.Activated == 1, "무시된 발동은 비용·피해·알림 없음");
        battle.EndTurn(mage);

        Check(battle.Act(mage, TurnIds.Fireball, orc), "3턴 쿨다운 해제 후 발동");
        battle.EndTurn(mage);
        battle.EndTurn(mage);
        mage.Status.SetResource(TurnIds.Mp, 5f);
        Check(!battle.Act(mage, TurnIds.Fireball, orc), "Mp 부족 시 무시");
        Check(mage.Mp == 5f, "Mp 부족 발동은 차감 없음");
    }

    private static void PoisonStack()
    {
        var mage = Mage(false);
        var orc = Orc();
        var battle = new Battle(mage, orc);
        battle.Act(mage, TurnIds.Poison, orc);
        Check(orc.Hp == 115f && mage.Mp == 45f, $"적용 즉시 5 피해, Mp 5 차감 (Orc {orc.Hp})");
        battle.EndTurn(orc);
        Check(orc.Hp == 110f, $"대상 턴 종료마다 주기 피해 (Orc {orc.Hp})");

        battle.Act(mage, TurnIds.Poison, orc);
        Check(orc.Hp == 110f && orc.EffectsApplied == 1, "재적용은 즉시 실행·적용 알림 없음");
        var expected = new[] { 100f, 90f, 80f };
        for (int i = 0; i < expected.Length; i++)
        {
            battle.EndTurn(orc);
            Check(orc.Hp == expected[i], $"2중첩 10 피해, 지속 갱신 {i + 1}/3 (Orc {orc.Hp})");
        }

        battle.EndTurn(orc);
        Check(orc.Hp == 80f && orc.Removed.SequenceEqual(new[] { EffectRemoveReason.Expired }), "갱신된 지속 끝에 만료, 추가 피해 없음");
    }

    private static void StunBlocksTurn()
    {
        var (battle, warrior, orc) = Duel();
        battle.Act(warrior, TurnIds.Bash, orc);
        Check(orc.Hp == 114f && orc.Stunned && orc.HasTagEvent(TurnIds.Stun), $"Bash 20×0.5-4 = 6 피해 + 스턴 태그 (Orc {orc.Hp})");
        battle.EndTurn(warrior);

        Check(!battle.Act(orc, TurnIds.Strike, warrior), "스턴 중 BlockedTags로 발동 불가");
        Check(warrior.Hp == 100f && orc.Activated == 0, "막힌 턴은 피해·알림 없음");
        battle.EndTurn(orc);
        Check(!orc.Stunned && orc.Events.Contains("tag State.Stun False"), "자기 턴 종료에 1턴 스턴 만료");

        Check(battle.Act(orc, TurnIds.Strike, warrior), "다음 턴 행동 가능");
        Check(warrior.Hp == 90f, $"15-5 = 10 피해 (Warrior {warrior.Hp})");
        Check(!battle.Act(warrior, TurnIds.Bash, orc), "Bash 쿨다운");
    }

    private static void StunCancelsChannel()
    {
        var mage = Mage(false);
        var orc = Orc();
        var battle = new Battle(mage, orc);
        mage.Status.SetResource(TurnIds.Mp, 20f);
        battle.Act(mage, TurnIds.Meditate, null);
        Check(mage.Channeling && mage.Completed == 0, "Meditate는 턴을 넘겨 활성 유지");
        battle.EndTurn(mage);
        Check(mage.Mp == 25f, $"활성 중 턴 종료마다 OnUpdate로 Mp 5 회복 (Mp {mage.Mp})");

        battle.Turn(orc, TurnIds.Bash, mage);
        Check(mage.Hp == 64.5f, $"Bash 15×0.5-2 = 5.5 피해 (Mage {mage.Hp})");
        Check(!mage.Channeling && mage.Cancelled == 1 && mage.Events.Contains("end 8 Cancelled"), "스턴 획득이 CancelledByTags로 채널링 취소");

        Check(!battle.Act(mage, TurnIds.Strike, orc), "스턴 중 행동 불가");
        battle.EndTurn(mage);
        Check(mage.Mp == 25f, $"취소 뒤에는 회복 없음 (Mp {mage.Mp})");
    }

    private static void WarCryBuff()
    {
        var (battle, warrior, orc) = Duel();
        battle.Turn(warrior, TurnIds.WarCry, null);
        Check(warrior.Status.Get(TurnIds.Atk) == 30f, $"Atk ×1.5 (Atk {warrior.Status.Get(TurnIds.Atk)})");
        battle.Act(warrior, TurnIds.Strike, orc);
        Check(orc.Hp == 94f, $"버프 중 30-4 = 26 피해 (Orc {orc.Hp})");
        battle.EndTurn(warrior);
        Check(warrior.Status.Get(TurnIds.Atk) == 20f && warrior.Removed.Last() == EffectRemoveReason.Expired, "2턴 뒤 만료로 수정자 회수");

        battle.Turn(warrior, TurnIds.WarCry, null);
        battle.Turn(warrior, TurnIds.WarCry, null);
        Check(warrior.Status.Get(TurnIds.Atk) == 30f && warrior.EffectsApplied == 2, "지속 중 재시전은 Refresh로 유지 (적용 알림 추가 없음)");
        battle.Turn(warrior, TurnIds.Strike, orc);
        Check(warrior.Status.Get(TurnIds.Atk) == 20f, "갱신 시점부터 2턴 뒤 만료");
    }

    private static void GuardHandle()
    {
        var (battle, warrior, orc) = Duel();
        battle.Turn(warrior, TurnIds.Guard, null);
        Check(warrior.Status.Get(TurnIds.Def) == 15f && warrior.GuardHandle.Id != 0, "Infinite 가드 Def +10, 핸들 보관");
        battle.Turn(orc, TurnIds.Strike, warrior);
        Check(warrior.Hp == 99f, $"15-15 → 최소 피해 1 (Warrior {warrior.Hp})");

        battle.BeginTurn(warrior);
        Check(warrior.Status.Get(TurnIds.Def) == 5f && warrior.Removed.Last() == EffectRemoveReason.Removed, "다음 턴 시작에 RemoveEffect(핸들) → Removed");
        battle.EndTurn(warrior);
        battle.Turn(orc, TurnIds.Strike, warrior);
        Check(warrior.Hp == 89f, $"가드 해제 뒤 10 피해 (Warrior {warrior.Hp})");
    }

    private static void ManaRegen()
    {
        var mage = Mage(true);
        var battle = new Battle(mage);
        Check(mage.Mp == 50f, "적용 시점 실행은 상한에서 제한");
        mage.Status.SetResource(TurnIds.Mp, 30f);
        battle.EndTurn(mage);
        battle.EndTurn(mage);
        Check(mage.Mp == 34f, $"EffectBehavior.OnExecute가 턴마다 Mp 2 회복 (Mp {mage.Mp})");
    }

    private static void DeathAndRespawn()
    {
        var warrior = Warrior();
        var mage = Mage(false);
        var goblin = Goblin();
        var battle = new Battle(warrior, mage, goblin);
        battle.Act(goblin, TurnIds.Burn, warrior);
        Check(warrior.Hp == 97f, $"Burn 8-5 = 3 피해 (Warrior {warrior.Hp})");
        battle.Act(warrior, TurnIds.Bash, goblin);
        battle.Act(mage, TurnIds.Poison, goblin);
        Check(goblin.Stunned && goblin.ActiveEffects == 2, "Goblin 스턴 + 독");

        goblin.Status.SetResource(TurnIds.Hp, 0f);
        battle.RemoveDead();
        Check(!goblin.Alive && !goblin.Stunned && !goblin.HasTagEvent(TurnIds.Stun), "사망 정리로 태그 비움");
        Check(goblin.ActiveEffects == 0 && goblin.Removed.Count(reason => reason == EffectRemoveReason.Released) == 2, "남은 효과는 Released로 제거");
        int activated = goblin.Activated;
        goblin.SkillAgent.ActivateSkill(TurnIds.Strike);
        Check(goblin.Activated == activated, "사망 뒤 발동 무시");

        battle.EndTurn(warrior);
        Check(warrior.Hp == 96f, $"시전자 사망 뒤 Burn은 source null → 최소 피해 1 (Warrior {warrior.Hp})");
        battle.EndTurn(warrior);
        Check(warrior.Hp == 96f && warrior.Removed.Last() == EffectRemoveReason.Expired, "Burn 2회 실행 뒤 만료");

        SpawnGoblin(goblin);
        Check(goblin.Alive && goblin.Hp == 60f && !goblin.Stunned && goblin.ActiveEffects == 0, "재스폰은 깨끗한 상태");
        Check(battle.Act(goblin, TurnIds.Strike, warrior) && warrior.Hp == 93f, $"재스폰 뒤 스킬 재등록·발동 (Warrior {warrior.Hp})");
    }

    private static void AIBattle()
    {
        var warrior = Warrior();
        var mage = Mage(true);
        var orc = Orc();
        var goblin = Goblin();
        var battle = new Battle(warrior, mage, orc, goblin);
        battle.RunAI(30, unit => TurnAI.Priorities(unit.Name, unit.Status, unit.Channeling));
        foreach (var line in battle.Log)
            Console.WriteLine("    " + line);

        Check(battle.IsOver, $"전투 종료 ({battle.Round}라운드)");
        Check(battle.Units.Exists(unit => unit.Stunned || unit.Events.Contains("tag State.Stun True")), "전투 중 스턴 발생");
        foreach (var unit in battle.Units)
        {
            if (unit.Alive)
                unit.Die();
        }

        foreach (var unit in battle.Units)
        {
            Check(unit.Activated == unit.Completed + unit.Cancelled, $"{unit.Name}: 발동 {unit.Activated} = 완료 {unit.Completed} + 취소 {unit.Cancelled}");
            Check(unit.ActiveEffects == 0, $"{unit.Name}: 적용 {unit.EffectsApplied} = 제거 {unit.Removed.Count}");
            Check(!unit.Stunned && !unit.HasTagEvent(TurnIds.Stun), $"{unit.Name}: 정리 뒤 태그 없음");
            Check(!unit.ResourceOutOfRange, $"{unit.Name}: Hp·Mp가 0~상한 유지");
        }
    }

    private static (Battle, Unit, Unit) Duel()
    {
        var warrior = Warrior();
        var orc = Orc();
        return (new Battle(warrior, orc), warrior, orc);
    }

    private static Unit Warrior()
    {
        var unit = new Unit("Warrior", true);
        unit.Spawn(100f, 30f, 20f, 5f, false, TurnDefs.StrikeSkill, TurnDefs.BashSkill, TurnDefs.WarCrySkill, TurnDefs.GuardSkill);
        return unit;
    }

    private static Unit Mage(bool manaRegen)
    {
        var unit = new Unit("Mage", true);
        unit.Spawn(70f, 50f, 12f, 2f, manaRegen, TurnDefs.StrikeSkill, TurnDefs.FireballSkill, TurnDefs.PoisonSkill, TurnDefs.MeditateSkill);
        return unit;
    }

    private static Unit Orc()
    {
        var unit = new Unit("Orc", false);
        unit.Spawn(120f, 0f, 15f, 4f, false, TurnDefs.StrikeSkill, TurnDefs.BashSkill);
        return unit;
    }

    private static Unit Goblin()
    {
        var unit = new Unit("Goblin", false);
        SpawnGoblin(unit);
        return unit;
    }

    private static void SpawnGoblin(Unit unit)
    {
        unit.Spawn(60f, 40f, 8f, 1f, false, TurnDefs.StrikeSkill, TurnDefs.PoisonSkill, TurnDefs.BurnSkill);
    }

    private static void Run(string name, Action scenario)
    {
        Console.WriteLine($"[{name}]");
        current = name;
        try
        {
            scenario();
        }
        catch (Exception error)
        {
            failures.Add($"{name}: {error.GetType().Name}: {error.Message}");
            Console.WriteLine($"  EXCEPTION {error}");
        }
    }

    private static void Check(bool valid, string message)
    {
        Console.WriteLine($"  {(valid ? "PASS" : "FAIL")} {message}");
        if (valid)
            pass++;
        else
            failures.Add($"{current}: {message}");
    }
}
