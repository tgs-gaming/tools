using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    [Serializable]
    public class ToolsManifest
    {
        public string schemaVersion;
        public string generatedAt;
        public RepositoryInfo repository;
        public string[] packages;

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (repository == null)
            {
                errors.Add("Repository metadata is missing.");
            }
            else
            {
                if (string.IsNullOrEmpty(repository.owner))
                {
                    errors.Add("Repository owner is required.");
                }

                if (string.IsNullOrEmpty(repository.name))
                {
                    errors.Add("Repository name is required.");
                }
            }

            return errors;
        }
    }

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
        public string pathInRepo;
        public string defaultRef;
        public string refLatest;
        public bool required;
        public PackageVersion[] versions;
        public string author;
        [NonSerialized] public PackageLoadStatus loadStatus;
        [NonSerialized] public string loadError;

        public string GetLatestRef(RepositoryInfo repository)
        {
            if (versions != null && versions.Length > 0 && !string.IsNullOrEmpty(id))
            {
                return id + "/v" + versions[versions.Length - 1].version;
            }

            if (!string.IsNullOrEmpty(defaultRef) && !string.Equals(defaultRef, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return defaultRef;
            }

            if (!string.IsNullOrEmpty(refLatest))
            {
                return refLatest;
            }

            if (repository != null && !string.IsNullOrEmpty(repository.defaultBranch))
            {
                return repository.defaultBranch;
            }

            return "main";
        }
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
