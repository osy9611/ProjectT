using ProjectT.Pivot;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    [RequireComponent(typeof(ComPivotAgent))]
    public abstract class ComBaseActor : MonoBehaviour
    {
        protected BaseActor actor;
        public BaseActor Actor { get => actor; set => actor = value; }

        protected ComPivotAgent pivotAgent;
        public ComPivotAgent PivotAgent => pivotAgent;

        protected virtual void Awake()
        {
            pivotAgent = GetComponent<ComPivotAgent>();
        }

        // Start is called before the first frame update
        private void Start()
        {
            OnEnter();
        }

        private void Update()
        {
            OnUpdate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            OnLateUpdate(Time.deltaTime);
        }

        private void OnEnable()
        {
            Enable();
        }

        private void OnDisable()
        {
            Disable();
        }

        #region MonoEvents
        protected virtual void OnInit()
        {
            if (actor != null)
                actor.OnInit();
        }

        protected virtual void OnEnter()
        {
            if (actor != null)
                actor.OnEnter();
        }

        protected virtual void OnUpdate(float dt)
        {
            if (actor != null)
                OnUpdate(dt);
        }

        protected virtual void OnLateUpdate(float dt) 
        {
            if (actor != null)
                OnLateUpdate(dt);
        }
        protected virtual void Enable() 
        {
            if (actor != null)
                Enable();
        }

        protected virtual void Disable() 
        {
            if (actor != null)
                Disable();
        }
        #endregion
    }
}