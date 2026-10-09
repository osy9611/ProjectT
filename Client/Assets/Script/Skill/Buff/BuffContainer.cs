using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine.Scripting;
using System.Linq;

namespace ProjectT.Skill
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BuffAttribute : Attribute
    {
        public BuffKind Kind { get; }
        public BuffAttribute(BuffKind kind)
        {
            Kind = kind;
        }
    }

    public static class BuffContainer 
    {
        private static readonly string[] nameSpace = { "ProjectT.Skill" };
        private static Dictionary<BuffKind, Func<BaseBuff>> typeBuffs;

        private static Dictionary<BuffKind, Func<BaseBuff>> BuildHandlersInternal()
        {
            var handlers = new Dictionary<BuffKind, Func<BaseBuff>>();
            var allTypes = Assembly.GetExecutingAssembly()
                            .GetTypes()
                            .Where(t => typeof(BaseBuff).IsAssignableFrom(t) && !t.IsAbstract)
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
                    Global.LogError($"[BuffContainer] Register<{type.Name}> Not Found Method ");
                    continue;
                }

                var handler = registerMethod.Invoke(null, null) as Func<BaseBuff>;
                if(handler == null)
                {
                    Global.LogError($"[BuffContainer] {type.Name} Register Fail");
                    continue;
                }

                handlers.Add(attr.Kind, handler);
                Global.Instance.Log($"[BuffContainer] Registered {attr.Kind} => {type.Name}");
            }

            return handlers;
        }

        private static Func<BaseBuff> Register<T>() where T : BaseBuff, new()
        {
            return () => new T();
        }

        public static BaseBuff Get(BuffKind type)
        {
            typeBuffs ??= BuildHandlersInternal();

            if (typeBuffs.TryGetValue(type, out var classType))
            {
                return classType();
            }

            return null;
        }
    }

    [Preserve]
    [Buff(BuffKind.AddATK)]
    public class AddATK : BaseBuff
    {

    }
}
