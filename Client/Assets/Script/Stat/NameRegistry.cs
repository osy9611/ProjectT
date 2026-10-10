using System;
using System.Collections.Generic;

namespace ProjectT.Stat
{
    // GameplayTag와 StatId가 같은 등록 규칙을 쓴다. Skill이 Stat을 참조하는 방향이라 Stat에 둔다.
    internal sealed class NameRegistry
    {
        // 이미 만든 id가 다른 이름을 가리키지 않도록 등록은 지우지 않는다.
        private readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        private readonly string owner;

        public NameRegistry(string owner)
        {
            this.owner = owner;
        }

        // 이름은 대소문자를 구별해 그대로 비교한다. 0은 등록되지 않은 값(default)이므로 1부터 준다.
        public int Register(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException($"[{owner}] Name cannot be null or empty.", nameof(name));

            if (!ids.TryGetValue(name, out int id))
            {
                id = ids.Count + 1;
                ids.Add(name, id);
            }

            return id;
        }
    }
}
