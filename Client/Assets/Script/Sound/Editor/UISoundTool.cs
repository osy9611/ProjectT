using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectT.Sound
{
    

    public class UISoundTool : EditorWindow
    {
        //UI 클릭 컴포넌트 정보 관련 클래스
        class UIClickSoundInfo
        {
            public UIClickSound ClickSound;
            public string ClickSoundType = string.Empty;
            public int[] SiblingPath;

            public UIClickSoundInfo(UIClickSound uiClickSound, Transform prefabRoot)
            {
                if (uiClickSound == null)
                    return;

                ClickSound = uiClickSound;
                ClickSoundType = uiClickSound.ClickSoundType;
                var siblingPath = new List<int>();
                Transform current = uiClickSound.transform;
                while (current != prefabRoot)
                {
                    if (current.parent == null)
                        return;

                    siblingPath.Add(current.GetSiblingIndex());
                    current = current.parent;
                }

                siblingPath.Reverse();
                SiblingPath = siblingPath.ToArray();
            }

            public void SetType(GameObject prefabRoot)
            {
                if (SiblingPath == null)
                    return;

                Transform target = prefabRoot.transform;
                foreach (int siblingIndex in SiblingPath)
                {
                    if (siblingIndex < 0 || siblingIndex >= target.childCount)
                        return;

                    target = target.GetChild(siblingIndex);
                }

                UIClickSound clickSound = target.GetComponent<UIClickSound>();
                if (clickSound != null)
                    clickSound.ClickSoundType = ClickSoundType;
            }
        }

        private string selectedFolderPath;
        private List<GameObject> prefabs = new List<GameObject>();
        private int selectPrefabIndex = -1;
        private Dictionary<GameObject, UIClickSoundInfo> prefabSounds = new Dictionary<GameObject, UIClickSoundInfo>();
        private Vector2 prefabListPos = Vector2.zero;
        private Vector2 prefabComInfoPos = Vector2.zero;
        private string setSoundType = string.Empty;

        [MenuItem("Tools/ClickSound")]
        static public void ShowWindow()
        {
            var window = GetWindow<UISoundTool>("UISoundTool");
            window.minSize = new Vector2(1000, 400);  // 최소 크기 설정
        }

        private void OnGUI()
        {
            GUILayout.BeginHorizontal();
            SelectFolder();
            SelectPrefabList();
            ShowPrefabInnerList();
            GUILayout.EndHorizontal();
        }

        private void SelectFolder()
        {
            GUILayout.BeginVertical();
            Event dragAndDropEvent = Event.current;

            if (dragAndDropEvent.type == EventType.DragUpdated || dragAndDropEvent.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (dragAndDropEvent.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();

                    foreach (var draggedObject in DragAndDrop.objectReferences)
                    {
                        string folderPath = AssetDatabase.GetAssetPath(draggedObject);
                        if (Directory.Exists(folderPath))
                        {
                            selectedFolderPath = folderPath;
                            break;
                        }
                    }
                }
            }

            GUILayout.Label($"Select Folder", GUILayout.Width(300));
            GUILayout.Label($"{selectedFolderPath}", EditorStyles.boldLabel, GUILayout.Width(300));

            GUILayout.Space(10);

            if (GUILayout.Button("Find", GUILayout.Width(300)))
            {
                GetPrefabList();
            }
            GUILayout.EndVertical();
        }

        private void SelectPrefabList()
        {
            GUILayout.BeginVertical();

            GUILayout.Label("Prefab List", EditorStyles.boldLabel);

            if (prefabs != null)
            {
                prefabListPos = EditorGUILayout.BeginScrollView(prefabListPos, GUILayout.Width(300), GUILayout.Height(300));
                for (int i = 0; i < prefabs.Count; ++i)
                {
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
                    bool removed = false;

                    if (GUILayout.Button("Remove", GUILayout.Width(80)))
                    {
                        RemovePrefabIndex(i);
                        removed = true;
                    }

                    if (!removed && GUILayout.Button("Show", GUILayout.Width(80)))
                    {
                        GetPrefabComponentList(i);
                    }

                    GUILayout.EndHorizontal();

                    if (removed)
                        break;
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(10);

            setSoundType = EditorGUILayout.TextField("Addressable AudioClip Path :", setSoundType, GUILayout.Width(300));

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("AddClickSound", GUILayout.Width(150)))
            {
                if (EditorUtility.DisplayDialog("Info", "작업중인 프리팹을 전부 닫아주세요!", "OK"))
                {
                    AddClickSound();
                    GetPrefabComponentList(selectPrefabIndex);
                    EditorUtility.DisplayDialog("Info", "Complete!", "OK");
                }
            }
            GUILayout.Space(2);
            if (GUILayout.Button("RemoveClickSound", GUILayout.Width(150)))
            {
                RemoveClickSound();
                GetPrefabComponentList(selectPrefabIndex);
                EditorUtility.DisplayDialog("Info", "Complete!", "OK");
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void ShowPrefabInnerList()
        {
            GUILayout.BeginVertical();
            string selectPrefabName = string.Empty;

            if (IsValidPrefabIndex(selectPrefabIndex) && prefabs[selectPrefabIndex] != null)
                selectPrefabName = prefabs[selectPrefabIndex].name;

            GUILayout.Label($"Components : " + selectPrefabName, EditorStyles.boldLabel);
            GUILayout.Label("Sound value: Addressable AudioClip Path", EditorStyles.miniLabel);

            if (prefabSounds != null)
            {
                prefabComInfoPos = EditorGUILayout.BeginScrollView(prefabComInfoPos, GUILayout.Width(400), GUILayout.Height(300));

                foreach (var compoent in prefabSounds)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(compoent.Key.name, GUILayout.Width(150));

                    if (compoent.Value.ClickSound == null)
                    {
                        GUILayout.Label("Null", GUILayout.Width(150));
                    }
                    else
                    {
                        compoent.Value.ClickSoundType = EditorGUILayout.TextField("", compoent.Value.ClickSoundType, GUILayout.Width(150));
                    }

                    GUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(35.59f);

            if (GUILayout.Button("Change", GUILayout.Width(300)))
            {
                if (!IsValidPrefabIndex(selectPrefabIndex) || prefabs[selectPrefabIndex] == null)
                {
                    EditorUtility.DisplayDialog("Warning", "변경할 프리팹을 선택해 주세요.", "OK");
                }
                else if (EditorUtility.DisplayDialog("Info", "프리팹을 변경하면 다른 프리팹에 영향이 있을 수 있습니다 그래도 바꾸시겠습니까?", "OK", "Cancel"))
                {
                    ChangeClickSound();
                    EditorUtility.DisplayDialog("Info", "Complete!", "OK");
                }
            }

            GUILayout.EndVertical();
        }

        private void GetPrefabList()
        {
            if (string.IsNullOrEmpty(selectedFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "폴더를 선택해 주세요", "OK");
                return;
            }

            selectPrefabIndex = -1;
            prefabSounds.Clear();

            prefabs.Clear();
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new string[] { selectedFolderPath });
            for (int i = 0; i < prefabGuids.Length; ++i)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);

                GameObject prefabObj = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                prefabs.Add(prefabObj);
            }
        }

        private void GetPrefabComponentList(int index)
        {
            if (!IsValidPrefabIndex(index))
                return;

            if (prefabs[index] == null)
                return;

            selectPrefabIndex = index;
            prefabSounds.Clear();

            UIClickSound[] clickSounds = prefabs[index].GetComponentsInChildren<UIClickSound>(true);
            foreach (var clickSound in clickSounds)
            {
                if (!prefabSounds.ContainsKey(clickSound.gameObject))
                    prefabSounds.Add(clickSound.gameObject, new UIClickSoundInfo(clickSound, prefabs[index].transform));
            }
        }

        private void AddClickSound()
        {
            foreach (var prefab in prefabs)
            {
                if (prefab == null)
                    continue;

                string prefabPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                    continue;

                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    Button[] buttons = prefabRoot.GetComponentsInChildren<Button>(true);
                    foreach (var button in buttons)
                    {
                        UIClickSound uiClickSound = button.gameObject.GetComponent<UIClickSound>();
                        if (uiClickSound != null)
                            continue;

                        uiClickSound = button.gameObject.AddComponent<UIClickSound>();
                        uiClickSound.ClickSoundType = setSoundType;
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
                finally
                {
                    if (prefabRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private void RemoveClickSound()
        {
            for (int i = 0; i < prefabs.Count; ++i)
            {
                GameObject prefab = prefabs[i];
                if (prefab == null)
                    continue;

                string prefabPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                    continue;

                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    UIClickSound[] uiClickSounds = prefabRoot.GetComponentsInChildren<UIClickSound>(true);
                    foreach (var component in uiClickSounds)
                        DestroyImmediate(component);

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                }
                finally
                {
                    if (prefabRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        private void ChangeClickSound()
        {
            if (!IsValidPrefabIndex(selectPrefabIndex) || prefabs[selectPrefabIndex] == null)
                return;

            string prefabPath = AssetDatabase.GetAssetPath(prefabs[selectPrefabIndex]);
            if (string.IsNullOrEmpty(prefabPath))
                return;

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                foreach (var info in prefabSounds.Values)
                    info.SetType(prefabRoot);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            GetPrefabComponentList(selectPrefabIndex);
        }

        private void RemovePrefabIndex(int index)
        {
            if (index >= 0 && index < prefabs.Count)
            {
                if (selectPrefabIndex == index)
                {
                    prefabSounds.Clear();
                    selectPrefabIndex = -1;
                }
                else if (selectPrefabIndex > index)
                {
                    selectPrefabIndex--;
                }

                prefabs.RemoveAt(index);
            }
        }

        private bool IsValidPrefabIndex(int index)
        {
            return index >= 0 && index < prefabs.Count;
        }

        void OnSelectionChange()
        {
            Repaint();
        }

    }
}

