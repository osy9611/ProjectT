using ProjectT.Pivot;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.UGUI
{
    public class ComHudAgent : MonoBehaviour
    {
        protected PivotInfo pivotInfo;

        [SerializeField]
        protected Animation ani;

        protected AnimationClip[] animationClips;

        public System.Action<GameObject, bool> OnDestory;

        protected void Start()
        {
            OnEnter();
        }

        protected virtual void OnEnter()
        {

        }

        protected virtual void OnUpdate(float dt)
        {
            CalcTransform();
        }

        protected virtual void OnLeave()
        {

        }


        protected virtual void LoadAni()
        {
            if (ani != null)
            {
                animationClips = new AnimationClip[ani.GetClipCount()];

                int i = 0;
                
                foreach(AnimationState state in ani)
                {
                    animationClips[i] = state.clip;
                    i++;
                }
            }
        }

        protected virtual void AniShowByIndex()
        {
            int aniIdx = UnityEngine.Random.Range(0, animationClips.Length);
            ani.clip = animationClips[aniIdx];
            ani.Play();
        }

        protected virtual void CalcTransform()
        {
            if (pivotInfo == null)
                return;
            transform.position = Camera.main.WorldToScreenPoint(pivotInfo.PivotTr.position);
        }

        public virtual void RegisterInfo(PivotInfo pivotInfo)
        {
            this.pivotInfo = pivotInfo;
        }

    }

}