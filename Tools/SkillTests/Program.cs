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

static partial class Program
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

    static SkillDefinition Skill(int id, SkillTag[] blocked = null, SkillTag[] cancelledBy = null, float coolTime = 0f, StatId cooldownStat = default, EffectDefinition[] effects = null, SkillCost[] costs = null, Func<BaseSkillAction> factory = null)
    {
        return new SkillDefinition(id, coolTime, factory ?? (() => new ProjectT.Skill.Tests.TestSkillAction()), blocked ?? Array.Empty<SkillTag>(), cancelledBy ?? Array.Empty<SkillTag>(), cooldownStat, effects ?? Array.Empty<EffectDefinition>(), costs ?? Array.Empty<SkillCost>());
    }

    static EffectDefinition TestEffect(int id, float duration, float period = 0f) => Effect(id, EffectDurationPolicy.Duration, duration, period, () => new ProjectT.Skill.Tests.TestBehavior());

    static EffectDefinition Effect(int id, EffectDurationPolicy policy, float duration, float period, Func<EffectBehavior> factory, IReadOnlyList<EffectModifier> modifiers = null, IReadOnlyList<SkillTag> tags = null, float power = 0f, DamageExecution execution = null, EffectStackPolicy stack = EffectStackPolicy.Refresh, int maxStack = 1, StatId? damageResource = null)
    {
        return new EffectDefinition(id, policy, duration, period, modifiers ?? Array.Empty<EffectModifier>(), tags ?? Array.Empty<SkillTag>(), power, execution, damageResource ?? Hp, stack, maxStack, factory);
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
