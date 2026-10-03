using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine.InputSystem;

namespace ProjectT
{
    public sealed class InputManager : ManagerBase
    {
        internal ProjectT.Controller.Controller Controller { get; private set; }

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            // 프로젝트 전역 에셋은 Input System이 소유하므로 복제하거나 파괴하지 않는다.
            var asset = InputSystem.actions;
            if (asset == null)
                throw new InvalidOperationException("Project-wide input actions are not assigned.");

            Controller = new ProjectT.Controller.Controller();
            Controller.Init(asset);
            Controller.SetInputAllowed(false);
            Controller.Enable();
            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            var controller = Controller;
            Controller = null;
            controller?.Release();
        }

        public void SetInputAllowed(bool allowed)
        {
            Controller.SetInputAllowed(allowed);
        }
    }
}
