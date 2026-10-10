using System;
using System.Collections.Generic;

namespace ProjectT.Stat
{
    public enum StatModOp
    {
        Add,
        Multiply,
        Override,
    }

    public sealed class Status
    {
        private readonly struct Modifier
        {
            public readonly StatModOp Op;
            public readonly float Value;
            public readonly object Source;

            public Modifier(StatModOp op, float value, object source)
            {
                Op = op;
                Value = value;
                Source = source;
            }
        }

        private sealed class StatEntry
        {
            public readonly StatId Id;
            public readonly List<Modifier> Modifiers = new List<Modifier>();
            public float BaseValue;
            public float CurrentValue;

            public StatEntry(StatId id)
            {
                Id = id;
            }
        }

        private sealed class Resource
        {
            public readonly StatId Id;
            public readonly StatId Max;
            public float Value;

            public Resource(StatId id, StatId max)
            {
                Id = id;
                Max = max;
            }
        }

        // 스탯은 처음 쓸 때 만든다. 정리해도 지우지 않으므로 알림 중 정리되거나 새 스탯이 추가되어도 앞쪽 순서가 유지된다.
        private readonly Dictionary<StatId, StatEntry> statById = new Dictionary<StatId, StatEntry>();
        private readonly List<StatEntry> stats = new List<StatEntry>();
        // 자원 정의는 오브젝트 단위 설정이라 정리해도 유지한다. 뒤에만 추가되므로 알림 중 새로 정의되어도 앞쪽 순서가 유지된다.
        private readonly List<Resource> resources = new List<Resource>();
        private int releaseCount;

        // 핸들러가 Status를 다시 호출하면 이후 알림의 인자가 현재 값과 다를 수 있으므로 핸들러는 Get·GetResource로 현재 값을 읽는다.
        public event Action<StatId, float, float> OnStatChanged;

        public event Action<StatId, float, float> OnResourceChanged;

        internal Status()
        {
        }

        public float Get(StatId stat) => statById.TryGetValue(stat, out StatEntry entry) ? entry.CurrentValue : 0f;

        public float GetBase(StatId stat) => statById.TryGetValue(stat, out StatEntry entry) ? entry.BaseValue : 0f;

        public void SetBase(StatId stat, float value)
        {
            StatEntry entry = GetOrAddEntryInternal(stat);
            entry.BaseValue = value;
            RecalculateInternal(entry);
        }

        public void AddModifier(StatId stat, StatModOp op, float value, object source)
        {
            StatEntry entry = GetOrAddEntryInternal(stat);
            entry.Modifiers.Add(new Modifier(op, value, source));
            RecalculateInternal(entry);
        }

        // 자원은 스탯과 따로 저장되어 수정자 대상이 아니고, 값은 0과 max 스탯의 현재 값 사이로 제한된다.
        public void DefineResource(StatId resource, StatId max)
        {
            if (resource == default || max == default)
                throw new ArgumentException($"[Status] Resource and max must be registered stats. Resource {resource.Name}, Max {max.Name}");

            foreach (Resource defined in resources)
            {
                if (defined.Id == resource)
                    throw new InvalidOperationException($"[Status] Resource {resource.Name} is already defined.");
            }

            resources.Add(new Resource(resource, max));
        }

        public float GetResource(StatId resource) => GetResourceInternal(resource).Value;

        public void SetResource(StatId resource, float value)
        {
            Resource entry = GetResourceInternal(resource);
            float previous = entry.Value;
            float current = Math.Clamp(value, 0f, Get(entry.Max));
            if (current == previous)
                return;

            entry.Value = current;
            OnResourceChanged?.Invoke(resource, previous, current);
        }

        // 핸들러가 실패하거나 수정자를 바꿔도 이번 제거 결과가 모든 스탯에 반영되도록 값을 모두 저장한 뒤 알린다.
        public void RemoveModifiers(object source)
        {
            int count = stats.Count;
            Span<float> previous = stackalloc float[count];
            for (int i = 0; i < count; i++)
            {
                StatEntry entry = stats[i];
                var list = entry.Modifiers;
                for (int j = list.Count - 1; j >= 0; j--)
                {
                    if (list[j].Source == source)
                        list.RemoveAt(j);
                }

                previous[i] = entry.CurrentValue;
                entry.CurrentValue = CalculateInternal(entry.BaseValue, list);
            }

            Span<float> previousResources = stackalloc float[resources.Count];
            Span<float> currentResources = stackalloc float[resources.Count];
            ClampResourcesInternal(previousResources, currentResources);
            int releases = releaseCount;
            for (int i = 0; i < count && releases == releaseCount; i++)
            {
                StatEntry entry = stats[i];
                if (entry.CurrentValue != previous[i])
                    OnStatChanged?.Invoke(entry.Id, previous[i], entry.CurrentValue);
            }

            NotifyResourcesInternal(previousResources, currentResources, releases);
        }

        // 구독자는 Disable에서 이미 해제했으므로 정리는 알림 없이 값을 비운다.
        internal void ReleaseInternal()
        {
            releaseCount++;
            foreach (StatEntry entry in stats)
            {
                entry.BaseValue = 0f;
                entry.CurrentValue = 0f;
                entry.Modifiers.Clear();
            }

            foreach (Resource resource in resources)
                resource.Value = 0f;
        }

        private StatEntry GetOrAddEntryInternal(StatId stat)
        {
            if (!statById.TryGetValue(stat, out StatEntry entry))
            {
                entry = new StatEntry(stat);
                statById.Add(stat, entry);
                stats.Add(entry);
            }

            return entry;
        }

        private Resource GetResourceInternal(StatId id)
        {
            foreach (Resource resource in resources)
            {
                if (resource.Id == id)
                    return resource;
            }

            throw new InvalidOperationException($"[Status] Resource {id.Name} is not defined.");
        }

        private void RecalculateInternal(StatEntry entry)
        {
            float previous = entry.CurrentValue;
            float current = CalculateInternal(entry.BaseValue, entry.Modifiers);
            if (current == previous)
                return;

            entry.CurrentValue = current;
            Span<float> previousResources = stackalloc float[resources.Count];
            Span<float> currentResources = stackalloc float[resources.Count];
            ClampResourcesInternal(previousResources, currentResources);
            int releases = releaseCount;
            OnStatChanged?.Invoke(entry.Id, previous, current);
            NotifyResourcesInternal(previousResources, currentResources, releases);
        }

        // 상한 스탯이 자원보다 작아지면 자원을 줄인다. 스탯 핸들러가 실패해도 자원이 상한을 넘지 않도록 알리기 전에 줄인다.
        // 상한이 바뀌지 않은 자원은 이미 상한 이하이므로 그대로다.
        private void ClampResourcesInternal(Span<float> previous, Span<float> current)
        {
            for (int i = 0; i < previous.Length; i++)
            {
                Resource resource = resources[i];
                previous[i] = resource.Value;
                resource.Value = Math.Min(resource.Value, Get(resource.Max));
                current[i] = resource.Value;
            }
        }

        // 알림 핸들러에서 반납되면(같은 호출에서 재스폰 포함) 저장해 둔 이전 스폰의 값을 새 구독자에게 보내지 않도록 멈춘다.
        private void NotifyResourcesInternal(Span<float> previous, Span<float> current, int releases)
        {
            for (int i = 0; i < previous.Length && releases == releaseCount; i++)
            {
                if (current[i] != previous[i])
                    OnResourceChanged?.Invoke(resources[i].Id, previous[i], current[i]);
            }
        }

        // 뒤에서부터 찾으므로 처음 만나는 Override가 가장 최근에 추가된 것이다.
        // 최종 값은 0 이상이다. Multiply는 합산되어 감소 합계가 100%를 넘으면 음수가 되고, 두 항이 모두 음수면 곱이 양수가 되므로 항별로 제한한다.
        private static float CalculateInternal(float baseValue, List<Modifier> list)
        {
            float add = 0f;
            float multiply = 0f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var modifier = list[i];
                if (modifier.Op == StatModOp.Override)
                    return Math.Max(modifier.Value, 0f);
                else if (modifier.Op == StatModOp.Add)
                    add += modifier.Value;
                else
                    multiply += modifier.Value - 1f;
            }

            return Math.Max(baseValue + add, 0f) * Math.Max(1f + multiply, 0f);
        }
    }
}
