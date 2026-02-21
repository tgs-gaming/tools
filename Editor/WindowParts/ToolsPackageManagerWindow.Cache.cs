using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private sealed class ManagedPackageLocation
        {
            public string PackageId;
            public string PackageRoot;
            public DateTime LastWriteTimeUtc;
            public bool IsEmbedded;
        }

        private void SynchronizeManagedPackageDuplicates()
        {
            var packageRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var embeddedRoot = GetEmbeddedPackagesRoot();
            if (!string.IsNullOrWhiteSpace(embeddedRoot))
            {
                embeddedRoot = Path.GetFullPath(embeddedRoot);
            }

            var fallbackRoot = ResolveInstallRoot(string.IsNullOrEmpty(_defaultInstallRoot)
                ? GetDefaultInstallRoot()
                : _defaultInstallRoot);
            if (!string.IsNullOrEmpty(fallbackRoot)
                && !string.Equals(fallbackRoot, embeddedRoot, StringComparison.OrdinalIgnoreCase))
            {
                packageRoots.Add(Path.GetFullPath(fallbackRoot));
            }

            foreach (var repository in _repositories)
            {
                var root = GetInstallRootForRepository(repository);
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                root = Path.GetFullPath(root);
                if (string.Equals(root, embeddedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                packageRoots.Add(root);
            }

            var packageLocations = new Dictionary<string, List<ManagedPackageLocation>>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageRoot in packageRoots)
            {
                CollectManagedPackageLocations(packageLocations, packageRoot, false);
            }

            CollectManagedPackageLocations(packageLocations, embeddedRoot, true);

            var removedCount = 0;
            foreach (var item in packageLocations)
            {
                var locations = item.Value;
                if (locations == null || locations.Count == 0)
                {
                    continue;
                }

                var embeddedLocations = new List<ManagedPackageLocation>();
                var packageLocationsOnly = new List<ManagedPackageLocation>();
                foreach (var location in locations)
                {
                    if (location == null)
                    {
                        continue;
                    }

                    if (location.IsEmbedded)
                    {
                        embeddedLocations.Add(location);
                    }
                    else
                    {
                        packageLocationsOnly.Add(location);
                    }
                }

                if (embeddedLocations.Count > 0 && packageLocationsOnly.Count > 0)
                {
                    foreach (var packageLocation in packageLocationsOnly)
                    {
                        if (TryDeleteManagedPackageDirectory(packageLocation))
                        {
                            removedCount++;
                        }
                    }

                    continue;
                }

                if (packageLocationsOnly.Count <= 1)
                {
                    continue;
                }

                packageLocationsOnly.Sort((left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));
                for (var i = 1; i < packageLocationsOnly.Count; i++)
                {
                    if (TryDeleteManagedPackageDirectory(packageLocationsOnly[i]))
                    {
                        removedCount++;
                    }
                }
            }

            if (removedCount > 0)
            {
                AssetDatabase.Refresh();
                Debug.Log("TGS Package Manager: removed " + removedCount + " duplicated package folder(s).");
            }
        }

        private static void CollectManagedPackageLocations(Dictionary<string, List<ManagedPackageLocation>> packagesById,
            string installRoot, bool isEmbedded)
        {
            if (packagesById == null || string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                return;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(installRoot);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Duplicate package check: failed to enumerate " + installRoot + ": " + ex.Message);
                return;
            }

            foreach (var directory in directories)
            {
                var packageJsonPath = Path.Combine(directory, "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    continue;
                }

                var packageId = ReadPackageIdForDuplicateCheck(packageJsonPath, directory);
                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                if (!packagesById.TryGetValue(packageId, out var locations))
                {
                    locations = new List<ManagedPackageLocation>();
                    packagesById[packageId] = locations;
                }

                locations.Add(new ManagedPackageLocation
                {
                    PackageId = packageId,
                    PackageRoot = directory,
                    LastWriteTimeUtc = File.GetLastWriteTimeUtc(packageJsonPath),
                    IsEmbedded = isEmbedded
                });
            }
        }

        private static string ReadPackageIdForDuplicateCheck(string packageJsonPath, string packageDirectory)
        {
            if (string.IsNullOrEmpty(packageJsonPath))
            {
                return Path.GetFileName(packageDirectory);
            }

            try
            {
                var json = File.ReadAllText(packageJsonPath);
                var info = JsonUtility.FromJson<PackageJsonInfo>(json);
                if (info != null && !string.IsNullOrWhiteSpace(info.name))
                {
                    return info.name.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Duplicate package check: failed to read " + packageJsonPath + ": " + ex.Message);
            }

            return Path.GetFileName(packageDirectory);
        }

        private static bool TryDeleteManagedPackageDirectory(ManagedPackageLocation location)
        {
            if (location == null || string.IsNullOrWhiteSpace(location.PackageRoot))
            {
                return false;
            }

            var removedDirectory = true;
            if (Directory.Exists(location.PackageRoot))
            {
                removedDirectory = TryDeleteDirectory(location.PackageRoot);
            }

            if (!removedDirectory)
            {
                Debug.LogWarning("Duplicate package check: failed to delete " + location.PackageRoot + ".");
                return false;
            }

            if (!TryDeleteDirectoryMetaFile(location.PackageRoot))
            {
                Debug.LogWarning("Duplicate package check: failed to delete meta for " + location.PackageRoot + ".");
            }

            Debug.Log("Duplicate package check: removed " + location.PackageId + " from " + location.PackageRoot + ".");
            return true;
        }

        private static bool TryDeleteDirectoryMetaFile(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return true;
            }

            var metaPath = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".meta";
            if (!File.Exists(metaPath))
            {
                return true;
            }

            try
            {
                File.SetAttributes(metaPath, FileAttributes.Normal);
                File.Delete(metaPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void RefreshLocalCache()
        {
            var previousPendingPush = CloneDictionary(_pendingPushCache);
            var previousPendingCommit = CloneDictionary(_pendingCommitCache);
            var previousGitInitialized = CloneDictionary(_gitInitializedCache);
            var previousGitHead = CloneDictionary(_gitHeadCache);
            var previousGitHeadMessage = CloneDictionary(_gitHeadMessageCache);
            var previousGitDetached = CloneDictionary(_gitDetachedCache);
            var previousRemoteExists = CloneDictionary(_remoteExistsCache);
            var previousRemoteUrl = CloneDictionary(_remoteUrlCache);

            var now = EditorApplication.timeSinceStartup;
            var shouldProbeGitState = now >= _nextLocalGitProbeAt;
            if (shouldProbeGitState)
            {
                _nextLocalGitProbeAt = now + LocalGitProbeIntervalSeconds;
            }

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

            var rootsSummary = installRoots.Count == 0
                ? "<none>"
                : string.Join(" | ", installRoots);

            var scannedRoots = 0;
            var scannedDirectories = 0;
            var discoveredPackages = 0;

            foreach (var installRoot in installRoots)
            {
                if (string.IsNullOrEmpty(installRoot) || !Directory.Exists(installRoot))
                {
                    continue;
                }

                scannedRoots++;

                foreach (var directory in Directory.GetDirectories(installRoot))
                {
                    scannedDirectories++;
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

                    discoveredPackages++;

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
                        var hadCachedState = false;
                        var reusedCachedState = !shouldProbeGitState && TryRestoreCachedGitState(id,
                            previousPendingPush, previousPendingCommit, previousGitHead, previousGitHeadMessage,
                            previousGitDetached, previousRemoteExists, previousRemoteUrl, previousGitInitialized,
                            out hadCachedState);
                        if (!reusedCachedState)
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
                        else if (!hadCachedState)
                        {
                            _pendingPushCache[id] = false;
                            _pendingCommitCache[id] = false;
                            _gitDetachedCache[id] = false;
                            _remoteExistsCache[id] = false;
                        }
                    }
                }
            }
        }

        private void RefreshLocalCacheForPackage(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageId = package.id;
            ClearLocalCacheForPackage(packageId);

            var packageRoot = GetPackageRoot(package);
            if (string.IsNullOrEmpty(packageRoot)
                || !Directory.Exists(packageRoot)
                || !File.Exists(Path.Combine(packageRoot, "package.json")))
            {
                return;
            }

            if (!TryReadLocalPackageInfo(packageRoot, out var localInfo, out var idFromJson, out var readError))
            {
                if (!string.IsNullOrEmpty(readError))
                {
                    Debug.LogWarning("Local cache: failed to read " + packageRoot + ": " + readError);
                }
                return;
            }

            if (!string.IsNullOrEmpty(idFromJson)
                && !string.Equals(idFromJson, packageId, StringComparison.OrdinalIgnoreCase))
            {
                ClearLocalCacheForPackage(idFromJson);
                packageId = idFromJson;
            }

            ApplyLocalPackageInfoToCaches(packageId, packageRoot, localInfo);
            RefreshGitStateForPackage(packageId, packageRoot);
        }

        private void ClearLocalCacheForPackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return;
            }

            _installedVersionsCache.Remove(packageId);
            _pendingPushCache.Remove(packageId);
            _pendingCommitCache.Remove(packageId);
            _gitInitializedCache.Remove(packageId);
            _gitHeadCache.Remove(packageId);
            _gitHeadMessageCache.Remove(packageId);
            _gitDetachedCache.Remove(packageId);
            _remoteExistsCache.Remove(packageId);
            _remoteUrlCache.Remove(packageId);

            for (var i = _localPackagesCache.Count - 1; i >= 0; i--)
            {
                var info = _localPackagesCache[i];
                if (info == null || string.IsNullOrEmpty(info.Id))
                {
                    continue;
                }

                if (string.Equals(info.Id, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    _localPackagesCache.RemoveAt(i);
                }
            }
        }

        private static bool TryReadLocalPackageInfo(string packageRoot, out PackageJsonInfo info, out string packageId,
            out string error)
        {
            info = null;
            packageId = null;
            error = null;

            if (string.IsNullOrEmpty(packageRoot))
            {
                error = "Package root is missing.";
                return false;
            }

            var packageJsonPath = Path.Combine(packageRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                error = "package.json not found.";
                return false;
            }

            try
            {
                var json = File.ReadAllText(packageJsonPath);
                info = JsonUtility.FromJson<PackageJsonInfo>(json);
                packageId = info != null && !string.IsNullOrEmpty(info.name) ? info.name : Path.GetFileName(packageRoot);
                return !string.IsNullOrEmpty(packageId);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void ApplyLocalPackageInfoToCaches(string packageId, string packageRoot, PackageJsonInfo info)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return;
            }

            var version = info != null ? info.version : string.Empty;
            var repositoryUrl = info != null && info.repository != null ? info.repository.url : null;
            _installedVersionsCache[packageId] = version ?? string.Empty;
            _localPackagesCache.Add(new LocalPackageInfo
            {
                Id = packageId,
                DisplayName = info != null ? info.displayName : null,
                Description = info != null ? info.description : null,
                Unity = info != null ? info.unity : null,
                Version = version ?? string.Empty,
                RootPath = packageRoot,
                Required = info != null && info.required,
                Dependencies = info != null ? info.dependencies : null,
                RepositoryUrl = repositoryUrl
            });
        }

        private void RefreshGitStateForPackage(string packageId, string packageRoot)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(packageRoot))
            {
                return;
            }

            var gitInitialized = Directory.Exists(Path.Combine(packageRoot, ".git"));
            _gitInitializedCache[packageId] = gitInitialized;
            if (!gitInitialized)
            {
                _pendingPushCache[packageId] = false;
                _pendingCommitCache[packageId] = false;
                _gitDetachedCache[packageId] = false;
                _remoteExistsCache[packageId] = false;
                return;
            }

            _pendingPushCache[packageId] = HasPendingPushChanges(packageRoot, packageId);
            _pendingCommitCache[packageId] = RunGitCapture(packageRoot, "status --porcelain", string.Empty, string.Empty);

            var hasRemote = RunGitCapture(packageRoot, "remote", string.Empty, "origin");
            _remoteExistsCache[packageId] = hasRemote;
            if (hasRemote)
            {
                var remoteUrl = RunGitGetOutput(packageRoot, "remote get-url origin", string.Empty);
                if (!string.IsNullOrEmpty(remoteUrl))
                {
                    _remoteUrlCache[packageId] = remoteUrl;
                }
            }

            var headCommit = RunGitGetOutput(packageRoot, "rev-parse --short HEAD", string.Empty);
            if (!string.IsNullOrEmpty(headCommit))
            {
                _gitHeadCache[packageId] = headCommit.Trim();
            }

            var headMessage = RunGitGetOutput(packageRoot, "log -1 --pretty=%s", string.Empty);
            if (!string.IsNullOrEmpty(headMessage))
            {
                _gitHeadMessageCache[packageId] = headMessage.Trim();
            }

            var branchName = RunGitGetOutput(packageRoot, "rev-parse --abbrev-ref HEAD", string.Empty);
            _gitDetachedCache[packageId] = string.Equals(branchName?.Trim(), "HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryRestoreCachedGitState(string packageId,
            Dictionary<string, bool> previousPendingPush,
            Dictionary<string, bool> previousPendingCommit,
            Dictionary<string, string> previousGitHead,
            Dictionary<string, string> previousGitHeadMessage,
            Dictionary<string, bool> previousGitDetached,
            Dictionary<string, bool> previousRemoteExists,
            Dictionary<string, string> previousRemoteUrl,
            Dictionary<string, bool> previousGitInitialized,
            out bool hadCachedState)
        {
            hadCachedState = false;
            if (string.IsNullOrEmpty(packageId))
            {
                return false;
            }

            if (previousGitInitialized != null && previousGitInitialized.TryGetValue(packageId, out var cachedGitInitialized))
            {
                _gitInitializedCache[packageId] = cachedGitInitialized;
                hadCachedState = true;
            }

            if (previousPendingPush != null && previousPendingPush.TryGetValue(packageId, out var pendingPush))
            {
                _pendingPushCache[packageId] = pendingPush;
                hadCachedState = true;
            }

            if (previousPendingCommit != null && previousPendingCommit.TryGetValue(packageId, out var pendingCommit))
            {
                _pendingCommitCache[packageId] = pendingCommit;
                hadCachedState = true;
            }

            if (previousGitHead != null && previousGitHead.TryGetValue(packageId, out var gitHead))
            {
                _gitHeadCache[packageId] = gitHead;
                hadCachedState = true;
            }

            if (previousGitHeadMessage != null && previousGitHeadMessage.TryGetValue(packageId, out var gitHeadMessage))
            {
                _gitHeadMessageCache[packageId] = gitHeadMessage;
                hadCachedState = true;
            }

            if (previousGitDetached != null && previousGitDetached.TryGetValue(packageId, out var gitDetached))
            {
                _gitDetachedCache[packageId] = gitDetached;
                hadCachedState = true;
            }

            if (previousRemoteExists != null && previousRemoteExists.TryGetValue(packageId, out var remoteExists))
            {
                _remoteExistsCache[packageId] = remoteExists;
                hadCachedState = true;
            }

            if (previousRemoteUrl != null && previousRemoteUrl.TryGetValue(packageId, out var remoteUrl))
            {
                _remoteUrlCache[packageId] = remoteUrl;
                hadCachedState = true;
            }

            return hadCachedState;
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
