using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.Skill
{
    internal class BuffController
    {
        private class ActiveBuff
        {
            public BaseBuff Buff;
            public float Elapsed;
            public int ExecuteCount;
            public bool IsRemoved;
        }

        private ComBaseActor actor;

        private List<ActiveBuff> activeBuffs = new List<ActiveBuff>();
        private HashSet<int> activeBuffIds = new HashSet<int>();
        private int releaseCount;

        public BuffController(ComBaseActor actor)
        {
            this.actor = actor;
        }

        public void Register(int buffId, ComBaseActor caster)
        {
            BuffDefinition definition = SkillDefinitions.GetBuff(buffId);

            if (definition == null || activeBuffIds.Contains(definition.Id))
                return;

            //버프 생성
            BaseBuff buff = BuffContainer.Get(definition.Kind);
            if(buff == null)
            {
                Global.LogError($"[BuffController] This buffInfo is not have BaseBuff");
                return;
            }

            buff.Init(actor, caster, definition);
            // OnApply가 실패하면 등록되지 않도록 성공한 뒤에 목록에 넣는다.
            int releases = releaseCount;
            buff.OnApply();
            // OnApply 중 Actor가 반납되면 이미 정리가 끝났으므로 등록하지 않고 다른 버프와 같이 만료시킨다.
            if (releases != releaseCount)
            {
                buff.OnExpire();
                return;
            }

            var active = new ActiveBuff { Buff = buff };
            activeBuffIds.Add(definition.Id);
            activeBuffs.Add(active);
            // 경과 0 시점의 주기 실행만 처리한다. 만료는 다음 Tick에서 판정한다.
            AdvanceInternal(active, 0f);
        }

        public void Tick(float deltaTime)
        {
            List<ActiveBuff> expired = null;
            int count = activeBuffs.Count;
            // OnExecute 중 Actor가 반납되면 순회 도중 목록이 비워진다.
            for (int i = 0; i < count && i < activeBuffs.Count; i++)
            {
                ActiveBuff active = activeBuffs[i];
                if (AdvanceInternal(active, deltaTime))
                    (expired ??= new List<ActiveBuff>()).Add(active);
            }

            if (expired == null)
                return;

            foreach (ActiveBuff active in expired)
                RemoveInternal(active);
        }

        public void ReleaseAll()
        {
            releaseCount++;
            ActiveBuff[] snapshot = activeBuffs.ToArray();
            try
            {
                foreach (ActiveBuff active in snapshot)
                    RemoveInternal(active);
            }
            finally
            {
                activeBuffs.Clear();
                activeBuffIds.Clear();
            }
        }

        private static bool AdvanceInternal(ActiveBuff active, float deltaTime)
        {
            BaseBuff buff = active.Buff;
            float interval = buff.Interval;
            float duration = buff.Duration;
            active.Elapsed += deltaTime;

            if (interval <= 0)
                return active.Elapsed >= duration;

            // 0.9 / 0.3 = 2.9999...처럼 기획값의 나눗셈 오차로 횟수가 1 줄지 않도록 정수 경계 근처는 올린다.
            int ticks = Mathf.FloorToInt(duration / interval + 1e-4f);
            while (!active.IsRemoved && active.ExecuteCount < ticks && active.ExecuteCount * interval <= active.Elapsed)
            {
                active.ExecuteCount++;
                buff.OnExecute();
            }

            return active.Elapsed >= ticks * interval;
        }

        private void RemoveInternal(ActiveBuff active)
        {
            if (active.IsRemoved)
                return;

            active.IsRemoved = true;
            activeBuffs.Remove(active);
            activeBuffIds.Remove(active.Buff.BuffID);
            active.Buff.OnExpire();
        }
    }
}
