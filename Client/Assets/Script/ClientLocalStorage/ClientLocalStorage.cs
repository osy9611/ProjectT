using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace ProjectT
{
    [Serializable]
    public abstract class ClientLocalStorage
    {
        private EClientLocalStorageType storageType;
        public EClientLocalStorageType StorageType { get => storageType; set => storageType = value; }

        public virtual void Init()
        {

        }

        public virtual void Save(string rootPath, FileMode streamType = FileMode.Create)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                Global.Instance.LogError($"[ClientLocalStorage] Save Fail RootPath Is Null");
                return;
            }

            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream($"{rootPath}/{StorageType.ToString()}.dat", streamType);
            formatter.Serialize(stream, this);
            stream.Close();

            CompleteSave();
        }

        public abstract void CompleteSave();

        public static ClientLocalStorage Load(string rootPath, EClientLocalStorageType Type)
        {
            ClientLocalStorage Result = null;

            BinaryFormatter formatter = new BinaryFormatter();
            FileStream stream = new FileStream($"{rootPath}/{Type.ToString()}.dat", FileMode.Open);
            Result = (ClientLocalStorage)formatter.Deserialize(stream);
            stream.Close();
            return Result;
        }

        public abstract void CompleteLoad();
    }

}