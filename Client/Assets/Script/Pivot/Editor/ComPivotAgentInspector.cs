using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectT.Pivot
{
    [CustomEditor(typeof(ComPivotAgent))]
    public class ComPivotAgentInspector : Editor
    {
        ComPivotAgent script;

        public override void OnInspectorGUI()
        {
            if(GUILayout.Button("Generate Pivot"))
            {
                script.GenerateData();
            }
            base.OnInspectorGUI();
        }

        private void OnEnable()
        {
            script = (ComPivotAgent)target;
        }
    }
}
