using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Util
{
    public static class ComUtilFunc
    {
        public static T GetOrAddChildComponent<T>(GameObject go) where T : Component
        {
            if (go == null)
                return null;

            T component = go.GetComponentInChildren<T>();
            if (component == null)
            {
                component = AddChildComponent<T>(go);
            }

            return component;
        }

        public static T AddChildComponent<T>(GameObject parent) where T : Component
        {
            GameObject go = new GameObject(typeof(T).ToString());

            if (parent != null)
            {
                go.transform.SetParent(parent.transform, true);

                go.transform.localPosition = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            return go.AddComponent<T>();
        }

        public static GameObject FindChild(GameObject go, string name = null, bool recursive = false)
        {
            Transform transform = FindChild<Transform>(go, name, recursive);
            if (transform == null)
                return null;

            return transform.gameObject;
        }

        public static T FindChild<T>(GameObject go, string name = null, bool recursive = false) where T : UnityEngine.Object
        {
            if (go == null)
                return null;

            if (!recursive)
            {
                for (int i = 0, range = go.transform.childCount; i < range; ++i)
                {
                    Transform transform = go.transform.GetChild(i);
                    if (string.IsNullOrEmpty(name) || transform.name == name)
                    {
                        T component = transform.GetComponent<T>();
                        if (component != null)
                            return component;
                    }
                }
            }
            else
            {
                foreach (T component in go.GetComponentsInChildren<T>())
                {
                    if (string.IsNullOrEmpty(name) || component.name == name)
                        return component;
                }
            }

            return null;
        }

        public static void SetLayer(GameObject root, int layerNumber, bool isChild = true)
        {
            if (root == null)
                return;

            if (isChild == true)
            {
                Transform[] childs = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < childs.Length; ++i)
                {
                    childs[i].gameObject.layer = layerNumber;
                }
            }
            else
            {
                root.layer = layerNumber;
            }
        }
    }

}

