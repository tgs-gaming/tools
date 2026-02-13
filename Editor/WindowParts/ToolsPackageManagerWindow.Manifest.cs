using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private IEnumerator LoadManifest()
        {
            _statusMessage = "Loading packages...";
            _lastPackageRefreshSucceeded = false;
            _packages = new List<PackageEntry>();
            _packageUnityRequirements.Clear();
            _packageCompatibility.Clear();
            _repositoryTags.Clear();
            ClearLocalCaches();
            ClearRepositoryAccessErrors();

            if (_repositories == null || _repositories.Count == 0)
            {
                _statusMessage = "No repositories configured.";
                yield break;
            }

            var packageLookup = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var repository in _repositories)
            {
                if (repository == null || string.IsNullOrWhiteSpace(repository.url))
                {
                    continue;
                }

                var repoUrl = GetRepositoryUrl(repository);
                var token = GetRepositoryAccessToken(repository);

                List<string> tags = null;
                string tagError = null;
                yield return LoadRepositoryRefs(repository, repoUrl, token, true,
                    result => tags = result,
                    error => tagError = error);

                if (!string.IsNullOrEmpty(tagError))
                {
                    RecordRepositoryAccessError(repository, tagError, "Tags");
                    tags = new List<string>();
                }
                else if (tags == null)
                {
                    tags = new List<string>();
                }

                if (!string.IsNullOrEmpty(repository.id))
                {
                    _repositoryTags[repository.id] = tags;
                }

                List<string> branches = null;
                string branchError = null;
                yield return LoadRepositoryRefs(repository, repoUrl, token, false,
                    result => branches = result,
                    error => branchError = error);

                if (!string.IsNullOrEmpty(branchError))
                {
                    RecordRepositoryAccessError(repository, branchError, "Branches");
                    continue;
                }

                if (branches == null)
                {
                    continue;
                }

                foreach (var branch in branches)
                {
                    if (string.IsNullOrEmpty(branch) || !branch.StartsWith(PackageBranchPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var packageId = branch.Substring(PackageBranchPrefix.Length);
                    if (string.IsNullOrEmpty(packageId))
                    {
                        continue;
                    }

                    if (packageLookup.ContainsKey(packageId))
                    {
                        continue;
                    }

                    var entry = new PackageEntry
                    {
                        id = packageId,
                        loadStatus = PackageLoadStatus.Pending,
                        repositoryId = repository.id
                    };
                    _packages.Add(entry);
                    packageLookup[packageId] = entry;
                }

                yield return null;
            }

            RefreshLocalCache();

            if (_packages.Count == 0)
            {
                _statusMessage = "No packages found in tool branches.";
                yield break;
            }

            foreach (var package in _packages)
            {
                if (package == null)
                {
                    continue;
                }

                package.loadStatus = PackageLoadStatus.Loading;
                package.loadError = null;

                if (string.IsNullOrEmpty(package.id))
                {
                    SetConfigError(package, "Package id is missing.");
                    continue;
                }

                if (package.pathInRepo == null)
                {
                    package.pathInRepo = string.Empty;
                }

                var repository = GetRepositoryConfigForPackage(package);
                if (repository == null)
                {
                    SetConfigError(package, "Repository metadata is missing.");
                    continue;
                }

                var tags = GetRepositoryTags(repository);
                yield return LoadPackageMetadata(repository, package, tags);
                ApplyLocalPackageOverrides(package);
                Repaint();
            }

            yield return EnsureAutoUpdatedPackagesInstalled();
            yield return EnsureDependenciesInstalled();
            _statusMessage = "Packages loaded.";
            _lastPackageRefreshSucceeded = _packages.Count > 0;
        }

        private List<string> GetRepositoryTags(RepositoryConfig repository)
        {
            if (repository == null || string.IsNullOrEmpty(repository.id))
            {
                return new List<string>();
            }

            return _repositoryTags.TryGetValue(repository.id, out var tags)
                ? (tags ?? new List<string>())
                : new List<string>();
        }

        private IEnumerator LoadRepositoryRefs(RepositoryConfig repository, string repoUrl, string token, bool loadTags,
            Action<List<string>> onSuccess, Action<string> onError)
        {
            if (ShouldUseRestAccess(repository))
            {
                if (!TryBuildRepositoryInfoFromUrl(repoUrl, "main", out var repoInfo))
                {
                    onError?.Invoke("Repository URL is invalid.");
                    yield break;
                }

                GitHubRequestError requestError = null;
                if (loadTags)
                {
                    GitHubTag[] remoteTags = null;
                    yield return _client.GetTags(repoInfo.owner, repoInfo.name, token,
                        result => remoteTags = result,
                        err => requestError = err);

                    if (requestError != null)
                    {
                        onError?.Invoke(requestError.ToString());
                        yield break;
                    }

                    onSuccess?.Invoke(ExtractRemoteTagNames(remoteTags));
                }
                else
                {
                    GitHubBranch[] remoteBranches = null;
                    yield return _client.GetBranches(repoInfo.owner, repoInfo.name, token,
                        result => remoteBranches = result,
                        err => requestError = err);

                    if (requestError != null)
                    {
                        onError?.Invoke(requestError.ToString());
                        yield break;
                    }

                    onSuccess?.Invoke(ExtractRemoteBranchNames(remoteBranches));
                }

                yield break;
            }

            var typeArgument = loadTags ? "--tags" : "--heads";
            var refPrefix = loadTags ? "refs/tags/" : "refs/heads/";
            if (!TryGetRemoteRefs(repository, repoUrl, token, typeArgument, refPrefix, out var refs, out var error))
            {
                onError?.Invoke(error);
                yield break;
            }

            onSuccess?.Invoke(refs);
        }

        private static List<string> ExtractRemoteTagNames(GitHubTag[] tags)
        {
            var names = new List<string>();
            if (tags == null || tags.Length == 0)
            {
                return names;
            }

            foreach (var tag in tags)
            {
                if (tag == null || string.IsNullOrWhiteSpace(tag.name))
                {
                    continue;
                }

                names.Add(tag.name.Trim());
            }

            return RemoveDuplicateRefs(names);
        }

        private static List<string> ExtractRemoteBranchNames(GitHubBranch[] branches)
        {
            var names = new List<string>();
            if (branches == null || branches.Length == 0)
            {
                return names;
            }

            foreach (var branch in branches)
            {
                if (branch == null || string.IsNullOrWhiteSpace(branch.name))
                {
                    continue;
                }

                names.Add(branch.name.Trim());
            }

            return RemoveDuplicateRefs(names);
        }

        private static List<string> RemoveDuplicateRefs(List<string> refs)
        {
            if (refs == null || refs.Count == 0)
            {
                return new List<string>();
            }

            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in refs)
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    continue;
                }

                var normalized = reference.Trim();
                if (seen.Add(normalized))
                {
                    results.Add(normalized);
                }
            }

            return results;
        }

        private static bool ShouldUseRestAccess(RepositoryConfig repository)
        {
            return repository != null && repository.isPublic;
        }

        private IEnumerator LoadPackageMetadata(RepositoryConfig repository, PackageEntry package, List<string> tags)
        {
            if (repository == null || package == null)
            {
                yield break;
            }

            var branchRef = BuildPackageBranchRef(package.id);
            var token = GetRepositoryAccessToken(repository);
            if (ShouldUseRestAccess(repository))
            {
                if (!TryBuildRepositoryInfoFromUrl(GetRepositoryUrl(repository), "main", out var repoInfo))
                {
                    SetConfigError(package, "Repository URL is invalid.");
                    RecordRepositoryAccessError(repository, "Invalid repository URL.", "Repository");
                    yield break;
                }

                yield return LoadPackageMetadataViaRest(repository, repoInfo, package, tags, branchRef, token);
                yield break;
            }

            yield return LoadPackageMetadataViaGit(repository, package, tags, branchRef, token);
        }

        private IEnumerator LoadPackageMetadataViaRest(RepositoryConfig repository, RepositoryInfo repoInfo,
            PackageEntry package, List<string> tags, string branchRef, string token)
        {
            GitHubContentItem[] packageItems = null;
            GitHubRequestError packageError = null;

            yield return _client.GetContents(repoInfo.owner, repoInfo.name, "package.json", branchRef, token,
                result => packageItems = result, err => packageError = err);

            if (packageError != null)
            {
                if (packageError.statusCode == 404)
                {
                    yield return ResolveMissingPackageJson(repository, repoInfo, package, branchRef, token);
                    yield break;
                }

                RecordRepositoryAccessError(repository, packageError, "package.json");
                SetConfigError(package, packageError.ToString());
                yield break;
            }

            var packageItem = FindPackageJsonItem(packageItems);
            if (packageItem == null || string.IsNullOrEmpty(packageItem.download_url))
            {
                SetConfigError(package, "package.json not found.");
                yield break;
            }

            string packageJson = null;
            GitHubRequestError downloadError = null;
            yield return _client.DownloadText(packageItem.download_url, token,
                text => packageJson = text, err => downloadError = err);

            if (downloadError != null)
            {
                RecordRepositoryAccessError(repository, downloadError, "package.json download");
                SetConfigError(package, downloadError.ToString());
                yield break;
            }

            ApplyPackageJson(package, packageJson);
            ApplyVersionsFromTags(package, tags);
        }

        private IEnumerator LoadPackageMetadataViaGit(RepositoryConfig repository, PackageEntry package,
            List<string> tags, string branchRef, string token)
        {
            var repoUrl = GetRepositoryUrl(repository);
            if (!TryLoadPackageJsonViaGit(repoUrl, branchRef, token, out var packageJson, out var error,
                    out var branchNotFound, out var packageJsonMissing))
            {
                if (branchNotFound)
                {
                    package.loadStatus = PackageLoadStatus.BranchNotFound;
                    package.loadError = "Branch not found: " + branchRef;
                    yield break;
                }

                if (packageJsonMissing)
                {
                    SetConfigError(package, "package.json not found.");
                    yield break;
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    RecordRepositoryAccessError(repository, error, "package.json");
                    SetConfigError(package, error);
                    yield break;
                }

                SetConfigError(package, "package.json not found.");
                yield break;
            }

            ApplyPackageJson(package, packageJson);
            ApplyVersionsFromTags(package, tags);
            yield break;
        }

        private static bool TryLoadPackageJsonViaGit(string repoUrl, string branchRef, string token,
            out string packageJson, out string error, out bool branchNotFound, out bool packageJsonMissing)
        {
            packageJson = null;
            error = null;
            branchNotFound = false;
            packageJsonMissing = false;

            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                error = "Repository URL is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(branchRef))
            {
                error = "Branch reference is missing.";
                return false;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "tgs-pm-manifest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDirectory);

                if (!TryRunGitCommand(tempDirectory, "init", string.Empty, out _, out var initError))
                {
                    error = string.IsNullOrWhiteSpace(initError) ? "Failed to initialize git." : initError;
                    return false;
                }

                var authenticatedUrl = BuildAuthenticatedRepoUrl(repoUrl.Trim(), token);
                var fetchArguments = "fetch --depth 1 " + QuoteGitArgument(authenticatedUrl) + " " +
                    QuoteGitArgument(branchRef);
                if (!TryRunGitCommand(tempDirectory, fetchArguments, string.Empty, out _, out var fetchError))
                {
                    if (IsGitBranchNotFoundError(fetchError))
                    {
                        branchNotFound = true;
                    }

                    error = string.IsNullOrWhiteSpace(fetchError) ? "Failed to fetch branch." : fetchError;
                    return false;
                }

                var showArguments = "show " + QuoteGitArgument("FETCH_HEAD:package.json");
                if (!TryRunGitCommand(tempDirectory, showArguments, string.Empty, out var jsonOutput, out var showError))
                {
                    if (IsGitPathNotFoundError(showError))
                    {
                        packageJsonMissing = true;
                        error = "package.json not found.";
                        return false;
                    }

                    error = string.IsNullOrWhiteSpace(showError) ? "Failed to read package.json from git." : showError;
                    return false;
                }

                packageJson = jsonOutput;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    TryDeleteDirectory(tempDirectory);
                }
            }
        }

        private static bool IsGitBranchNotFoundError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.IndexOf("couldn't find remote ref", StringComparison.OrdinalIgnoreCase) >= 0
                   || error.IndexOf("remote branch", StringComparison.OrdinalIgnoreCase) >= 0
                   && error.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                   || error.IndexOf("could not find remote branch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsGitPathNotFoundError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.IndexOf("path 'package.json' does not exist", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IEnumerator ResolveMissingPackageJson(RepositoryConfig repository, RepositoryInfo repoInfo,
            PackageEntry package, string branchRef, string token)
        {
            GitHubRequestError branchError = null;

            yield return _client.GetContents(repoInfo.owner, repoInfo.name, string.Empty, branchRef, token,
                _ => { }, err => branchError = err);

            if (branchError != null && branchError.statusCode == 404)
            {
                package.loadStatus = PackageLoadStatus.BranchNotFound;
                package.loadError = "Branch not found: " + branchRef;
                yield break;
            }

            if (branchError != null)
            {
                RecordRepositoryAccessError(repository, branchError, "Branch");
                SetConfigError(package, branchError.ToString());
                yield break;
            }

            SetConfigError(package, "package.json not found.");
        }

        private static GitHubContentItem FindPackageJsonItem(GitHubContentItem[] items)
        {
            if (items == null || items.Length == 0)
            {
                return null;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.name, "package.json", StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return items.Length == 1 ? items[0] : null;
        }

        private void ApplyPackageJson(PackageEntry package, string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                SetConfigError(package, "package.json is empty.");
                return;
            }

            PackageJsonInfo info;
            try
            {
                info = JsonUtility.FromJson<PackageJsonInfo>(json);
            }
            catch (Exception ex)
            {
                SetConfigError(package, "package.json parse error: " + ex.Message);
                return;
            }

            if (info == null || string.IsNullOrEmpty(info.name))
            {
                SetConfigError(package, "package.json missing name.");
                return;
            }

            package.displayName = string.IsNullOrEmpty(info.displayName) ? info.name : info.displayName;
            package.description = info.description;
            var parsedDependencies = info.dependencies ?? ParseDependenciesFromJson(json);
            package.dependencies = parsedDependencies ?? Array.Empty<string>();
            package.required = info.required;

            if (package.versions == null || package.versions.Length == 0)
            {
                package.versions = BuildVersionEntries(new[] { info.version });
            }

            if (!string.IsNullOrEmpty(info.pathInRepo))
            {
                package.pathInRepo = info.pathInRepo;
            }
            else if (package.pathInRepo == null)
            {
                package.pathInRepo = string.Empty;
            }

            if (info.author != null && !string.IsNullOrEmpty(info.author.name))
            {
                package.author = info.author.name;
            }

            if (!string.IsNullOrEmpty(info.unity))
            {
                _packageUnityRequirements[package.id] = info.unity;
                _packageCompatibility[package.id] = IsUnityCompatible(info.unity);
            }
            else
            {
                _packageUnityRequirements[package.id] = string.Empty;
                _packageCompatibility[package.id] = true;
            }

            if (package.versions == null || package.versions.Length == 0)
            {
                SetConfigError(package, "No version information found.");
                return;
            }

            package.loadStatus = PackageLoadStatus.Loaded;
            package.loadError = null;
        }

        private static bool TryGetRemoteRefs(RepositoryConfig repository, string repoUrl, string token,
            string typeArgument, string refPrefix, out List<string> refs, out string error)
        {
            refs = new List<string>();
            error = null;

            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                error = "Repository URL is missing.";
                return false;
            }

            var authenticatedUrl = BuildAuthenticatedRepoUrl(repoUrl.Trim(), token);
            var argumentUrl = QuoteGitArgument(authenticatedUrl);
            var arguments = "ls-remote " + typeArgument + " " + argumentUrl;
            if (!TryRunGitCommand(arguments, string.Empty, out var output, out error))
            {
                return false;
            }

            if (string.IsNullOrEmpty(output))
            {
                return true;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 2)
                {
                    continue;
                }

                var refName = parts[1]?.Trim();
                if (string.IsNullOrEmpty(refName))
                {
                    continue;
                }

                if (!refName.StartsWith(refPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (refName.EndsWith("^{}", StringComparison.Ordinal))
                {
                    refName = refName.Substring(0, refName.Length - 3);
                }

                var name = refName.Substring(refPrefix.Length);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (seen.Add(name))
                {
                    refs.Add(name);
                }
            }

            return true;
        }

        private static bool TryRunGitCommand(string arguments, string token, out string output, out string error)
        {
            return TryRunGitCommand(Path.GetTempPath(), arguments, token, out output, out error);
        }

        private static bool TryRunGitCommand(string workingDirectory, string arguments, string token, out string output,
            out string error)
        {
            output = null;
            error = null;

            var gitArgs = BuildGitArguments(arguments, token);
            var startInfo = new ProcessStartInfo("git", gitArgs)
            {
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Path.GetTempPath() : workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "Failed to start git.";
                        return false;
                    }

                    var stdOut = process.StandardOutput.ReadToEnd();
                    var stdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        error = string.IsNullOrWhiteSpace(stdErr) ? "Git command failed." : stdErr.Trim();
                        return false;
                    }

                    output = stdOut.Trim();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string QuoteGitArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '"' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void ClearRepositoryAccessErrors()
        {
            _repositoryAccessErrors.Clear();
        }

        private void RecordRepositoryAccessError(RepositoryConfig repository, GitHubRequestError error, string context)
        {
            if (repository == null || error == null)
            {
                return;
            }

            if (error.statusCode == 404)
            {
                return;
            }

            RecordRepositoryAccessError(repository, error.ToString(), context);
        }

        private void RecordRepositoryAccessError(RepositoryConfig repository, string error, string context)
        {
            if (repository == null || string.IsNullOrEmpty(error))
            {
                return;
            }

            var repoLabel = BuildRepositoryAccessLabel(repository);
            var details = string.IsNullOrEmpty(context) ? error : context + ": " + error;
            if (string.IsNullOrEmpty(repoLabel))
            {
                repoLabel = "Unknown repository";
            }

            _repositoryAccessErrors[repoLabel] = details;
        }

        private static string BuildRepositoryAccessLabel(RepositoryConfig repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(repository.url))
            {
                return BuildRepositoryShortLabel(repository.url);
            }

            return string.IsNullOrEmpty(repository.id) ? "Repository" : repository.id;
        }

        private static bool TryBuildRepositoryInfoFromUrl(string repoUrl, string defaultBranch, out RepositoryInfo repository)
        {
            repository = null;
            if (!TryGetRepoInfoFromUrl(repoUrl, out var owner, out var repo))
            {
                return false;
            }

            repository = new RepositoryInfo
            {
                owner = owner,
                name = repo,
                defaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch,
                description = null
            };
            return true;
        }

        private static bool TryGetRepoInfoFromUrl(string repoUrl, out string owner, out string repo)
        {
            owner = null;
            repo = null;

            if (string.IsNullOrEmpty(repoUrl))
            {
                return false;
            }

            try
            {
                var uri = new Uri(repoUrl);
                var path = uri.AbsolutePath.Trim('/');
                var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    owner = segments[0];
                    repo = segments[1];
                    if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        repo = repo.Substring(0, repo.Length - 4);
                    }
                    return true;
                }
            }
            catch (UriFormatException)
            {
                if (repoUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
                {
                    var separatorIndex = repoUrl.IndexOf(':');
                    if (separatorIndex > 0 && separatorIndex + 1 < repoUrl.Length)
                    {
                        var path = repoUrl.Substring(separatorIndex + 1).Trim('/');
                        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (segments.Length >= 2)
                        {
                            owner = segments[0];
                            repo = segments[1];
                            if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                            {
                                repo = repo.Substring(0, repo.Length - 4);
                            }
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
