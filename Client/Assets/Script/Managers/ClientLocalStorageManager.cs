using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ProjectT
{
    public class ClientLocalStorageManager : ManagerBase
    {
        [Serializable]
        private sealed class StorageFile
        {
            public int Version;
            public string Data;
        }

        private sealed class StorageDefinition
        {
            public readonly Type Type;
            public readonly string FileName;
            public readonly int Version;
            public readonly Func<ClientLocalStorage> Create;

            public StorageDefinition(Type type, string fileName, int version, Func<ClientLocalStorage> create)
            {
                Type = type;
                FileName = fileName;
                Version = version;
                Create = create;
            }
        }

        private static readonly StorageDefinition[] StorageDefinitions =
        {
            new StorageDefinition(typeof(OptionStorage), "Option.json", 1, () => new OptionStorage())
        };

        private readonly bool loadData;
        private readonly string storageRootOverride;
        private readonly Dictionary<Type, ClientLocalStorage> StorageDatas = new Dictionary<Type, ClientLocalStorage>();
        private string assetFolderPath;

        public ClientLocalStorageManager(bool loadData = false, string storageRootOverride = null)
        {
            this.loadData = loadData;
            this.storageRootOverride = storageRootOverride;
        }

        protected override UniTask OnInitializeAsync(CancellationToken token)
        {
            assetFolderPath = storageRootOverride == null
                ? Path.Combine(Application.persistentDataPath, "ClientLocalStorage")
                : Path.GetFullPath(storageRootOverride);

            Directory.CreateDirectory(assetFolderPath);

            if (loadData)
                LoadAllDataInternal(token);

            return UniTask.CompletedTask;
        }

        protected override void OnShutdown(ShutdownReason reason)
        {
            try
            {
                if (reason != ShutdownReason.InitializationFailure)
                    SaveAllDataInternal();
            }
            finally
            {
                StorageDatas.Clear();
            }
        }

        public T GetOrCreateData<T>() where T : ClientLocalStorage
        {
            ThrowIfWorkUnavailableInternal();

            StorageDefinition definition = GetDefinitionInternal(typeof(T));

            if (StorageDatas.TryGetValue(definition.Type, out var storage))
                return (T)storage;

            // 기존 파일을 먼저 읽어야 loadData=false에서 기본값이 저장 데이터를 덮어쓰지 않는다.
            storage = LoadStorageInternal(definition) ?? definition.Create();
            StorageDatas.Add(definition.Type, storage);
            return (T)storage;
        }

        public void SaveData<T>() where T : ClientLocalStorage
        {
            ThrowIfWorkUnavailableInternal();

            StorageDefinition definition = GetDefinitionInternal(typeof(T));

            if (!StorageDatas.TryGetValue(definition.Type, out var storage))
                throw new InvalidOperationException($"{definition.Type.Name} has not been loaded.");

            SaveStorageInternal(definition, storage);
        }

        public void LoadAllData()
        {
            ThrowIfWorkUnavailableInternal();
            LoadAllDataInternal(LifetimeToken);
        }

        private static StorageDefinition GetDefinitionInternal(Type type)
        {
            foreach (var definition in StorageDefinitions)
            {
                if (definition.Type == type)
                    return definition;
            }

            throw new ArgumentException($"Storage type {type.Name} is not registered.", nameof(type));
        }

        private void LoadAllDataInternal(CancellationToken token)
        {
            foreach (var definition in StorageDefinitions)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    ClientLocalStorage storage = LoadStorageInternal(definition);

                    if (storage != null)
                        StorageDatas[definition.Type] = storage;
                    else
                        StorageDatas.Remove(definition.Type);
                }
                catch
                {
                    StorageDatas.Remove(definition.Type);
                    throw;
                }
            }
        }

        private ClientLocalStorage LoadStorageInternal(StorageDefinition definition)
        {
            string path = Path.Combine(assetFolderPath, definition.FileName);
            string backupPath = path + ".bak";
            InvalidDataException invalidFile = null;

            if (File.Exists(path))
            {
                try
                {
                    return ReadStorageInternal(path, definition);
                }
                catch (InvalidDataException error)
                {
                    invalidFile = error;
                    QuarantineInternal(path);
                }
            }

            if (File.Exists(backupPath))
            {
                try
                {
                    ClientLocalStorage storage = ReadStorageInternal(backupPath, definition);
                    File.Copy(backupPath, path, true);

                    if (invalidFile != null)
                        Global.LogException(invalidFile);

                    return storage;
                }
                catch (InvalidDataException error)
                {
                    QuarantineInternal(backupPath);
                    invalidFile = invalidFile == null
                        ? error
                        : new InvalidDataException($"Both {definition.FileName} and its backup are invalid.", new AggregateException(invalidFile, error));
                }
            }

            if (invalidFile != null)
                Global.LogException(invalidFile);

            return null;
        }

        private static ClientLocalStorage ReadStorageInternal(string path, StorageDefinition definition)
        {
            string json = File.ReadAllText(path);
            StorageFile file;

            try
            {
                file = JsonUtility.FromJson<StorageFile>(json);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException($"Failed to parse {path}.", error);
            }

            if (file == null)
                throw new InvalidDataException($"{path} does not contain storage data.");

            // 알 수 없는 버전을 손상 파일로 취급하면 종료 저장이 새 버전 데이터를 덮어쓸 수 있다.
            if (file.Version != definition.Version)
                throw new NotSupportedException($"{path} has unsupported storage version {file.Version}.");

            if (string.IsNullOrEmpty(file.Data))
                throw new InvalidDataException($"{path} does not contain storage data.");

            try
            {
                ClientLocalStorage storage = JsonUtility.FromJson(file.Data, definition.Type) as ClientLocalStorage;

                if (storage == null)
                    throw new InvalidDataException($"{path} contains the wrong storage type.");

                return storage;
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException($"Failed to parse the data in {path}.", error);
            }
        }

        private void SaveAllDataInternal()
        {
            List<Exception> errors = null;

            foreach (var definition in StorageDefinitions)
            {
                if (!StorageDatas.TryGetValue(definition.Type, out var storage))
                    continue;

                try
                {
                    SaveStorageInternal(definition, storage);
                }
                catch (Exception error)
                {
                    if (errors == null)
                        errors = new List<Exception>();

                    errors.Add(error);
                }
            }

            if (errors != null)
                throw new AggregateException("One or more client storage files could not be saved.", errors);
        }

        private void SaveStorageInternal(StorageDefinition definition, ClientLocalStorage storage)
        {
            var file = new StorageFile
            {
                Version = definition.Version,
                Data = JsonUtility.ToJson(storage)
            };

            string json = JsonUtility.ToJson(file);
            string path = Path.Combine(assetFolderPath, definition.FileName);
            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";

            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, backupPath);
                }
                catch (NotSupportedException)
                {
                    ReplaceWithMovesInternal(temporaryPath, path, backupPath);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }

        private static void ReplaceWithMovesInternal(string temporaryPath, string path, string backupPath)
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(path, backupPath);

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (Exception moveError)
            {
                try
                {
                    File.Move(backupPath, path);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Storage replacement and rollback both failed.", moveError, rollbackError);
                }

                throw;
            }
        }

        private static void QuarantineInternal(string path)
        {
            File.Move(path, path + ".corrupt." + Guid.NewGuid().ToString("N"));
        }
    }
}
