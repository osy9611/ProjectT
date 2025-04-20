using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using ProjectT.Skill;

namespace ProjectT
{
    //TODO
    //해당 코드는 임의로 구현한 코드이기 때문에 추후 작업이 필요함
    public class ContainerPoolHandler<T>
    {
        public Func<T> Create;
        public Action<T> Return;

        public ContainerPoolHandler(Func<T> create, Action<T> ret)
        {
            Create = create;
            Return = ret;
        }
    }

    public abstract class GenericContainer<TBase, TAttr, TEnum> 
        where TBase : class, new()  
        where TAttr : Attribute
        where TEnum : System.Enum
    {
        public static bool AlreadyRegister = false;
        private static readonly Dictionary<TEnum, ContainerPoolHandler<TBase>> types = new();

        protected abstract string[] namespaces { get; }

        protected abstract TEnum GetKeyFromAttribute(TAttr attr);
        protected abstract string containerName { get; }

        public void AutoRegister()
        {
            if (AlreadyRegister)
                return;

            var allTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type => typeof(TBase).IsAssignableFrom(type))
                .Where(type => namespaces.Any(ns => type.Namespace != null && type.Namespace.StartsWith(ns)));

            foreach(var type in allTypes)
            {
                var attr = type.GetCustomAttribute<TAttr>();
                if (attr == null)
                    continue;

                TEnum key = GetKeyFromAttribute(attr);

                MethodInfo registerMethod = typeof(BuffContainer)
                    .GetMethod(nameof(Register), BindingFlags.NonPublic | BindingFlags.Static)
                    ?.MakeGenericMethod(type);

                if (registerMethod == null)
                {
                    Global.Instance.LogError($"[GenericContainer] Register<{type.Name}> Not Found Method ");
                    continue;
                }

                var handler = registerMethod.Invoke(null, null) as ContainerPoolHandler<TBase>;
                if(handler == null)
                {
                    Global.Instance.LogError($"[GenericContainer] {type.Name} Register Fail");
                    continue;
                }

                types.Add(key, handler);
                Global.Instance.Log($"[GenericContainer] Registered {key} => {type.Name}");
            }

            AlreadyRegister = true;
        }

        public ContainerPoolHandler<TBase> Register<T>() where T : TBase, new()
        {
            return new ContainerPoolHandler<TBase>(
                 () => Global.Pool.Get<T>(),
                 obj => Global.Pool.Return((T)obj));
        }

        public TBase Get(TEnum type)
        {
            if (types.TryGetValue(type, out var classType))
            {
                return classType.Create?.Invoke();
            }

            return null;
        }

        public static void Return(TEnum type, TBase baseClass)
        {
            if(types.TryGetValue(type, out var classType))
            {
                classType.Return(baseClass);
            }
        }
    }
}