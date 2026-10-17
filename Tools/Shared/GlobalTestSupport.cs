using Cysharp.Threading.Tasks;
using UnityEngine;

namespace ProjectT
{
    // 테스트 프로젝트는 필요한 매니저만 직접 등록하고 쓰는 접근자만 각자의 partial에 둔다.
    public partial class Global
    {
        private void RegisterManagers(ManagerHost host)
        {
        }

        internal static Global BindForTests(ManagerHost host)
        {
            var global = new GameObject("Global").AddComponent<Global>();
            global.host = host;
            s_instance = global;
            return global;
        }

        internal void InitForTests() => Init();

        internal void UpdateForTests() => Update();

        internal UniTask InitializeForTests() => InitializeAsync();

        internal static void ResetStaticsForTests() => ResetStatics();
    }
}
