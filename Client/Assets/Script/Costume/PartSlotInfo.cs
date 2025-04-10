using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.Costume
{
    [System.Serializable]
    public class PartSlotInfo
    {
        public int Index;
        public Transform Slot;
        public Renderer Renderer;
        public bool IsSkinnedMesh;

        private PartAssetData? assetData;
        public PartAssetData? AssetData => assetData;

        internal void SetAssetData(PartAssetData? assetData)
        {
            this.assetData = assetData;
        }
    }
}