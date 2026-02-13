using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    [Serializable]
    public class RepositoryInfo
    {
        public string owner;
        public string name;
        public string defaultBranch;
        public string description;
    }

    [Serializable]
    public class PackageEntry
    {
        public string id;
        public string displayName;
        public string description;
        public string[] dependencies;
        public string pathInRepo;
        public string defaultRef;
        public string refLatest;
        public bool required;
        public PackageVersion[] versions;
        public string author;
        [NonSerialized] public string repositoryId;
        [NonSerialized] public PackageLoadStatus loadStatus;
        [NonSerialized] public string loadError;
    }

    [Serializable]
    public class PackageVersion
    {
        public string version;
    }

    public enum PackageLoadStatus
    {
        Pending,
        Loading,
        Loaded,
        BranchNotFound,
        ConfigError
    }
}
