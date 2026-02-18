using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private bool HasAnyUpdate()
        {
            return HasAnyUpdate(_packages, _installedVersionsCache);
        }

        private bool HasAnyUpdate(List<PackageEntry> packages, Dictionary<string, string> installedVersions)
        {
            if (packages == null || packages.Count == 0)
            {
                return false;
            }

            foreach (var package in packages)
            {
                if (package == null || string.IsNullOrEmpty(package.id))
                {
                    continue;
                }

                var upmInfo = GetUpmPackageInfo(package);
                var upmVersion = upmInfo != null ? upmInfo.version : null;
                var isUpmInstalled = upmInfo != null;
                var installedVersion = isUpmInstalled ? upmVersion : GetInstalledVersionCached(package, installedVersions);
                if (!string.IsNullOrEmpty(installedVersion) && IsUpdateAvailable(package, installedVersion))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator UpdateAllPackages()
        {
            if (_packages == null || _packages.Count == 0)
            {
                yield break;
            }

            foreach (var package in _packages)
            {
                if (package == null || string.IsNullOrEmpty(package.id))
                {
                    continue;
                }

                var upmInfo = GetUpmPackageInfo(package);
                var upmVersion = upmInfo != null ? upmInfo.version : null;
                var isUpmInstalled = upmInfo != null;

                var installedVersion = isUpmInstalled ? upmVersion : GetInstalledVersionCached(package);
                if (string.IsNullOrEmpty(installedVersion) || !IsUpdateAvailable(package, installedVersion))
                {
                    continue;
                }

                var latestVersion = GetLatestVersion(package);
                var reference = !string.IsNullOrEmpty(latestVersion)
                ? BuildVersionRef(package, latestVersion)
                : BuildPackageBranchRef(package.id);

                if (isUpmInstalled)
                {
                    yield return UpdatePackageViaUpm(package, reference, latestVersion);
                }
                else
                {
                    yield return InstallPackage(package, reference, "Update", latestVersion);
                }
            }
        }

        private List<PackageListItem> BuildPackageListItems(List<PackageEntry> packages)
        {
            return BuildPackageListItems(packages, _localPackagesCache, _installedVersionsCache, _packageUnityRequirements,
                _packageCompatibility);
        }

        private List<PackageListItem> BuildPackageListItems(List<PackageEntry> packages,
            List<LocalPackageInfo> localPackages,
            Dictionary<string, string> installedVersions,
            Dictionary<string, string> packageUnityRequirements,
            Dictionary<string, bool> packageCompatibility)
        {
            var items = new List<PackageListItem>();
            if (packages == null)
            {
                packages = new List<PackageEntry>();
            }

            var localInfoById = new Dictionary<string, LocalPackageInfo>(StringComparer.OrdinalIgnoreCase);
            if (localPackages != null)
            {
                foreach (var info in localPackages)
                {
                    if (info == null || string.IsNullOrEmpty(info.Id))
                    {
                        continue;
                    }

                    localInfoById[info.Id] = info;
                }
            }

            var remoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                if (package == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(package.id))
                {
                    remoteIds.Add(package.id);
                }

                var upmInfo = GetUpmPackageInfo(package);
                var upmVersion = upmInfo != null ? upmInfo.version : null;
                var isUpmInstalled = upmInfo != null;
                var installedVersion = GetInstalledVersionCached(package, installedVersions);
                var isInstalled = !string.IsNullOrEmpty(installedVersion);
                if (isInstalled && localInfoById.TryGetValue(package.id, out var localInfo))
                {
                    package.dependencies = localInfo.Dependencies ?? Array.Empty<string>();
                }
                var effectiveVersion = isUpmInstalled ? upmVersion : installedVersion;
                var hasUpdate = !string.IsNullOrEmpty(effectiveVersion) && IsUpdateAvailable(package, effectiveVersion);
                items.Add(new PackageListItem(package, installedVersion, isInstalled, hasUpdate, false, isUpmInstalled, upmVersion));
            }

            foreach (var localItem in BuildLocalOnlyPackages(remoteIds, localPackages, packageUnityRequirements, packageCompatibility))
            {
                items.Add(localItem);
            }

            items.Sort((left, right) =>
            {
                var leftRequired = left.Package != null && left.Package.required;
                var rightRequired = right.Package != null && right.Package.required;
                if (leftRequired != rightRequired)
                {
                    return rightRequired.CompareTo(leftRequired);
                }

                if (left.IsLocalOnly != right.IsLocalOnly)
                {
                    return right.IsLocalOnly.CompareTo(left.IsLocalOnly);
                }

                if (left.IsLocalOnly)
                {
                    var leftLocalName = left.Package.displayName ?? left.Package.id ?? string.Empty;
                    var rightLocalName = right.Package.displayName ?? right.Package.id ?? string.Empty;
                    return string.Compare(leftLocalName, rightLocalName, StringComparison.OrdinalIgnoreCase);
                }

                var leftInstalled = left.IsInstalled || left.IsUpmInstalled;
                var rightInstalled = right.IsInstalled || right.IsUpmInstalled;
                if (leftInstalled != rightInstalled)
                {
                    return rightInstalled.CompareTo(leftInstalled);
                }

                if (leftInstalled && left.HasUpdate != right.HasUpdate)
                {
                    return right.HasUpdate.CompareTo(left.HasUpdate);
                }

                var leftName = left.Package.displayName ?? left.Package.id ?? string.Empty;
                var rightName = right.Package.displayName ?? right.Package.id ?? string.Empty;
                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });

            return items;
        }

        private static string GetInstalledVersionCached(PackageEntry package, Dictionary<string, string> installedVersions)
        {
            if (package == null || string.IsNullOrEmpty(package.id) || installedVersions == null)
            {
                return null;
            }

            return installedVersions.TryGetValue(package.id, out var version) ? version : null;
        }

        private void BeginManualPackageRefresh()
        {
            if (_manualPackageRefresh)
            {
                return;
            }

            _manualPackageRefresh = true;
            _packageListSnapshot = CapturePackageListSnapshot();
            _usePackageListSnapshot = _packageListSnapshot != null;
        }

        private void FinalizeManualPackageRefresh()
        {
            var snapshot = _packageListSnapshot;
            _manualPackageRefresh = false;

            if (snapshot == null)
            {
                _usePackageListSnapshot = false;
                return;
            }

            var currentSignature = ComputePackageListSignature(null);
            var shouldRestore = !_lastPackageRefreshSucceeded
                || string.Equals(currentSignature, snapshot.PackageListSignature, StringComparison.Ordinal);

            if (shouldRestore)
            {
                RestorePackageListSnapshot(snapshot);
            }

            _packageListSnapshot = null;
            _usePackageListSnapshot = false;
        }

        private PackageListSnapshot CapturePackageListSnapshot()
        {
            var snapshot = new PackageListSnapshot
            {
                Packages = _packages != null ? new List<PackageEntry>(_packages) : new List<PackageEntry>(),
                PackageUnityRequirements = CloneDictionary(_packageUnityRequirements),
                PackageCompatibility = CloneDictionary(_packageCompatibility),
                LocalPackagesCache = CloneLocalPackages(_localPackagesCache),
                InstalledVersionsCache = CloneDictionary(_installedVersionsCache),
                PendingPushCache = CloneDictionary(_pendingPushCache),
                PendingCommitCache = CloneDictionary(_pendingCommitCache),
                GitInitializedCache = CloneDictionary(_gitInitializedCache),
                GitHeadCache = CloneDictionary(_gitHeadCache),
                GitHeadMessageCache = CloneDictionary(_gitHeadMessageCache),
                GitDetachedCache = CloneDictionary(_gitDetachedCache),
                RemoteExistsCache = CloneDictionary(_remoteExistsCache),
                RemoteUrlCache = CloneDictionary(_remoteUrlCache)
            };

            snapshot.PackageListSignature = ComputePackageListSignature(snapshot);
            return snapshot;
        }

        private void RestorePackageListSnapshot(PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            _packages = snapshot.Packages != null ? new List<PackageEntry>(snapshot.Packages) : new List<PackageEntry>();
            RestoreDictionary(_packageUnityRequirements, snapshot.PackageUnityRequirements);
            RestoreDictionary(_packageCompatibility, snapshot.PackageCompatibility);
            RestoreLocalPackages(snapshot.LocalPackagesCache);
            RestoreDictionary(_installedVersionsCache, snapshot.InstalledVersionsCache);
            RestoreDictionary(_pendingPushCache, snapshot.PendingPushCache);
            RestoreDictionary(_pendingCommitCache, snapshot.PendingCommitCache);
            RestoreDictionary(_gitInitializedCache, snapshot.GitInitializedCache);
            RestoreDictionary(_gitHeadCache, snapshot.GitHeadCache);
            RestoreDictionary(_gitHeadMessageCache, snapshot.GitHeadMessageCache);
            RestoreDictionary(_gitDetachedCache, snapshot.GitDetachedCache);
            RestoreDictionary(_remoteExistsCache, snapshot.RemoteExistsCache);
            RestoreDictionary(_remoteUrlCache, snapshot.RemoteUrlCache);
        }

        private static Dictionary<string, T> CloneDictionary<T>(Dictionary<string, T> source)
        {
            if (source == null)
            {
                return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, T>(source, source.Comparer);
        }

        private static void RestoreDictionary<T>(Dictionary<string, T> target, Dictionary<string, T> source)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (source == null)
            {
                return;
            }

            foreach (var entry in source)
            {
                target[entry.Key] = entry.Value;
            }
        }

        private static List<LocalPackageInfo> CloneLocalPackages(List<LocalPackageInfo> source)
        {
            var list = new List<LocalPackageInfo>();
            if (source == null)
            {
                return list;
            }

            foreach (var info in source)
            {
                if (info == null)
                {
                    continue;
                }

                list.Add(new LocalPackageInfo
                {
                    Id = info.Id,
                    DisplayName = info.DisplayName,
                    Description = info.Description,
                    Unity = info.Unity,
                    Version = info.Version,
                    RootPath = info.RootPath,
                    Required = info.Required,
                    Dependencies = info.Dependencies != null ? (string[])info.Dependencies.Clone() : null,
                    RepositoryUrl = info.RepositoryUrl
                });
            }

            return list;
        }

        private void RestoreLocalPackages(List<LocalPackageInfo> localPackages)
        {
            _localPackagesCache.Clear();
            if (localPackages == null)
            {
                return;
            }

            foreach (var info in localPackages)
            {
                if (info != null)
                {
                    _localPackagesCache.Add(info);
                }
            }
        }

        private string ComputePackageListSignature(PackageListSnapshot snapshot)
        {
            var packages = snapshot != null ? snapshot.Packages : _packages;
            var localPackages = snapshot != null ? snapshot.LocalPackagesCache : _localPackagesCache;
            var installedVersions = snapshot != null ? snapshot.InstalledVersionsCache : _installedVersionsCache;
            var packageUnityRequirements = snapshot != null ? snapshot.PackageUnityRequirements : _packageUnityRequirements;
            var packageCompatibility = snapshot != null ? snapshot.PackageCompatibility : _packageCompatibility;
            var pendingPush = snapshot != null ? snapshot.PendingPushCache : _pendingPushCache;
            var pendingCommit = snapshot != null ? snapshot.PendingCommitCache : _pendingCommitCache;
            var gitInitialized = snapshot != null ? snapshot.GitInitializedCache : _gitInitializedCache;
            var gitHead = snapshot != null ? snapshot.GitHeadCache : _gitHeadCache;
            var gitHeadMessage = snapshot != null ? snapshot.GitHeadMessageCache : _gitHeadMessageCache;
            var gitDetached = snapshot != null ? snapshot.GitDetachedCache : _gitDetachedCache;
            var remoteExists = snapshot != null ? snapshot.RemoteExistsCache : _remoteExistsCache;
            var remoteUrl = snapshot != null ? snapshot.RemoteUrlCache : _remoteUrlCache;

            var items = BuildPackageListItems(packages, localPackages, installedVersions, packageUnityRequirements,
                packageCompatibility);
            var builder = new StringBuilder();
            builder.Append("items=").Append(items.Count);

            foreach (var item in items)
            {
                if (item == null || item.Package == null)
                {
                    continue;
                }

                var package = item.Package;
                builder.Append("|id=").Append(package.id ?? string.Empty);
                builder.Append(";repo=").Append(package.repositoryId ?? string.Empty);
                builder.Append(";name=").Append(package.displayName ?? string.Empty);
                builder.Append(";desc=").Append(package.description ?? string.Empty);
                builder.Append(";author=").Append(package.author ?? string.Empty);
                builder.Append(";req=").Append(package.required);
                builder.Append(";status=").Append((int)package.loadStatus);
                builder.Append(";error=").Append(package.loadError ?? string.Empty);
                builder.Append(";path=").Append(package.pathInRepo ?? string.Empty);

                if (!string.IsNullOrEmpty(package.id) && packageUnityRequirements != null
                    && packageUnityRequirements.TryGetValue(package.id, out var requirement))
                {
                    builder.Append(";unity=").Append(requirement ?? string.Empty);
                }
                else
                {
                    builder.Append(";unity=");
                }

                if (!string.IsNullOrEmpty(package.id) && packageCompatibility != null
                    && packageCompatibility.TryGetValue(package.id, out var compatible))
                {
                    builder.Append(";compat=").Append(compatible);
                }
                else
                {
                    builder.Append(";compat=true");
                }

                builder.Append(";deps=");
                AppendStringArray(builder, package.dependencies);

                builder.Append(";versions=");
                if (package.versions != null)
                {
                    for (var i = 0; i < package.versions.Length; i++)
                    {
                        var version = package.versions[i];
                        builder.Append(version != null ? version.version : string.Empty).Append(",");
                    }
                }

                builder.Append(";installed=").Append(item.InstalledVersion ?? string.Empty);
                builder.Append(";isInstalled=").Append(item.IsInstalled);
                builder.Append(";isUpm=").Append(item.IsUpmInstalled);
                builder.Append(";upmVersion=").Append(item.UpmVersion ?? string.Empty);
                builder.Append(";hasUpdate=").Append(item.HasUpdate);
                builder.Append(";localOnly=").Append(item.IsLocalOnly);

                AppendCacheValue(builder, "git", gitInitialized, package.id);
                AppendCacheValue(builder, "gitHead", gitHead, package.id);
                AppendCacheValue(builder, "gitMsg", gitHeadMessage, package.id);
                AppendCacheValue(builder, "gitDetached", gitDetached, package.id);
                AppendCacheValue(builder, "pendingPush", pendingPush, package.id);
                AppendCacheValue(builder, "pendingCommit", pendingCommit, package.id);
                AppendCacheValue(builder, "remoteExists", remoteExists, package.id);
                AppendCacheValue(builder, "remoteUrl", remoteUrl, package.id);
            }

            if (localPackages != null && localPackages.Count > 0)
            {
                var ordered = new List<LocalPackageInfo>(localPackages);
                ordered.Sort((left, right) =>
                    string.Compare(left?.Id, right?.Id, StringComparison.OrdinalIgnoreCase));
                foreach (var info in ordered)
                {
                    if (info == null)
                    {
                        continue;
                    }

                    builder.Append("|localId=").Append(info.Id ?? string.Empty);
                    builder.Append(";localName=").Append(info.DisplayName ?? string.Empty);
                    builder.Append(";localDesc=").Append(info.Description ?? string.Empty);
                    builder.Append(";localUnity=").Append(info.Unity ?? string.Empty);
                    builder.Append(";localVersion=").Append(info.Version ?? string.Empty);
                    builder.Append(";localRequired=").Append(info.Required);
                    builder.Append(";localRepo=").Append(info.RepositoryUrl ?? string.Empty);
                    builder.Append(";localRoot=").Append(info.RootPath ?? string.Empty);
                    builder.Append(";localDeps=");
                    AppendStringArray(builder, info.Dependencies);
                }
            }

            return builder.ToString();
        }

        private static void AppendStringArray(StringBuilder builder, string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]))
                {
                    builder.Append(values[i]);
                }

                if (i < values.Length - 1)
                {
                    builder.Append(",");
                }
            }
        }

        private static void AppendCacheValue<T>(StringBuilder builder, string label, Dictionary<string, T> cache, string packageId)
        {
            if (builder == null)
            {
                return;
            }

            builder.Append(";").Append(label).Append("=");
            if (!string.IsNullOrEmpty(packageId) && cache != null && cache.TryGetValue(packageId, out var value))
            {
                builder.Append(value);
            }
        }

        internal List<PackageEntry> GetAvailablePackagesSnapshot()
        {
            var list = new List<PackageEntry>();
            if (_packages == null)
            {
                return list;
            }

            foreach (var package in _packages)
            {
                if (package == null || package.loadStatus != PackageLoadStatus.Loaded || string.IsNullOrEmpty(package.id))
                {
                    continue;
                }

                list.Add(package);
            }

            list.Sort((left, right) =>
            string.Compare(left.displayName ?? left.id, right.displayName ?? right.id, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private string ResolveGitInitializationRef(PackageEntry package, string installedVersion, bool isUpmInstalled, string upmVersion)
        {
            if (package == null)
            {
                return string.Empty;
            }

            var version = isUpmInstalled ? upmVersion : installedVersion;
            if (string.IsNullOrEmpty(version))
            {
                return BuildPackageBranchRef(package.id);
            }

            var tags = GetRepositoryTags(GetRepositoryConfigForPackage(package));
            if (IsTagVersion(package, version, tags))
            {
                return BuildVersionRef(package, version);
            }

            return BuildPackageBranchRef(package.id);
        }

        private string GetSelectedVersionLabel(PackageEntry package)
        {
            if (package == null || package.versions == null || package.versions.Length == 0)
            {
                return null;
            }

            var selectedIndex = GetSelectedIndex(package.id);
            if (selectedIndex < 0)
            {
                selectedIndex = GetDefaultSelectedIndex(package, GetInstalledVersionCached(package));
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, package.versions.Length - 1);
            var selectedVersion = package.versions[selectedIndex];
            return selectedVersion != null ? selectedVersion.version : null;
        }

        private int GetSelectedIndex(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return -1;
            }

            return _selectedVersions.TryGetValue(packageId, out var index) ? index : -1;
        }

        private PackageVersion GetSelectedVersion(PackageEntry package)
        {
            if (package == null || package.versions == null || package.versions.Length == 0)
            {
                return null;
            }

            var index = GetSelectedIndex(package.id);
            index = Mathf.Clamp(index, 0, package.versions.Length - 1);
            return package.versions[index];
        }

        private string BuildUpmGitUrl(PackageEntry package, string referenceOverride = null)
        {
            var repository = GetRepositoryConfigForPackage(package);
            var repoUrl = GetRepositoryUrl(repository);
            if (string.IsNullOrEmpty(repoUrl))
            {
                return null;
            }

            var normalizedRepoUrl = NormalizeUpmRepoUrl(repoUrl);
            var reference = string.IsNullOrEmpty(referenceOverride) ? GetUpmTargetRef(package) : referenceOverride;
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            return normalizedRepoUrl + "#" + reference;
        }

        private string GetUpmTargetRef(PackageEntry package)
        {
            if (package == null)
            {
                return string.Empty;
            }

            var selectedVersion = GetSelectedVersionLabel(package);
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                return BuildVersionRef(package, selectedVersion);
            }

            return BuildPackageBranchRef(package.id);
        }

        private bool TryGetPackageRoot(PackageEntry package, bool isUpmInstalled, out string packageRoot)
        {
            packageRoot = null;
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            if (isUpmInstalled)
            {
                var upmInfo = GetUpmPackageInfo(package);
                var resolved = upmInfo != null ? upmInfo.resolvedPath : null;
                if (!string.IsNullOrEmpty(resolved) && Directory.Exists(resolved))
                {
                    packageRoot = resolved;
                    return true;
                }

                return false;
            }

            var localInfo = GetLocalPackageInfo(package.id);
            if (localInfo != null && !string.IsNullOrEmpty(localInfo.RootPath) && Directory.Exists(localInfo.RootPath))
            {
                packageRoot = localInfo.RootPath;
                return true;
            }

            packageRoot = GetPackageRoot(package);
            return Directory.Exists(packageRoot);
        }

        private bool IsLocalRepository(PackageEntry package)
        {
            var repository = GetRepositoryConfigForPackage(package);
            var repoUrl = GetRepositoryUrl(repository);
            if (string.IsNullOrEmpty(repoUrl))
            {
                return false;
            }

            if (Directory.Exists(repoUrl))
            {
                return true;
            }

            if (Path.IsPathRooted(repoUrl))
            {
                return Directory.Exists(repoUrl);
            }

            if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return true;
            }

            return false;
        }

        private void PublishPackage(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var repository = ResolvePublishRepository(package);
            if (repository == null)
            {
                _statusMessage = "Package repository is not configured. Select it when creating the package.";
                return;
            }

            if (!ConfirmRepositoryAction(repository, "Publish Package",
            "Are you sure? This package will be available for the entire team."))
            {
                return;
            }

            PublishPackageToRepository(package, repository);
        }

        private RepositoryConfig ResolvePublishRepository(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            var localInfo = GetLocalPackageInfo(package.id);
            var repositoryUrl = localInfo != null ? localInfo.RepositoryUrl : null;
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return null;
            }

            return FindRepositoryConfigByUrl(repositoryUrl);
        }

        private void PublishPackageToRepository(PackageEntry package, RepositoryConfig repository)
        {
            if (package == null || string.IsNullOrEmpty(package.id) || repository == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(repository.url))
            {
                _statusMessage = "Repository URL is missing.";
                return;
            }

            var installRoot = package.required
                ? GetEmbeddedPackagesRootOrFallback()
                : GetInstallRootForRepository(repository);
            var packageRoot = string.IsNullOrEmpty(installRoot) ? null : Path.Combine(installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("PublishPackage: directory not found for " + package.id);
                return;
            }

            Debug.Log("PublishPackage: publishing " + package.id + " from " + packageRoot);
            TrySetupGit(packageRoot, repository.url, BuildPackageBranchRef(package.id), repository.accessToken);
            RunGit(packageRoot, "add -A", repository.accessToken);
            RunGit(packageRoot, "commit -m \"Publish package\" --allow-empty", repository.accessToken, logErrors: true);
            var publishBranch = BuildPackageBranchRef(package.id);
            var publishRemoteUrl = BuildAuthenticatedRepoUrl(repository.url, repository.accessToken);
            RunGit(packageRoot,
                "push -u " + QuoteGitArgument(publishRemoteUrl) + " " + publishBranch,
                string.Empty,
                "push -u " + repository.url + " " + publishBranch,
                true);
            _statusMessage = "Published " + package.id + ".";
            RefreshLocalCache();
            StartOperation(LoadManifest());
        }

        public void CommitPackage(PackageEntry package, string message)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                _statusMessage = "Commit message is required.";
                return;
            }

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("CommitPackage: directory not found for " + package.id);
                return;
            }

            Debug.Log("CommitPackage: committing " + package.id + " from " + packageRoot);
            var token = GetRepositoryAccessToken(GetRepositoryConfigForPackage(package));
            RunGit(packageRoot, "add -A", token);
            RunGit(packageRoot, "commit -m \"" + EscapeGitMessage(message) + "\"", token, logErrors: true);
            _statusMessage = "Committed " + package.id + ".";
            StartOperation(RefreshSinglePackage(package));
        }

        public void CreateVersionTag(PackageEntry package, string version, string releaseNotes)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(version))
            {
                _statusMessage = "Version is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(releaseNotes))
            {
                _statusMessage = "Release notes are required.";
                return;
            }

            version = version.Trim();
            releaseNotes = releaseNotes.Trim();

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("CreateVersion: directory not found for " + package.id);
                return;
            }

            var repository = GetRepositoryConfigForPackage(package);
            var token = GetRepositoryAccessToken(repository);
            var tag = package.id + "-v" + version;
            var remoteUrl = BuildAuthenticatedRepoUrl(GetRepositoryUrl(repository), token);
            if (TagExists(packageRoot, remoteUrl, tag))
            {
                _statusMessage = "Version tag already exists: " + tag + ".";
                return;
            }

            var packageJsonPath = Path.Combine(packageRoot, "package.json");
            if (!TryUpdatePackageJsonVersion(packageJsonPath, version, out var packageJsonError))
            {
                _statusMessage = packageJsonError;
                return;
            }

            var changelogPath = Path.Combine(packageRoot, "CHANGELOG.md");
            if (!TryUpdateChangelog(changelogPath, version, releaseNotes, out var changelogError))
            {
                _statusMessage = changelogError;
                return;
            }

            var commitMessage = package.id + "-v" + version;
            RunGit(packageRoot, "add \"package.json\" \"CHANGELOG.md\"", token);
            RunGit(packageRoot, "commit -m \"" + EscapeGitMessage(commitMessage) + "\"", token, logErrors: true);
            var branch = BuildPackageBranchRef(package.id);
            RunGit(packageRoot,
                "push " + QuoteGitArgument(remoteUrl) + " HEAD:" + branch,
                string.Empty,
                "push " + GetRepositoryUrl(repository) + " HEAD:" + branch,
                true);

            Debug.Log("CreateVersion: tagging " + tag + " for " + package.id);
            RunGit(packageRoot, "tag " + tag, token, logErrors: true);
            RunGit(packageRoot,
                "push " + QuoteGitArgument(remoteUrl) + " " + tag,
                string.Empty,
                "push " + GetRepositoryUrl(repository) + " " + tag,
                true);
            _statusMessage = "Created tag " + tag + ".";
            StartOperation(RefreshSinglePackage(package));
        }

        private bool TagExists(string packageRoot, string remoteUrl, string tag)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(tag) || string.IsNullOrWhiteSpace(remoteUrl))
            {
                return false;
            }

            var localMatch = RunGitGetOutput(packageRoot, "tag -l " + tag, string.Empty);
            if (!string.IsNullOrEmpty(localMatch))
            {
                return true;
            }

            var remoteMatch = RunGitGetOutput(packageRoot,
                "ls-remote --tags " + QuoteGitArgument(remoteUrl) + " " + tag, string.Empty);
            return !string.IsNullOrEmpty(remoteMatch);
        }

        private bool RemoteExists(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _remoteExistsCache.TryGetValue(package.id, out var exists) && exists;
        }

        private void OpenRemoteRepository(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var repoUrl = GetRemoteUrlForPackage(package);
            if (string.IsNullOrEmpty(repoUrl))
            {
                return;
            }

            var branch = BuildPackageBranchRef(package.id);
            Application.OpenURL(BuildRemoteBranchUrl(repoUrl, branch));
        }

        private string GetRemoteUrlForPackage(PackageEntry package)
        {
            if (package != null && _remoteUrlCache.TryGetValue(package.id, out var url) && !string.IsNullOrEmpty(url))
            {
                return url;
            }

            var repository = GetRepositoryConfigForPackage(package);
            var repoUrl = GetRepositoryUrl(repository);
            if (!string.IsNullOrEmpty(repoUrl))
            {
                return repoUrl;
            }

            return null;
        }

        private void PushUpdate(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var repository = GetRepositoryConfigForPackage(package);
            if (repository == null)
            {
                _statusMessage = "Repository is not configured for " + package.id + ".";
                return;
            }

            if (!ConfirmRepositoryAction(repository, "Push Update",
            "Are you sure? This package will be available for the entire team."))
            {
                return;
            }

            PushUpdateToRepository(package, repository);
        }

        private void PushUpdateToRepository(PackageEntry package, RepositoryConfig repository)
        {
            if (package == null || string.IsNullOrEmpty(package.id) || repository == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(repository.url))
            {
                _statusMessage = "Repository URL is missing.";
                return;
            }

            var installRoot = package.required
                ? GetEmbeddedPackagesRootOrFallback()
                : GetInstallRootForRepository(repository);
            
            var packageRoot = string.IsNullOrEmpty(installRoot) ? null : Path.Combine(installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("PushUpdate: directory not found for " + package.id);
                return;
            }

            Debug.Log("PushUpdate: pushing " + package.id + " from " + packageRoot);
            EnsureGitRemote(packageRoot, repository.url, repository.accessToken);
            var pushBranch = BuildPackageBranchRef(package.id);
            var pushRemoteUrl = BuildAuthenticatedRepoUrl(repository.url, repository.accessToken);
            RunGit(packageRoot,
                "push -u " + QuoteGitArgument(pushRemoteUrl) + " " + pushBranch,
                string.Empty,
                "push -u " + repository.url + " " + pushBranch,
                true);
            
            RunGit(packageRoot,
                "fetch --all ",
                string.Empty,
                "fetch --all",
                true);
            
            _statusMessage = "Pushed " + package.id + ".";
            StartOperation(RefreshSinglePackage(package));
        }

        private void UninstallPackageSafe(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("UninstallPackage: directory not found for " + package.id);
                return;
            }

            if (!TryDeleteDirectory(packageRoot))
            {
                SetOperationError("Uninstall", package, GetInstalledVersionCached(package), "Check file permissions.");
                Debug.LogWarning("UninstallPackage: failed to delete " + packageRoot);
                return;
            }

            if (!TryDeletePackageMeta(packageRoot))
            {
                SetOperationError("Uninstall", package, GetInstalledVersionCached(package), "Failed to delete package meta file.");
                return;
            }

            AssetDatabase.Refresh();
            _statusMessage = "Uninstalled " + package.id + ".";
            RefreshLocalCache();
            StartOperation(LoadManifest());
        }

        private bool TryDeletePackageMeta(string packageRoot)
        {
            if (string.IsNullOrEmpty(packageRoot))
            {
                return true;
            }

            var metaPath = packageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".meta";
            if (!File.Exists(metaPath))
            {
                return true;
            }

            try
            {
                File.Delete(metaPath);
                return true;
            }
            catch (Exception ex)
            {
                _statusMessage = "Failed to delete meta for package folder: " + ex.Message;
                return false;
            }
        }

        private void SetOperationError(string operation, PackageEntry package, string version, string details)
        {
            var packageId = package != null ? package.id : "unknown";
            var resolvedVersion = string.IsNullOrEmpty(version) ? "unknown" : version;
            var message = "ERROR: " + operation + " failed for " + packageId + " (" + resolvedVersion + ").";
            if (!string.IsNullOrEmpty(details))
            {
                message += " " + details;
            }

            _statusMessage = message;
        }

        private string ResolveLatestRef(PackageEntry package)
        {
            if (package == null)
            {
                return string.Empty;
            }

            var tags = GetRepositoryTags(GetRepositoryConfigForPackage(package));
            var latestVersion = GetLatestVersionFromTags(package.id, tags);
            if (!string.IsNullOrEmpty(latestVersion))
            {
                return BuildVersionRef(package, latestVersion);
            }

            return BuildPackageBranchRef(package.id);
        }

        private int GetDefaultSelectedIndex(PackageEntry package, string installedVersion)
        {
            if (package == null || package.versions == null || package.versions.Length == 0)
            {
                return 0;
            }

            if (!string.IsNullOrEmpty(installedVersion))
            {
                for (var i = 0; i < package.versions.Length; i++)
                {
                    if (string.Equals(package.versions[i].version, installedVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return package.versions.Length - 1;
        }

        private IEnumerator EnsureAutoUpdatedPackagesInstalled()
        {
            if (_packages == null || _packages.Count == 0)
            {
                yield break;
            }

            foreach (var package in _packages)
            {
                if (package == null || package.loadStatus != PackageLoadStatus.Loaded)
                {
                    continue;
                }

                var shouldAutoUpdate = ShouldAutoUpdate(package);
                var shouldAutoInstall = package.required || shouldAutoUpdate;
                if (!shouldAutoInstall)
                {
                    continue;
                }

                var upmInfo = GetUpmPackageInfo(package);
                var upmVersion = upmInfo != null ? upmInfo.version : null;
                var isUpmInstalled = upmInfo != null;
                var installedVersion = isUpmInstalled ? upmVersion : GetInstalledVersionCached(package);
                var needsInstall = string.IsNullOrEmpty(installedVersion);
                var needsUpdate = !needsInstall && IsUpdateAvailable(package, installedVersion);
                if (!needsInstall && (!shouldAutoUpdate || !needsUpdate))
                {
                    continue;
                }

                var latestVersion = GetLatestVersion(package);
                var reference = !string.IsNullOrEmpty(latestVersion)
                ? BuildVersionRef(package, latestVersion)
                : BuildPackageBranchRef(package.id);
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (isUpmInstalled)
                {
                    yield return UpdatePackageViaUpm(package, reference, latestVersion);
                }
                else
                {
                    var operation = needsInstall ? "Installation" : "Update";
                    yield return InstallPackage(package, reference, operation, latestVersion);
                }
            }
        }

        private IEnumerator EnsureDependenciesInstalled()
        {
            if (_packages == null || _packages.Count == 0)
            {
                yield break;
            }

            var packageLookup = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in _packages)
            {
                if (package == null || string.IsNullOrEmpty(package.id))
                {
                    continue;
                }

                packageLookup[package.id] = package;
            }

            foreach (var package in _packages)
            {
                if (package == null || package.loadStatus != PackageLoadStatus.Loaded)
                {
                    continue;
                }

                if (package.dependencies == null || package.dependencies.Length == 0)
                {
                    continue;
                }

                var isPackageInstalled = IsPackageInstalled(package, out _, out _);
                var shouldUpdateDependencies = ShouldAutoUpdate(package);
                if (!isPackageInstalled && !shouldUpdateDependencies)
                {
                    continue;
                }

                foreach (var dependency in package.dependencies)
                {
                    if (!TryParseDependency(dependency, out var dependencyId, out var dependencyVersion)
                    || string.IsNullOrEmpty(dependencyId))
                    {
                        continue;
                    }

                    if (!packageLookup.TryGetValue(dependencyId, out var dependencyPackage)
                    || dependencyPackage.loadStatus != PackageLoadStatus.Loaded)
                    {
                        continue;
                    }

                    var targetVersion = !string.IsNullOrEmpty(dependencyVersion)
                    ? dependencyVersion
                    : GetLatestVersion(dependencyPackage);
                    var reference = !string.IsNullOrEmpty(targetVersion)
                    ? BuildVersionRef(dependencyPackage, targetVersion)
                    : BuildPackageBranchRef(dependencyPackage.id);
                    if (string.IsNullOrEmpty(reference))
                    {
                        continue;
                    }

                    var isDependencyInstalled = IsPackageInstalled(dependencyPackage, out var isUpmInstalled,
                    out var installedVersion);
                    if (!isDependencyInstalled)
                    {
                        yield return InstallPackage(dependencyPackage, reference, "Dependency installation", targetVersion);
                        continue;
                    }

                    if (string.IsNullOrEmpty(targetVersion))
                    {
                        continue;
                    }

                    if (string.Equals(installedVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!shouldUpdateDependencies)
                    {
                        continue;
                    }

                    if (isUpmInstalled)
                    {
                        yield return UpdatePackageViaUpm(dependencyPackage, reference, targetVersion);
                    }
                    else
                    {
                        yield return InstallPackage(dependencyPackage, reference, "Dependency update", targetVersion);
                    }
                }
            }
        }

        private bool ShouldAutoUpdate(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            if (package.required)
            {
                return false;
            }

            return IsRepositoryAutoUpdateEnabled(package) && IsAutoUpdateEnabled(package.id);
        }

        private void ApplyVersionsFromTags(PackageEntry package, List<string> tags)
        {
            if (package == null)
            {
                return;
            }

            var versions = ResolveVersionsFromTags(package.id, tags);
            if (versions.Length == 0)
            {
                return;
            }

            package.versions = BuildVersionEntries(versions);
        }

        private IEnumerator InstallPackage(PackageEntry package, string reference, string operation, string targetVersion)
        {
            var repositoryConfig = GetRepositoryConfigForPackage(package);
            if (repositoryConfig == null)
            {
                _statusMessage = "Repository information is missing.";
                yield break;
            }

            var installRoot = package.required
                ? GetEmbeddedPackagesRootOrFallback()
                : GetInstallRootForRepository(repositoryConfig);
            var token = GetRepositoryAccessToken(repositoryConfig);

            var repoUrl = GetRepositoryUrl(repositoryConfig);
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                _statusMessage = "Repository URL is invalid.";
                yield break;
            }

            if (!repositoryConfig.isPublic)
            {
                yield return InstallPackageViaGit(repoUrl, package, reference, installRoot, token, operation, targetVersion);
                yield break;
            }

            if (!TryBuildRepositoryInfoFromUrl(repoUrl, "main", out var repository))
            {
                _statusMessage = "Repository URL is invalid.";
                yield break;
            }

            var hadError = false;
            var errorMessage = string.Empty;
            yield return _installer.InstallPackage(repository, package, reference, installRoot, token,
            OnInstallProgress, message =>
            {
                hadError = true;
                errorMessage = message;
                _statusMessage = message;
            });

            if (!hadError)
            {
                _statusMessage = "Installed " + package.id + ".";
                if (IsGitInitialized(package))
                {
                    UpdateGitForInstalledPackage(package, reference);
                }
                RefreshLocalCache();
                yield break;
            }

            if (IsGitPackAccessDenied(errorMessage) && TryCleanReinstall(package))
            {
                hadError = false;
                errorMessage = string.Empty;
                yield return _installer.InstallPackage(repository, package, reference, installRoot, token,
                OnInstallProgress, message =>
                {
                    hadError = true;
                    errorMessage = message;
                    _statusMessage = message;
                });

                if (!hadError)
                {
                    _statusMessage = "Installed " + package.id + ".";
                    if (IsGitInitialized(package))
                    {
                        UpdateGitForInstalledPackage(package, reference);
                    }
                    RefreshLocalCache();
                    yield break;
                }
            }

            SetOperationError(operation, package, targetVersion, errorMessage);
        }

        private IEnumerator InstallPackageViaGit(string repoUrl, PackageEntry package, string reference, string installRoot,
            string token, string operation, string targetVersion)
        {
            if (package == null)
            {
                _statusMessage = "Package information is missing.";
                yield break;
            }

            if (string.IsNullOrEmpty(reference))
            {
                _statusMessage = "Package reference is missing.";
                SetOperationError(operation, package, targetVersion, _statusMessage);
                yield break;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "tgs-pm-install-" + Guid.NewGuid().ToString("N"));
            var hadError = false;
            var errorMessage = string.Empty;
            try
            {
                Directory.CreateDirectory(tempDirectory);
                var cloneArgs = "clone --depth 1 --branch " + QuoteGitArgument(reference) + " " +
                                QuoteGitArgument(repoUrl.Trim()) + " " + QuoteGitArgument(tempDirectory);
                if (!TryRunGitCommand(cloneArgs, token, out _, out var cloneError))
                {
                    hadError = true;
                    errorMessage = string.IsNullOrWhiteSpace(cloneError) ? "Failed to clone repository." : cloneError;
                    _statusMessage = errorMessage;
                    yield break;
                }

                var pathInRepo = package.pathInRepo ?? string.Empty;
                var normalizedPath = pathInRepo.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var sourceRoot = string.IsNullOrEmpty(normalizedPath) ? tempDirectory : Path.Combine(tempDirectory, normalizedPath);
                if (!Directory.Exists(sourceRoot))
                {
                    hadError = true;
                    errorMessage = "Package path not found in repository: " + package.pathInRepo;
                    _statusMessage = errorMessage;
                    yield break;
                }

                var packageRoot = Path.Combine(installRoot, package.id);
                if (Directory.Exists(packageRoot))
                {
                    if (!TryDeleteDirectory(packageRoot))
                    {
                        hadError = true;
                        errorMessage = "Failed to clean install " + package.id + ". Check file permissions.";
                        _statusMessage = errorMessage;
                        yield break;
                    }
                }

                Directory.CreateDirectory(packageRoot);
                CopyDirectoryRecursive(sourceRoot, packageRoot);

                AssetDatabase.Refresh();

                try
                {
                    Client.Resolve();
                }
                catch (Exception)
                {
                    // Resolve can fail when called during compilation; ignore.
                }
            }
            catch (Exception ex)
            {
                hadError = true;
                errorMessage = ex.Message;
                _statusMessage = "Installation failed: " + errorMessage;
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    TryDeleteDirectory(tempDirectory);
                }
            }

            if (!hadError)
            {
                _statusMessage = "Installed " + package.id + ".";
                if (IsGitInitialized(package))
                {
                    UpdateGitForInstalledPackage(package, reference);
                }
                RefreshLocalCache();
                yield break;
            }

            SetOperationError(operation, package, targetVersion, errorMessage);
        }

        private IEnumerator InstallPackageViaUpm(PackageEntry package)
        {
            if (package == null)
            {
                _statusMessage = "ERROR: UPM install failed for unknown package.";
                yield break;
            }

            var upmUrl = BuildUpmGitUrl(package);
            if (string.IsNullOrEmpty(upmUrl))
            {
                SetOperationError("UPM Install", package, GetUpmTargetRef(package), "Missing repository URL.");
                yield break;
            }

            _lastUpmUrl = upmUrl;
            _statusMessage = "Installing " + package.id + " via UPM...";
            AddRequest request;
            try
            {
                request = Client.Add(upmUrl);
            }
            catch (Exception ex)
            {
                SetOperationError("UPM Install", package, GetUpmTargetRef(package), ex.Message);
                yield break;
            }

            while (!request.IsCompleted)
            {
                yield return null;
            }

            if (request.Status == StatusCode.Success)
            {
                _statusMessage = "Installed " + package.id + " via UPM.";
                RefreshLocalCache();
                yield break;
            }

            var errorMessage = request.Error != null ? request.Error.message : "Unknown UPM error.";
            SetOperationError("UPM Install", package, GetUpmTargetRef(package), errorMessage);
        }

        private IEnumerator RemovePackageViaUpm(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                _statusMessage = "ERROR: UPM uninstall failed for unknown package.";
                yield break;
            }

            RemoveRequest request;
            try
            {
                request = Client.Remove(package.id);
            }
            catch (Exception ex)
            {
                SetOperationError("UPM Uninstall", package, GetUpmTargetRef(package), ex.Message);
                yield break;
            }

            while (!request.IsCompleted)
            {
                yield return null;
            }

            if (request.Status == StatusCode.Success)
            {
                _statusMessage = "Uninstalled " + package.id + " via UPM.";
                RefreshLocalCache();
                yield break;
            }

            var errorMessage = request.Error != null ? request.Error.message : "Unknown UPM error.";
            SetOperationError("UPM Uninstall", package, GetUpmTargetRef(package), errorMessage);
        }

        private IEnumerator UpdatePackageViaUpm(PackageEntry package, string reference, string targetVersion)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                _statusMessage = "ERROR: UPM update failed for unknown package.";
                yield break;
            }

            var upmUrl = BuildUpmGitUrl(package, reference);
            if (string.IsNullOrEmpty(upmUrl))
            {
                SetOperationError("UPM Update", package, targetVersion, "Missing repository URL.");
                yield break;
            }

            _statusMessage = "Updating " + package.id + " via UPM...";
            RemoveRequest removeRequest;
            try
            {
                removeRequest = Client.Remove(package.id);
            }
            catch (Exception ex)
            {
                SetOperationError("UPM Update", package, targetVersion, ex.Message);
                yield break;
            }

            while (!removeRequest.IsCompleted)
            {
                yield return null;
            }

            if (removeRequest.Status != StatusCode.Success)
            {
                var removeError = removeRequest.Error != null ? removeRequest.Error.message : "Unknown UPM error.";
                SetOperationError("UPM Update", package, targetVersion, removeError);
                yield break;
            }

            AddRequest addRequest;
            try
            {
                addRequest = Client.Add(upmUrl);
            }
            catch (Exception ex)
            {
                SetOperationError("UPM Update", package, targetVersion, ex.Message);
                yield break;
            }

            while (!addRequest.IsCompleted)
            {
                yield return null;
            }

            if (addRequest.Status == StatusCode.Success)
            {
                _statusMessage = "Updated " + package.id + " via UPM.";
                RefreshLocalCache();
                yield break;
            }

            var addError = addRequest.Error != null ? addRequest.Error.message : "Unknown UPM error.";
            SetOperationError("UPM Update", package, targetVersion, addError);
        }

        private bool TryCleanReinstall(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                return true;
            }

            try
            {
                if (!TryDeleteDirectory(packageRoot))
                {
                    _statusMessage = "Failed to clean install " + package.id + ". Check file permissions.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Failed to clean install " + package.id + ": " + ex.Message;
                return false;
            }

            return true;
        }

        public void BeginCreatePackage(CreatePackageData data)
        {
            if (_isBusy)
            {
                return;
            }

            StartOperation(CreatePackageFromTemplate(data));
        }

        private IEnumerator CreatePackageFromTemplate(CreatePackageData data)
        {
            if (data == null)
            {
                _statusMessage = "Missing package data.";
                Debug.LogWarning("CreatePackage: missing data.");
                yield break;
            }

            var repository = FindRepositoryById(data.RepositoryId);
            if (repository == null)
            {
                _statusMessage = "Repository selection is required.";
                Debug.LogWarning("CreatePackage: repository missing.");
                yield break;
            }

            if (string.IsNullOrEmpty(data.Name) || string.IsNullOrEmpty(data.Author))
            {
                _statusMessage = "Name and author are required.";
                Debug.LogWarning("CreatePackage: name/author missing.");
                yield break;
            }

            var packageId = BuildPackageId(data.Name, repository.packagePrefix);
            if (string.IsNullOrEmpty(packageId))
            {
                _statusMessage = "Invalid package name.";
                Debug.LogWarning("CreatePackage: invalid package name.");
                yield break;
            }

            var version = string.IsNullOrEmpty(data.Version) ? "1.0.0" : data.Version;
            var installRoot = GetInstallRootForRepository(repository);
            if (string.IsNullOrEmpty(installRoot))
            {
                _statusMessage = "Install root is missing for repository.";
                Debug.LogWarning("CreatePackage: install root missing.");
                yield break;
            }

            var packageRoot = Path.Combine(installRoot, packageId);
            if (Directory.Exists(packageRoot))
            {
                _statusMessage = "Package folder already exists: " + packageRoot;
                Debug.LogWarning("CreatePackage: folder already exists: " + packageRoot);
                yield break;
            }

            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(Path.Combine(packageRoot, "Editor"));
            Directory.CreateDirectory(Path.Combine(packageRoot, "Runtime"));

            try
            {
                WritePackageFiles(packageRoot, packageId, data.Name, data.Author, data.Description, version,
                data.UnityVersion, data.Required, data.Dependencies, repository.url);
            }
            catch (Exception ex)
            {
                _statusMessage = "Failed to write package files: " + ex.Message;
                Debug.LogError("CreatePackage: failed to write files. " + ex);
                yield break;
            }

            var repoUrl = repository.url;
            if (!string.IsNullOrEmpty(repoUrl))
            {
                TrySetupGit(packageRoot, repoUrl, BuildPackageBranchRef(packageId), repository.accessToken);
            }
            _statusMessage = "Package created: " + packageId;
            Debug.Log("CreatePackage: created " + packageId + " at " + packageRoot);
            StartOperation(LoadManifest());
        }

        private void OnInstallProgress(InstallProgress progress)
        {
            if (progress.title != null)
            {
                EditorUtility.DisplayProgressBar(progress.title, progress.info ?? string.Empty, progress.progress);
            }
        }
    }
}
