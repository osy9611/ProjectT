using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace ProjectT.Pivot
{
    public enum PivotType
    {
        Effect,
        UI,
        ProjectTile
    }

    [System.Serializable]
    public class PivotInfo
    {
        [SerializeField] private PivotType type;
        public PivotType Type => type;

        [SerializeField] private int id;
        public int Id => id;

        [SerializeField] private Transform pivotTr;
        public Transform PivotTr => pivotTr;

        [SerializeField] private bool setParent;
        public bool SetParent => setParent;

        [SerializeField] private Vector3 posOffset;
        public Vector3 PosOffset;

        [SerializeField] private Vector3 rotOffset;
        public Vector3 RotOffset => rotOffset;

        [SerializeField] private Vector3 scaleOffset = Vector3.one;
        public Vector3 ScaleOffset => scaleOffset;

        public PivotInfo(PivotType type, int id, Transform pivotTr,bool setParent, Vector3 posOffset, Vector3 rotOffset, Vector3 scaleOffset)
        {
            this.type = type;
            this.id = id;
            this.pivotTr = pivotTr;
            this.setParent = setParent;
            this.posOffset = posOffset;
            this.rotOffset = rotOffset;
            this.scaleOffset = scaleOffset;
        }
    }


    public class ComPivotAgent : MonoBehaviour
    {
        [SerializeField] private List<PivotInfo> pivots = new List<PivotInfo>();

        public List<PivotInfo> Pivots => pivots;

        private Dictionary<int, PivotInfo> dic_Pivot = new Dictionary<int, PivotInfo>();

        private void Awake()
        {
            for(int i=0;i<pivots.Count;++i)
            {
                if (!dic_Pivot.ContainsKey(pivots[i].Id))
                    dic_Pivot.Add(pivots[i].Id, pivots[i]);
            }
        }

        public PivotInfo GetPivotInfo(int id)
        {
            if (dic_Pivot.TryGetValue(id, out var pivot))
                return pivot;

            return null;
        }

        public Vector3 GetPos(int id)
        {
            var info = GetPivotInfo(id);
            return info.PivotTr.position + info.PosOffset;
        }

#if UNITY_EDITOR
        public void GenerateData()
        {

        }
#endif
    }

}