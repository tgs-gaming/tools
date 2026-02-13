using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private void RefreshLocalCache()
        {
            ClearLocalCaches();
            var installRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fallbackRoot = ResolveInstallRoot(string.IsNullOrEmpty(_defaultInstallRoot)
                ? GetDefaultInstallRoot()
                : _defaultInstallRoot);
            if (!string.IsNullOrEmpty(fallbackRoot))
            {
                installRoots.Add(fallbackRoot);
            }
            var embeddedRoot = GetEmbeddedPackagesRoot();
            if (!string.IsNullOrEmpty(embeddedRoot))
            {
                installRoots.Add(embeddedRoot);
            }
            foreach (var repository in _repositories)
            {
                var root = GetInstallRootForRepository(repository);
                if (!string.IsNullOrEmpty(root))
                {
                    installRoots.Add(root);
                }
            }

            foreach (var installRoot in installRoots)
            {
                if (string.IsNullOrEmpty(installRoot) || !Directory.Exists(installRoot))
                {
                    continue;
                }

                foreach (var directory in Directory.GetDirectories(installRoot))
                {
                    var packageJsonPath = Path.Combine(directory, "package.json");
                    if (!File.Exists(packageJsonPath))
                    {
                        continue;
                    }

                    PackageJsonInfo info;
                    string json = null;
                    try
                    {
                        json = File.ReadAllText(packageJsonPath);
                        info = JsonUtility.FromJson<PackageJsonInfo>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Local cache: failed to read " + packageJsonPath + ": " + ex.Message);
                        continue;
                    }

                    var id = info != null && !string.IsNullOrEmpty(info.name) ? info.name : Path.GetFileName(directory);
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var version = info != null ? info.version : string.Empty;
                    var repositoryUrl = info != null && info.repository != null ? info.repository.url : null;
                    _installedVersionsCache[id] = version ?? string.Empty;
                    _localPackagesCache.Add(new LocalPackageInfo
                    {
                        Id = id,
                        DisplayName = info != null ? info.displayName : null,
                        Description = info != null ? info.description : null,
                        Unity = info != null ? info.unity : null,
                        Version = version ?? string.Empty,
                        RootPath = directory,
                        Required = info != null && info.required,
                        Dependencies = info != null ? (info.dependencies ?? ParseDependenciesFromJson(json)) : null,
                        RepositoryUrl = repositoryUrl
                    });

                    var gitInitialized = Directory.Exists(Path.Combine(directory, ".git"));
                    _gitInitializedCache[id] = gitInitialized;

                    if (gitInitialized)
                    {
                        var pending = HasPendingPushChanges(directory, id);
                        _pendingPushCache[id] = pending;

                        var pendingCommit = RunGitCapture(directory, "status --porcelain", string.Empty, string.Empty);
                        _pendingCommitCache[id] = pendingCommit;

                        var hasRemote = RunGitCapture(directory, "remote", string.Empty, "origin");
                        _remoteExistsCache[id] = hasRemote;
                        if (hasRemote)
                        {
                            var remoteUrl = RunGitGetOutput(directory, "remote get-url origin", string.Empty);
                            if (!string.IsNullOrEmpty(remoteUrl))
                            {
                                _remoteUrlCache[id] = remoteUrl;
                            }
                        }

                        var headCommit = RunGitGetOutput(directory, "rev-parse --short HEAD", string.Empty);
                        if (!string.IsNullOrEmpty(headCommit))
                        {
                            _gitHeadCache[id] = headCommit.Trim();
                        }

                        var headMessage = RunGitGetOutput(directory, "log -1 --pretty=%s", string.Empty);
                        if (!string.IsNullOrEmpty(headMessage))
                        {
                            _gitHeadMessageCache[id] = headMessage.Trim();
                        }

                        var branchName = RunGitGetOutput(directory, "rev-parse --abbrev-ref HEAD", string.Empty);
                        _gitDetachedCache[id] = string.Equals(branchName?.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }

        private bool HasPendingPushChanges(string packageRoot, string packageId)
        {
            if (string.IsNullOrEmpty(packageRoot) || !Directory.Exists(packageRoot))
            {
                return false;
            }

            var aheadCount = GetAheadCount(packageRoot, "@{u}");
            if (aheadCount > 0)
            {
                return true;
            }

            var currentBranch = RunGitGetOutput(packageRoot, "rev-parse --abbrev-ref HEAD", string.Empty);
            if (string.IsNullOrWhiteSpace(currentBranch)
                || string.Equals(currentBranch.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var branchRef = currentBranch.Trim();
            var remoteBranchRef = "origin/" + branchRef;
            if (RunGitCapture(packageRoot, "rev-parse --verify " + QuoteGitArgument(remoteBranchRef), string.Empty, null))
            {
                return GetAheadCount(packageRoot, remoteBranchRef) > 0;
            }

            var expectedBranch = BuildPackageBranchRef(packageId);
            if (!string.Equals(branchRef, expectedBranch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var localCommitCount = RunGitGetOutput(packageRoot, "rev-list --count HEAD", string.Empty);
            return TryParsePositiveInt(localCommitCount);
        }

        private static int GetAheadCount(string packageRoot, string baseRef)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(baseRef))
            {
                return 0;
            }

            var counts = RunGitGetOutput(packageRoot,
                "rev-list --left-right --count " + QuoteGitArgument(baseRef + "...HEAD"),
                string.Empty);
            if (string.IsNullOrWhiteSpace(counts))
            {
                return 0;
            }

            var parts = counts.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return 0;
            }

            return int.TryParse(parts[1], out var ahead) ? ahead : 0;
        }

        private static bool TryParsePositiveInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return int.TryParse(value.Trim(), out var parsed) && parsed > 0;
        }

        private void ClearLocalCaches()
        {
            _installedVersionsCache.Clear();
            _pendingPushCache.Clear();
            _pendingCommitCache.Clear();
            _gitInitializedCache.Clear();
            _gitHeadCache.Clear();
            _gitHeadMessageCache.Clear();
            _gitDetachedCache.Clear();
            _remoteExistsCache.Clear();
            _remoteUrlCache.Clear();
            _localPackagesCache.Clear();
        }

        private string GetInstalledVersionCached(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return _installedVersionsCache.TryGetValue(package.id, out var version) ? version : null;
        }

        private LocalPackageInfo GetLocalPackageInfo(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return null;
            }

            foreach (var info in _localPackagesCache)
            {
                if (info == null || string.IsNullOrEmpty(info.Id))
                {
                    continue;
                }

                if (string.Equals(info.Id, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return null;
        }

        private LocalPackageInfo GetLocalPackageInfo(string packageId, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return GetLocalPackageInfo(packageId);
            }

            if (string.IsNullOrEmpty(packageId))
            {
                return null;
            }

            var localPackages = snapshot.LocalPackagesCache;
            if (localPackages == null)
            {
                return null;
            }

            foreach (var info in localPackages)
            {
                if (info == null || string.IsNullOrEmpty(info.Id))
                {
                    continue;
                }

                if (string.Equals(info.Id, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return null;
        }

        private void ApplyLocalPackageOverrides(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var installedVersion = GetInstalledVersionCached(package);
            if (string.IsNullOrEmpty(installedVersion))
            {
                return;
            }

            var localInfo = GetLocalPackageInfo(package.id);
            if (localInfo == null)
            {
                return;
            }

            if (localInfo.Dependencies != null)
            {
                package.dependencies = localInfo.Dependencies;
            }
        }

        private List<PackageListItem> BuildLocalOnlyPackages(HashSet<string> remoteIds,
            List<LocalPackageInfo> localPackages,
            Dictionary<string, string> packageUnityRequirements,
            Dictionary<string, bool> packageCompatibility)
        {
            var items = new List<PackageListItem>();
            if (localPackages == null || localPackages.Count == 0)
            {
                return items;
            }

            foreach (var info in localPackages)
            {
                if (info == null || string.IsNullOrEmpty(info.Id))
                {
                    continue;
                }

                if (remoteIds.Contains(info.Id))
                {
                    continue;
                }

                var entry = new PackageEntry
                {
                    id = info.Id,
                    displayName = string.IsNullOrEmpty(info.DisplayName) ? info.Id : info.DisplayName,
                    description = info.Description,
                    required = info.Required,
                    dependencies = info.Dependencies,
                    versions = BuildVersionEntries(new[] { info.Version }),
                    loadStatus = PackageLoadStatus.Loaded
                };
                if (!string.IsNullOrWhiteSpace(info.RepositoryUrl))
                {
                    var repository = FindRepositoryConfigByUrl(info.RepositoryUrl);
                    if (repository != null)
                    {
                        entry.repositoryId = repository.id;
                    }
                }

                if (!string.IsNullOrEmpty(info.Unity))
                {
                    if (packageUnityRequirements != null)
                    {
                        packageUnityRequirements[info.Id] = info.Unity;
                    }
                    if (packageCompatibility != null)
                    {
                        packageCompatibility[info.Id] = IsUnityCompatible(info.Unity);
                    }
                }

                var installedVersion = info.Version ?? string.Empty;
                items.Add(new PackageListItem(entry, installedVersion, !string.IsNullOrEmpty(installedVersion), false, true, false, null));
            }

            return items;
        }

        private string GetInstalledVersion(PackageEntry package)
        {
            if (package == null)
            {
                return null;
            }

            var packageRoot = GetPackageRoot(package);
            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            var packageJsonPath = Path.Combine(packageRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(packageJsonPath);
                var info = JsonUtility.FromJson<PackageJsonInfo>(json);
                return info != null ? info.version : "Unknown";
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        private bool IsPackageInstalled(PackageEntry package, out bool isUpmInstalled, out string installedVersion)
        {
            var upmInfo = GetUpmPackageInfo(package);
            isUpmInstalled = upmInfo != null;
            if (isUpmInstalled)
            {
                installedVersion = upmInfo.version;
                return true;
            }

            installedVersion = GetInstalledVersionCached(package);
            return !string.IsNullOrEmpty(installedVersion);
        }
    }
}
