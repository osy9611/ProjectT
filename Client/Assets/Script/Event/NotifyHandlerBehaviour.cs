using Cysharp.Threading.Tasks;
using ProjectT;
using System;
using System.Threading;
using UnityEngine;

public abstract class NotifyHandlerBehaviour : MonoBehaviour
{
    private CancellationTokenSource subscriptionLifetime;

    protected virtual void OnEnable()
    {
        ReleaseSubscriptionsInternal();
        subscriptionLifetime = new CancellationTokenSource();
        SubscribeInternalAsync(subscriptionLifetime).Forget(Global.LogException);
    }

    protected virtual void OnDisable()
    {
        ReleaseSubscriptionsInternal();
    }

    protected virtual void OnDestroy()
    {
        ReleaseSubscriptionsInternal();
    }

    protected abstract void OnSubscribe(CancellationToken token);

    private async UniTask SubscribeInternalAsync(CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        bool subscribed = false;

        try
        {
            if (Global.Instance == null)
                await UniTask.WaitUntil(() => Global.Instance != null, cancellationToken: token);

            await Global.Instance.WhenReadyAsync(token);

            token.ThrowIfCancellationRequested();

            if (this == null || !isActiveAndEnabled)
                return;

            OnSubscribe(token);
            subscribed = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || Global.Instance == null ||
            Global.Instance.State == ManagerState.Stopping || Global.Instance.State == ManagerState.Stopped)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(subscriptionLifetime, lifetime))
            {
                try
                {
                    ReleaseSubscriptionsInternal();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(error, cleanupError);
                }
            }

            throw;
        }
        finally
        {
            // A previous activation must not release subscriptions created by a later OnEnable.
            if (!subscribed && ReferenceEquals(subscriptionLifetime, lifetime))
                ReleaseSubscriptionsInternal();
        }
    }

    private void ReleaseSubscriptionsInternal()
    {
        var lifetime = subscriptionLifetime;
        subscriptionLifetime = null;

        if (lifetime == null)
            return;

        try
        {
            lifetime.Cancel();
        }
        finally
        {
            lifetime.Dispose();
        }
    }
}
