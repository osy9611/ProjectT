using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProjectT.Addressable
{
    public class IResource
    {
        protected string path;
        public string Path => path;

        private object resourceData;
        public object ResourceData => resourceData; 

        private bool dontDestroy;
        public bool DontDestroy { get => dontDestroy; set => dontDestroy = value; }

        public IResource(string path, object resourceData)
        {
            this.path = path;
            this.resourceData = resourceData;
        }
    }
}
