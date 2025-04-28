using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace ProjectT.Skill
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SkillActionAttribute : Attribute
    {
        public DesignEnum.SkillType SkillType { get; }

        public SkillActionAttribute(DesignEnum.SkillType skillType)
        {
            SkillType = skillType;
        }
    }

    public class SkillPoolHandler
    {
        public Func<BaseSkillAction> Create;
        public Action<BaseSkillAction> Return;

        public SkillPoolHandler(Func<BaseSkillAction> create, Action<BaseSkillAction> ret)
        {
            Create = create;
            Return = ret;
        }
    }

    public static class SkillActionContainer 
    {
        private static readonly string[] nameSpace = { "ProjectT.Skill" };
        public static bool AlreadyRegister = false;

        private static Dictionary<DesignEnum.SkillType, SkillPoolHandler> typeActions = new();

        public static void AutoRegister()
        {
            if (AlreadyRegister)
                return;

            var allTypes = Assembly.GetExecutingAssembly()
                          .GetTypes()
                          .Where(t=> typeof(BaseSkillAction).IsAssignableFrom(t) && t.IsAbstract)
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
                    Global.Instance.LogError($"[SkillActionContainer] Register<{type.Name}> Not Found Method");
                    continue;
                }

                var handler = registerMethod.Invoke(null, null) as SkillPoolHandler;
                if (handler == null)
                {
                    Global.Instance.LogError($"[SkillActionContainer] {type.Name} Register Fail");
                    continue;
                }

                typeActions.Add(attr.SkillType, handler);
                Debug.Log($"[SkillActionContainer] Registered {attr.SkillType} => {type.Name}");
            }

            AlreadyRegister = true;
        }


        private static SkillPoolHandler Register<T>() where T : BaseSkillAction, new()
        {
            return new SkillPoolHandler(
                () => Global.Pool.Get<T>(),
                action => Global.Pool.Return((T)action));
        }

        public static BaseSkillAction Get(sbyte type)
        {
            return Get((DesignEnum.SkillType)type);
        }

        public static BaseSkillAction Get(DesignEnum.SkillType type)
        {
            if(typeActions.TryGetValue(type,out var handler))
            {
                return handler.Create?.Invoke();
            }

            Global.Instance.LogError($"[SkillActionContainer] Get SkillAction Fail typeActions Not Found {type}");
            return null;
        }

        public static void Return(DesignEnum.SkillType type, BaseSkillAction skillAction)
        {
            if (typeActions.TryGetValue(type, out var handler))
            {
                handler.Return?.Invoke(skillAction);
            }
        }

    }
}
