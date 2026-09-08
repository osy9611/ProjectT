using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using ProjectT;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class NotifyHandlerBehaviour : MonoBehaviour, INotifyHandler
{
    protected bool isConnected = false;
    private CancellationTokenSource connectionWait;
    private NotificationManager connectedManager;
    #region Event
    protected virtual void OnEnable()
    {
        connectionWait?.Cancel();
        connectionWait?.Dispose();
        connectionWait = new CancellationTokenSource();
        ConnectWhenReady(connectionWait.Token).Forget();
    }

    protected virtual void OnDisable()
    {
        connectionWait?.Cancel();
        connectionWait?.Dispose();
        connectionWait = null;
        DisconnectHandler();
    }
    #endregion

    #region EventHandler
    public bool IsConnected { get => isConnected; }

    public string HandlerName
    {
        get => this.gameObject.name;
        set => this.gameObject.name = value;
    }


    public virtual bool IsActiveAndEnabled()
    {
        if (isConnected == false)
            return false;

        if (enabled == false)
            return false;

        if (gameObject == null)
            return false;

        if (gameObject.activeSelf == false)
            return false;

        return isActiveAndEnabled;
    }

    public abstract eNotifyHandler GetHandlerType();

    public int GetOrder()
    {
        return (int)GetHandlerType();
    }


    private async UniTask ConnectWhenReady(CancellationToken token)
    {
        try
        {
            await UniTask.WaitUntil(() => Global.Instance != null, cancellationToken: token);
            await Global.Instance.WhenReadyAsync(token);
            token.ThrowIfCancellationRequested();
            if (this != null && isActiveAndEnabled)
                ConnectHandler();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            UnityEngine.Debug.LogException(error);
        }
    }

    public virtual void ConnectHandler()
    {
        if (isConnected || !Global.TryGetReady<NotificationManager>(out var manager))
            return;
        connectedManager = manager;
        manager.ConnectHandler(this);
    }

    public virtual void DisconnectHandler()
    {
        var manager = connectedManager;
        connectedManager = null;
        if (manager != null && manager.State == ManagerState.Ready)
            manager.DisconnectHandler(this);
        isConnected = false;
    }

    public void OnConnectHandler()
    {
        isConnected = true;
    }

    public void OnDisConnectHandler()
    {
        isConnected = false;
    }

    public abstract void OnNotify(INotify notify);
    #endregion
}
