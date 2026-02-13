using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private static bool IsGitInitializedAtPath(string packageRoot)
        {
            if (string.IsNullOrEmpty(packageRoot))
            {
                return false;
            }

            return Directory.Exists(Path.Combine(packageRoot, ".git"));
        }

        private bool IsGitInitialized(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _gitInitializedCache.TryGetValue(package.id, out var initialized) && initialized;
        }

        private bool IsGitInitialized(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return IsGitInitialized(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return snapshot.GitInitializedCache != null
                && snapshot.GitInitializedCache.TryGetValue(package.id, out var initialized)
                && initialized;
        }

        private string GetGitHeadCommit(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return _gitHeadCache.TryGetValue(package.id, out var commit) ? commit : null;
        }

        private string GetGitHeadCommit(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return GetGitHeadCommit(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return snapshot.GitHeadCache != null && snapshot.GitHeadCache.TryGetValue(package.id, out var commit)
                ? commit
                : null;
        }

        private string GetGitHeadMessage(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return _gitHeadMessageCache.TryGetValue(package.id, out var message) ? message : null;
        }

        private string GetGitHeadMessage(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return GetGitHeadMessage(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return snapshot.GitHeadMessageCache != null
                && snapshot.GitHeadMessageCache.TryGetValue(package.id, out var message)
                ? message
                : null;
        }

        private bool IsGitDetached(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _gitDetachedCache.TryGetValue(package.id, out var detached) && detached;
        }

        private bool IsGitDetached(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return IsGitDetached(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return snapshot.GitDetachedCache != null
                && snapshot.GitDetachedCache.TryGetValue(package.id, out var detached)
                && detached;
        }

        private bool HasPendingPush(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _pendingPushCache.TryGetValue(package.id, out var pending) && pending;
        }

        private bool HasPendingPush(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return HasPendingPush(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return snapshot.PendingPushCache != null
                && snapshot.PendingPushCache.TryGetValue(package.id, out var pending)
                && pending;
        }

        private bool HasPendingCommit(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _pendingCommitCache.TryGetValue(package.id, out var pending) && pending;
        }

        private bool HasPendingCommit(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return HasPendingCommit(package);
            }

            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return snapshot.PendingCommitCache != null
                && snapshot.PendingCommitCache.TryGetValue(package.id, out var pending)
                && pending;
        }

        private string[] GetPendingCommitFiles(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return new string[0];
            }

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                return new string[0];
            }

            var token = GetRepositoryAccessToken(GetRepositoryConfigForPackage(package));
            var output = RunGitGetOutput(packageRoot, "status --porcelain", token);
            if (string.IsNullOrEmpty(output))
            {
                return new string[0];
            }

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var files = new List<string>();
            foreach (var line in lines)
            {
                if (line.Length > 3)
                {
                    files.Add(line.Substring(2));
                }
            }

            return files.ToArray();
        }

        private void SetupGitForInstalledPackage(PackageEntry package, string reference, string packageRootOverride = null)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = string.IsNullOrEmpty(packageRootOverride)
            ? GetPackageRoot(package)
            : packageRootOverride;
            if (!Directory.Exists(packageRoot))
            {
                Debug.LogWarning("SetupGitForInstalledPackage: directory not found for " + package.id);
                return;
            }

            var repository = GetRepositoryConfigForPackage(package);
            var repoUrl = GetRepositoryUrl(repository);
            if (string.IsNullOrEmpty(repoUrl))
            {
                Debug.LogWarning("SetupGitForInstalledPackage: repo url missing.");
                return;
            }

            var branchRef = BuildPackageBranchRef(package.id);
            var refToUse = string.IsNullOrEmpty(reference) ? branchRef : reference;
            var token = GetRepositoryAccessToken(repository);
            TrySetupGit(packageRoot, repoUrl, branchRef, token);
            RunGit(packageRoot, "fetch --all --tags", token);

            if (RemoteBranchExists(packageRoot, branchRef, token))
            {
                ForceCheckoutRemoteBranch(packageRoot, branchRef, token);
                return;
            }

            if (IsTagRef(package.id, refToUse))
            {
                RunGit(packageRoot, "checkout -f " + refToUse, token);
                EnsureBranchTipWhenMatching(packageRoot, package.id, refToUse, token);
            }
            else
            {
                RunGit(packageRoot, "checkout -B " + refToUse + " origin/" + refToUse, token);
            }
        }

        private void RemoveGitForInstalledPackage(PackageEntry package, string packageRootOverride = null)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = string.IsNullOrEmpty(packageRootOverride)
            ? GetPackageRoot(package)
            : packageRootOverride;
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                return;
            }

            var gitRoot = Path.Combine(packageRoot, ".git");
            if (!Directory.Exists(gitRoot))
            {
                _statusMessage = "No Git metadata found for " + package.id + ".";
                return;
            }

            if (!TryDeleteDirectory(gitRoot))
            {
                _statusMessage = "Failed to remove Git metadata for " + package.id + ".";
                return;
            }

            _gitInitializedCache.Remove(package.id);
            _gitHeadCache.Remove(package.id);
            _gitHeadMessageCache.Remove(package.id);
            _gitDetachedCache.Remove(package.id);
            _remoteExistsCache.Remove(package.id);
            _remoteUrlCache.Remove(package.id);
            _pendingCommitCache.Remove(package.id);
            _pendingPushCache.Remove(package.id);
            _statusMessage = "Removed Git metadata for " + package.id + ".";
        }

        private void UpdateGitForInstalledPackage(PackageEntry package, string reference)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = GetPackageRoot(package);
            if (!Directory.Exists(packageRoot))
            {
                return;
            }

            var refToUse = string.IsNullOrEmpty(reference) ? BuildPackageBranchRef(package.id) : reference;
            var token = GetRepositoryAccessToken(GetRepositoryConfigForPackage(package));
            RunGit(packageRoot, "fetch --all --tags", token);

            if (IsTagRef(package.id, refToUse))
            {
                RunGit(packageRoot, "checkout -f " + refToUse, token);
                EnsureBranchTipWhenMatching(packageRoot, package.id, refToUse, token);
            }
            else
            {
                RunGit(packageRoot, "checkout -B " + refToUse + " origin/" + refToUse, token);
            }
        }

        private void EnsureBranchTipWhenMatching(string packageRoot, string packageId, string tagRef, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(tagRef))
            {
                return;
            }

            var branchRef = BuildPackageBranchRef(packageId);
            var tagHash = ResolveRefCommitHash(packageRoot, tagRef, token);
            var branchHash = ResolveRefCommitHash(packageRoot, "origin/" + branchRef, token);
            if (string.IsNullOrEmpty(branchHash))
            {
                return;
            }

            if (string.IsNullOrEmpty(tagHash) || string.Equals(tagHash.Trim(), branchHash.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                RunGit(packageRoot, "checkout -B " + branchRef + " origin/" + branchRef, token);
            }
        }

        private static string ResolveRefCommitHash(string packageRoot, string gitRef, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(gitRef))
            {
                return null;
            }

            return RunGitGetOutput(packageRoot, "rev-parse " + QuoteGitArgument(gitRef + "^{commit}"), token);
        }

        private static bool IsTagRef(string packageId, string reference)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(reference))
            {
                return false;
            }

            var prefix = packageId + "-v";
            return reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RemoteBranchExists(string packageRoot, string branchRef, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(branchRef))
            {
                return false;
            }

            var remoteBranchRef = "refs/remotes/origin/" + branchRef;
            return RunGitCapture(packageRoot, "show-ref --verify " + QuoteGitArgument(remoteBranchRef), token, null);
        }

        private static void ForceCheckoutRemoteBranch(string packageRoot, string branchRef, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(branchRef))
            {
                return;
            }

            var stashSnapshotRoot = Path.Combine(Path.GetTempPath(), "tgs-pm-git-init-stash-" + Guid.NewGuid().ToString("N"));
            var localBranch = QuoteGitArgument(branchRef);
            var remoteBranch = QuoteGitArgument("origin/" + branchRef);

            try
            {
                Directory.CreateDirectory(stashSnapshotRoot);
                CopyDirectoryRecursiveWithoutGit(packageRoot, stashSnapshotRoot);

                RunGit(packageRoot, "checkout -B " + localBranch + " " + remoteBranch, token);
                RunGit(packageRoot, "reset --hard " + remoteBranch, token);

                RestoreSnapshotIntoWorkingTree(stashSnapshotRoot, packageRoot);
                if (HasPendingWorkingTreeChanges(packageRoot, token))
                {
                    var stashMessage = QuoteGitArgument("tgs-pm initialize backup " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
                    RunGit(packageRoot, "stash push -a -m " + stashMessage, token);
                }

                RunGit(packageRoot, "reset --hard " + remoteBranch, token);
                RunGit(packageRoot, "clean -fdx", token);
            }
            finally
            {
                if (Directory.Exists(stashSnapshotRoot))
                {
                    TryDeleteDirectory(stashSnapshotRoot);
                }
            }
        }

        private static bool HasPendingWorkingTreeChanges(string packageRoot, string token)
        {
            if (string.IsNullOrEmpty(packageRoot))
            {
                return false;
            }

            var status = RunGitGetOutput(packageRoot, "status --porcelain", token);
            return !string.IsNullOrWhiteSpace(status);
        }

        private static void RestoreSnapshotIntoWorkingTree(string snapshotRoot, string packageRoot)
        {
            if (string.IsNullOrEmpty(snapshotRoot) || string.IsNullOrEmpty(packageRoot) || !Directory.Exists(snapshotRoot))
            {
                return;
            }

            CopyDirectoryRecursiveWithoutGit(snapshotRoot, packageRoot);
        }

        private static void CopyDirectoryRecursiveWithoutGit(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath) || !Directory.Exists(sourcePath))
            {
                return;
            }

            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var fileName = Path.GetFileName(file);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                var destinationFile = Path.Combine(destinationPath, fileName);
                File.Copy(file, destinationFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourcePath))
            {
                var dirName = Path.GetFileName(directory);
                if (string.IsNullOrEmpty(dirName) || string.Equals(dirName, ".git", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destinationDir = Path.Combine(destinationPath, dirName);
                CopyDirectoryRecursiveWithoutGit(directory, destinationDir);
            }
        }

        private static void TrySetupGit(string packageRoot, string repoUrl, string branchName, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(repoUrl) || string.IsNullOrEmpty(branchName))
            {
                Debug.LogWarning("TrySetupGit: missing data for git setup.");
                return;
            }

            RunGit(packageRoot, "init", token);
            RunGit(packageRoot, "remote remove origin", token);
            RunGit(packageRoot, "remote add origin " + repoUrl, token);
            RunGit(packageRoot, "checkout --orphan " + branchName, token);
        }

        private static void RunGit(string workingDirectory, string arguments, string token, string redactedArguments = null, bool logErrors = false)
        {
            var rewrittenArguments = RewriteOriginPushArguments(workingDirectory, arguments, token);
            var rewrittenUsesTokenInUrl = !string.Equals(rewrittenArguments, arguments, StringComparison.Ordinal);
            var gitArgs = BuildGitArguments(rewrittenArguments, rewrittenUsesTokenInUrl ? string.Empty : token);
            var startInfo = new System.Diagnostics.ProcessStartInfo("git", gitArgs)
            {
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            var loggedArgs = string.IsNullOrEmpty(redactedArguments) ? arguments : redactedArguments;
                            if (logErrors)
                            Debug.LogError("Git command failed (" + loggedArgs + "): " + error);
                        }
                        else if (!string.IsNullOrEmpty(output))
                        {
                            Debug.Log("Git: " + output.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var loggedArgs = string.IsNullOrEmpty(redactedArguments) ? arguments : redactedArguments;
                if (logErrors)
                Debug.LogError("Git command exception (" + loggedArgs + "): " + ex.Message);
            }
        }

        private static bool RunGitCapture(string workingDirectory, string arguments, string token, string contains)
        {
            var output = RunGitGetOutput(workingDirectory, arguments, token);
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            if (string.IsNullOrEmpty(contains))
            {
                return true;
            }

            return output.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string RunGitGetOutput(string workingDirectory, string arguments, string token)
        {
            var rewrittenArguments = RewriteOriginPushArguments(workingDirectory, arguments, token);
            var rewrittenUsesTokenInUrl = !string.Equals(rewrittenArguments, arguments, StringComparison.Ordinal);
            var gitArgs = BuildGitArguments(rewrittenArguments, rewrittenUsesTokenInUrl ? string.Empty : token);
            var startInfo = new System.Diagnostics.ProcessStartInfo("git", gitArgs)
            {
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning("Git command failed (" + arguments + "): " + error);
                        return null;
                    }

                    return output.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Git command exception (" + arguments + "): " + ex.Message);
                return null;
            }
        }

        private static string BuildGitArguments(string arguments, string token)
        {
            const string transportOptions = "-c http.version=HTTP/1.1 -c http.acceptEncoding=identity";
            var normalized = NormalizeToken(token);
            if (string.IsNullOrEmpty(normalized))
            {
                return transportOptions + " " + arguments;
            }

            var raw = "x-access-token:" + normalized;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            return transportOptions + " -c http.extraHeader=\"Authorization: Basic " + encoded + "\" " + arguments;
        }

        private static string BuildAuthenticatedRepoUrl(string repoUrl, string token)
        {
            var normalizedToken = NormalizeToken(token);
            if (string.IsNullOrEmpty(normalizedToken) || string.IsNullOrWhiteSpace(repoUrl))
            {
                return repoUrl;
            }

            if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri))
            {
                return repoUrl;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return repoUrl;
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return repoUrl;
            }

            var userName = uri.Host.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0
                ? "x-access-token"
                : "oauth2";

            var builder = new UriBuilder(uri)
            {
                UserName = userName,
                Password = normalizedToken
            };

            return builder.Uri.AbsoluteUri;
        }

        private static string RewriteOriginPushArguments(string workingDirectory, string arguments, string token)
        {
            var normalizedToken = NormalizeToken(token);
            if (string.IsNullOrEmpty(normalizedToken) || string.IsNullOrWhiteSpace(arguments))
            {
                return arguments;
            }

            var trimmed = arguments.TrimStart();
            if (!trimmed.StartsWith("push ", StringComparison.OrdinalIgnoreCase))
            {
                return arguments;
            }

            var originToken = " origin ";
            var originIndex = arguments.IndexOf(originToken, StringComparison.Ordinal);
            if (originIndex < 0)
            {
                return arguments;
            }

            var remoteUrl = RunGitGetOutput(workingDirectory, "remote get-url origin", string.Empty);
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return arguments;
            }

            var authenticatedUrl = BuildAuthenticatedRepoUrl(remoteUrl, normalizedToken);
            if (string.IsNullOrWhiteSpace(authenticatedUrl) ||
                string.Equals(authenticatedUrl, remoteUrl, StringComparison.OrdinalIgnoreCase))
            {
                return arguments;
            }

            var replacement = " " + QuoteGitArgument(authenticatedUrl) + " ";
            return arguments.Substring(0, originIndex) + replacement + arguments.Substring(originIndex + originToken.Length);
        }

        private static string NormalizeRemoteRepoUrl(string remoteUrl)
        {
            if (string.IsNullOrEmpty(remoteUrl))
            {
                return string.Empty;
            }

            var url = remoteUrl.Trim();
            if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            {
                var separatorIndex = url.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var host = url.Substring(4, separatorIndex - 4);
                    var path = url.Substring(separatorIndex + 1);
                    url = "https://" + host + "/" + path;
                }
            }

            if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                {
                    var path = uri.AbsolutePath.TrimStart('/');
                    url = "https://" + uri.Host + "/" + path;
                }
            }

            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - 4);
            }

            return url;
        }

        private static string BuildRemoteBranchUrl(string remoteUrl, string branch)
        {
            if (string.IsNullOrEmpty(remoteUrl) || string.IsNullOrEmpty(branch))
            {
                return remoteUrl;
            }

            var url = NormalizeRemoteRepoUrl(remoteUrl);
            if (string.IsNullOrEmpty(url))
            {
                return remoteUrl;
            }

            return url + "/tree/" + branch;
        }
    }
}
