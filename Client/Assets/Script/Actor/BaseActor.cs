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
            {
                var snapshot = inputContexts;
                DetachInputContextsInternal(snapshot);
                ResetInputContextsInternal(snapshot);
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
                if (released || !enabled)
                    return;

                if (inputContexts.Contains(snapshot[i]))
                    controller.AddContext(snapshot[i]);
            }
        }

        // 종료 중 Controller가 먼저 해제되면 컨텍스트가 분리되어 있으므로 Global.Input을 조회하지 않고 등록된 Controller에서만 제거한다.
        private static void DetachInputContextsInternal(List<InputContext> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
                snapshot[i].Owner?.RemoveContextInternal(snapshot[i]);
        }

        // onReset 예외로 남은 컨텍스트가 비활성·해제된 Actor에 입력을 전달하지 않도록 모든 등록을 해제한 뒤 호출한다.
        internal void ResetInputContextsInternal(List<InputContext> snapshot)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                // onReset에서 Actor가 다시 활성화되면 추가 경로가 등록을 이어받으므로 리셋을 중단한다.
                if (!released && enabled)
                    break;

                snapshot[i].ResetAllInternal();
            }
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

            ResetInputContextsInternal(ReleaseStateInternal());
        }

        // 소유 컴포넌트의 비활성화가 실패해도 해제 상태를 확정할 수 있도록 onReset 없이 상태만 해제하고 분리한 컨텍스트를 반환한다.
        internal List<InputContext> ReleaseStateInternal()
        {
            if (released)
                return new List<InputContext>();

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

            DetachInputContextsInternal(previousContexts);
            return previousContexts;
        }

        public virtual void OnInit() { }

        public abstract void OnEnter();

        public abstract void OnUpdate(float dt);

        public virtual void OnLateUpdate(float dt) { }

        public virtual void Enable() { }

        public virtual void Disable() { }
    }
}
