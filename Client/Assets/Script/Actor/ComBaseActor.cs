using ProjectT.Pivot;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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
                List<Exception> errors = null;
                ErrorCollector.Run(ref errors, current, item => DeactivateActorInternal());
                ErrorCollector.Run(ref errors, current, item => item.Release());
                currentActor = null;
                initializedActor = null;
                enteredActor = null;
                activeActor = null;
                ThrowIfAnyInternal(errors);
            }
            finally
            {
                changingActor = wasChanging;
            }
        }

        // 단일 오류는 원래 타입으로 전달한다. 교체·파괴 중 거부 예외를 호출자가 그대로 받아야 한다.
        private static void ThrowIfAnyInternal(List<Exception> errors)
        {
            if (errors == null)
                return;

            if (errors.Count == 1)
                ExceptionDispatchInfo.Capture(errors[0]).Throw();

            throw new AggregateException(errors);
        }

        private bool IsCurrentInternal(BaseActor current)
        {
            return current != null && ReferenceEquals(actor, current) && !current.IsReleased;
        }

        private bool IsActiveInternal(BaseActor current)
        {
            return IsCurrentInternal(current) && ReferenceEquals(activeActor, current);
        }

        private void ReleaseFailedActorInternal(BaseActor current)
        {
            if (ReferenceEquals(actor, current))
            {
                currentActor = null;
                initializedActor = null;
                enteredActor = null;
            }

            current.Release();
        }

        private void InitializeActorInternal()
        {
            var current = actor;
            if (!awoken || !IsCurrentInternal(current) || ReferenceEquals(initializedActor, current))
                return;

            try
            {
                OnInit();
                if (IsCurrentInternal(current))
                {
                    current.OnInit();
                    if (IsCurrentInternal(current))
                        initializedActor = current;
                }
            }
            catch (Exception error)
            {
                List<Exception> errors = null;
                ErrorCollector.Run(ref errors, current, ReleaseFailedActorInternal);
                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }

                throw;
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

            try
            {
                OnEnter();
                if (IsCurrentInternal(current))
                {
                    current.OnEnter();
                    if (IsCurrentInternal(current))
                        enteredActor = current;
                }
            }
            catch (Exception error)
            {
                List<Exception> errors = null;
                ErrorCollector.Run(ref errors, current, ReleaseFailedActorInternal);
                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }

                throw;
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
            try
            {
                current.SetEnabledInternal(true);

                if (IsActiveInternal(current))
                {
                    Enable();
                    if (IsActiveInternal(current))
                        current.Enable();
                }
            }
            catch (Exception error)
            {
                List<Exception> errors = null;
                if (ReferenceEquals(activeActor, current))
                {
                    activeActor = null;
                    ErrorCollector.Run(ref errors, current, item => item.SetEnabledInternal(false));
                }

                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }

                throw;
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
