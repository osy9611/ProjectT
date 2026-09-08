using ProjectT;
using ProjectT.Costume;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT
{

    public class CostumeManager : ManagerBase
    {
        public bool ChangeColor(ComCostumeAgent agent, int partIdx, Color color, IArgs args = null)
        {
            if (agent == null)
                return false;

            PartAssetData data = PartAssetData.Create(0, color, args);
            PartAssetData? result = null;
            agent.ChangeOrAttach(data, out result);

            return true;
        }

        public bool ChangeMaterial(ComCostumeAgent agent, int partIdx, Material material, IArgs args = null)
        {
            if (agent == null)
                return false;

            PartAssetData data = PartAssetData.Create(0, material, args);
            PartAssetData? result = null;
            agent.ChangeOrAttach(data, out result);

            return true;
        }

        public bool ChangeGameObject(ComCostumeAgent agent, int partIdx, GameObject gameobject, IArgs args = null)
        {
            if (agent == null)
                return false;

            PartAssetData data = PartAssetData.Create(0, gameobject, args);
            PartAssetData? result = null;
            agent.ChangeOrAttach(data, out result);

            return true;
        }

        public bool ChangeMeshRenderer(ComCostumeAgent agent, int partIdx, MeshRenderer renderer, bool? sameBoneOrder = null, IArgs args = null)
        {
            if (agent == null)
                return false;

            PartAssetData data;
            PartAssetData? result = null;
            if (sameBoneOrder == null)
            {
                data = PartAssetData.Create(partIdx, renderer, args);
            }
            else
            {
                data = PartAssetData.Create(partIdx, renderer, (bool)sameBoneOrder, args);
            }

            agent.ChangeOrAttach(data, out result);

            return true;
        }

        public bool SkinnedMeshRenderer(ComCostumeAgent agent, int partIdx, SkinnedMeshRenderer renderer, bool? sameBoneOrder = null, IArgs args = null)
        {
            if (agent == null)
                return false;

            PartAssetData data;
            PartAssetData? result = null;
            if (sameBoneOrder == null)
            {
                data = PartAssetData.Create(partIdx, renderer, args);
            }
            else
            {
                data = PartAssetData.Create(partIdx, renderer, (bool)sameBoneOrder, args);
            }

            agent.ChangeOrAttach(data, out result);

            return true;
        }
    }

}