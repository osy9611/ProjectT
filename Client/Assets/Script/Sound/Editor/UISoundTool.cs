using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
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

            public UIClickSoundInfo(UIClickSound uiClickSound)
            {
                if (uiClickSound == null)
                    return;
                ClickSound = uiClickSound;
                ClickSoundType = uiClickSound.ClickSoundType;
            }

            public void SetType()
            {
                ClickSound.ClickSoundType = ClickSoundType;
            }
        }

        private string selectedFolderPath;
        private List<GameObject> prefabs = new List<GameObject>();
        private int selectPrefabIndex = -1;
        private Dictionary<GameObject, UIClickSoundInfo> prefabSounds = new Dictionary<GameObject, UIClickSoundInfo>();
        private Vector2 prefabListPos = Vector2.zero;
        private Vector2 prefabComInfoPos = Vector2.zero;
        private string setSoundType = DesignEnum.SoundList.None.ToString();

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

                    if (GUILayout.Button("Remove", GUILayout.Width(80)))
                    {
                        RemovePrefabIndex(i);
                    }

                    if (GUILayout.Button("Show", GUILayout.Width(80)))
                    {
                        GetPrefabComponentList(i);
                    }

                    GUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(10);

            setSoundType = EditorGUILayout.TextField("Sound Type :", setSoundType, GUILayout.Width(300));

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
            GUILayout.EndHorizontal();
        }

        private void ShowPrefabInnerList()
        {
            GUILayout.BeginVertical();
            string selectPrefabName = string.Empty;

            if (selectPrefabIndex != -1 && selectPrefabIndex < prefabs.Count)
                selectPrefabName = prefabs[selectPrefabIndex].name;

            GUILayout.Label($"Components : " + selectPrefabName, EditorStyles.boldLabel);

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
                if (prefabs.Count == 0)
                {
                    EditorUtility.DisplayDialog("Warning", "프리팹 리스트가 비어있습니다.", "OK");
                }

                if (EditorUtility.DisplayDialog("Info", "프리팹을 변경하면 다른 프리팹에 영향이 있을 수 있습니다 그래도 바꾸시겠습니까?", "OK", "Cancel"))
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
            if (index == -1)
                return;

            if (prefabs[index] == null)
                return;

            Button[] buttons = prefabs[index].GetComponentsInChildren<Button>(true);

            if (buttons == null)
                return;


            selectPrefabIndex = index;
            prefabSounds.Clear();

            foreach (var button in buttons)
            {
                var uiClickSound = button.gameObject.GetComponent<UIClickSound>();
                if (!prefabSounds.ContainsKey(button.gameObject))
                    prefabSounds.Add(button.gameObject, new UIClickSoundInfo(uiClickSound));
            }
        }

        private void AddClickSound()
        {
            foreach (var prefab in prefabs)
            {
                GameObject clonePrefab = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

                if (clonePrefab == null)
                    continue;

                Button[] buttons = prefab.GetComponentsInChildren<Button>(true);

                if (buttons == null)
                    continue;

                foreach (var button in buttons)
                {
                    var uiClickSound = button.gameObject.GetComponent<UIClickSound>();
                    if (uiClickSound == null)
                    {
                        var origin = PrefabUtility.GetCorrespondingObjectFromSource(button);
                        if (origin != null)
                        {
                            if (origin.GetComponent<UIClickSound>() == null)
                                continue;

                        }

                        uiClickSound = button.gameObject.AddComponent<UIClickSound>();
                        uiClickSound.ClickSoundType = setSoundType;
                    }
                }

                PrefabUtility.SavePrefabAsset(prefab);
                DestroyImmediate(clonePrefab);
            }
        }

        private void RemoveClickSound()
        {
            for (int i = 0; i < prefabs.Count; ++i)
            {
                GameObject clonePrefab = PrefabUtility.InstantiatePrefab(prefabs[i]) as GameObject;

                if (clonePrefab == null)
                    continue;

                UIClickSound[] uiClickSounds = clonePrefab.GetComponentsInChildren<UIClickSound>(true);

                if (uiClickSounds == null)
                    continue;

                foreach (var component in uiClickSounds)
                {
                    DestroyImmediate(component, true);
                }

                string prefabPath = AssetDatabase.GetAssetPath(prefabs[i]);

                PrefabUtility.SaveAsPrefabAsset(clonePrefab, prefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                DestroyImmediate(clonePrefab, true);
            }
        }

        private void ChangeClickSound()
        {
            UIClickSound[] uiClickSounds = prefabs[selectPrefabIndex].GetComponentsInChildren<UIClickSound>(true);

            if (uiClickSounds == null)
                return;

            foreach (var component in uiClickSounds)
            {
                if (prefabSounds.TryGetValue(component.gameObject, out var info))
                {
                    info.SetType();
                }
            }

            PrefabUtility.SavePrefabAsset(prefabs[selectPrefabIndex]);
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
                prefabs.RemoveAt(index);
            }
        }

        void OnSelectionChange()
        {
            Repaint();
        }

    }
}

