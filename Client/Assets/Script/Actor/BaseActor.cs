using System;
using System.Collections.Generic;
using ProjectT.Scene;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectT
{
    public abstract class BaseActor
    {
        private SceneBase scene;
        private ProjectT.Controller.Controller controller;
        private InputActionAsset ownedAsset;
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
            scene.InputAllowedChanged += OnInputAllowedChangedInternal;
            scene.Stopped += OnSceneStoppedInternal;
        }

        protected T CreateController<T>(InputActionAsset asset) where T : ProjectT.Controller.Controller, new()
        {
            if (released)
                throw new ObjectDisposedException(GetType().Name);

            if (scene == null || scene.State == SceneState.Created || scene.State == SceneState.Stopped)
                throw new InvalidOperationException("Actor must be bound to an entering or active scene.");

            scene.LifetimeToken.ThrowIfCancellationRequested();

            if (controller != null)
                throw new InvalidOperationException("Actor already owns a controller.");

            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            var clone = UnityEngine.Object.Instantiate(asset);
            T created = null;

            try
            {
                created = new T();
                controller = created;
                ownedAsset = clone;
                created.SetInputAllowed(false);
                created.Init(clone);

                if (released || !ReferenceEquals(controller, created))
                    throw new InvalidOperationException("Actor was released during controller initialization.");

                if (enabled)
                {
                    created.SetInputAllowed(scene.IsInputAllowed);
                    created.Enable();
                }

                return created;
            }
            catch (Exception error)
            {
                List<Exception> errors = null;
                if (created == null)
                    ErrorCollector.Run(ref errors, clone, UnityEngine.Object.Destroy);
                else if (ReferenceEquals(controller, created))
                {
                    controller = null;
                    ownedAsset = null;
                    ErrorCollector.Run(ref errors, created, item => item.Release());
                    ErrorCollector.Run(ref errors, clone, UnityEngine.Object.Destroy);
                }

                if (errors != null)
                {
                    errors.Insert(0, error);
                    throw new AggregateException(errors);
                }

                throw;
            }
        }

        internal void SetEnabledInternal(bool enabled)
        {
            if (released || this.enabled == enabled)
                return;

            this.enabled = enabled;
            var current = controller;
            if (current == null)
                return;

            current.SetInputAllowed(enabled && scene.IsInputAllowed);
            if (released || this.enabled != enabled || !ReferenceEquals(controller, current))
                return;

            if (enabled)
                current.Enable();
            else
                current.Disable();
        }

        private void OnInputAllowedChangedInternal()
        {
            if (!released && controller != null)
                controller.SetInputAllowed(enabled && scene.IsInputAllowed);
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
            {
                previousScene.InputAllowedChanged -= OnInputAllowedChangedInternal;
                previousScene.Stopped -= OnSceneStoppedInternal;
            }

            var previousController = controller;
            var previousAsset = ownedAsset;
            controller = null;
            ownedAsset = null;

            try
            {
                previousController?.Release();
            }
            finally
            {
                if (previousAsset != null)
                    UnityEngine.Object.Destroy(previousAsset);
            }
        }

        public virtual void OnInit() { }

        public abstract void OnEnter();

        public abstract void OnUpdate(float dt);

        public virtual void OnLateUpdate(float dt) { }

        public virtual void Enable() { }

        public virtual void Disable() { }
    }
}
