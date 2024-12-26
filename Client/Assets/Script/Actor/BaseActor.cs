using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    public abstract class BaseActor 
    {
        public virtual void OnInit() { }
        public abstract void OnEnter();
        public abstract void OnUpdate(float dt);
        public virtual void OnLateUpdate(float dt) { }
        public virtual void Enable() { }
        public virtual void Disable() { }
    }
}

