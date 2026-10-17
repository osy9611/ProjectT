using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DesignEnum;
using DesignTable;
using ProjectT;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;

namespace ProjectT.Skill.Tests
{
    public sealed class TestActor : ComBaseActor { }

    // ComBaseActor가 아닌 소유자도 코어를 쓸 수 있는지 확인한다.
    public sealed class PlainOwner : IAbilityOwner
    {
        public SkillAgent SkillAgent { get; set; }
        public Status Status { get; set; }
        public int SpawnSerial => 0;
    }

    // Skill 도우미가 만드는 정의의 팩토리로 생성된다.
    public sealed class ReactivateOnEndAction : BaseSkillAction
    {
        public static Action OnEndHook;

        protected override void Activate()
        {
        }

        protected override void OnEnd(SkillEndReason reason) => OnEndHook?.Invoke();
    }

    public sealed class ReleasingCanActivateAction : BaseSkillAction
    {
        public static Action CanActivateHook;

        protected override bool CanActivate()
        {
            CanActivateHook?.Invoke();
            return base.CanActivate();
        }

        protected override void Activate()
        {
        }
    }

    public sealed class TestSkillAction : BaseSkillAction
    {
        public static int Activates, Updates, Cancels;
        public static bool EndOnUpdate;
        public static Action<TestSkillAction> UpdateHook;
        public static Action CancelHook;
        public static Action ActivateHook;
        public static List<string> Log;

        protected override void Activate()
        {
            Activates++;
            ActivateHook?.Invoke();
        }

        public override void OnUpdate(float deltaTime)
        {
            Updates++;
            UpdateHook?.Invoke(this);
            if (EndOnUpdate)
                End();
        }

        protected override void OnEnd(SkillEndReason reason)
        {
            if (reason != SkillEndReason.Cancelled)
                return;

            Cancels++;
            Log?.Add("skill");
            CancelHook?.Invoke();
        }

        public void ApplyEffectsTo(SkillAgent target) => ApplyEffects(target);
    }

    public sealed class TestBehavior : EffectBehavior
    {
        public static int Removes, Applies;
        public static bool ThrowOnApply;
        public static readonly Dictionary<int, int> Executes = new Dictionary<int, int>();
        public static readonly List<EffectRemoveReason> Reasons = new List<EffectRemoveReason>();
        public static Action<TestBehavior> ExecuteHook;
        public static Action<TestBehavior, EffectRemoveReason> RemoveHook;
        public static Action ApplyHook;
        public static List<string> Log;

        public int EffectId => Definition.Id;

        public override void OnApply()
        {
            Applies++;
            if (ThrowOnApply)
                throw new InvalidOperationException("apply");

            ApplyHook?.Invoke();
        }

        public override void OnExecute()
        {
            Executes[EffectId] = ExecuteCount(EffectId) + 1;
            ExecuteHook?.Invoke(this);
        }

        public override void OnRemove(EffectRemoveReason reason)
        {
            Removes++;
            Reasons.Add(reason);
            Log?.Add("effect");
            RemoveHook?.Invoke(this, reason);
        }

        public static int ExecuteCount(int effectId) => Executes.TryGetValue(effectId, out var count) ? count : 0;
    }

    // 훅 순서와 적용마다 새 인스턴스인지를 기록한다. 부여 태그가 OnApply 전에 걸리고 OnRemove 뒤에 풀리는지도 함께 남긴다.
    public sealed class OrderBehavior : EffectBehavior
    {
        public static readonly List<string> Log = new List<string>();
        public static int Created;
        private readonly int instance = ++Created;

        public override void OnApply() => Log.Add($"apply#{instance}:{Owner.SkillAgent.HasTag(SkillDefinitions.StunTag)}");

        public override void OnExecute() => Log.Add($"execute#{instance}");

        public override void OnRemove(EffectRemoveReason reason) => Log.Add($"remove#{instance}:{reason}:{Owner.SkillAgent.HasTag(SkillDefinitions.StunTag)}");
    }

    public sealed class RecordingDamageExecution : DamageExecution
    {
        public readonly List<(Status Source, Status Target, float Power)> Calls = new List<(Status, Status, float)>();
        public float Result;

        public override float Execute(Status source, Status target, float power)
        {
            Calls.Add((source, target, power));
            return Result;
        }
    }
}

namespace ExternalGame
{
    // 코어·어댑터와 다른 네임스페이스의 실행 클래스도 팩토리로 쓸 수 있는지 확인한다.
    public sealed class ExternalSkillAction : BaseSkillAction
    {
        protected override void Activate()
        {
        }
    }
}

static class Program
{
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly SkillTag Stun = SkillDefinitions.StunTag;
    static readonly SkillTag TagA = SkillTag.Register("Test.A"), TagB = SkillTag.Register("Test.B"), TagC = SkillTag.Register("Test.C");
    static readonly StatId MaxHp = SkillDefinitions.MaxHpStat, Atk = SkillDefinitions.AtkStat, Def = SkillDefinitions.DefStat, MoveSpeed = SkillDefinitions.MoveSpeedStat, CooldownReduction = SkillDefinitions.CooldownReductionStat, Hp = SkillDefinitions.HpStat;
    static readonly StatId Mp = StatId.Register("Test.Mp"), MaxMp = StatId.Register("Test.MaxMp");

    static int Main()
    {
        var tests = new List<(string Name, Action Body)>
        {
            ("[P0 D2] 등록 호출 없이 첫 사용에서 스킬을 등록한다", SkillRegistersOnFirstUse),
            ("[P0 D4] 종료 후 재발동한 스킬이 계속 갱신된다", ReactivatedSkillKeepsUpdating),
            ("[P0 D4] 갱신 중 다른 스킬을 발동해도 순회가 깨지지 않는다", ActivationDuringUpdateKeepsLoop),
            ("[P0 D4] 취소된 스킬은 다음 갱신에서 실행되지 않는다", CanceledSkillIsNotUpdated),
            ("[P0 End] 즉시형 스킬이 쿨다운 후 다시 발동된다", InstantSkillActivatesAgainAfterCooldown),
            ("[P0 D5] 중복 GiveSkill은 예외이고 인스턴스는 하나만 남는다", DuplicateSkillKeepsOneInstance),
            ("[정책] OnApply 실패는 전파되고 효과는 등록되지 않는다", ApplyFailureDoesNotRegister),
            ("[계약] 효과 만료 시 정확히 한 번 제거된다", EffectExpiryRemovesOnce),
            ("[계약] ReleaseInternal은 효과를 스킬보다 먼저 정리한다", ReleaseClearsEffectsBeforeSkills),
            ("[P1] 닫힌 SkillAgent는 GiveSkill/ApplyEffect를 거부한다", ClosedAgentRejectsRegistration),
            ("[P1 시간] 주기 없는 효과는 duration에 만료된다", NoPeriodEffectExpiresAtDuration),
            ("[P1 시간] 주기 효과는 0부터 period마다 ticks회 실행 후 만료된다", PeriodicEffectTicksAndExpires),
            ("[P1 시간] 큰 deltaTime은 ticks 한도 안에서 한 번에 따라잡는다", LargeDeltaCatchesUp),
            ("[P1 시간] ticks가 0이면 실행 없이 다음 Tick에 만료된다", ZeroTicksExpiresNextTick),
            ("[결함] 나눗셈 오차가 있어도 ticks를 정수 경계에서 정확히 계산한다", TicksToleratesDivisionError),
            ("[정책] OnApply 중 정리되면 효과는 남지 않고 한 번 제거된다", ReleaseDuringApplyRemovesOnce),
            ("[P1] 실행 콜백에서 추가한 효과는 다음 Tick부터 진행된다", EffectAppliedDuringTickStartsNextTick),
            ("[P1] CancelActiveSkillsInternal 후 TickInternal이 정상 동작한다", CancelActiveKeepsTickWorking),
            ("[P1] ApplyEffect의 source가 동작에 저장된다", SourceIsStored),
            ("[P1] Tick 중 ReleaseInternal이 호출되어도 순회가 깨지지 않는다", ReleaseDuringTickKeepsLoop),
            ("[P1 리뷰] 정리 중 OnRemove에서 등록하면 예외로 드러나고 남지 않는다", RegistrationDuringReleaseThrows),
            ("[P1 리뷰] Cancel 중 정리되어도 활성 스킬 취소 순회가 깨지지 않는다", ReleaseDuringCancelActiveKeepsLoop),
            ("[P1b] 테이블 행과 enum을 정의로 변환한다", ConverterMapsTableRows),
            ("[M1 리뷰] 없는 행은 id가 담긴 KeyNotFoundException이고 실패는 캐시하지 않는다", ConverterThrowsForMissingRow),
            ("[M1 리뷰] 같은 Id의 다른 정의로 Refresh해도 처음 정의의 주기·지속시간을 유지한다", RefreshKeepsFirstDefinitionValues),
            ("[P1b] 같은 id는 같은 정의 인스턴스를 반환한다", ConverterCachesById),
            ("[P1b] Global.Table이 바뀌면 캐시를 버리고 새 값을 읽는다", ConverterInvalidatesOnTableChange),
            ("[P1b] 알 수 없는 테이블 enum은 예외다", ConverterThrowsOnUnknownEnum),
            ("[P2a] Base·Add·Multiply·Override 계산", StatusCalculatesModifiers),
            ("[P2a] 수정자 추가 순서와 관계없이 같은 값이다", StatusIsOrderIndependent),
            ("[P2a] RemoveModifiers(source)는 그 출처만 제거해 값을 정확히 되돌린다", StatusRemovesModifiersBySource),
            ("[P2a] Override는 최근 것이 이기고 제거하면 이전 Override, 다음 공식으로 돌아간다", StatusUsesLatestOverride),
            ("[P2a] 변경 이벤트는 실제 변경마다 한 번, 올바른 old/new로 발생한다", StatusRaisesEventOnlyOnChange),
            ("[P2a] 이벤트 핸들러가 수정자를 추가해도 값이 일관된다", StatusHandlerReentrancyStaysConsistent),
            ("[P2a] ReleaseInternal은 이벤트 없이 값과 수정자를 비운다", StatusReleaseClearsSilently),
            ("[P2a 리뷰] RemoveModifiers 중 핸들러가 실패해도 모든 스탯이 갱신된다", StatusRemoveSurvivesHandlerFailure),
            ("[P2b] usePercent로 종류별 수정자를 만들고 Stun·Dot은 공용 빈 배열이다", ConverterBuildsModifiers),
            ("[P2b] 수정자 효과는 적용 시 수정자를 걸고 만료·정리 시 기본값으로 되돌린다", ModifierEffectAppliesAndRemovesModifiers),
            ("[P2b] 같은 스탯의 효과는 합산된다(+20%·+10% → ×1.3)", ModifierEffectsSumAdditively),
            ("[P2b D-12] 최종 스탯은 0 미만이 되지 않는다(Override 포함)", StatusClampsAtZero),
            ("[P2b D-12] 감소 효과 합계가 100%를 넘으면 0이다", ModifierEffectClampsAtZero),
            ("[P2b] Status 없는 Actor에 수정자 효과를 적용하면 예외이고 등록되지 않는다", ModifierEffectWithoutStatusThrows),
            ("[P2b D-15] 실행 클래스를 만들지 못하면 예외가 전파되고 등록되지 않는다", ActionFactoryFailureDoesNotRegister),
            ("[P2b D-14] 작은 delta를 오래 누적해도 한 프레임 안에 만료된다", LongRunExpiresOnTime),
            ("[리뷰] 0.1초 프레임에서 경계 프레임의 실행·만료가 밀리지 않는다", BoundaryFramesAreNotDelayed),
            ("[리뷰] 두 항이 모두 음수여도 최종 스탯은 0이다", StatusClampsNegativeTerms),
            ("[P3 D-13] OnRemove는 자연 만료에 Expired, 정리에 Released를 받는다", RemoveReceivesReason),
            ("[P3 D-13] 연쇄 효과는 Expired에서 다음 효과를 적용하고 Released에서는 적용하지 않는다", ChainEffectAppliesNextOnlyOnExpiry),
            ("[P3 D-13] 제거 콜백에서 같은 id를 다시 적용하면 새 효과로 등록된다", ChainEffectCanReapplySameId),
            ("[P3 Refresh] 같은 id를 다시 적용하면 OnApply 없이 만료 시점이 갱신되고 시전자는 유지된다", RefreshRestartsTimer),
            ("[P4 Refresh] 주기 효과 갱신은 즉시 실행하지 않고 위상을 유지해 ticks회 더 실행한 뒤 한 주기 뒤에 만료된다", RefreshKeepsPeriodicPhase),
            ("[P3 Refresh] 수정자 효과 갱신은 수정자를 중복으로 걸지 않는다", RefreshKeepsModifiers),
            ("[P3 Refresh] 같은 Tick에 만료되는 효과를 다른 제거 콜백이 다시 적용하면 순서와 관계없이 남는다", ReapplyDuringExpiryKeepsEffect),
            ("[P4 Refresh] Tick 중 다른 효과의 콜백에서 갱신되면 순회 순서와 관계없이 그 Tick 시점부터 duration 뒤에 만료된다", RefreshDuringExecuteUsesTickTime),
            ("[P4 Refresh] 실행 콜백에서 자신을 갱신하면 위상을 유지하고 그 시점부터 ticks회 더 실행한다", SelfRefreshDuringExecuteKeepsPhase),
            ("[P4a 리뷰] 다른 효과 콜백에서 주기 효과를 갱신해도 순서와 관계없이 같다", PeriodicRefreshFromOtherEffectIsOrderIndependent),
            ("[P4 Refresh] 한 프레임에 여러 번 갱신해도 실행 한도는 한 번 갱신한 것과 같다", RepeatedRefreshDoesNotStack),
            ("[M2 만료 A] 같은 Tick에 만료된 주기 효과를 다른 제거 콜백이 다시 적용하면 새로 적용되어 경과 0의 실행부터 시작한다", ReapplyAfterExpiryStartsNewApplication),
            ("[P3 태그] 겹친 스턴은 마지막 스턴이 끝날 때 해제되고 이벤트는 0과 1 사이 전환에서만 발생한다", OverlappingStunsKeepStateUntilLast),
            ("[P3 태그] 스턴을 얻으면 활성 스킬을 취소하고 스턴 중 발동은 막힌다", StunBlocksActivationAndCancelsActiveSkills),
            ("[P3 태그] ReleaseInternal은 남은 태그를 이벤트 없이 비운다", ReleaseClearsStatesSilently),
            ("[P3 태그] 스턴 취소 콜백에서 정리되어도 태그 카운트가 음수가 되지 않는다", ReleaseDuringStunCancelLeavesNoState),
            ("[P3 리뷰] Activate 중 자기 스턴을 얻으면 그 스킬도 취소된다", SelfStunDuringActivateCancelsSkill),
            ("[M3 어댑터] 변환은 Strun에만 스턴 태그를 부여한다", ConverterMapsGrantedTags),
            ("[P3 D-7] 쿨다운 감소율을 적용하고 1 이상은 0, Status 없는 Actor는 기본값이다", CooldownReductionShortensCooldown),
            ("[P4 HP] SetHp는 0과 MaxHp 사이로 제한하고 실제 변경에만 이벤트를 낸다", SetHpClampsAndRaisesOnChange),
            ("[P4 HP] MaxHp 기본값이 HP보다 작아지면 HP를 줄이고 늘어날 때는 HP를 바꾸지 않는다", MaxHpDecreaseClampsHp),
            ("[P4 HP] MaxHp 수정자 추가·제거로 MaxHp가 줄면 HP를 줄인다", MaxHpModifierDecreaseClampsHp),
            ("[P4 HP] ReleaseInternal은 이벤트 없이 HP를 0으로 되돌린다", StatusReleaseResetsHpSilently),
            ("[M4 리뷰] 스탯 알림에서 반납되면 남은 이전 값 알림을 보내지 않는다", ReleaseDuringStatNotificationStopsOldNotifications),
            ("[M4 리뷰] 재계산 알림에서 반납되면 자원 알림을 보내지 않는다", ReleaseDuringRecalculateStopsResourceNotification),
            ("[M4 리뷰] 정의 수정자와 자원 정의의 default 스탯은 거부한다", UnregisteredStatsAreRejected),
            ("[P4 데미지] 기본 계산은 power를 그대로 반환한다", FlatDamageReturnsPower),
            ("[P4 데미지] 정의가 가진 계산 클래스를 시전자·대상 Status, power로 호출하고 음수 결과는 0으로 본다", PeriodicDamageUsesDefinitionExecution),
            ("[P4 데미지] 시전자가 없거나 Status가 없으면 source는 null이다(파괴·반납은 [M6 출처])", PeriodicDamageSourceIsNullWithoutLiveCaster),
            ("[P4a 리뷰] NaN 피해는 HP를 바꾸지 않는다", PeriodicDamageIgnoresNaN),
            ("[P4a 리뷰] Tick 중 반납 후 재스폰하면 이전 Tick이 새 효과를 판정하지 않는다", ReleaseAndRespawnDuringTickStopsOldTick),
            ("[P4 도트] 도트는 주기마다 power만큼 HP를 줄이고 0에서 멈춘다", DotReducesHpEachTick),
            ("[P4 도트] Status 없는 Actor에 도트를 적용하면 예외이고 등록되지 않는다", DotWithoutStatusThrows),
            ("[P4 도트] 변환은 Dot에만 usePercent를 power로, 공용 기본 계산을 부여한다", ConverterMapsDotDamage),
            ("[M1] 테이블 없이 만든 정의로 GiveSkill하고 같은 Id는 다른 인스턴스여도 중복 예외다", GiveSkillUsesDefinitionAndRejectsSameId),
            ("[M1] 같은 Id의 다른 정의 인스턴스로 ApplyEffect하면 Refresh된다", ApplyEffectRefreshesBySameId),
            ("[M2 Instant] 피해와 실행을 한 번만 하고 등록·핸들·이벤트·제거 콜백이 없다", InstantExecutesOnceWithoutRegistration),
            ("[M2 Instant] 수정자·부여 태그·주기가 있는 Instant와 MaxStack 0은 정의 생성에서 예외다", InvalidDefinitionsThrow),
            ("[M2 Infinite] 시간으로 만료되지 않는다", InfiniteNeverExpires),
            ("[M2 Infinite] 주기 Infinite는 계속 실행되고 Refresh해도 바뀌지 않는다", PeriodicInfiniteKeepsExecuting),
            ("[M2 Infinite] 핸들로 제거하면 Removed로 끝난다", InfiniteRemovedByHandle),
            ("[M2 핸들] RemoveEffect는 Removed로 제거하고 수정자·태그를 되돌린다", RemoveEffectRevertsWithRemovedReason),
            ("[M2 핸들] 이미 제거된 핸들과 기본 핸들은 아무것도 하지 않는다", StaleHandleIsNoOp),
            ("[M2 핸들] 만료 뒤 다시 적용하면 새 핸들이고 이전 핸들은 새 효과를 제거하지 않는다", HandleIsNotReusedAfterExpiry),
            ("[M2 이벤트] 적용·제거 이벤트는 핸들과 사유(Expired·Removed·Released)를 전달한다", EffectEventsCarryHandleAndReason),
            ("[M2 이벤트] Refresh·Ignore·Stack 재적용은 같은 핸들을 돌려주고 이벤트를 내지 않는다", ReapplyRaisesNoEvents),
            ("[M2 중첩] Ignore는 타이머를 바꾸지 않는다", IgnoreKeepsTimer),
            ("[M2 중첩] Stack은 MaxStack까지만 쌓인다", StackIsCappedAtMaxStack),
            ("[M2 중첩] Stack 수정자는 중첩 수만큼 Add는 곱하고 Multiply는 증가분을 곱하며 Override와 다른 출처는 그대로다", StackScalesModifiers),
            ("[M2 리뷰] 중첩은 수정자를 떼지 않아 Hp와 Override 순서를 유지한다", StackKeepsHpAndOverrideOrder),
            ("[M2 리뷰] 중첩 중 스탯 알림에서 제거해도 수정자가 남지 않는다", RemoveDuringStackDoesNotLeakModifiers),
            ("[M2 리뷰] Instant 피해 알림에서 반납되면 실행 콜백을 호출하지 않는다", InstantSkipsExecuteAfterRelease),
            ("[M2 리뷰] 주기 없는 지속·무한 피해 효과는 정의 오류다", PeriodlessDamageEffectIsInvalid),
            ("[M2 중첩] Stack 주기 피해는 power × 중첩 수다", StackScalesPeriodicDamage),
            ("[M2 중첩] Stack은 지속 시간을 갱신하고 모든 중첩이 한 번에 만료된다", StackRefreshesAndExpiresOnce),
            ("[M2 중첩] Stack은 태그를 한 번만 부여한다", StackGrantsStateOnce),
            ("[M2 중첩] Stack 수정자 교체 중 스탯 알림에서 정리되면 정리된 Status에 수정자가 남지 않는다", ReleaseDuringStackModifierSwapLeavesNoModifier),
            ("[M2 만료 A] 같은 Tick의 만료는 목록 순서와 관계없이 같고 제거 콜백의 재적용은 새 적용이다", ExpiryOrderIsListIndependent),
            ("[M2 만료 A] 제거 콜백에서 정리되면 남은 만료 효과는 Released로 한 번 제거된다", ReleaseDuringExpiryCallbacksEndsPendingAsReleased),
            ("[M2] Tick 중 콜백에서 다른 효과를 제거해도 순회가 깨지지 않는다", RemoveDuringTickKeepsLoop),
            ("[M2] 피해 알림에서 제거된 효과는 제거 콜백 뒤에 실행 콜백을 받지 않는다", RemoveDuringDamageSkipsExecuteHook),
            ("[M2 동작] 팩토리는 적용마다 새 동작을 만들고 훅은 내장 효과 적용 뒤·되돌리기 전에 사유와 함께 호출된다", BehaviorHooksRunInOrderPerApplication),
            ("[M3 태그] 같은 이름은 같은 태그이고 Name을 유지한다", TagRegisterIsIdempotent),
            ("[M3 태그] 다른 이름은 다른 태그이고 같음 비교는 id로 한다", DistinctNamesAreDistinctTags),
            ("[M3 태그] default는 등록된 어떤 태그와도 다르고 Name이 없다", DefaultTagIsInvalid),
            ("[M3 효과] 여러 태그를 부여하면 태그마다 카운트와 이벤트가 따로다", EffectGrantsMultipleTags),
            ("[M3 효과] 겹친 효과의 태그는 그 태그를 준 마지막 효과가 끝날 때 해제된다", OverlappingTagCountsAcrossEffects),
            ("[M3 능력] 스킬은 자기 BlockedTags로만 막히고 태그 없는 스킬은 스턴 중에도 발동한다", SkillIsBlockedOnlyByOwnBlockedTags),
            ("[M3 능력] 태그를 얻으면 그 태그를 CancelledByTags에 가진 활성 스킬만 취소된다", GainingTagCancelsOnlyMatchingSkills),
            ("[M3 능력] 취소 태그를 이미 가졌거나 Activate 중에 얻으면 발동 직후 취소된다", ActivationCancelsOnAnyCancelledByTag),
            ("[M3 능력] 여러 태그 획득의 취소 콜백에서 정리되면 태그 이벤트가 없고 카운트가 비어 있다", ReleaseDuringTagCancelLeavesNoTags),
            ("[M3 어댑터] 모든 스킬은 같은 스턴 태그로 막히고 취소된다", AdapterSkillsUseStunTag),
            ("[M3 재스폰] 태그 획득의 취소 콜백에서 다시 스폰해 같은 태그를 얻으면 획득 알림은 한 번이고 새 스폰의 태그가 유지된다", RespawnDuringTagCancelKeepsNewTag),
            ("[M3 재스폰] 여러 태그 획득의 취소 콜백에서 다시 스폰해 뒤 태그를 얻으면 새 스폰의 태그가 유지된다", RespawnDuringMultiTagCancelKeepsLaterTag),
            ("[M3 재스폰] 효과 제거의 태그 해제 알림에서 다시 스폰해 뒤 태그를 얻으면 새 스폰의 태그가 유지된다", RespawnDuringTagRemovalKeepsLaterTag),
            ("[M3 재스폰] 태그 취소 콜백에서 다시 스폰하면 새 스폰의 스킬은 취소되지 않는다", RespawnDuringCancelByTagKeepsNewSkills),
            ("[M3 재스폰] 갱신 콜백에서 다시 스폰하면 새 스폰의 스킬은 이전 갱신에서 실행되지 않는다", RespawnDuringUpdateSkipsNewSkills),
            ("[M3 재스폰] 활성 스킬 취소 콜백에서 다시 스폰하면 새 스폰의 스킬은 취소되지 않는다", RespawnDuringCancelActiveKeepsNewSkills),
            ("[M3 태그] 이름이 null이거나 비어 있으면 등록을 거부한다", TagRegisterRejectsEmptyName),
            ("[M3 태그] 정의의 태그 목록에 default 태그가 있으면 거부한다", DefinitionsRejectDefaultTag),
            ("[M4 스탯] 같은 이름은 같은 StatId이고 Name을 유지한다", StatIdRegisterIsIdempotent),
            ("[M4 스탯] 다른 이름은 다른 StatId이고 같음 비교는 id로 한다", DistinctNamesAreDistinctStats),
            ("[M4 스탯] default StatId는 등록된 어떤 스탯과도 다르고 Name이 없다", DefaultStatIdIsInvalid),
            ("[M4 스탯] 이름이 null이거나 비어 있으면 등록을 거부한다", StatIdRegisterRejectsEmptyName),
            ("[M4 스탯] 스탯 등록은 태그 등록과 따로라 같은 이름을 쓸 수 있고 태그 id를 소모하지 않는다", StatRegistryIsSeparateFromTags),
            ("[M4 스탯] 한 번도 설정하지 않은 스탯은 0이고 수정자만 걸어도 계산된다", UnknownStatReadsZero),
            ("[M4 자원] 상한이 다른 두 자원은 각자의 상한으로만 제한된다", ResourcesClampIndependently),
            ("[M4 자원] 기본값·수정자로 상한이 줄면 같은 상한의 자원을 모두 줄이고 스탯, 자원 순서로 알린다", MaxDecreaseClampsResourcesAfterStatEvent),
            ("[M4 자원] 정의하지 않은 자원의 읽기·쓰기와 중복 정의는 예외이고 자원은 스탯이 아니다", UndefinedResourceThrows),
            ("[M4 자원] ReleaseInternal은 자원 정의를 유지하고 값만 이벤트 없이 0으로 되돌린다", ReleaseKeepsResourceDefinitions),
            ("[M4 피해] 피해 효과는 정의의 DamageResource를 깎는다", DamageEffectDamagesConfiguredResource),
            ("[M4 피해] DamageExecution이 있는데 DamageResource가 없으면 정의 생성에서 예외다", DamageEffectRequiresResource),
            ("[M4 피해] 대상에 피해 자원이 정의되지 않았으면 아무것도 바꾸기 전에 예외다", DamageEffectWithUndefinedResourceThrowsBeforeApply),
            ("[M4 쿨다운] 스킬은 자기 CooldownReductionStat만 읽고 없으면 감소하지 않는다", CooldownReductionUsesSkillStat),
            ("[M4 어댑터] 테이블 스킬은 CooldownReduction을, 도트는 Hp를 쓴다", AdapterMapsStatIds),
            ("[M5 팩토리] GiveSkill마다 팩토리로 실행 인스턴스를 하나 만들고 네임스페이스와 관계없다", FactoryCreatesOneActionPerGiveSkill),
            ("[M6 리뷰] OnEnd 중 같은 스킬의 재발동은 무시되어 발동·종료 순서가 유지된다", ActivationInsideOnEndIsIgnored),
            ("[M6 리뷰] 발동 조건 판정 중 반납되면 발동하지 않는다", ReleaseInsideCanActivateDoesNotActivate),
            ("[M5 효과] ApplyEffects는 정의의 모든 효과를 소유자 출처로 넘긴 대상(다른 대상·자신)에 건다", ApplyEffectsAppliesDefinitionEffectsWithOwnerSource),
            ("[M5 효과] 앞선 효과의 콜백에서 대상이 반납되면 남은 효과를 걸지 않는다", ApplyEffectsStopsWhenTargetReleased),
            ("[M5 비용] 자원이 부족하면 발동·쿨다운·이벤트·차감이 없다", InsufficientCostBlocksActivation),
            ("[M5 비용] 비용은 Activate 뒤 Commit에서 차감되고 자원이 비용과 같아도 발동한다", CostIsDeductedOnCommit),
            ("[M5 비용] 여러 비용은 모두 충분해야 발동하고 모두 차감된다", MultipleCostsRequireAllAndDeductAll),
            ("[M5 비용] 비용이 있는데 Status가 없거나 자원이 정의되지 않았으면 예외다", CostRequiresStatusAndDefinedResource),
            ("[M5 비용] 정의는 default 자원과 음수 비용을 거부한다", DefinitionRejectsInvalidCosts),
            ("[M5 비용] Activate나 비용 알림에서 반납되면 다음 스폰의 자원을 차감하지 않고 발동을 알리지 않는다", ReleaseDuringActivationSkipsRemainingCosts),
            ("[M5 이벤트] 발동은 Activate·Commit 뒤에, 종료는 End에서 Completed로 알린다", SkillEventsFollowActivationAndEnd),
            ("[M5 이벤트] Activate 중 끝난 스킬은 발동 알림 뒤에 종료를 알린다(즉시형·Activate 중 취소)", EndDuringActivateIsReportedAfterActivated),
            ("[M5 이벤트] 태그·직접 취소·비활성화·정리 취소는 Cancelled로 한 번만 알린다", CancelPathsRaiseEndedOnce),
            ("[M5 이벤트] 막힌 발동(차단 태그·쿨다운·비용·이미 활성)은 알리지 않는다", BlockedActivationRaisesNoEvents),
            ("[M5 이벤트] 발동 직후 취소 태그로 취소되면 발동·종료 모두 알리지 않는다", CancelledRightAfterActivationRaisesNoEvents),
            ("[M5 이벤트] 발동 알림에서 얻은 취소 태그는 그 스킬을 취소한다", TagGainedInActivatedHandlerCancelsSkill),
            ("[M5 이벤트] 발동 알림에서 반납되면 미룬 종료를 알리지 않는다", ReleaseInActivatedHandlerDropsDeferredEnd),
            ("[M5 리뷰] Activate 중 끝난 스킬을 발동 알림에서 다시 발동하면 무시되고 발동·종료가 한 쌍이며 종료 알림에서는 다시 발동된다", ReactivationInActivatedHandlerIsIgnoredForEndedSkill),
            ("[M5 리뷰] Activate 중 끝난 스킬을 비용 알림에서 다시 발동하면 무시된다", ReactivationDuringCommitIsIgnored),
            ("[M5 리뷰] Activate 중 비활성화되면 발동 중인 스킬도 이벤트 없이 취소된다(첫 발동·목록에 남은 이전 항목)", DeactivationDuringActivateCancelsActivatingSkill),
            ("[M5 리뷰] Activate 중 얻은 취소 태그는 목록에 남은 이전 항목과 관계없이 이벤트 없이 취소한다", TagGainedDuringActivateCancelsWithoutEvents),
            ("[M5 리뷰] 정의는 같은 자원의 중복 비용과 NaN 비용을 거부한다", DefinitionRejectsDuplicateAndNaNCosts),
            ("[M5 리뷰] 정리 중 효과 제거 콜백의 발동은 무시되고 비용·이벤트 없이 정리가 끝난다", ActivationDuringReleaseIsIgnored),
            ("[M5 리뷰] 팩토리가 이미 초기화된 인스턴스를 돌려주면 예외이고 등록되지 않는다", SharedActionInstanceIsRejected),
            ("[M5 리뷰] 앞선 효과의 콜백에서 시전자가 반납되면 남은 효과를 걸지 않는다", ApplyEffectsStopsWhenOwnerReleased),
            ("[M5 어댑터] skill_type은 실행 클래스 팩토리로, skill_buffId는 효과 목록으로 바꾸고 비용은 없다", AdapterMapsFactoriesAndEffects),
            ("[M6 출처] 적용 뒤 시전자의 스폰 번호가 바뀌면(반납·재스폰·파괴) 주기 피해의 source는 null이고 피해는 계속된다", ChangedSourceSerialDropsSourceStatus),
            ("[M6 소유자] ComBaseActor가 아닌 IAbilityOwner도 소유자·시전자로 효과와 스킬을 쓴다", AbilityOwnerDoesNotNeedActor),
        };

        int failed = 0;
        foreach (var test in tests)
        {
            ResetState();
            try
            {
                test.Body();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("FAIL " + test.Name + " :: " + error.GetType().Name + ": " + error.Message);
            }
        }

        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
        return failed == 0 ? 0 : 1;
    }

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

        Check(GetActions(agent).Count == 0, "스킬이 등록되었다");
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

    static void ReleaseDuringStunCancelLeavesNoState()
    {
        AddBuff(1, BuffType.Strun, 1f);
        var agent = NewAgent();
        agent.GiveSkill(StunSkill(10));
        agent.ActivateSkill(10);
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
        };
        int events = 0;
        agent.OnTagChanged += (tag, active) => events++;

        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        Check(!agent.HasTag(Stun) && EffectCount(agent) == 0, "정리 후 스턴이 남았다");
        Check(events == 0, $"정리된 스턴의 태그 이벤트 기대 0, 실제 {events}");

        agent.OpenInternal();
        agent.ApplyEffect(SkillDefinitions.GetEffect(1), null);
        Check(agent.HasTag(Stun), "정리 후 다시 얻은 스턴이 적용되지 않았다");
        agent.TickInternal(1f);
        Check(!agent.HasTag(Stun), "다시 얻은 스턴이 해제되지 않았다");
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

    static void PeriodicDamageIgnoresNaN()
    {
        var ownerStatus = NewStatus();
        ownerStatus.SetResource(Hp, 10f);
        var execution = new ProjectT.Skill.Tests.RecordingDamageExecution { Result = float.NaN };
        var agent = NewAgent(ownerStatus);

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 1f, 0.5f, null, power: 5f, execution: execution), null);

        Check(execution.Calls.Count == 1 && ownerStatus.GetResource(Hp) == 10f, $"NaN 피해가 HP를 바꿨다: {ownerStatus.GetResource(Hp)}");
    }

    static void ReleaseAndRespawnDuringTickStopsOldTick()
    {
        var respawned = TestEffect(2, 0f);
        var agent = NewAgent();
        agent.ApplyEffect(TestEffect(1, 1f, 0.5f), null);
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = behavior =>
        {
            if (behavior.EffectId != 1)
                return;

            ProjectT.Skill.Tests.TestBehavior.ExecuteHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            agent.ApplyEffect(respawned, null);
        };

        agent.TickInternal(0.5f);

        Check(EffectCount(agent) == 1, "새 스폰의 효과가 이전 Tick에서 판정되었다");
        agent.TickInternal(0.1f);
        Check(EffectCount(agent) == 0, "새 스폰의 효과가 다음 Tick에 만료되지 않았다");
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

    static void GiveSkillUsesDefinitionAndRejectsSameId()
    {
        var agent = NewAgent();
        var definition = Skill(10, coolTime: 1f, factory: () => new ExternalGame.ExternalSkillAction());
        agent.GiveSkill(definition);
        var first = GetAction(agent, 10);
        Check(first is ExternalGame.ExternalSkillAction && ReferenceEquals(first.Spec.Definition, definition), "넘긴 정의로 스킬을 만들지 않았다");

        CheckThrows<ArgumentException>(() => agent.GiveSkill(Skill(10, coolTime: 2f, factory: () => new ExternalGame.ExternalSkillAction())), "같은 Id의 다른 정의");

        Check(GetActions(agent).Count == 1 && ReferenceEquals(GetAction(agent, 10), first), "기존 인스턴스가 유지되지 않았다");
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

    static void InstantSkipsExecuteAfterRelease()
    {
        var status = NewStatus();
        status.SetBase(MaxHp, 100f);
        status.SetResource(Hp, 100f);
        var agent = NewAgent(status);
        status.OnResourceChanged += (resource, previous, current) => agent.ReleaseInternal();
        var instant = Effect(1, EffectDurationPolicy.Instant, 0f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), power: 3f, execution: new FlatDamageExecution());

        agent.ApplyEffect(instant, null);

        CheckExecutes(1, 0, "반납 뒤 Instant 실행 콜백");
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

    static void ReleaseDuringStackModifierSwapLeavesNoModifier()
    {
        var status = NewStatus();
        var effect = Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, modifiers: new[] { new EffectModifier(Atk, StatModOp.Add, 2f) }, stack: EffectStackPolicy.Stack, maxStack: 3);
        var agent = NewAgent(status);
        agent.ApplyEffect(effect, null);
        bool released = false;
        status.OnStatChanged += (type, previous, current) =>
        {
            if (released)
                return;

            released = true;
            agent.ReleaseInternal();
            status.ReleaseInternal();
        };

        agent.ApplyEffect(effect, null);

        Check(released && status.Get(Atk) == 0f, $"정리된 Status에 수정자가 남았다: {status.Get(Atk)}");
        status.SetBase(Atk, 10f);
        CheckStat(status, Atk, 10f, "다음 스폰");
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

    static void ReleaseDuringExpiryCallbacksEndsPendingAsReleased()
    {
        var chained = TestEffect(3, 1f);
        var agent = NewAgent();
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = (behavior, reason) =>
        {
            if (behavior.EffectId == 1 && reason == EffectRemoveReason.Expired)
                agent.ReleaseInternal();
            else if (behavior.EffectId == 2 && reason == EffectRemoveReason.Expired)
                agent.ApplyEffect(chained, null);
        };
        agent.ApplyEffect(TestEffect(1, 1f), null);
        agent.ApplyEffect(TestEffect(2, 1f), null);

        agent.TickInternal(1f);

        string reasons = string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons);
        Check(reasons == "Expired,Released" && EffectCount(agent) == 0 && ProjectT.Skill.Tests.TestBehavior.Applies == 2, $"남은 만료 효과의 사유 {reasons}, 효과 {EffectCount(agent)}, 적용 {ProjectT.Skill.Tests.TestBehavior.Applies}");
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

    static void ReleaseDuringTagCancelLeavesNoTags()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.ActivateSkill(10);
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
        };
        int events = 0;
        agent.OnTagChanged += (tag, active) => events++;
        var effect = Effect(1, EffectDurationPolicy.Duration, 1f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), tags: new[] { TagA, TagB });

        var handle = agent.ApplyEffect(effect, null);

        Check(handle.Id == 0 && EffectCount(agent) == 0 && ((IDictionary)GetField(agent, "tagCounts")).Count == 0, "정리 뒤 효과나 태그 카운트가 남았다");
        Check(events == 0 && string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Released", $"태그 이벤트 기대 0, 실제 {events}, 사유 {string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons)}");

        agent.OpenInternal();
        agent.ApplyEffect(effect, null);
        Check(agent.HasTag(TagA) && agent.HasTag(TagB) && events == 2, "정리 후 다시 얻은 태그가 적용되지 않았다");
        agent.TickInternal(1f);
        Check(!agent.HasTag(TagA) && !agent.HasTag(TagB) && events == 4, "다시 얻은 태그가 해제되지 않았다");
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

    static void RespawnDuringTagCancelKeepsNewTag()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.ActivateSkill(10);
        var respawned = Effect(2, EffectDurationPolicy.Duration, 1f, 0f, null, tags: new[] { TagA });
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            agent.ApplyEffect(respawned, null);
        };
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}");

        var handle = agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);

        Check(string.Join(",", events) == "Test.A:True", "태그 이벤트: " + string.Join(",", events));
        Check(handle.Id == 0 && EffectCount(agent) == 1 && agent.HasTag(TagA) && TagCount(agent, TagA) == 1, $"새 스폰의 태그: HasTag {agent.HasTag(TagA)}, 카운트 {TagCount(agent, TagA)}, 효과 {EffectCount(agent)}");
        agent.TickInternal(1f);
        Check(!agent.HasTag(TagA) && string.Join(",", events) == "Test.A:True,Test.A:False", "새 스폰의 효과 만료 뒤: " + string.Join(",", events));
    }

    static void RespawnDuringMultiTagCancelKeepsLaterTag()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, cancelledBy: new[] { TagA }));
        agent.ActivateSkill(10);
        var respawned = Effect(2, EffectDurationPolicy.Duration, 1f, 0f, null, tags: new[] { TagB });
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            agent.ApplyEffect(respawned, null);
        };
        var events = new List<string>();
        agent.OnTagChanged += (tag, active) => events.Add($"{tag.Name}:{active}");

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, () => new ProjectT.Skill.Tests.TestBehavior(), tags: new[] { TagA, TagB }), null);

        Check(EffectCount(agent) == 1 && !agent.HasTag(TagA) && agent.HasTag(TagB) && TagCount(agent, TagB) == 1, $"새 스폰의 태그: A {agent.HasTag(TagA)}, B {agent.HasTag(TagB)}, B 카운트 {TagCount(agent, TagB)}");
        Check(string.Join(",", events) == "Test.B:True" && string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons) == "Released", $"태그 이벤트 {string.Join(",", events)}, 사유 {string.Join(",", ProjectT.Skill.Tests.TestBehavior.Reasons)}");
        agent.TickInternal(1f);
        Check(!agent.HasTag(TagB) && string.Join(",", events) == "Test.B:True,Test.B:False", "새 스폰의 효과 만료 뒤: " + string.Join(",", events));
    }

    static void RespawnDuringTagRemovalKeepsLaterTag()
    {
        var agent = NewAgent();
        var handle = agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA, TagB }), null);
        var respawned = Effect(2, EffectDurationPolicy.Duration, 1f, 0f, null, tags: new[] { TagB });
        bool respawn = true;
        agent.OnTagChanged += (tag, active) =>
        {
            if (!respawn || tag != TagA || active)
                return;

            respawn = false;
            agent.ReleaseInternal();
            agent.OpenInternal();
            agent.ApplyEffect(respawned, null);
        };

        agent.RemoveEffect(handle);

        Check(EffectCount(agent) == 1 && !agent.HasTag(TagA) && agent.HasTag(TagB) && TagCount(agent, TagB) == 1, $"새 스폰의 태그: A {agent.HasTag(TagA)}, B {agent.HasTag(TagB)}, B 카운트 {TagCount(agent, TagB)}");
        agent.TickInternal(1f);
        Check(!agent.HasTag(TagB) && EffectCount(agent) == 0, "새 스폰의 효과가 만료되며 태그를 해제하지 않았다");
    }

    static void RespawnDuringCancelByTagKeepsNewSkills()
    {
        var agent = NewAgent();
        GiveAndActivate(agent, Skill(10, cancelledBy: new[] { TagA }), Skill(11, cancelledBy: new[] { TagA }));
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            GiveAndActivate(agent, Skill(10, cancelledBy: new[] { TagA }), Skill(11, cancelledBy: new[] { TagA }));
        };

        agent.ApplyEffect(Effect(1, EffectDurationPolicy.Duration, 5f, 0f, null, tags: new[] { TagA }), null);

        Check(GetAction(agent, 10).IsActive && GetAction(agent, 11).IsActive && GetActiveActions(agent).Count == 2, "새 스폰의 스킬이 취소되었다");
    }

    static void RespawnDuringUpdateSkipsNewSkills()
    {
        var agent = NewAgent();
        GiveAndActivate(agent, Skill(10), Skill(11));
        ProjectT.Skill.Tests.TestSkillAction.UpdateHook = _ =>
        {
            ProjectT.Skill.Tests.TestSkillAction.UpdateHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            GiveAndActivate(agent, Skill(10), Skill(11));
        };

        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 1 && GetActiveActions(agent).Count == 2, $"이전 갱신이 새 스폰의 스킬을 실행했다 Updates={ProjectT.Skill.Tests.TestSkillAction.Updates}");
        agent.TickInternal(0.1f);
        Check(ProjectT.Skill.Tests.TestSkillAction.Updates == 3, $"새 스폰의 스킬이 다음 갱신에서 실행되지 않았다 Updates={ProjectT.Skill.Tests.TestSkillAction.Updates}");
    }

    static void RespawnDuringCancelActiveKeepsNewSkills()
    {
        var agent = NewAgent();
        GiveAndActivate(agent, Skill(10), Skill(11));
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = () =>
        {
            ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
            agent.ReleaseInternal();
            agent.OpenInternal();
            GiveAndActivate(agent, Skill(10), Skill(11));
        };

        agent.CancelActiveSkillsInternal();

        Check(GetAction(agent, 10).IsActive && GetAction(agent, 11).IsActive && GetActiveActions(agent).Count == 2, "새 스폰의 스킬이 취소되었다");
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

    static void ApplyEffectsStopsWhenTargetReleased()
    {
        var owner = NewAgent();
        var target = NewAgent();
        owner.GiveSkill(Skill(10, effects: new[] { TestEffect(1, 1f), TestEffect(2, 1f) }));
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = () =>
        {
            ProjectT.Skill.Tests.TestBehavior.ApplyHook = null;
            target.ReleaseInternal();
            target.OpenInternal();
        };

        ((ProjectT.Skill.Tests.TestSkillAction)GetAction(owner, 10)).ApplyEffectsTo(target);

        Check(ProjectT.Skill.Tests.TestBehavior.Applies == 1 && EffectCount(target) == 0, $"반납 뒤 남은 효과가 다시 스폰된 대상에 걸렸다. 적용 {ProjectT.Skill.Tests.TestBehavior.Applies}, 효과 {EffectCount(target)}");
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

    static void ReleaseDuringActivationSkipsRemainingCosts()
    {
        var status = ManaStatus(10f);
        status.SetResource(Hp, 10f);
        var agent = NewAgent(status);
        var events = RecordSkillEvents(agent);
        bool respawning = false;
        void Respawn()
        {
            respawning = true;
            agent.ReleaseInternal();
            status.ReleaseInternal();
            agent.OpenInternal();
            status.SetBase(MaxHp, 10f);
            status.SetBase(MaxMp, 10f);
            status.SetResource(Hp, 10f);
            status.SetResource(Mp, 10f);
        }

        var costs = new[] { new SkillCost(Mp, 2f), new SkillCost(Hp, 3f) };
        agent.GiveSkill(Skill(10, costs: costs));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = Respawn;
        agent.ActivateSkill(10);
        Check(status.GetResource(Mp) == 10f && status.GetResource(Hp) == 10f && events.Count == 0, $"Activate 중 반납: Mp {status.GetResource(Mp)}, Hp {status.GetResource(Hp)}, 이벤트 {string.Join(",", events)}");

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = null;
        respawning = false;
        status.OnResourceChanged += (resource, previous, current) =>
        {
            if (!respawning && resource == Mp)
                Respawn();
        };
        agent.GiveSkill(Skill(10, costs: costs));
        agent.ActivateSkill(10);
        Check(status.GetResource(Mp) == 10f && status.GetResource(Hp) == 10f && events.Count == 0, $"비용 알림 중 반납: Mp {status.GetResource(Mp)}, Hp {status.GetResource(Hp)}, 이벤트 {string.Join(",", events)}");
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
        Check(string.Join(",", events) == "activated:1,ended:1:False" && !GetAction(agent, 1).IsActive && GetActiveActions(agent).Count == 0, "즉시형 스킬 이벤트: " + string.Join(",", events));

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

    static void ReleaseInActivatedHandlerDropsDeferredEnd()
    {
        AddSkill(1, SkillType.Normal, 0f);
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(SkillDefinitions.GetSkill(1));
        agent.OnSkillActivated += _ =>
        {
            agent.ReleaseInternal();
            agent.OpenInternal();
        };

        agent.ActivateSkill(1);

        Check(string.Join(",", events) == "activated:1", "반납 뒤 미룬 종료가 알려졌다: " + string.Join(",", events));
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
        Check(string.Join(",", events) == "activated:10,ended:10:False" && ProjectT.Skill.Tests.TestSkillAction.Activates == 1 && !GetAction(agent, 10).IsActive && GetActiveActions(agent).Count == 0, $"발동 알림 중 재발동: {string.Join(",", events)}, 발동 {ProjectT.Skill.Tests.TestSkillAction.Activates}");

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
        Check(status.GetResource(Mp) == 9f && !GetAction(agent, 10).IsActive && GetActiveActions(agent).Count == 0, $"Mp {status.GetResource(Mp)}, 활성 {GetAction(agent, 10).IsActive}, 목록 {GetActiveActions(agent).Count}");
    }

    static void DeactivationDuringActivateCancelsActivatingSkill()
    {
        var agent = NewAgent();
        var events = RecordSkillEvents(agent);
        agent.GiveSkill(Skill(10));
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = () => agent.CancelActiveSkillsInternal();

        agent.ActivateSkill(10);
        Check(events.Count == 0 && !GetAction(agent, 10).IsActive && GetActiveActions(agent).Count == 0, $"첫 발동: 이벤트 {string.Join(",", events)}, 활성 {GetAction(agent, 10).IsActive}, 목록 {GetActiveActions(agent).Count}");

        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = null;
        agent.ActivateSkill(10);
        agent.CancelSkill(10);
        events.Clear();
        Check(GetActiveActions(agent).Count == 1, "취소된 이전 항목이 목록에 남아 있지 않다");

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
        Check(ProjectT.Skill.Tests.TestBehavior.Removes == 1 && EffectCount(agent) == 0 && GetActions(agent).Count == 0, "정리가 끝나지 않았다");
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

        Check(GetActions(second).Count == 0 && ReferenceEquals(shared.Owner, firstOwner) && ReferenceEquals(GetAction(first, 10), shared), "거부된 GiveSkill이 상태를 바꿨다");
    }

    static void ApplyEffectsStopsWhenOwnerReleased()
    {
        var owner = NewAgent();
        var target = NewAgent();
        owner.GiveSkill(Skill(10, effects: new[] { TestEffect(1, 1f), TestEffect(2, 1f) }));
        var action = (ProjectT.Skill.Tests.TestSkillAction)GetAction(owner, 10);
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = () =>
        {
            ProjectT.Skill.Tests.TestBehavior.ApplyHook = null;
            owner.ReleaseInternal();
            owner.OpenInternal();
        };

        action.ApplyEffectsTo(target);

        Check(ProjectT.Skill.Tests.TestBehavior.Applies == 1 && string.Join(",", GetEffectIds(target)) == "1", $"반납된 시전자를 출처로 남은 효과를 걸었다. 적용 {ProjectT.Skill.Tests.TestBehavior.Applies}, 효과 {string.Join(",", GetEffectIds(target))}");
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

    static List<string> RecordSkillEvents(SkillAgent agent)
    {
        var events = new List<string>();
        agent.OnSkillActivated += definition => events.Add($"activated:{definition.Id}");
        agent.OnSkillEnded += (definition, reason) => events.Add($"ended:{definition.Id}:{reason == SkillEndReason.Cancelled}");
        return events;
    }

    static Status ManaStatus(float mp)
    {
        var status = NewStatus();
        status.SetBase(MaxMp, 10f);
        status.DefineResource(Mp, MaxMp);
        status.SetResource(Mp, mp);
        return status;
    }

    static void GiveAndActivate(SkillAgent agent, params SkillDefinition[] definitions)
    {
        foreach (var definition in definitions)
        {
            agent.GiveSkill(definition);
            agent.ActivateSkill(definition.Id);
        }
    }

    static int TagCount(SkillAgent agent, SkillTag tag)
    {
        var counts = (IDictionary)GetField(agent, "tagCounts");
        return counts.Contains(tag) ? (int)counts[tag] : 0;
    }

    // 테이블 스킬의 기본값처럼 스턴에 막히고 취소된다.
    static SkillDefinition StunSkill(int id) => Skill(id, new[] { Stun }, new[] { Stun });

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

    static void ReleaseInsideCanActivateDoesNotActivate()
    {
        var agent = NewAgent();
        agent.GiveSkill(Skill(10, factory: () => new ProjectT.Skill.Tests.ReleasingCanActivateAction()));
        var events = new List<string>();
        agent.OnSkillActivated += definition => events.Add($"act:{definition.Id}");
        var action = GetAction(agent, 10);
        ProjectT.Skill.Tests.ReleasingCanActivateAction.CanActivateHook = () => agent.ReleaseInternal();

        agent.ActivateSkill(10);

        Check(events.Count == 0 && GetActiveActions(agent).Count == 0, $"발동 조건 판정 중 반납된 스킬이 발동되었다: {string.Join(",", events)}, 활성 {action.IsActive}");
        ProjectT.Skill.Tests.ReleasingCanActivateAction.CanActivateHook = null;
    }

    static SkillDefinition Skill(int id, SkillTag[] blocked = null, SkillTag[] cancelledBy = null, float coolTime = 0f, StatId cooldownStat = default, EffectDefinition[] effects = null, SkillCost[] costs = null, Func<BaseSkillAction> factory = null)
    {
        return new SkillDefinition(id, coolTime, factory ?? (() => new ProjectT.Skill.Tests.TestSkillAction()), blocked ?? Array.Empty<SkillTag>(), cancelledBy ?? Array.Empty<SkillTag>(), cooldownStat, effects ?? Array.Empty<EffectDefinition>(), costs ?? Array.Empty<SkillCost>());
    }

    static EffectDefinition TestEffect(int id, float duration, float period = 0f) => Effect(id, EffectDurationPolicy.Duration, duration, period, () => new ProjectT.Skill.Tests.TestBehavior());

    static EffectDefinition Effect(int id, EffectDurationPolicy policy, float duration, float period, Func<EffectBehavior> factory, IReadOnlyList<EffectModifier> modifiers = null, IReadOnlyList<SkillTag> tags = null, float power = 0f, DamageExecution execution = null, EffectStackPolicy stack = EffectStackPolicy.Refresh, int maxStack = 1, StatId? damageResource = null)
    {
        return new EffectDefinition(id, policy, duration, period, modifiers ?? Array.Empty<EffectModifier>(), tags ?? Array.Empty<SkillTag>(), power, execution, damageResource ?? Hp, stack, maxStack, factory);
    }

    static void ReleaseDuringStatNotificationStopsOldNotifications()
    {
        var status = NewStatus();
        var source = new object();
        status.AddModifier(MaxHp, StatModOp.Add, 5f, source);
        status.AddModifier(Atk, StatModOp.Add, 5f, source);
        status.SetResource(Hp, 15f);
        var events = new List<string>();
        bool released = false;
        status.OnStatChanged += (stat, previous, current) =>
        {
            events.Add($"stat:{stat.Name}");
            if (released)
                return;

            released = true;
            status.ReleaseInternal();
        };
        status.OnResourceChanged += (resource, previous, current) => events.Add($"res:{resource.Name}");

        status.RemoveModifiers(source);

        Check(events.Count == 1, "반납 뒤에도 이전 값 알림이 나갔다: " + string.Join(",", events));
    }

    static void ReleaseDuringRecalculateStopsResourceNotification()
    {
        var status = NewStatus();
        status.SetResource(Hp, 10f);
        var events = new List<string>();
        status.OnStatChanged += (stat, previous, current) => status.ReleaseInternal();
        status.OnResourceChanged += (resource, previous, current) => events.Add($"res:{resource.Name}");

        status.SetBase(MaxHp, 5f);

        Check(events.Count == 0, "반납 뒤에도 자원 알림이 나갔다: " + string.Join(",", events));
    }

    static void UnregisteredStatsAreRejected()
    {
        CheckThrows<ArgumentException>(() => new EffectDefinition(1, EffectDurationPolicy.Duration, 1f, 0f, new[] { new EffectModifier(default, StatModOp.Add, 1f) }, Array.Empty<SkillTag>(), 0f, null, default, EffectStackPolicy.Refresh, 1, null), "수정자의 default 스탯");
        var status = new Status();
        CheckThrows<ArgumentException>(() => status.DefineResource(default, MaxHp), "default 자원");
        CheckThrows<ArgumentException>(() => status.DefineResource(Hp, default), "default 상한");
    }

    static Status NewStatus()
    {
        var status = new Status();
        status.SetBase(MaxHp, 10f);
        status.SetBase(Atk, 10f);
        status.SetBase(Def, 10f);
        status.DefineResource(Hp, MaxHp);
        return status;
    }

    static SkillAgent NewAgent(Status status = null)
    {
        var actor = new ProjectT.Skill.Tests.TestActor { Status = status };
        var agent = new SkillAgent(actor);
        actor.SkillAgent = agent;
        agent.OpenInternal();
        return agent;
    }

    static void AddSkill(int id, SkillType type, float coolTime, int buffId = -1)
    {
        Global.Table.SkillInfos.Rows[id] = new skillInfo { skill_Id = id, skill_type = (sbyte)type, skill_coolTime = coolTime, skill_buffId = buffId };
    }

    static void AddBuff(int id, BuffType type, float duration, float interval = 0f, float usePercent = 0f)
    {
        Global.Table.BuffInfos.Rows[id] = new buffInfo { buff_Id = id, buff_type = (sbyte)type, buff_duration = duration, buff_interval = interval, buff_usePercent = usePercent };
    }

    static object GetField(object target, string name) => target.GetType().GetField(name, PrivateInstance).GetValue(target);

    static IDictionary GetActions(SkillAgent agent) => (IDictionary)GetField(GetField(agent, "actionController"), "actions");

    static IList GetActiveActions(SkillAgent agent) => (IList)GetField(GetField(agent, "actionController"), "activeActions");

    static BaseSkillAction GetAction(SkillAgent agent, int id) => (BaseSkillAction)GetActions(agent)[id];

    static IList GetActiveEffects(SkillAgent agent) => (IList)GetField(GetField(agent, "effectController"), "activeEffects");

    static int EffectCount(SkillAgent agent) => GetActiveEffects(agent).Count;

    static T GetActiveField<T>(object active, string name) => (T)active.GetType().GetField(name).GetValue(active);

    static int GetStackCount(SkillAgent agent, int index) => GetActiveField<int>(GetActiveEffects(agent)[index], "StackCount");

    static List<EffectBehavior> GetBehaviors(SkillAgent agent)
    {
        var behaviors = new List<EffectBehavior>();
        foreach (var active in GetActiveEffects(agent))
            behaviors.Add(GetActiveField<EffectBehavior>(active, "Behavior"));
        return behaviors;
    }

    static List<int> GetEffectIds(SkillAgent agent)
    {
        var ids = new List<int>();
        foreach (var active in GetActiveEffects(agent))
            ids.Add(GetActiveField<EffectDefinition>(active, "Definition").Id);
        return ids;
    }

    static void ResetState()
    {
        Global.Table = new DataMgr();
        ProjectT.Skill.Tests.TestSkillAction.Activates = 0;
        ProjectT.Skill.Tests.TestSkillAction.Updates = 0;
        ProjectT.Skill.Tests.TestSkillAction.Cancels = 0;
        ProjectT.Skill.Tests.TestSkillAction.EndOnUpdate = false;
        ProjectT.Skill.Tests.TestSkillAction.UpdateHook = null;
        ProjectT.Skill.Tests.TestSkillAction.CancelHook = null;
        ProjectT.Skill.Tests.TestSkillAction.ActivateHook = null;
        ProjectT.Skill.Tests.TestSkillAction.Log = null;
        ProjectT.Skill.Tests.TestBehavior.Removes = 0;
        ProjectT.Skill.Tests.TestBehavior.Applies = 0;
        ProjectT.Skill.Tests.TestBehavior.Reasons.Clear();
        ProjectT.Skill.Tests.TestBehavior.ThrowOnApply = false;
        ProjectT.Skill.Tests.TestBehavior.Executes.Clear();
        ProjectT.Skill.Tests.TestBehavior.ExecuteHook = null;
        ProjectT.Skill.Tests.TestBehavior.RemoveHook = null;
        ProjectT.Skill.Tests.TestBehavior.ApplyHook = null;
        ProjectT.Skill.Tests.TestBehavior.Log = null;
        ProjectT.Skill.Tests.OrderBehavior.Log.Clear();
        ProjectT.Skill.Tests.OrderBehavior.Created = 0;
    }

    static void CheckExecutes(int effectId, int expected, string when)
    {
        int actual = ProjectT.Skill.Tests.TestBehavior.ExecuteCount(effectId);
        Check(actual == expected, $"{when}: 효과 {effectId} 실행 횟수 기대 {expected}, 실제 {actual}");
    }

    static void CheckStat(Status status, StatId type, float expected, string when)
    {
        float actual = status.Get(type);
        Check(actual == expected, $"{when}: {type.Name} 기대 {expected}, 실제 {actual}");
    }

    static void CheckStatNear(Status status, StatId type, float expected, string when)
    {
        float actual = status.Get(type);
        Check(Math.Abs(actual - expected) < 1e-4f, $"{when}: {type.Name} 기대 {expected}, 실제 {actual}");
    }

    static T CaptureThrows<T>(Action body, string name) where T : Exception
    {
        try
        {
            body();
        }
        catch (T error)
        {
            return error;
        }
        throw new Exception(name + "이 거부되지 않았다");
    }

    static void CheckThrows<T>(Action body, string name) where T : Exception
    {
        try
        {
            body();
        }
        catch (T)
        {
            return;
        }
        throw new Exception(name + "이 거부되지 않았다");
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
