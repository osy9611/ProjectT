using ProjectT.Pool;
using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using System.Linq;
namespace ProjectT.Skill
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BuffAttribute : Attribute
    {
        public DesignEnum.BuffType BuffType { get; }
        public BuffAttribute(DesignEnum.BuffType buffType)
        {
            BuffType = buffType;
        }
    }

    public class BuffPoolHandler
    {
        public Func<BaseBuff> Create;
        public Action<BaseBuff> Return;
        public BuffPoolHandler(Func<BaseBuff> create, Action<BaseBuff> ret)
        {
            Create = create;
            Return = ret;
        }
    }

    public static class BuffContainer 
    {
        private static readonly string[] nameSpace = { "ProjectT.Skill" };
        public static bool AlreadyRegister = false;
        private static Dictionary<DesignEnum.BuffType, BuffPoolHandler> typeBuffs = new();

        public static void AutoRegister()
        {
            if (AlreadyRegister)
                return;

            var allTypes = Assembly.GetExecutingAssembly()
                            .GetTypes()
                            .Where(t => typeof(BaseBuff).IsAssignableFrom(t))
                            .Where(t => nameSpace.Any(ns => t.Namespace != null && t.Namespace.StartsWith(ns)));

            foreach (var type in allTypes)
            {
                var attr = type.GetCustomAttribute<BuffAttribute>();
                if (attr == null)
                    continue;

                MethodInfo registerMethod = typeof(BuffContainer)
                    .GetMethod(nameof(Register), BindingFlags.NonPublic | BindingFlags.Static)
                    ?.MakeGenericMethod(type);

                if(registerMethod == null)
                {
                    Global.Instance.LogError($"[BuffContainer] Register<{type.Name}> Not Found Method ");
                    continue;
                }

                var handler = registerMethod.Invoke(null, null) as BuffPoolHandler;
                if(handler == null)
                {
                    Global.Instance.LogError($"[BuffContainer] {type.Name} Register Fail");
                    continue;
                }

                typeBuffs.Add(attr.BuffType, handler);
                Global.Instance.Log($"[BuffContainer] Registered {attr.BuffType} => {type.Name}");
            }

            AlreadyRegister = true;
        }

        private static BuffPoolHandler Register<T>() where T : BaseBuff, new()
        {
            return new BuffPoolHandler(
                () => Global.Pool.Get<T>(),
                buff => Global.Pool.Return((T)buff));
        }

        public static BaseBuff Get(sbyte type)
        {
            return Get((DesignEnum.BuffType)type);
        }

        public static BaseBuff Get(DesignEnum.BuffType type)
        {
            if (typeBuffs.TryGetValue(type, out var classType))
            {
                return classType.Create.Invoke();
            }

            return null;
        }

        public static void Return(DesignEnum.BuffType type, BaseBuff baseBuff)
        {
            if (typeBuffs.TryGetValue(type, out var classType))
            {
                classType.Return(baseBuff);
            }
        }
    }

    [Buff(DesignEnum.BuffType.AddATK)]
    public class AddATK : BaseBuff
    {
    }
}
