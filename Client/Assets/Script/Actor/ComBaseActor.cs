using ProjectT.Controller;
using ProjectT.Pivot;
using ProjectT.Pool;
using ProjectT.Skill;
using ProjectT.Stat;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    [RequireComponent(typeof(ComPivotAgent))]
    public abstract class ComBaseActor : MonoBehaviour, IPoolable, IAbilityOwner
    {
        private enum ActorState
        {
            None,
            Entering,
            // OnEnter가 실패한 스폰. 활성화·재스폰하지 않고 반납·파괴에서 정리한다.
            EnterFailed,
            Entered,
            Active,
            // 비활성화 콜백에서의 재활성화는 허용하고 반납만 막는다.
            Deactivating,
            Ending,
            // 비활성화·스폰 종료가 중간에 실패한 Actor는 상태가 오염되었을 수 있으므로 다시 활성화·스폰·반납하지 않고 파괴로 정리한다.
            // Faulted는 파괴가 스폰을 끝내고, EndFaulted는 이미 끝내다 실패했으므로 파괴에서 다시 정리하지 않는다.
            Faulted,
            EndFaulted,
            Destroying,
            Destroyed
        }

        private ActorState state;
        private bool initialized;
        // Controller/onReset 콜백 중 등록·해제되어도 순회 중인 스냅샷이 유효하도록 리스트를 수정하지 않고 교체한다(copy-on-write).
        private List<InputContext> inputContexts = new List<InputContext>();

        protected ComPivotAgent pivotAgent;
        public ComPivotAgent PivotAgent => pivotAgent;

        private SkillAgent skillAgent;
        public SkillAgent SkillAgent => skillAgent;

        private Status status;
        public Status Status => status;

        private int spawnSerial;
        public int SpawnSerial => spawnSerial;

        protected virtual void Awake()
        {
            InitializeInternal();
        }

        // 만료와 쿨다운이 반영된 뒤 OnUpdate가 판단하도록 먼저 틱한다. 틱 중 비활성화·반납되면 OnUpdate를 호출하지 않는다.
        // 틱·OnUpdate 실패는 Enable 실패처럼 격리하지 않고 Active로 둔다. 남은 태그·수정자는 반납·파괴의 해제가 정리하고, 보고는 Unity 실행 경계가 한다.
        // 다른 Actor에 건 효과의 실패도 이 경로로 올라오므로 여기서 격리하면 실패하지 않은 Actor가 격리된다.
        private void Update()
        {
            if (state == ActorState.Active && AutoTickSkills)
                skillAgent?.TickInternal(Time.deltaTime);

            if (state == ActorState.Active)
                OnUpdate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (state == ActorState.Active)
                OnLateUpdate(Time.deltaTime);
        }

        private void OnEnable()
        {
            ActivateInternal();
        }

        private void OnDisable()
        {
            DeactivateInternal();
        }

        private void OnDestroy()
        {
            try
            {
                EndSpawnInternal(ActorState.Destroying);
            }
            finally
            {
                state = ActorState.Destroyed;
                UnregisterAllInputContextsInternal();
            }
        }

        void IPoolable.OnGet()
        {
        }

        // 풀 반납은 스폰을 끝낸다. 스폰 전환, 비활성화, 스킬·효과 호출, Status 알림 중 반납되면 진행 중인 처리가 반납된 객체에 상태를 남기므로 거부한다.
        // 반납은 호출이 끝난 뒤(틱 뒤의 OnUpdate, 호출 반환 뒤) 한다.
        void IPoolable.OnReturn()
        {
            if (state == ActorState.Entering || state == ActorState.Ending || state == ActorState.Destroying)
                throw new InvalidOperationException("Actor cannot be returned during a spawn transition.");

            if (state == ActorState.Deactivating)
                throw new InvalidOperationException("Actor cannot be returned during deactivation.");

            if (skillAgent?.IsInCall == true || status?.IsInCall == true)
                throw new InvalidOperationException("Actor cannot be returned during a skill, effect or status call.");

            if (state == ActorState.Faulted || state == ActorState.EndFaulted)
                throw new InvalidOperationException("Faulted actor cannot be returned to the pool.");

            EndSpawnInternal(ActorState.Ending);
        }

        public void Spawn()
        {
            if (state == ActorState.Destroying || state == ActorState.Destroyed)
                throw new ObjectDisposedException(GetType().Name);

            if (state == ActorState.Faulted || state == ActorState.EndFaulted)
                throw new InvalidOperationException("Faulted actor cannot be spawned.");

            if (state != ActorState.None)
                throw new InvalidOperationException("Actor is already spawned or in a spawn transition.");

            // 파생 클래스가 base.Awake()를 호출하지 않았거나 OnInit이 실패했으면 여기서 다시 초기화한다. 활성 계층에 있으면 Awake는 이미 지났다.
            if (!initialized)
            {
                if (!gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Actor must be active before it is spawned.");

                InitializeInternal();
            }

            spawnSerial++;
            // 스킬 등록은 스폰 종료가 해제를 보장하는 상태부터 열어 OnEnter에서 등록할 수 있게 한다.
            state = ActorState.Entering;
            skillAgent?.OpenInternal();
            bool completed = false;
            try
            {
                OnEnter();
                completed = true;
            }
            finally
            {
                if (!completed && state == ActorState.Entering)
                    state = ActorState.EnterFailed;
            }

            if (state != ActorState.Entering)
                return;

            state = ActorState.Entered;
            if (isActiveAndEnabled)
                ActivateInternal();
        }

        protected void RegisterInputContext(InputContext context)
        {
            if (state == ActorState.Destroying || state == ActorState.Destroyed)
                throw new ObjectDisposedException(GetType().Name);

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (inputContexts.Contains(context))
                return;

            // 비활성 Actor가 가진 컨텍스트는 Controller에 등록되어 있지 않으므로 공유를 거부하지 못한다.
            if (context.Owner != null)
                throw new InvalidOperationException("Input context is already registered elsewhere.");

            // Controller 검증이 실패해도 Actor에 등록 흔적이 남지 않도록 Controller에 먼저 추가한다.
            if (state == ActorState.Active)
                Global.Input.AddContext(context);

            var updated = new List<InputContext>(inputContexts);
            updated.Add(context);
            inputContexts = updated;
        }

        protected void UnregisterInputContext(InputContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!inputContexts.Contains(context))
                return;

            var updated = new List<InputContext>(inputContexts);
            updated.Remove(context);
            inputContexts = updated;
            context.Owner?.RemoveContext(context);
        }

        // 턴제처럼 프레임 시간과 다른 단위로 효과·쿨다운을 진행하려면 false로 두고 TickSkills로 직접 진행한다.
        protected virtual bool AutoTickSkills => true;

        // 자동 틱과 같이 Active일 때만 진행해 비활성·반납된 Actor의 효과와 쿨다운이 흐르지 않게 한다.
        protected void TickSkills(float deltaTime)
        {
            if (state == ActorState.Active)
                skillAgent?.TickInternal(deltaTime);
        }

        protected SkillAgent AddSkillAgent()
        {
            if (skillAgent != null)
                throw new InvalidOperationException("SkillAgent is already added.");

            skillAgent = new SkillAgent(this);
            return skillAgent;
        }

        protected Status AddStatus()
        {
            if (status != null)
                throw new InvalidOperationException("Status is already added.");

            status = new Status();
            return status;
        }

        // 훅이 실패하면 다음 단계로 진행하지 않도록 훅이 모두 끝난 뒤 상태를 진행한다.
        private void InitializeInternal()
        {
            pivotAgent = GetComponent<ComPivotAgent>();
            // 실패한 OnInit이 추가한 에이전트와 Status가 Spawn의 재초기화에서 중복 추가로 막히지 않도록 버린다. Open 전이라 등록된 것이 없다.
            skillAgent = null;
            status = null;
            OnInit();
            initialized = true;
        }

        private void ActivateInternal()
        {
            if (state != ActorState.Entered && state != ActorState.Deactivating)
                return;

            state = ActorState.Active;
            AddInputContextsInternal();

            if (state == ActorState.Active)
                Enable();
        }

        private void DeactivateInternal()
        {
            if (state != ActorState.Active)
                return;

            bool completed = false;
            try
            {
                RunDeactivationInternal(ActorState.Deactivating);
                completed = true;
            }
            finally
            {
                // 콜백이 재활성화한 뒤 실패하면 다시 등록된 입력이 파괴 전까지 격리된 Actor에 전달되지 않도록 분리한다.
                if (!completed && state != ActorState.Destroying && state != ActorState.Destroyed)
                {
                    state = ActorState.Faulted;
                    DetachInputContextsInternal(inputContexts);
                }
                else if (completed && state == ActorState.Deactivating)
                    state = ActorState.Entered;
            }
        }

        // 스폰 종료의 비활성화는 Ending·Destroying 상태로 진행해 콜백에서의 재활성화를 막는다.
        private void RunDeactivationInternal(ActorState during)
        {
            state = during;
            var snapshot = inputContexts;
            DetachInputContextsInternal(snapshot);
            ResetInputContextsInternal(snapshot);

            // 파생 클래스가 Disable에서 구독을 해제하기 전에 취소 효과를 받도록 먼저 취소한다.
            if (ContinueDeactivationInternal(during))
                skillAgent?.CancelActiveSkillsInternal();

            if (ContinueDeactivationInternal(during))
                Disable();
        }

        // 콜백이 재활성화한 뒤 다시 비활성화하면 Entered로 끝나므로, 남은 단계를 이어받으며 반납 거부를 다시 건다.
        private bool ContinueDeactivationInternal(ActorState during)
        {
            if (state != during && !(during == ActorState.Deactivating && state == ActorState.Entered))
                return false;

            state = during;
            return true;
        }

        private void EndSpawnInternal(ActorState ending)
        {
            // 스폰하지 않은 동안 출처로 잡힌 효과도 반납·파괴 뒤에는 이 Actor의 Status를 쓰지 않도록 스폰 상태와 관계없이 바꾼다.
            spawnSerial++;
            var previous = state;
            if (previous == ActorState.None || previous == ActorState.EndFaulted || previous == ActorState.Destroyed)
                return;

            state = ending;
            bool completed = false;
            try
            {
                if (previous == ActorState.Active)
                    RunDeactivationInternal(ending);

                ResetInputContextsInternal(inputContexts);

                // 기반 클래스가 관리하는 모듈은 파생 훅보다 먼저 정리한다. 버프가 건 수정자는 Status를 비우기 전에 제거되어야 한다.
                skillAgent?.ReleaseInternal();
                status?.ReleaseInternal();
                OnRelease();
                completed = true;
            }
            finally
            {
                if (state == ActorState.Ending)
                    state = completed ? ActorState.None : ActorState.EndFaulted;
            }
        }

        private void AddInputContextsInternal()
        {
            var snapshot = inputContexts;
            if (snapshot.Count == 0)
                return;

            for (int i = 0; i < snapshot.Count; i++)
            {
                if (state != ActorState.Active)
                    return;

                if (inputContexts.Contains(snapshot[i]))
                    Global.Input.AddContext(snapshot[i]);
            }
        }

        private void UnregisterAllInputContextsInternal()
        {
            var previous = inputContexts;
            inputContexts = new List<InputContext>();
            DetachInputContextsInternal(previous);
        }

        // 종료 중 Controller가 먼저 해제되면 컨텍스트가 분리되어 있으므로 Global.Input을 조회하지 않고 등록된 Controller에서만 제거한다.
        private static void DetachInputContextsInternal(List<InputContext> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
                snapshot[i].Owner?.RemoveContextInternal(snapshot[i]);
        }

        // onReset 예외로 남은 컨텍스트가 비활성화된 Actor에 입력을 전달하지 않도록 모든 등록을 해제한 뒤 호출한다.
        private void ResetInputContextsInternal(List<InputContext> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                // onReset에서 Actor가 다시 활성화되면 추가 경로가 등록을 이어받으므로 리셋을 중단한다.
                if (state == ActorState.Active)
                    break;

                snapshot[i].ResetAllInternal();
            }
        }

        #region MonoEvents
        protected virtual void OnInit()
        {
        }

        protected virtual void OnEnter()
        {
        }

        protected virtual void OnUpdate(float dt)
        {
        }

        protected virtual void OnLateUpdate(float dt)
        {
        }

        protected virtual void Enable()
        {
        }

        protected virtual void Disable()
        {
        }

        protected virtual void OnRelease()
        {
        }
        #endregion
    }
}
