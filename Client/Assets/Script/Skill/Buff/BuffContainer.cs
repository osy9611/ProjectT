using ProjectT.Pool;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
namespace ProjectT.Skill
{
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
        private static Dictionary<DesignEnum.BuffType, BuffPoolHandler> typeMap = new()
        {
            { DesignEnum.BuffType.AddATK, Register<AddATK>()},
        };

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
            if (typeMap.TryGetValue(type, out var classType))
            {
                return classType.Create.Invoke();
            }

            return null;
        }

        public static void Return(DesignEnum.BuffType type, BaseBuff baseBuff)
        {
            if (typeMap.TryGetValue(type, out var classType))
            {
                classType.Return(baseBuff);
            }
        }
    }

    public class AddATK : BaseBuff
    {
    }
}
