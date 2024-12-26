using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Scene
{
    public abstract class SceneBase : MonoBehaviour
    {
        private float currentProgress = 0.0f;

        public List<SceneBase> SubScenes = new List<SceneBase>();

        public string SceneFileName;

        #region Methods
        public virtual async UniTask OnEnter(float progress,params object[] data) { }
        public virtual void OnExit() { }

        public abstract void OnInitialize();

        public abstract void OnFinalize();

        public virtual void OnUpdate(float dt) { }

        public virtual async UniTask LoadAdditiveScene(System.Action callback) { }
        #endregion
    }

}
