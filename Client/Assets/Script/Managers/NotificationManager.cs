using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ProjectT
{
    public static class MethodInfoExtensions
    {
        public static bool IsOverride(this MethodInfo m)
        {
            if (m == null) throw new System.ArgumentNullException("IsOverride");
            return (m.GetBaseDefinition().DeclaringType != m.DeclaringType) ? true : false;
        }
    }

    public class WaitForNotifyInfo : System.IDisposable
    {
        private string methodName = string.Empty;
        public string MethodName { get => methodName; }

        private eNotifyHandler receiverHandlerTypes = eNotifyHandler.Default;

        public eNotifyHandler ReceiverHandlerTypes { get => receiverHandlerTypes; }

        private object[] args = null;
        public object[] Args { get => args; }

        private float duration = 0.0f;
        private float currentTime = 0.0f;

        private int currentFrameCount = -1;

        public WaitForNotifyInfo(string methodName, eNotifyHandler receiverHandlerTypes, params object[] args) : this(methodName, receiverHandlerTypes, 0.0f, args)
        {
        }

        public WaitForNotifyInfo(string methodName, eNotifyHandler receiverHandlerTypes, float seconds, params object[] args)
        {
            this.methodName = methodName;
            this.receiverHandlerTypes = receiverHandlerTypes;
            this.args = args;
            this.duration = seconds;
            this.currentTime = 0.0f;

            currentFrameCount = duration == 0.0f ? Time.frameCount : -1;
        }

        public bool CheckWaitStatus(float delta)
        {
            if (currentFrameCount != -1)
                return (currentFrameCount != Time.frameCount) ? true : false;
            else
            {
                currentTime = currentTime + delta;
                return (currentTime > duration) ? true : false;
            }
        }

        public void Dispose()
        {
            System.GC.SuppressFinalize(this);
        }
    }

    public class NotificationManager : ManagerBase
    {
        protected List<INotifyHandler> handlers = new List<INotifyHandler>();
        protected List<WaitForNotifyInfo> waitForNotifyInfos = new List<WaitForNotifyInfo>();

        #region ManagerBase
        public override void OnAppStart()
        {
            Name = typeof(NotificationManager).ToString();

            if (string.IsNullOrEmpty(Name))
            {
                throw new System.Exception("Manager Name Is Empty");
            }

            AllDisconnectHandler();
        }

        public override void OnAppEnd()
        {
            AllDisconnectHandler();
            DestoryRootObject();
        }

        public override void OnAppFocuse(bool focused)
        {
        }

        public override void OnAppPause(bool paused)
        {
        }

        public override void OnEnter()
        {
        }

        public override void OnFixedUpdate(float dt)
        {
        }

        public override void OnLateUpdate()
        {
        }

        public override void OnUpdate(float dt)
        {
            UpdateHandler(dt);
            UpdateWaitForNotify(dt);
        }

        public override void OnLeave()
        {
        }
        #endregion

        public void ConnectHandler(INotifyHandler handler)
        {
            if (handler == null)
            {
                Global.Instance.LogWarning("NotificationManager.ConnectHandler(null)");
                return;
            }

            if (handlers != null)
            {
                handlers.Add(handler);

                handlers.Sort((a, b) =>
                {
                    return b.GetOrder().CompareTo(a.GetOrder());
                });

                Global.Instance.Log($"NotificationManager.ConnectHandler({handler.HandlerName}) -> Handler Count: {handlers.Count}");
            }

            handler.OnConnectHandler();
        }

        public void DisconnectHandler(INotifyHandler handler)
        {
            if (handler == null)
            {
                Global.Instance.LogWarning("NotifycationManager.DisconnectHandler( null )");
                return;
            }

            handler.OnDisConnectHandler();

            if (handlers == null)
                return;

            handlers.Remove(handler);

            Global.Instance.Log($"NotificationManager.DisconnectHandler({handler.HandlerName}) -> Handler Count {handlers.Count}");
        }

        protected void AllDisconnectHandler()
        {
            foreach (var info in waitForNotifyInfos)
            {
                if (info == null)
                    continue;

                info.Dispose();
            }

            waitForNotifyInfos.Clear();

            foreach (var handler in handlers)
            {
                if (handler == null)
                    continue;

                handler.OnDisConnectHandler();
            }

            handlers.Clear();
        }

        protected void UpdateHandler(float delta)
        {
            List<INotifyHandler> eventHandlers = null;

            foreach (var handler in handlers)
            {
                if (handler == null)
                    continue;
                if (handler.IsActiveAndEnabled())
                    continue;

                if (eventHandlers == null)
                    eventHandlers = new List<INotifyHandler>();

                eventHandlers.Add(handler);
            }

            if (eventHandlers != null)
            {
                foreach (var handler in eventHandlers)
                    DisconnectHandler(handler);
            }
        }

        private void UpdateWaitForNotify(float delta)
        {
            if (waitForNotifyInfos.Count == 0 || waitForNotifyInfos.Any() == false)
                return;

            List<WaitForNotifyInfo> removeWaitForNotifyInfos = new List<WaitForNotifyInfo>();
            for (int i = 0; i < waitForNotifyInfos.Count; ++i)
            {
                bool isCompleted = waitForNotifyInfos[i].CheckWaitStatus(delta);
                if (isCompleted)
                {
                    NotifyToEventHandler(waitForNotifyInfos[i].MethodName, waitForNotifyInfos[i].ReceiverHandlerTypes, waitForNotifyInfos[i].Args);
                    removeWaitForNotifyInfos.Add(waitForNotifyInfos[i]);
                }
            }

            for (int i = 0; i < removeWaitForNotifyInfos.Count; ++i)
            {
                removeWaitForNotifyInfos[i].Dispose();
                waitForNotifyInfos.Remove(removeWaitForNotifyInfos[i]);
            }
        }

        public void WaitForFrameOfNextNotifyToEventHandler(string methodName, eNotifyHandler notifyHandlerTypes, params object[] args)
        {
            waitForNotifyInfos.Add(new WaitForNotifyInfo(methodName, notifyHandlerTypes, 0.0f, args));
        }

        public void WaitForSecondsNotifyToEventHandler(string methodName, eNotifyHandler notifyHandlerTypes, float seconds, params object[] args)
        {
            waitForNotifyInfos.Add(new WaitForNotifyInfo(methodName, notifyHandlerTypes, seconds, args));
        }

        public string NotifyToEventHandler(string methodName, eNotifyHandler notifyHandlerTypes, params object[] args)
        {
            return NotifyToEventHandler(methodName, notifyHandlerTypes, false, args);
        }

        public string NotifyToEventHandler(string methodName, eNotifyHandler notifyHandlerTypes, bool includeInActive, params object[] args)
        {
            string result = string.Empty;

            List<INotifyHandler> eventHandlers = new List<INotifyHandler>();
            foreach (INotifyHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                if (!includeInActive && !handler.IsActiveAndEnabled())
                    continue;

                eNotifyHandler notifyHandlerType = handler.GetHandlerType();
                if ((notifyHandlerType & notifyHandlerTypes) == notifyHandlerType)
                    eventHandlers.Add(handler);
            }

            result = NotifyToHandler<INotifyHandler>(eventHandlers, false, true, methodName, args);
            if (!string.IsNullOrEmpty(result))
                Global.Instance.LogError(result);
            else
                Global.Instance.Log($"NotifyToPluginHandler({methodName})");


            return result;
        }

        public string NotifyToEventHandler(params object[] args)
        {
            string result = string.Empty;

            List<INotifyHandler> eventHandlers = new List<INotifyHandler>();
            foreach (INotifyHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                if (!handler.IsActiveAndEnabled())
                    continue;

                eventHandlers.Add(handler);
            }

            result = NotifyToHandler<INotifyHandler>(eventHandlers, false, true, "OnNotify", args);
            if (!string.IsNullOrEmpty(result))
                Global.Instance.LogError(result);
            else
                Global.Instance.Log($"NotifyToPluginHandler(OnNotify)");

            return result;
        }

        public string NotifyToEventHandler(string methodName, params object[] args)
        {
            string result = string.Empty;

            List<INotifyHandler> eventHandlers = new List<INotifyHandler>();
            foreach(INotifyHandler handler in handlers)
            {
                if (handler == null)
                    continue;

                if (!handler.IsActiveAndEnabled())
                    continue;

                eventHandlers.Add(handler);
            }

            result = NotifyToHandler<INotifyHandler>(eventHandlers, false, true, methodName, args);
            if (!string.IsNullOrEmpty(result))
                Global.Instance.LogError(result);
            else
                Global.Instance.Log($"NotifyToPluginHandler({methodName})");


            return result;
        }

        protected string NotifyToHandler<T>(List<T> handlers, bool hasOverride, bool hasResultValue, string methodName, params object[] args)
        {
            if (handlers == null)
                return $"NotificationManager.NotifyToHandler -> null is handlers. method name :{methodName}";

            if (handlers.Count == 0)
                return $"NotificationManager.NotifyToHandler -> zero count handler list. method name : {methodName}";

            System.Type[] types = new System.Type[args.Length];
            for(int i=0;i<types.Length;++i)
            {
                if (args[i] == null)
                    continue;

                types[i] = args[i].GetType();
            }

            string result = string.Empty;
            foreach(T handler in handlers)
            {
                if (handler == null)
                    continue;

                MethodInfo method = handler.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                                                System.Type.DefaultBinder, types, null);

                bool returnValue = false;
                bool IsNotify = false;

                if(method != null)
                {
                    try
                    {
                        IsNotify = true;
                        if(hasOverride)
                            IsNotify = method.IsOverride();

                        if(IsNotify)
                        {
                            var returnValueObject = method.Invoke(handler, args);
                            if (hasResultValue && returnValueObject != null)
                                returnValue = System.Convert.ToBoolean(returnValueObject);
                        }
                    }
                    catch(System.Exception ex)
                    {
                        System.Exception ex2 = ex.InnerException;
                        while (ex2 != null)
                        {
                            Global.Instance.LogError(ex2.Message + ex2.StackTrace);
                            ex2 = ex2.InnerException;
                        }
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(result))
                        return $"NotificationManager.NotifyToHandler -> not find method. handler name : {handler}, method name : {methodName}";
                    else
                        return $"{result}NotificationManager.NotifyToHandler -> not find method. handler name : {handler}, method name : {methodName}";
                }

                if (returnValue)
                    return result;
            }

            return result;
        }
    }
}
