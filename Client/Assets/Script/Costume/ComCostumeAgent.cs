using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.Costume
{

    public class ComCostumeAgent : MonoBehaviour
    {
        [SerializeField] int id;
        [SerializeField] List<PartSlotInfo> slots;

        public int Id => id;
        public IReadOnlyList<PartSlotInfo> Slots => slots;

        public PartSlotInfo this[int partIndex]
        {
            get
            {
                if(partIndex >= 0 && partIndex < slots.Count)
                {
                    Global.LogError($"part' index is incorrect");
                }
                UnityEngine.Assertions.Assert.IsTrue(partIndex >= 0 && partIndex < slots.Count, "part' index is incorrect");
                return slots[partIndex];
            }
        }

        public bool TryDetach(int partIndex, out PartAssetData? preAssetData)
        {
            if(partIndex >= 0 && partIndex < slots.Count)
            {
                Global.LogError($"part' index is incorrect");
            }

            var slotInfo = slots[partIndex];
            var renderer = slotInfo.Slot.GetComponent<Renderer>();

            if(renderer == null)
            {
                PartAssetData? oldAssetData = slots[partIndex].AssetData;

                DetachFirstChildInternal(slotInfo.Slot);

                preAssetData = oldAssetData;

                slotInfo.SetAssetData(null);

                return true;
            }

            preAssetData = null;
            return false;
        }

        internal void ChangeOrAttach(PartAssetData assetData, out PartAssetData? preAssetData)
        {
            if (assetData.PartIndex >= 0 && assetData.PartIndex < slots.Count)
            {
                Global.LogError($"part' index is incorrect");
            }

            PartAssetData? oldAssetData = slots[assetData.PartIndex].AssetData;
            ChangeOrAttach(assetData);

            preAssetData = oldAssetData;
        }

        internal void ChangeOrAttach(PartAssetData assetData)
        {
            UnityEngine.Assertions.Assert.IsTrue(assetData.PartIndex >= 0 && assetData.PartIndex < slots.Count, "part' index is incorrect");

            var slotInfo = slots[assetData.PartIndex];

            switch (assetData.AssetType)
            {
                case PartAssetType.Renderer_MeshOrSkin:
                    ChangeRendererInternal(slotInfo, assetData);
                    break;
                case PartAssetType.GameObject:
                    AttachGameObjectInternal(slotInfo.Slot, assetData.GameObject);
                    break;
                case PartAssetType.Color:
                    if (!TryChangeColorInternal(slotInfo.Renderer, assetData))
                        return;
                    break;
                case PartAssetType.Material:
                    if (!TryChangeMaterialInternal(slotInfo.Renderer, assetData))
                        return;
                    break;
            }

            slotInfo.SetAssetData(assetData);
        }

        private void ChangeRendererInternal(PartSlotInfo slotInfo, PartAssetData assetData)
        {
            if (slotInfo.IsSkinnedMesh)
            {
                var srcRenderer = slotInfo.Renderer as SkinnedMeshRenderer;
                var newRenderer = assetData.Renderer as SkinnedMeshRenderer;

                srcRenderer.sharedMesh = newRenderer.sharedMesh;
                srcRenderer.sharedMaterial = newRenderer.sharedMaterial;

                if (!assetData.SameBoneOrder)
                {
                    Transform[] childs = transform.GetComponentsInChildren<Transform>(true);

                    Transform[] bones = new Transform[newRenderer.bones.Length];

                    for (int boneOrder = 0, range = newRenderer.bones.Length; boneOrder < range; ++boneOrder)
                    {
                        bones[boneOrder] = System.Array.Find<Transform>(childs, c => c.name == newRenderer.bones[boneOrder].name);
                    }

                    srcRenderer.bones = bones;
                }
            }
            else
            {
                var srcRenderer = slotInfo.Renderer as MeshRenderer;
                var newRenderer = slotInfo.Renderer as MeshRenderer;

                srcRenderer.GetComponent<MeshFilter>().sharedMesh = newRenderer.GetComponent<MeshFilter>().sharedMesh;
                srcRenderer.sharedMaterial = newRenderer.sharedMaterial;
            }
        }

        private static void AttachGameObjectInternal(Transform slot, GameObject gameObject)
        {
            DetachFirstChildInternal(slot);

            if (gameObject != null)
            {
                gameObject.transform.parent = slot;
                gameObject.transform.localPosition = Vector3.zero;
                gameObject.transform.localRotation = Quaternion.identity;
                gameObject.transform.localScale = Vector3.one;
            }
        }

        private static bool TryChangeColorInternal(Renderer renderer, PartAssetData assetData)
        {
            if (assetData.Args == null)
            {
                renderer.material.color = assetData.Color.Value;
                return true;
            }

            if (!TryGetMaterialIndexInternal(renderer, assetData.Args, out int materialIndex))
                return false;

            renderer.materials[materialIndex].color = assetData.Color.Value;
            return true;
        }

        private static bool TryChangeMaterialInternal(Renderer renderer, PartAssetData assetData)
        {
            if (assetData.Args == null)
            {
                renderer.sharedMaterial = assetData.Material;
                return true;
            }

            if (!TryGetMaterialIndexInternal(renderer, assetData.Args, out int materialIndex))
                return false;

            renderer.sharedMaterials[materialIndex] = assetData.Material;
            return true;
        }

        private static bool TryGetMaterialIndexInternal(Renderer renderer, IArgs args, out int materialIndex)
        {
            materialIndex = 0;

            Args<int>? val = args as Args<int>?;
            if (!val.HasValue)
                return false;
            if (renderer.materials.Length - 1 < val.Value.Arg1 || val.Value.Arg1 < 0)
                return false;

            materialIndex = val.Value.Arg1;
            return true;
        }

        private static void DetachFirstChildInternal(Transform slot)
        {
            if (slot.childCount > 0)
                slot.GetChild(0).parent = null;
        }



#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var meshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            Vector3 before = meshRenderer.bones[0].position;
            for (int i = 0, range = meshRenderer.bones.Length; i < range; ++i)
            {
                Gizmos.DrawLine(meshRenderer.bones[i].position, before);
                UnityEditor.Handles.Label(meshRenderer.bones[i].transform.position, i.ToString());
                before = meshRenderer.bones[i].position;
            }
        }
#endif
    }

}