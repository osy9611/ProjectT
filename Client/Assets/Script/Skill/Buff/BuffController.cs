using Cysharp.Threading.Tasks;
using DesignTable;
using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
namespace ProjectT.Skill
{
    public class BuffTaskHandler
    {
        public BaseBuff BaseBuff;
        public CancellationTokenSource TokenSource;
        public UniTask Task;

        public BuffTaskHandler(BaseBuff BaseBuff,CancellationTokenSource TokenSource, UniTask Task)
        {
            this.BaseBuff = BaseBuff;
            this.TokenSource = TokenSource;
            this.Task = Task;
        }
    }

    public class BuffController
    {
        private BaseActor actor;

        private Dictionary<int, BuffTaskHandler> buffTaskHandlers;

        private CancellationToken token;

        public void Init(BaseActor actor)
        {
            this.actor = actor;

            if (buffTaskHandlers == null)
                buffTaskHandlers = new Dictionary<int, BuffTaskHandler>();
        }

        public void Register(int buffId)
        {
            buffInfo info = Global.Table.BuffInfos.Get(buffId);

            if (info == null)
                return;

            //버프 생성
            BaseBuff buff = BuffContainer.Get(info.buff_type);
            if(buff == null)
            {
                Global.Instance.LogError($"[BuffController] This buffInfo is not have BaseBuff");
                return;
            }

            buff.Init(actor, info);
            buff.OnApply();

            //Create Handler
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            UniTask task = HandlerBuffHandlerExpiration(buff, tokenSource.Token);
            BuffTaskHandler handler = new BuffTaskHandler(buff,tokenSource, task);

            //버프 등록
            buffTaskHandlers.Add(buffId, handler);
        }

        public void UnRegister(BaseBuff baseBuff)
        {
            if (buffTaskHandlers.TryGetValue(baseBuff.BuffID, out var handler))
            {
                handler.TokenSource.Cancel();
                handler.TokenSource.Dispose();
                baseBuff.OnExpire();
                BuffContainer.Return(baseBuff.buffType, baseBuff);

                buffTaskHandlers.Remove(baseBuff.BuffID);
            }
        }

        async UniTask HandlerBuffHandlerExpiration(BaseBuff BaseBuff, CancellationToken Token)
        {
            try
            {
                if (BaseBuff.Interval > 0)
                {
                    int ticks = Mathf.FloorToInt(BaseBuff.Duration / BaseBuff.Interval);
                    for (int i = 0; i < ticks; ++i)
                    {
                        BaseBuff.OnExecute();
                        await UniTask.Delay(TimeSpan.FromSeconds(BaseBuff.Interval), cancellationToken: Token);
                    }
                }
                else
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(BaseBuff.Duration), cancellationToken: Token);
                }
            }
            catch (OperationCanceledException ex)
            {
                Global.Instance.LogError($"[BuffController] Error : {ex}");
            }
            finally
            {
                UnRegister(BaseBuff);
            }
        }
    }
}
