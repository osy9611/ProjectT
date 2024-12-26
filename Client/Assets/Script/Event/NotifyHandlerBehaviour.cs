using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class NotifyHandlerBehaviour : MonoBehaviour, INotifyHandler
{
    protected bool isConnected = false;
    #region Event
    protected virtual void OnEnable()
    {
        ConnectHandler();
    }

    protected virtual void OnDisable()
    {
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


    public virtual void ConnectHandler()
    {
    }

    public virtual void DisconnectHandler()
    {
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
