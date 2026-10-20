using ProjectT.Controller;
using ProjectT.Pivot;
using ProjectT.Skill;
using ProjectT.SkillAdapter;
using ProjectT.Stat;
using ProjectT.UGUI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectT.Sample
{
    public class SampleActor : ComBaseActor
    {
        private const float MoveSpeed = 3f;
        public const int SkillId = 201;
        public const float BaseHp = 100f;
        public const float BaseAtk = 10f;
        private const float HudHeight = 1.3f;

        private static int s_nextId;

        private InputContext context;
        private PivotInfo hudPivot;
        private SampleHpHud hud;
        internal InputContext Context => context;
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
            hudPivot ??= CreateHudPivotInternal();
            AddSkillAgent();
            AddStatus().DefineResource(SkillDefinitions.HpStat, SkillDefinitions.MaxHpStat);
            FeatureSampleRunner.Write($"{name} OnInit");
        }

        protected override void OnEnter()
        {
            Id = ++s_nextId;
            // 반납 때 Status 값과 등록된 스킬이 비워지므로 스폰마다 다시 채운다.
            Status.SetBase(SkillDefinitions.MaxHpStat, BaseHp);
            Status.SetBase(SkillDefinitions.AtkStat, BaseAtk);
            Status.SetResource(SkillDefinitions.HpStat, BaseHp);
            SkillAgent.GiveSkill(SkillDefinitions.GetSkill(SkillId));
            hud = Global.UI.GetHud<SampleHpHud>(HudDefine.eHudType.SampleHpBar, hudPivot);
            UpdateHpBarInternal();
            FeatureSampleRunner.Write($"Actor{Id} OnEnter");
        }

        protected override void OnUpdate(float dt)
        {
            transform.position += new Vector3(MoveDirection.x, 0f, MoveDirection.y) * (MoveSpeed * dt);
        }

        protected override void Enable()
        {
            SkillAgent.OnSkillActivated += OnSkillActivatedInternal;
            SkillAgent.OnSkillEnded += OnSkillEndedInternal;
            SkillAgent.OnTagChanged += OnTagChangedInternal;
            SkillAgent.OnEffectApplied += OnEffectAppliedInternal;
            SkillAgent.OnEffectRemoved += OnEffectRemovedInternal;
            Status.OnResourceChanged += OnResourceChangedInternal;
            FeatureSampleRunner.Write($"Actor{Id} Enable");
        }

        protected override void Disable()
        {
            SkillAgent.OnSkillActivated -= OnSkillActivatedInternal;
            SkillAgent.OnSkillEnded -= OnSkillEndedInternal;
            SkillAgent.OnTagChanged -= OnTagChangedInternal;
            SkillAgent.OnEffectApplied -= OnEffectAppliedInternal;
            SkillAgent.OnEffectRemoved -= OnEffectRemovedInternal;
            Status.OnResourceChanged -= OnResourceChangedInternal;
            FeatureSampleRunner.Write($"Actor{Id} Disable");
        }

        protected override void OnRelease()
        {
            FeatureSampleRunner.Write($"Actor{Id} OnRelease");
            // OnEnter가 HUD를 대여하기 전에 실패해도 반납 경로가 이 훅을 호출한다.
            if (hud != null)
                hud.Release();

            hud = null;
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

        public void UseSkill()
        {
            SkillAgent.ActivateSkill(SkillId);
        }

        public EffectHandle ApplyEffect(int effectId)
        {
            return SkillAgent.ApplyEffect(SkillDefinitions.GetEffect(effectId), this);
        }

        public void Damage(float amount)
        {
            Status.SetResource(SkillDefinitions.HpStat, Status.GetResource(SkillDefinitions.HpStat) - amount);
        }

        private PivotInfo CreateHudPivotInternal()
        {
            var pivot = new GameObject("HudPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, HudHeight, 0f);
            return new PivotInfo(PivotType.UI, 0, pivot, false, Vector3.zero, Vector3.zero, Vector3.one);
        }

        private void UpdateHpBarInternal()
        {
            hud.SetRatio(Status.GetResource(SkillDefinitions.HpStat) / Status.Get(SkillDefinitions.MaxHpStat));
        }

        private void OnResourceChangedInternal(StatId resource, float previous, float current)
        {
            if (resource == SkillDefinitions.HpStat)
                UpdateHpBarInternal();
        }

        private void OnSkillActivatedInternal(SkillDefinition definition)
        {
            FeatureSampleRunner.Write($"Actor{Id} Skill {definition.Id} Activated");
        }

        private void OnSkillEndedInternal(SkillDefinition definition, SkillEndReason reason)
        {
            FeatureSampleRunner.Write($"Actor{Id} Skill {definition.Id} Ended {reason}");
        }

        private void OnTagChangedInternal(SkillTag tag, bool added)
        {
            FeatureSampleRunner.Write($"Actor{Id} Tag {tag.Name} {(added ? "Added" : "Removed")}");
        }

        private void OnEffectAppliedInternal(EffectHandle handle, EffectDefinition definition)
        {
            FeatureSampleRunner.Write($"Actor{Id} Effect {definition.Id} Applied");
        }

        private void OnEffectRemovedInternal(EffectHandle handle, EffectDefinition definition, EffectRemoveReason reason)
        {
            FeatureSampleRunner.Write($"Actor{Id} Effect {definition.Id} Removed {reason}");
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
