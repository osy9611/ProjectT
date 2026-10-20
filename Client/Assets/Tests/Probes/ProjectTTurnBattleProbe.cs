#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ProjectT;
using ProjectT.Sample.TurnBattle;
using ProjectT.Skill;
using UnityEngine;

// TurnBattleSample 씬에서 TurnBattleRunner의 공개 API로 턴제 전투 시나리오를 실제 ComBaseActor·풀 경로로 실행한다.
public class ProjectTTurnBattleProbe : MonoBehaviour
{
    private static readonly List<string> s_results = new List<string>();
    private readonly List<string> errors = new List<string>();
    private int pass;
    private int fail;
    private string current;
    private TurnBattleRunner runner;

    public static bool Running { get; private set; }
    public static string Summary { get; private set; } = "not started";
    public static IReadOnlyList<string> Results => s_results;

    public static void Run()
    {
        if (Running)
            throw new InvalidOperationException("Probe is already running.");

        s_results.Clear();
        Summary = "running";
        Running = true;
        new GameObject("ProjectTTurnBattleProbe").AddComponent<ProjectTTurnBattleProbe>();
    }

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLogInternal;
        runner = FindAnyObjectByType<TurnBattleRunner>();
        float deadline = Time.realtimeSinceStartup + 30f;
        while ((runner == null || !runner.IsReady) && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            runner = runner != null ? runner : FindAnyObjectByType<TurnBattleRunner>();
        }

        current = "setup";
        Check(runner != null && runner.IsReady, "TurnBattleRunner 준비");
        if (runner != null && runner.IsReady)
        {
            Scenario("기본 공격", BasicAttack);
            Scenario("Mp 비용·쿨다운", CostAndCooldown);
            Scenario("독 중첩·갱신", PoisonStack);
            Scenario("스턴 차단·해제", StunBlocksTurn);
            yield return Step("수동 틱(AutoTickSkills false)", ManualTickOnly());
            Scenario("스턴이 채널링 취소", StunCancelsChannel);
            Scenario("비활성화가 채널링 취소", DisableCancelsChannel);
            Scenario("공격 버프·갱신", WarCryBuff);
            Scenario("가드 핸들 제거", GuardHandle);
            Scenario("Mp 재생 동작", ManaRegen);
            Scenario("풀 반납 사망·출처 상실·재대여", DeathAndRespawn);
            Scenario("AI 전투", AIBattle);
            runner.StartBattle();
        }

        Application.logMessageReceived -= OnLogInternal;
        current = "console";
        foreach (var error in errors)
            Record($"[Turn] ERROR LOG {error}");

        Check(errors.Count == 0, $"Error/Exception 로그 없음 ({errors.Count})");
        Summary = $"pass={pass} fail={fail}";
        Debug.Log($"PROJECTT_TURN_BATTLE_RESULT {Summary}");
        Running = false;
        Destroy(gameObject);
    }

    private void BasicAttack()
    {
        var (warrior, orc) = Duel();
        Check(runner.Act(warrior, TurnIds.Strike, orc), "Strike 발동");
        Check(orc.Hp == 104f, $"20 Atk - 4 Def = 16 피해 (Orc {orc.Hp})");
        Check(warrior.Activated == 1 && warrior.Completed == 1, "발동·종료 알림 한 쌍");
    }

    private void CostAndCooldown()
    {
        runner.ClearUnits();
        var mage = Spawn(TurnDefs.MageNoRegen);
        var orc = Spawn(TurnDefs.Orc);
        Check(runner.Act(mage, TurnIds.Fireball, orc), "1턴 Fireball 발동");
        Check(mage.Mp == 40f && orc.Hp == 100f, $"Mp 10 차감, 20 피해 (Mp {mage.Mp}, Orc {orc.Hp})");
        runner.EndTurn(mage);
        Check(!runner.Act(mage, TurnIds.Fireball, orc) && mage.Mp == 40f && mage.Activated == 1, "2턴 쿨다운 중 무시");
        runner.EndTurn(mage);
        Check(runner.Act(mage, TurnIds.Fireball, orc), "3턴 쿨다운 해제 후 발동");
        runner.EndTurn(mage);
        runner.EndTurn(mage);
        mage.Status.SetResource(TurnIds.Mp, 5f);
        Check(!runner.Act(mage, TurnIds.Fireball, orc) && mage.Mp == 5f, "Mp 부족 시 무시·차감 없음");
    }

    private void PoisonStack()
    {
        runner.ClearUnits();
        var mage = Spawn(TurnDefs.MageNoRegen);
        var orc = Spawn(TurnDefs.Orc);
        runner.Act(mage, TurnIds.Poison, orc);
        Check(orc.Hp == 115f && mage.Mp == 45f, $"적용 즉시 5 피해 (Orc {orc.Hp})");
        runner.EndTurn(orc);
        Check(orc.Hp == 110f, $"대상 턴 종료마다 주기 피해 (Orc {orc.Hp})");
        runner.Act(mage, TurnIds.Poison, orc);
        Check(orc.Hp == 110f && orc.EffectsApplied == 1, "재적용은 즉시 실행·적용 알림 없음");
        foreach (float expected in new[] { 100f, 90f, 80f })
        {
            runner.EndTurn(orc);
            Check(orc.Hp == expected, $"2중첩 10 피해 (Orc {orc.Hp})");
        }

        runner.EndTurn(orc);
        Check(orc.Hp == 80f && orc.Removed.SequenceEqual(new[] { EffectRemoveReason.Expired }), "갱신된 지속 끝에 만료");
    }

    private void StunBlocksTurn()
    {
        var (warrior, orc) = Duel();
        runner.Act(warrior, TurnIds.Bash, orc);
        Check(orc.Hp == 114f && orc.Stunned && orc.HasTagEvent(TurnIds.Stun), $"Bash 6 피해 + 스턴 (Orc {orc.Hp})");
        runner.EndTurn(warrior);
        Check(!runner.Act(orc, TurnIds.Strike, warrior) && warrior.Hp == 100f && orc.Activated == 0, "스턴 중 BlockedTags로 발동 불가");
        runner.EndTurn(orc);
        Check(!orc.Stunned && orc.Events.Contains("tag State.Stun False"), "자기 턴 종료에 스턴 만료");
        Check(runner.Act(orc, TurnIds.Strike, warrior) && warrior.Hp == 90f, $"다음 턴 행동 (Warrior {warrior.Hp})");
    }

    // ComBaseActor.Update가 틱하지 않아야 프레임이 흘러도 턴 단위 효과·쿨다운이 유지된다.
    private IEnumerator ManualTickOnly()
    {
        var (warrior, orc) = Duel();
        runner.Act(warrior, TurnIds.Bash, orc);
        for (int i = 0; i < 30; i++)
            yield return null;

        Check(orc.Stunned, "30프레임 뒤에도 스턴 유지");
        Check(!runner.Act(warrior, TurnIds.Bash, orc), "30프레임 뒤에도 Bash 쿨다운 유지");
        runner.EndTurn(orc);
        Check(!orc.Stunned, "EndTurn(TickSkills)으로만 만료");
    }

    private void StunCancelsChannel()
    {
        runner.ClearUnits();
        var mage = Spawn(TurnDefs.MageNoRegen);
        var orc = Spawn(TurnDefs.Orc);
        mage.Status.SetResource(TurnIds.Mp, 20f);
        runner.Act(mage, TurnIds.Meditate, null);
        Check(mage.Channeling, "Meditate 활성 유지");
        runner.EndTurn(mage);
        Check(mage.Mp == 25f, $"턴 종료마다 OnUpdate로 Mp 5 회복 (Mp {mage.Mp})");
        runner.Act(orc, TurnIds.Bash, mage);
        Check(mage.Hp == 64.5f && !mage.Channeling && mage.Cancelled == 1, $"스턴이 CancelledByTags로 취소 (Mage {mage.Hp})");
        runner.EndTurn(mage);
        Check(mage.Mp == 25f, "취소 뒤 회복 없음");
    }

    private void DisableCancelsChannel()
    {
        runner.ClearUnits();
        var mage = Spawn(TurnDefs.MageNoRegen);
        mage.Status.SetResource(TurnIds.Mp, 20f);
        runner.Act(mage, TurnIds.Meditate, null);
        mage.gameObject.SetActive(false);
        Check(!mage.Channeling && mage.Cancelled == 1, "OnDisable이 활성 스킬 취소");
        mage.EndTurn();
        Check(mage.Mp == 20f, "비활성 중 TickSkills는 진행하지 않음");
        mage.gameObject.SetActive(true);
        Check(mage.Alive && mage.ActiveEffects == 0, "재활성화 뒤 스폰 유지");
    }

    private void WarCryBuff()
    {
        var (warrior, orc) = Duel();
        PlayInternal(warrior, TurnIds.WarCry, null);
        Check(warrior.Status.Get(TurnIds.Atk) == 30f, "Atk ×1.5");
        runner.Act(warrior, TurnIds.Strike, orc);
        Check(orc.Hp == 94f, $"버프 중 26 피해 (Orc {orc.Hp})");
        runner.EndTurn(warrior);
        Check(warrior.Status.Get(TurnIds.Atk) == 20f, "2턴 뒤 만료");
        PlayInternal(warrior, TurnIds.WarCry, null);
        PlayInternal(warrior, TurnIds.WarCry, null);
        Check(warrior.Status.Get(TurnIds.Atk) == 30f && warrior.EffectsApplied == 2, "재시전 Refresh");
        PlayInternal(warrior, TurnIds.Strike, orc);
        Check(warrior.Status.Get(TurnIds.Atk) == 20f, "갱신 시점부터 2턴 뒤 만료");
    }

    private void GuardHandle()
    {
        var (warrior, orc) = Duel();
        PlayInternal(warrior, TurnIds.Guard, null);
        Check(warrior.Status.Get(TurnIds.Def) == 15f && warrior.GuardHandle.Id != 0, "Infinite 가드 Def +10");
        PlayInternal(orc, TurnIds.Strike, warrior);
        Check(warrior.Hp == 99f, $"최소 피해 1 (Warrior {warrior.Hp})");
        runner.BeginTurn(warrior);
        Check(warrior.Status.Get(TurnIds.Def) == 5f && warrior.Removed.Last() == EffectRemoveReason.Removed, "RemoveEffect(핸들) → Removed");
    }

    private void ManaRegen()
    {
        runner.ClearUnits();
        var mage = Spawn(TurnDefs.Mage);
        Check(mage.Mp == 50f, "적용 시점 실행은 상한에서 제한");
        mage.Status.SetResource(TurnIds.Mp, 30f);
        runner.EndTurn(mage);
        runner.EndTurn(mage);
        Check(mage.Mp == 34f, $"EffectBehavior.OnExecute로 턴마다 Mp 2 (Mp {mage.Mp})");
    }

    private void DeathAndRespawn()
    {
        runner.ClearUnits();
        var warrior = Spawn(TurnDefs.Warrior);
        var mage = Spawn(TurnDefs.MageNoRegen);
        var goblin = Spawn(TurnDefs.Goblin);
        runner.Act(goblin, TurnIds.Burn, warrior);
        Check(warrior.Hp == 97f, $"Burn 3 피해 (Warrior {warrior.Hp})");
        runner.Act(warrior, TurnIds.Bash, goblin);
        runner.Act(mage, TurnIds.Poison, goblin);
        Check(goblin.Stunned && goblin.ActiveEffects == 2, "Goblin 스턴 + 독");

        int serial = goblin.SpawnSerial;
        goblin.Status.SetResource(TurnIds.Hp, 0f);
        runner.RemoveDead();
        Check(!goblin.Alive && !goblin.gameObject.activeSelf, "사망 시 풀 반납(비활성)");
        Check(goblin.SpawnSerial != serial, "반납으로 SpawnSerial 변경");
        Check(!goblin.Stunned && !goblin.HasTagEvent(TurnIds.Stun) && goblin.ActiveEffects == 0, "정리로 태그·효과 제거");
        Check(goblin.Removed.Count(reason => reason == EffectRemoveReason.Released) == 2, "남은 효과는 Released");

        runner.EndTurn(warrior);
        Check(warrior.Hp == 96f, $"시전자 반납 뒤 Burn source null → 1 피해 (Warrior {warrior.Hp})");

        var respawned = Spawn(TurnDefs.Goblin);
        Check(ReferenceEquals(respawned, goblin), "풀이 같은 인스턴스를 재대여");
        Check(goblin.Alive && goblin.Hp == 60f && !goblin.Stunned && goblin.ActiveEffects == 0, "재스폰은 깨끗한 상태");
        Check(runner.Act(goblin, TurnIds.Strike, warrior) && warrior.Hp == 93f, $"재스폰 뒤 스킬 재등록 (Warrior {warrior.Hp})");
    }

    private void AIBattle()
    {
        runner.StartBattle();
        var units = runner.Units.ToList();
        for (int i = 0; i < 400 && !runner.IsOver; i++)
            runner.PlayAITurn();

        Check(runner.IsOver, $"전투 종료 ({runner.Round}라운드)");
        Check(units.Any(unit => unit.Events.Contains("tag State.Stun True")), "전투 중 스턴 발생");
        runner.ClearUnits();
        foreach (var unit in units)
        {
            Check(unit.Activated == unit.Completed + unit.Cancelled, $"{unit.name}: 발동 {unit.Activated} = 완료 {unit.Completed} + 취소 {unit.Cancelled}");
            Check(unit.ActiveEffects == 0 && !unit.HasTagEvent(TurnIds.Stun) && !unit.ResourceOutOfRange, $"{unit.name}: 효과·태그 정리, 자원 범위 유지");
        }
    }

    private void PlayInternal(TurnUnit unit, int skillId, TurnUnit target)
    {
        runner.BeginTurn(unit);
        runner.Act(unit, skillId, target);
        runner.EndTurn(unit);
    }

    private (TurnUnit, TurnUnit) Duel()
    {
        runner.ClearUnits();
        return (Spawn(TurnDefs.Warrior), Spawn(TurnDefs.Orc));
    }

    private TurnUnit Spawn(TurnUnitSpec spec)
    {
        return runner.SpawnUnit(spec, new Vector3(spec.IsPlayer ? -3f : 3f, 0.5f, runner.Units.Count * 1.5f - 2f));
    }

    private void Scenario(string name, Action scenario)
    {
        current = name;
        try
        {
            scenario();
        }
        catch (Exception error)
        {
            Check(false, $"예외 {error.GetType().Name}: {error.Message}");
        }
    }

    private IEnumerator Step(string name, IEnumerator routine)
    {
        current = name;
        while (true)
        {
            try
            {
                if (!routine.MoveNext())
                    yield break;
            }
            catch (Exception error)
            {
                Check(false, $"예외 {error.GetType().Name}: {error.Message}");
                yield break;
            }

            yield return routine.Current;
        }
    }

    private void OnLogInternal(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message.Split('\n')[0]);
    }

    private void Check(bool valid, string message)
    {
        if (valid)
            pass++;
        else
            fail++;

        Record($"[Turn] {(valid ? "PASS" : "FAIL")} {current}: {message}");
    }

    private static void Record(string line)
    {
        s_results.Add(line);
        Debug.Log(line);
    }
}
#endif
