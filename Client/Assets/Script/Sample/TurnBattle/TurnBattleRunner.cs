using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectT.Sample.TurnBattle
{
    // Skill System을 실제 Actor 생명주기(풀 대여·반납, 수동 틱)로 확인하는 2:2 턴제 전투 샘플이다.
    public class TurnBattleRunner : MonoBehaviour
    {
        private const int MaxLogLines = 18;
        private const float AutoInterval = 0.6f;

        private readonly List<TurnUnit> units = new List<TurnUnit>();
        private readonly List<string> log = new List<string>();
        private GameObject template;
        private int turnIndex;
        private bool ready;
        private bool autoBattle;
        private float nextAutoTime;
        private TurnUnit selectedTarget;
        // OnGUI의 Layout·Repaint 사이에 표시 항목이 바뀌지 않도록 버튼 입력은 Update에서 실행한다.
        private System.Action pendingAction;
        private Vector2 scroll;

        public IReadOnlyList<TurnUnit> Units => units;
        public IReadOnlyList<string> Log => log;
        public int Round { get; private set; }
        public TurnUnit Current => turnIndex < units.Count ? units[turnIndex] : null;
        public bool IsOver => !units.Exists(unit => unit.Alive && unit.Spec.IsPlayer) || !units.Exists(unit => unit.Alive && !unit.Spec.IsPlayer);
        public bool IsReady => ready;

        private void Start()
        {
            InitializeInternalAsync().Forget(Global.LogException);
        }

        private async UniTask InitializeInternalAsync()
        {
            var token = this.GetCancellationTokenOnDestroy();
            if (await Global.WhenReadyAsync(token).SuppressCancellationThrow())
                return;

            // 비활성 템플릿에 붙여 두면 풀이 만든 복제본이 대여될 때 처음 Awake된다.
            template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "TurnUnit";
            template.SetActive(false);
            template.transform.SetParent(transform, false);
            template.AddComponent<TurnUnit>();

            StartBattle();
            ready = true;
        }

        private void Update()
        {
            foreach (var unit in units)
            {
                if (unit.Alive)
                    unit.GetComponent<Renderer>().material.color = unit.Stunned ? Color.yellow : unit.Spec.IsPlayer ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.35f, 0.3f);
            }

            var action = pendingAction;
            pendingAction = null;
            action?.Invoke();

            if (ready && autoBattle && !IsOver && Time.time >= nextAutoTime)
            {
                nextAutoTime = Time.time + AutoInterval;
                PlayAITurn();
            }
        }

        public void StartBattle()
        {
            ClearUnits();
            SpawnUnit(TurnDefs.Warrior, new Vector3(-3f, 0.5f, 1.5f));
            SpawnUnit(TurnDefs.Mage, new Vector3(-3f, 0.5f, -1.5f));
            SpawnUnit(TurnDefs.Orc, new Vector3(3f, 0.5f, 1.5f));
            SpawnUnit(TurnDefs.Goblin, new Vector3(3f, 0.5f, -1.5f));
            Round = 1;
            turnIndex = 0;
            autoBattle = false;
            Write("Battle start");
        }

        public void ClearUnits()
        {
            foreach (var unit in units)
            {
                if (unit.Alive)
                    Global.Pool.Return(unit.gameObject);
            }

            units.Clear();
            log.Clear();
            selectedTarget = null;
        }

        // 풀에서 반납된 인스턴스를 다시 받으면 목록의 이전 항목을 지우고 새 스폰으로 넣는다.
        public TurnUnit SpawnUnit(TurnUnitSpec spec, Vector3 position)
        {
            var obj = Global.Pool.Get(template, null, Global.Resource.AppScope);
            obj.transform.position = position;
            var unit = obj.GetComponent<TurnUnit>();
            unit.Setup(spec);
            unit.Spawn();
            units.Remove(unit);
            units.Add(unit);
            return unit;
        }

        // 가드는 시전자의 다음 턴 시작까지 유지되는 게임 규칙이라 핸들로 직접 제거한다.
        public void BeginTurn(TurnUnit unit)
        {
            if (unit.GuardHandle.Id != 0)
                unit.SkillAgent.RemoveEffect(unit.GuardHandle);
        }

        // 발동 여부는 OnSkillActivated 알림으로 판단한다. 막힌 발동(쿨다운·비용·스턴)은 SkillAgent가 조용히 무시한다.
        public bool Act(TurnUnit unit, int skillId, TurnUnit target)
        {
            unit.Target = target;
            int before = unit.Activated;
            unit.SkillAgent.ActivateSkill(skillId);
            bool activated = unit.Activated != before;
            Write($"{unit.name} {TurnIds.SkillName(skillId)} -> {(target == null ? "self" : target.name)}: {(activated ? "ok" : unit.Stunned ? "blocked (stun)" : "ignored")}");
            RemoveDead();
            return activated;
        }

        public void EndTurn(TurnUnit unit)
        {
            if (!unit.Alive)
                return;

            unit.EndTurn();
            RemoveDead();
        }

        public void RemoveDead()
        {
            foreach (var unit in units)
            {
                if (unit.Alive && unit.Hp <= 0f)
                {
                    Global.Pool.Return(unit.gameObject);
                    Write($"{unit.name} dies");
                }
            }
        }

        public void PlayTurn(int skillId, TurnUnit target)
        {
            var unit = Current;
            BeginTurn(unit);
            Act(unit, skillId, target);
            FinishTurnInternal(unit);
        }

        // 우선순위대로 시도해 처음 발동된 스킬로 턴을 쓴다. 스턴이면 모든 시도가 막힌다.
        public void PlayAITurn()
        {
            var unit = Current;
            BeginTurn(unit);
            var enemy = LowestHpEnemyInternal(unit);
            bool acted = false;
            foreach (var skillId in TurnAI.Priorities(unit.Spec.Name, unit.Status, unit.Channeling))
            {
                if (Act(unit, skillId, TurnIds.IsSelfSkill(skillId) ? null : enemy))
                {
                    acted = true;
                    break;
                }

                if (unit.Stunned)
                    break;
            }

            if (!acted)
                Write($"{unit.name} {(unit.Channeling ? "keeps meditating" : "loses the turn")}");

            FinishTurnInternal(unit);
        }

        private void FinishTurnInternal(TurnUnit unit)
        {
            EndTurn(unit);
            if (IsOver)
            {
                Write($"{(units.Exists(other => other.Alive && other.Spec.IsPlayer) ? "Players" : "Enemies")} win");
                return;
            }

            do
            {
                if (++turnIndex >= units.Count)
                {
                    turnIndex = 0;
                    Round++;
                    Write($"Round {Round}");
                }
            }
            while (!Current.Alive);
        }

        private TurnUnit LowestHpEnemyInternal(TurnUnit unit)
        {
            TurnUnit best = null;
            foreach (var other in units)
            {
                if (other.Alive && other.Spec.IsPlayer != unit.Spec.IsPlayer && (best == null || other.Hp < best.Hp))
                    best = other;
            }

            return best;
        }

        private void Write(string message)
        {
            Debug.Log($"[TurnBattle] {message}");
            log.Add(message);
            if (log.Count > MaxLogLines)
                log.RemoveAt(0);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, Screen.height - 20), GUI.skin.box);
            if (!ready)
            {
                GUILayout.Label("Waiting for Global...");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"[Turn Battle] Round {Round}");
            foreach (var unit in units)
            {
                string state = unit.Alive ? $"{unit.Hp:0.#}/{unit.Spec.Hp} hp  {unit.Mp:0.#}/{unit.Spec.Mp} mp  Atk {unit.Status.Get(TurnIds.Atk):0.#} Def {unit.Status.Get(TurnIds.Def):0.#}  fx {unit.ActiveEffects}{(unit.Stunned ? "  STUN" : "")}{(unit.Channeling ? "  MEDITATE" : "")}" : "dead";
                GUILayout.Label($"{(unit == Current && !IsOver ? "> " : "  ")}{unit.name}: {state}");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart"))
                pendingAction = StartBattle;

            autoBattle = GUILayout.Toggle(autoBattle, "Auto Battle");
            GUILayout.EndHorizontal();

            if (!IsOver)
                DrawTurnInternal();

            GUILayout.Space(8);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var line in log)
                GUILayout.Label(line);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTurnInternal()
        {
            var unit = Current;
            if (GUILayout.Button($"AI Turn ({unit.name})"))
                pendingAction = PlayAITurn;

            if (!unit.Spec.IsPlayer)
                return;

            if (selectedTarget == null || !selectedTarget.Alive || selectedTarget.Spec.IsPlayer)
                selectedTarget = LowestHpEnemyInternal(unit);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Target", GUILayout.Width(50));
            foreach (var other in units)
            {
                if (other.Alive && !other.Spec.IsPlayer && GUILayout.Toggle(other == selectedTarget, other.name, GUI.skin.button))
                    selectedTarget = other;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (var skill in unit.Spec.Skills)
            {
                string cost = skill.Costs.Count > 0 ? $" {skill.Costs[0].Amount}mp" : "";
                if (GUILayout.Button(TurnIds.SkillName(skill.Id) + cost))
                {
                    var target = TurnIds.IsSelfSkill(skill.Id) ? null : selectedTarget;
                    pendingAction = () => PlayTurn(skill.Id, target);
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
