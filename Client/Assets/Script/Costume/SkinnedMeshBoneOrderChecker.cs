using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{
    public class SkinnedMeshBoneOrderChecker : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var meshrenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            Vector3 before = meshrenderer.bones[0].position;
            for (int i = 0, range = meshrenderer.bones.Length; i < range; ++i)
            {
                Gizmos.DrawLine(meshrenderer.bones[i].position, before);
                UnityEditor.Handles.Label(meshrenderer.bones[i].transform.position, i.ToString());
                before = meshrenderer.bones[i].position;
            }
        }
#endif
    }

}   