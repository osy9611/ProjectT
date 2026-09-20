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

            if (info == null || buffTaskHandlers.ContainsKey(buffId))
                return;

            //버프 생성
            BaseBuff buff = BuffContainer.Get(info.buff_type);
            if(buff == null)
            {
                Global.Instance.LogError($"[BuffController] This buffInfo is not have BaseBuff");
                return;
            }

            try { buff.Init(actor, info); }
            catch
            {
                BuffContainer.Return((DesignEnum.BuffType)info.buff_type, buff);
                throw;
            }
            var tokenSource = new CancellationTokenSource();
            var handler = new BuffTaskHandler(buff, tokenSource, UniTask.CompletedTask);
            buffTaskHandlers.Add(buffId, handler);
            try
            {
                buff.OnApply();
                if (!buffTaskHandlers.TryGetValue(buffId, out var current) || !ReferenceEquals(current, handler))
                    return;
                handler.Task = HandlerBuffHandlerExpiration(buffId, handler);
                handler.Task.Forget(Global.LogException);
            }
            catch
            {
                UnRegister(buffId, handler);
                throw;
            }
        }

        public void Clear()
        {
            if (buffTaskHandlers == null)
                return;
            var errors = new List<Exception>();
            foreach (var pair in new List<KeyValuePair<int, BuffTaskHandler>>(buffTaskHandlers))
            {
                try { UnRegister(pair.Key, pair.Value); }
                catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0)
                throw new AggregateException(errors);
        }


        public void UnRegister(BaseBuff baseBuff)
        {
            if (baseBuff == null)
                return;
            BuffTaskHandler found = null;
            int id = 0;
            foreach (var pair in buffTaskHandlers)
            {
                if (!ReferenceEquals(pair.Value.BaseBuff, baseBuff))
                    continue;
                found = pair.Value;
                id = pair.Key;
                break;
            }
            if (found != null)
                UnRegister(id, found);
        }

        private void UnRegister(int id, BuffTaskHandler handler)
        {
            if (!buffTaskHandlers.TryGetValue(id, out var current) || !ReferenceEquals(current, handler))
                return;
            buffTaskHandlers.Remove(id);
            var buff = handler.BaseBuff;
            var type = buff.buffType;
            try { handler.TokenSource.Cancel(); }
            finally
            {
                handler.TokenSource.Dispose();
                try { buff.OnExpire(); }
                finally { BuffContainer.Return(type, buff); }
            }
        }

        async UniTask HandlerBuffHandlerExpiration(int id, BuffTaskHandler handler)
        {
            var BaseBuff = handler.BaseBuff;
            var Token = handler.TokenSource.Token;
            var interval = BaseBuff.Interval;
            var duration = BaseBuff.Duration;
            try
            {
                if (interval > 0)
                {
                    int ticks = Mathf.FloorToInt(duration / interval);
                    for (int i = 0; i < ticks; ++i)
                    {
                        Token.ThrowIfCancellationRequested();
                        BaseBuff.OnExecute();
                        Token.ThrowIfCancellationRequested();
                        await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: Token);
                    }
                }
                else
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: Token);
                }
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
            }
            finally
            {
                UnRegister(id, handler);
            }
        }
    }
}
