using ProjectT.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectT.Sample
{
    public class SampleActor : ComBaseActor
    {
        private const float MoveSpeed = 3f;

        private static int s_nextId;

        private InputContext context;
        public int Id { get; private set; }
        public Vector2 MoveDirection { get; private set; }
        public int JumpCount { get; private set; }

        protected override void OnInit()
        {
            // 여러 Actor가 같은 입력을 함께 받도록 consume을 끈다. 상위 우선순위 컨텍스트가 consume하면 이 컨텍스트는 받지 못한다.
            context ??= new InputContext($"SampleActor{GetInstanceID()}", 0)
                .Bind("Player/Move", OnMoveInternal, eInputEvent.Performed | eInputEvent.Cancel, consume: false, onReset: () => MoveDirection = Vector2.zero)
                .Bind("Player/Jump", OnJumpInternal, eInputEvent.Performed, consume: false);

            RegisterInputContext(context);
            FeatureSampleRunner.Write($"{name} OnInit");
        }

        protected override void OnEnter()
        {
            Id = ++s_nextId;
            FeatureSampleRunner.Write($"Actor{Id} OnEnter");
        }

        protected override void OnUpdate(float dt)
        {
            transform.position += new Vector3(MoveDirection.x, 0f, MoveDirection.y) * (MoveSpeed * dt);
        }

        protected override void Enable()
        {
            FeatureSampleRunner.Write($"Actor{Id} Enable");
        }

        protected override void Disable()
        {
            FeatureSampleRunner.Write($"Actor{Id} Disable");
        }

        protected override void OnRelease()
        {
            FeatureSampleRunner.Write($"Actor{Id} OnRelease");
            MoveDirection = Vector2.zero;
            JumpCount = 0;
        }

        public void Move(Vector2 direction)
        {
            MoveDirection = direction;
        }

        public void Jump()
        {
            JumpCount++;
            FeatureSampleRunner.Write($"Actor{Id} Jump {JumpCount}");
        }

        private void OnMoveInternal(InputAction.CallbackContext callback)
        {
            Move(callback.ReadValue<Vector2>());
        }

        private void OnJumpInternal(InputAction.CallbackContext callback)
        {
            Jump();
        }
    }
}
