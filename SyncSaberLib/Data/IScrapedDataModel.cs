﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace SyncSaberLib.Data
{
    public abstract class IScrapedDataModel<T, DataType>
        where T : List<DataType>, new() where DataType : IEquatable<DataType>
    {
        private static readonly string ASSEMBLY_PATH = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static readonly DirectoryInfo DATA_DIRECTORY = new DirectoryInfo(Path.Combine(ASSEMBLY_PATH, "ScrapedData"));

        [JsonIgnore]
        public bool Initialized { get; protected set; }
        public bool HasData { get { return Data != null && Data.Count > 0; } }
        public virtual T Data { get; protected set; }
        [JsonIgnore]
        public bool ReadOnly { get; protected set; }
        [JsonIgnore]
        public string DefaultPath { get; protected set; }
        [JsonIgnore]
        public FileInfo CurrentFile { get; protected set; }

        public virtual JToken ReadScrapedFile(string filePath)
        {
            JToken results = null;
            string backupPath = filePath + ".bak";
            if(File.Exists(backupPath)) // Last write was unsuccessful, delete corrupted file and use the backup
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(backupPath, filePath);
            }
            if (File.Exists(filePath))
                using (StreamReader file = File.OpenText(filePath))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    //results = (JObject)serializer.Deserialize(file, typeof(JObject));
                    results = JToken.Parse(file.ReadToEnd());
                }
            return results;
        }

        public virtual void WriteFile(string filePath = "")
        {
            if (string.IsNullOrEmpty(filePath))
                filePath = CurrentFile.FullName;
            string backupPath = "";
            //Backup file before overwriting
            if (File.Exists(filePath))
            {
                backupPath = filePath + ".bak";
                File.Move(filePath, backupPath);
            }
            using (StreamWriter file = File.CreateText(filePath))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, Data);
            }
            //Write was successful, delete backup
            if(!string.IsNullOrEmpty(backupPath) && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
                               
        }
        public abstract void Initialize(string filePath = "");


        public virtual bool AddOrUpdate(DataType item, bool exceptionOnNull = false)
        {
            if (Equals(item, default(DataType)))
                if (exceptionOnNull)
                    throw new ArgumentNullException("Item cannot be null.");
                else
                    return false;
            DataType added = default;
            DataType removed = default;
            bool successful = false;
            lock (Data)
            {
                var match = Data.Where(s => s.Equals(item));
                if (match.Count() == 0)
                {
                    //Logger.Debug($"Adding song {song.key} - {song.songName} by {song.authorName} to ScrapedData");
                    Data.Add(item);
                    added = item;
                    successful = true;
                }
                else
                {
                    removed = match.First();
                    Data.Remove(removed);
                    Data.Add(item);
                    added = item;
                    successful = true;
                }
            }
            if (!(Equals(added, default(DataType)) || Equals(removed, default(DataType))))
            {
                CollectionChanged?.Invoke(this, new CollectionChangedEventArgs<DataType>(added, removed));
            }
            return successful;
        }

        public event EventHandler<CollectionChangedEventArgs<DataType>> CollectionChanged;
    }

    public class CollectionChangedEventArgs<T>
        where T : IEquatable<T>
    {
        public T ItemAdded;
        public T ItemRemoved;
        public CollectionChangedEventArgs(T Added, T Removed)
        {
            ItemAdded = Added;
            ItemRemoved = Removed;
        }
    }

    public static class JsonExtensions
    {
        public static void Populate<T>(this JToken value, T target) where T : class
        {
            using (var sr = value.CreateReader())
            {
                JsonSerializer.CreateDefault().Populate(sr, target); // Uses the system default JsonSerializerSettings
            }
        }
    }
}
