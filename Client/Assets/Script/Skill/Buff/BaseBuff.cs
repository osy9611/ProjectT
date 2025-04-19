using Cysharp.Threading.Tasks;
using DesignTable;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ProjectT.Skill
{

    public class BaseBuff
    {
        protected BaseActor ownerActor;
        protected buffInfo buffInfo;

        public DesignEnum.BuffType buffType { get => (DesignEnum.BuffType)buffInfo.buff_type; }

        public int BuffID { get => buffInfo.buff_Id; }

        public float Interval { get => buffInfo.buff_interval; }

        public float Duration { get => buffInfo.buff_duration; }

        public virtual void Init(params object[] args)
        {
            if(args.Length > 0)
            {
                ownerActor = args[0] as BaseActor;
                buffInfo = args[1] as buffInfo;
            }
        }

        //활성화가 될때
        public virtual void OnApply()
        {
            Global.Instance.Log($"[BaseBuff] OnApply");
        }

        //실행 중일때
        public virtual void OnExecute()
        {
            Global.Instance.Log($"[BaseBuff] OnExecute");
        }

        //버프 제거될때
        public virtual void OnExpire()
        {
            Global.Instance.Log($"[BaseBuff] OnExpire");
        }
    }
}
