using ProjectT.Controller;
using ProjectT.Pivot;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    [RequireComponent(typeof(ComPivotAgent))]
    public abstract class ComBaseActor : MonoBehaviour
    {
        private BaseActor currentActor;
        protected BaseActor actor { get => currentActor; set => Actor = value; }
        private BaseActor initializedActor;
        private BaseActor enteredActor;
        private BaseActor activeActor;
        private bool awoken;
        private bool started;
        private bool changingActor;
        private bool destroying;
        public BaseActor Actor
        {
            get => actor;
            set
            {
                if (ReferenceEquals(actor, value))
                    return;

                if (changingActor || destroying)
                    throw new InvalidOperationException("Actor cannot be changed during replacement or destruction.");

                value?.ValidateOwnerInternal(this);
                changingActor = true;
                try
                {
                    var previous = actor;
                    if (previous != null)
                        ReleaseOwnedActorInternal(previous);

                    if (destroying)
                        throw new InvalidOperationException("Actor cannot be attached during destruction.");

                    value?.AttachInternal(this);
                    currentActor = value;
                    initializedActor = null;
                    enteredActor = null;

                    if (awoken)
                        InitializeActorInternal();

                    if (started)
                        EnterActorInternal();
                }
                finally
                {
                    changingActor = false;
                }
            }
        }

        protected ComPivotAgent pivotAgent;
        public ComPivotAgent PivotAgent => pivotAgent;

        protected virtual void Awake()
        {
            pivotAgent = GetComponent<ComPivotAgent>();
            awoken = true;
            InitializeActorInternal();
        }

        private void Start()
        {
            if (!awoken)
            {
                pivotAgent = GetComponent<ComPivotAgent>();
                awoken = true;
            }

            started = true;
            EnterActorInternal();
        }

        private void Update()
        {
            var current = actor;
            if (!IsActiveInternal(current))
                return;

            OnUpdate(Time.deltaTime);
            if (IsActiveInternal(current))
                current.OnUpdate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            var current = actor;
            if (!IsActiveInternal(current))
                return;

            OnLateUpdate(Time.deltaTime);
            if (IsActiveInternal(current))
                current.OnLateUpdate(Time.deltaTime);
        }

        private void OnEnable()
        {
            if (started)
                ActivateActorInternal();
        }

        private void OnDisable()
        {
            DeactivateActorInternal();
        }

        private void OnDestroy()
        {
            destroying = true;
            ReleaseOwnedActorInternal(actor);
        }

        internal void ReleaseOwnedActorInternal(BaseActor current)
        {
            if (current == null || !ReferenceEquals(actor, current))
                return;

            bool wasChanging = changingActor;
            changingActor = true;
            try
            {
                // 비활성화 훅은 현재 Actor일 때만 호출되므로 훅 이후에 소유를 해제한다.
                List<InputContext> contexts;
                try
                {
                    DeactivateActorInternal();
                }
                finally
                {
                    currentActor = null;
                    initializedActor = null;
                    enteredActor = null;
                    activeActor = null;
                    contexts = current.ReleaseStateInternal();
                }

                current.ResetInputContextsInternal(contexts);
            }
            finally
            {
                changingActor = wasChanging;
            }
        }

        private bool IsCurrentInternal(BaseActor current)
        {
            return current != null && ReferenceEquals(actor, current) && !current.IsReleased;
        }

        private bool IsActiveInternal(BaseActor current)
        {
            return IsCurrentInternal(current) && ReferenceEquals(activeActor, current);
        }

        private void InitializeActorInternal()
        {
            var current = actor;
            if (!awoken || !IsCurrentInternal(current) || ReferenceEquals(initializedActor, current))
                return;

            OnInit();
            if (IsCurrentInternal(current))
            {
                current.OnInit();
                if (IsCurrentInternal(current))
                    initializedActor = current;
            }
        }

        private void EnterActorInternal()
        {
            if (!started)
                return;

            InitializeActorInternal();
            var current = actor;
            if (!IsCurrentInternal(current) || !ReferenceEquals(initializedActor, current) || ReferenceEquals(enteredActor, current))
                return;

            OnEnter();
            if (IsCurrentInternal(current))
            {
                current.OnEnter();
                if (IsCurrentInternal(current))
                    enteredActor = current;
            }

            if (IsCurrentInternal(current) && isActiveAndEnabled)
                ActivateActorInternal();
        }

        private void ActivateActorInternal()
        {
            var current = actor;
            if (!IsCurrentInternal(current) || !ReferenceEquals(enteredActor, current) || ReferenceEquals(activeActor, current))
                return;

            activeActor = current;
            current.SetEnabledInternal(true);

            if (IsActiveInternal(current))
            {
                Enable();
                if (IsActiveInternal(current))
                    current.Enable();
            }
        }

        private void DeactivateActorInternal()
        {
            var current = activeActor;
            if (current == null)
                return;

            activeActor = null;
            current.SetEnabledInternal(false);

            if (IsCurrentInternal(current) && activeActor == null)
            {
                Disable();
                if (IsCurrentInternal(current) && activeActor == null)
                    current.Disable();
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
        #endregion
    }
}
