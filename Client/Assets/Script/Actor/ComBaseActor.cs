using ProjectT.Controller;
using ProjectT.Pivot;
using ProjectT.Pool;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    [RequireComponent(typeof(ComPivotAgent))]
    public abstract class ComBaseActor : MonoBehaviour, IPoolable
    {
        private enum SpawnState
        {
            None,
            Entering,
            Entered,
            Active
        }

        private SpawnState spawnState;
        private bool initialized;
        private bool destroying;
        private bool spawnTransitioning;
        // Controller/onReset 콜백 중 등록·해제되어도 순회 중인 스냅샷이 유효하도록 리스트를 수정하지 않고 교체한다(copy-on-write).
        private List<InputContext> inputContexts = new List<InputContext>();

        protected ComPivotAgent pivotAgent;
        public ComPivotAgent PivotAgent => pivotAgent;

        protected virtual void Awake()
        {
            InitializeInternal();
        }

        private void Update()
        {
            if (spawnState == SpawnState.Active)
                OnUpdate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (spawnState == SpawnState.Active)
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
            destroying = true;
            try
            {
                EndSpawnInternal();
            }
            finally
            {
                UnregisterAllInputContextsInternal();
            }
        }

        void IPoolable.OnGet()
        {
        }

        // 풀 반납은 스폰을 끝낸다. 스폰 전환 중 반납되면 진행 중인 전환이 반납된 객체에 상태를 남기므로 거부한다.
        void IPoolable.OnReturn()
        {
            if (spawnTransitioning)
                throw new InvalidOperationException("Actor cannot be returned during a spawn transition.");

            EndSpawnInternal();
        }

        public void Spawn()
        {
            if (destroying)
                throw new ObjectDisposedException(GetType().Name);

            if (spawnTransitioning || spawnState != SpawnState.None)
                throw new InvalidOperationException("Actor is already spawned or in a spawn transition.");

            // 파생 클래스가 base.Awake()를 호출하지 않았거나 OnInit이 실패했으면 여기서 다시 초기화한다. 활성 계층에 있으면 Awake는 이미 지났다.
            if (!initialized)
            {
                if (!gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Actor must be active before it is spawned.");

                InitializeInternal();
            }

            // OnEnter가 실패하면 Entering에 남아 이후 활성화와 재시도를 막고, 반납·파괴에서 정리된다.
            spawnState = SpawnState.Entering;
            spawnTransitioning = true;
            try
            {
                OnEnter();
            }
            finally
            {
                spawnTransitioning = false;
            }

            if (spawnState != SpawnState.Entering)
                return;

            spawnState = SpawnState.Entered;
            if (isActiveAndEnabled)
                ActivateInternal();
        }

        protected void RegisterInputContext(InputContext context)
        {
            if (destroying)
                throw new ObjectDisposedException(GetType().Name);

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Actor != null && !ReferenceEquals(context.Actor, this))
                throw new InvalidOperationException("Input context is already registered to another actor.");

            if (inputContexts.Contains(context))
                return;

            // Controller 검증이 실패해도 Actor에 등록 흔적이 남지 않도록 Controller에 먼저 추가한다.
            if (spawnState == SpawnState.Active)
                Global.Input.Controller.AddContext(context);

            context.Actor = this;
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
            context.Actor = null;
            context.Owner?.RemoveContext(context);
        }

        // 훅이 실패하면 다음 단계로 진행하지 않도록 훅이 모두 끝난 뒤 상태를 진행한다.
        private void InitializeInternal()
        {
            pivotAgent = GetComponent<ComPivotAgent>();
            OnInit();
            initialized = true;
        }

        private void ActivateInternal()
        {
            if (spawnTransitioning || spawnState != SpawnState.Entered)
                return;

            spawnState = SpawnState.Active;
            AddInputContextsInternal();

            if (spawnState == SpawnState.Active)
                Enable();
        }

        private void DeactivateInternal()
        {
            if (spawnState != SpawnState.Active)
                return;

            spawnState = SpawnState.Entered;
            var snapshot = inputContexts;
            DetachInputContextsInternal(snapshot);

            // 리셋이 실패해도 Enable의 구독이 다음 스폰으로 넘어가지 않도록 Disable은 실행한다.
            try
            {
                ResetInputContextsInternal(snapshot);
            }
            finally
            {
                if (spawnState == SpawnState.Entered)
                    Disable();
            }
        }

        private void EndSpawnInternal()
        {
            if (spawnState == SpawnState.None)
                return;

            // 입력 리셋 콜백이 반납 중인 Actor를 다시 활성화하거나 스폰하지 않도록 리셋 전에 전환 잠금을 건다.
            bool wasTransitioning = spawnTransitioning;
            spawnTransitioning = true;
            try
            {
                bool deactivated = false;
                try
                {
                    DeactivateInternal();
                    deactivated = true;
                }
                finally
                {
                    spawnState = SpawnState.None;

                    // 스폰 종료는 다시 호출되지 않으므로 앞선 실패와 관계없이 OnRelease를 호출한다. 비활성화 중 입력 리셋이 이미 실패했을 수 있어 그 경우 최종 리셋은 재시도하지 않는다.
                    try
                    {
                        if (deactivated)
                            ResetInputContextsInternal(inputContexts);
                    }
                    finally
                    {
                        OnRelease();
                    }
                }
            }
            finally
            {
                spawnTransitioning = wasTransitioning;
            }
        }

        private void AddInputContextsInternal()
        {
            var snapshot = inputContexts;
            if (snapshot.Count == 0)
                return;

            var controller = Global.Input.Controller;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (spawnState != SpawnState.Active)
                    return;

                if (inputContexts.Contains(snapshot[i]))
                    controller.AddContext(snapshot[i]);
            }
        }

        private void UnregisterAllInputContextsInternal()
        {
            var previous = inputContexts;
            inputContexts = new List<InputContext>();
            foreach (var context in previous)
                context.Actor = null;

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
                if (spawnState == SpawnState.Active)
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
