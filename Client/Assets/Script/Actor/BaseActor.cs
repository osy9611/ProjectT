using System;
using System.Collections.Generic;
using ProjectT.Controller;
using ProjectT.Scene;

namespace ProjectT
{
    public abstract class BaseActor
    {
        private SceneBase scene;
        private List<InputContext> inputContexts = new List<InputContext>();
        private bool enabled;
        private bool released;
        private ComBaseActor owner;

        public SceneBase Scene => scene;
        internal bool IsReleased => released;

        internal void ValidateOwnerInternal(ComBaseActor owner)
        {
            if (released)
                throw new ObjectDisposedException(GetType().Name);

            if (this.owner != null && !ReferenceEquals(this.owner, owner))
                throw new InvalidOperationException("Actor is already owned by another component.");
        }

        internal void AttachInternal(ComBaseActor owner)
        {
            ValidateOwnerInternal(owner);
            this.owner = owner;
        }

        public void BindScene(SceneBase scene)
        {
            if (released)
                throw new ObjectDisposedException(GetType().Name);

            if (scene == null)
                throw new ArgumentNullException(nameof(scene));

            if (ReferenceEquals(this.scene, scene))
                return;

            if (this.scene != null)
                throw new InvalidOperationException("Actor is already bound to a scene.");

            if (scene.State == SceneState.Stopped || scene.LifetimeToken.IsCancellationRequested)
                throw new InvalidOperationException("Scene is stopping.");

            this.scene = scene;
            scene.Stopped += OnSceneStoppedInternal;
        }

        protected void RegisterInputContext(InputContext context)
        {
            if (released)
                throw new ObjectDisposedException(GetType().Name);

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Actor != null && !ReferenceEquals(context.Actor, this))
                throw new InvalidOperationException("Input context is already registered to another actor.");

            if (inputContexts.Contains(context))
                return;

            // Controller 검증이 실패해도 Actor에 등록 흔적이 남지 않도록 Controller에 먼저 추가한다.
            if (enabled)
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

            RemoveFromListInternal(context);
            context.Actor = null;
            context.Owner?.RemoveContext(context);
        }

        private void RemoveFromListInternal(InputContext context)
        {
            var updated = new List<InputContext>(inputContexts);
            updated.Remove(context);
            inputContexts = updated;
        }

        internal void SetEnabledInternal(bool enabled)
        {
            if (released || this.enabled == enabled)
                return;

            this.enabled = enabled;
            if (enabled)
                AddInputContextsInternal();
            else
                RemoveInputContextsInternal(inputContexts);
        }

        private void AddInputContextsInternal()
        {
            var snapshot = inputContexts;
            if (snapshot.Count == 0)
                return;

            var controller = Global.Input.Controller;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (released || !enabled)
                    return;

                if (inputContexts.Contains(snapshot[i]))
                    controller.AddContext(snapshot[i]);
            }
        }

        // 종료 중 Controller가 먼저 해제되면 컨텍스트가 분리되어 있으므로 Global.Input을 조회하지 않고 등록된 Controller에서만 제거한다.
        private void RemoveInputContextsInternal(List<InputContext> snapshot)
        {
            List<Exception> errors = null;
            for (int i = 0; i < snapshot.Count; i++)
            {
                // onReset에서 Actor가 다시 활성화되면 추가 경로가 등록을 이어받으므로 제거를 중단한다.
                if (!released && enabled)
                    break;

                var controller = snapshot[i].Owner;
                if (controller != null)
                    ErrorCollector.Run(ref errors, snapshot[i], controller.RemoveContext);
            }

            ErrorCollector.ThrowIfAny(errors);
        }

        private void OnSceneStoppedInternal()
        {
            if (owner != null)
                owner.ReleaseOwnedActorInternal(this);
            else
                Release();
        }

        public void Release()
        {
            if (released)
                return;

            released = true;
            enabled = false;
            owner = null;

            var previousScene = scene;
            scene = null;
            if (previousScene != null)
                previousScene.Stopped -= OnSceneStoppedInternal;

            var previousContexts = inputContexts;
            inputContexts = new List<InputContext>();
            foreach (var context in previousContexts)
                context.Actor = null;

            RemoveInputContextsInternal(previousContexts);
        }

        public virtual void OnInit() { }

        public abstract void OnEnter();

        public abstract void OnUpdate(float dt);

        public virtual void OnLateUpdate(float dt) { }

        public virtual void Enable() { }

        public virtual void Disable() { }
    }
}
