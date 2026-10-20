using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public string name;

        public static void DestroyImmediate(Object value)
        {
            var component = (Component)value;
            component.gameObject.Components.Remove(component);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform parent;
        public int childCount => Children.Count;
        public Transform GetChild(int index) => Children[index];
        public int GetSiblingIndex() => parent.Children.IndexOf(this);
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform;
        public bool activeSelf = true;

        public GameObject(string name = "object")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this };
            Components.Add(value);
            return value;
        }

        public T GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            var values = new List<T>();
            Collect(this, includeInactive, values);
            return values.ToArray();
        }

        private static void Collect<T>(GameObject root, bool inactive, List<T> values) where T : Component
        {
            values.AddRange(root.Components.OfType<T>());
            foreach (var child in root.transform.Children)
            {
                if (inactive || child.gameObject.activeSelf)
                    Collect(child.gameObject, inactive, values);
            }
        }

        public void AddChild(GameObject child)
        {
            child.transform.parent = transform;
            transform.Children.Add(child.transform);
        }
    }

    public struct Vector2
    {
        public static Vector2 zero => default;
        public Vector2(float x, float y) { }
    }

    public enum EventType { DragUpdated, DragPerform }
    public class Event { public static Event current = new Event(); public EventType type; }
    public static class GUILayout
    {
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginVertical() { }
        public static void EndVertical() { }
        public static object Width(float value) => null;
        public static object Height(float value) => null;
        public static void Label(string value, params object[] options) { }
        public static void Space(float value) { }
        public static bool Button(string value, params object[] options) => false;
    }
}

namespace UnityEngine.UI
{
    public class Button : UnityEngine.Component { }
}

namespace ProjectT.Sound
{
    public class UIClickSound : UnityEngine.Component
    {
        public string ClickSoundType { get; set; }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public class EditorWindow : Object
    {
        public Vector2 minSize;
        public static T GetWindow<T>(string title) where T : new() => new T();
        public void Repaint() { }
    }
    public class MenuItem : Attribute { public MenuItem(string name) { } }
    public enum DragAndDropVisualMode { Copy }
    public static class DragAndDrop
    {
        public static DragAndDropVisualMode visualMode;
        public static Object[] objectReferences = Array.Empty<Object>();
        public static void AcceptDrag() { }
    }
    public static class EditorStyles { public static object boldLabel; public static object miniLabel; }
    public static class EditorGUILayout
    {
        public static Vector2 BeginScrollView(Vector2 value, params object[] options) => value;
        public static void EndScrollView() { }
        public static void ObjectField(Object value, Type type, bool scene) { }
        public static string TextField(string title, string value, params object[] options) => value;
    }
    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok, string cancel = null) => true;
    }
    public static class AssetDatabase
    {
        public static readonly Dictionary<string, GameObject> Assets = new Dictionary<string, GameObject>();
        public static string GetAssetPath(Object value) => Assets.FirstOrDefault(x => x.Value == value).Key ?? string.Empty;
        public static string[] FindAssets(string filter, string[] folders) => Assets.Keys.ToArray();
        public static string GUIDToAssetPath(string guid) => guid;
        public static T LoadAssetAtPath<T>(string path) where T : Object => (T)(Object)Assets[path];
    }
    public static class PrefabUtility
    {
        public static readonly List<string> Trace = new List<string>();
        public static readonly HashSet<GameObject> Loaded = new HashSet<GameObject>();
        public static Exception LoadError, SaveError, UnloadError;

        public static GameObject LoadPrefabContents(string path)
        {
            Trace.Add("load:" + path);
            if (LoadError != null)
                throw LoadError;
            var root = Clone(AssetDatabase.Assets[path]);
            Loaded.Add(root);
            return root;
        }

        public static GameObject SaveAsPrefabAsset(GameObject root, string path)
        {
            Trace.Add("save:" + path);
            if (SaveError != null)
                throw SaveError;
            CopyTo(root, AssetDatabase.Assets[path]);
            return AssetDatabase.Assets[path];
        }

        public static void UnloadPrefabContents(GameObject root)
        {
            Trace.Add("unload");
            Loaded.Remove(root);
            if (UnloadError != null)
                throw UnloadError;
        }

        public static void Reset()
        {
            Trace.Clear();
            Loaded.Clear();
            AssetDatabase.Assets.Clear();
            LoadError = SaveError = UnloadError = null;
        }

        // 프리팹 직렬화를 흉내 낸다. 툴이 다루는 컴포넌트만 복사한다.
        private static GameObject Clone(GameObject source)
        {
            var copy = new GameObject(source.name);
            CopyTo(source, copy);
            return copy;
        }

        private static void CopyTo(GameObject source, GameObject target)
        {
            target.activeSelf = source.activeSelf;
            target.Components.Clear();
            target.transform.Children.Clear();
            foreach (var component in source.Components)
            {
                if (component is UnityEngine.UI.Button)
                    target.AddComponent<UnityEngine.UI.Button>();
                else if (component is ProjectT.Sound.UIClickSound sound)
                    target.AddComponent<ProjectT.Sound.UIClickSound>().ClickSoundType = sound.ClickSoundType;
            }
            foreach (var child in source.transform.Children)
                target.AddChild(Clone(child.gameObject));
        }
    }
}
