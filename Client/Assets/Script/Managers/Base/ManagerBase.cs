using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    public abstract class ManagerBase
    {
        protected string m_name = string.Empty;
        public string Name { get => m_name; set => m_name = value; }

        protected Transform m_rootObject = null;
        public Transform RootObject { get => m_rootObject; set => m_rootObject = value; }

        protected void CreateRootObject(Transform transform, string name)
        {
            if (m_rootObject == null)
            {
                m_rootObject = new GameObject(name).transform;
            }

            m_rootObject.transform.SetParent(transform, true);
        }

        protected void DestoryRootObject()
        {
            if (m_rootObject != null)
            {
                GameObject.Destroy(m_rootObject);
                m_rootObject = null;
            }
        }

        public abstract void OnEnter();
        public abstract void OnLeave();
        public abstract void OnFixedUpdate(float dt);
        public abstract void OnUpdate(float dt);
        public abstract void OnLateUpdate();

        public abstract void OnAppStart();
        public abstract void OnAppEnd();

        public abstract void OnAppFocuse(bool focused);

        public abstract void OnAppPause(bool paused);
    }
}
