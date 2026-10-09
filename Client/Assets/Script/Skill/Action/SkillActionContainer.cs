using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace ProjectT.Skill
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SkillActionAttribute : Attribute
    {
        public SkillActionKind Kind { get; }

        public SkillActionAttribute(SkillActionKind kind)
        {
            Kind = kind;
        }
    }

    public static class SkillActionContainer 
    {
        private static readonly string[] nameSpace = { "ProjectT.Skill" };

        private static Dictionary<SkillActionKind, Func<BaseSkillAction>> typeActions;

        private static Dictionary<SkillActionKind, Func<BaseSkillAction>> BuildHandlersInternal()
        {
            var handlers = new Dictionary<SkillActionKind, Func<BaseSkillAction>>();
            var allTypes = Assembly.GetExecutingAssembly()
                          .GetTypes()
                          .Where(t=> typeof(BaseSkillAction).IsAssignableFrom(t) && !t.IsAbstract)
                          .Where(t => nameSpace.Any(ns=>t.Namespace != null && t.Namespace.StartsWith(ns)));

            foreach (var type in allTypes)
            {
                var attr = type.GetCustomAttribute<SkillActionAttribute>();
                if (attr == null)
                    continue;

                MethodInfo registerMethod = typeof(SkillActionContainer)
                    .GetMethod(nameof(Register), BindingFlags.NonPublic | BindingFlags.Static)
                    ?.MakeGenericMethod(type);

                if(registerMethod == null)
                {
                    Global.LogError($"[SkillActionContainer] Register<{type.Name}> Not Found Method");
                    continue;
                }

                var handler = registerMethod.Invoke(null, null) as Func<BaseSkillAction>;
                if (handler == null)
                {
                    Global.LogError($"[SkillActionContainer] {type.Name} Register Fail");
                    continue;
                }

                handlers.Add(attr.Kind, handler);
                Debug.Log($"[SkillActionContainer] Registered {attr.Kind} => {type.Name}");
            }

            return handlers;
        }

        private static Func<BaseSkillAction> Register<T>() where T : BaseSkillAction, new()
        {
            return () => new T();
        }

        public static BaseSkillAction Get(SkillActionKind type)
        {
            typeActions ??= BuildHandlersInternal();

            if(typeActions.TryGetValue(type,out var handler))
            {
                return handler();
            }

            Global.LogError($"[SkillActionContainer] Get SkillAction Fail typeActions Not Found {type}");
            return null;
        }

        public static void Clear(SkillActionKind type)
        {
            typeActions?.Remove(type);
        }

        public static void ClearAll()
        {
            typeActions = null;
        }

    }
}
