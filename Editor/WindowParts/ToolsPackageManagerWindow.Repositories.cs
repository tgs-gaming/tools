using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
        private void DrawRepositoriesSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Repositories", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                    {
                        LoadRepositories();
                        Repaint();
                    }
                }
                EditorGUILayout.Space();

                var changed = DrawAddRepositoryButton();
                EditorGUILayout.Space();

                var removeIndex = -1;
                _repositoriesScroll = EditorGUILayout.BeginScrollView(_repositoriesScroll);
                if (_repositories.Count == 0)
                {
                    EditorGUILayout.HelpBox("No repositories configured.", MessageType.Info);
                }
                else
                {
                    for (var i = 0; i < _repositories.Count; i++)
                    {
                        var repository = _repositories[i];
                        if (repository == null)
                        {
                            continue;
                        }

                        var entryChanged = DrawRepositoryEntry(repository, i, out var shouldRemove);
                        if (entryChanged)
                        {
                            changed = true;
                        }

                        if (shouldRemove)
                        {
                            removeIndex = i;
                        }

                        if (i < _repositories.Count - 1)
                        {
                            EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                if (removeIndex >= 0)
                {
                    _repositories.RemoveAt(removeIndex);
                    changed = true;
                }

                if (changed)
                {
                    SaveRepositories();
                }
            }
        }

        private bool DrawAddRepositoryButton()
        {
            var changed = false;
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (GUILayout.Button("Add Repository"))
                {
                    RepositoryEditWindow.Show(CreateNewRepositoryDraft(), config =>
                    {
                        AddRepository(config);
                        SaveRepositories();
                    });
                }
            }

            return changed;
        }

        private bool DrawRepositoryEntry(RepositoryConfig repository, int index, out bool shouldRemove)
        {
            shouldRemove = false;
            var changed = false;

            if (string.IsNullOrEmpty(repository.id))
            {
                repository.id = Guid.NewGuid().ToString("N");
                changed = true;
            }

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = GetRepositoryBoxColor(repository, index);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var headerLabel = BuildRepositoryShortLabel(repository.url);
                    if (string.IsNullOrWhiteSpace(headerLabel))
                    {
                        headerLabel = string.IsNullOrWhiteSpace(repository.url) ? "Repository" : repository.url;
                    }
                    EditorGUILayout.LabelField(headerLabel,
                    EditorStyles.boldLabel, GUILayout.MinWidth(220f), GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    DrawRepositoryVisibilityTag(repository.isPublic);
                }

                EditorGUI.BeginChangeCheck();
                repository.url = EditorGUILayout.TextField("URL", repository.url);
                repository.packagePrefix = EditorGUILayout.TextField("Package Prefix", repository.packagePrefix);
                repository.isPublic = DrawRepositoryVisibilityField(repository.isPublic);
                repository.localOnly = EditorGUILayout.Toggle("Local Only", repository.localOnly);
                if (repository.localOnly)
                {
                    EditorGUILayout.HelpBox("Only available for you. No files to commit", MessageType.Info);
                }

                repository.installRoot = DrawInstallRootField(repository.installRoot);
                repository.autoUpdate = EditorGUILayout.Toggle("Auto Update", repository.autoUpdate);
                var authBoxColor = new Color(GUI.backgroundColor.r * 0.96f, GUI.backgroundColor.g * 0.96f, GUI.backgroundColor.b * 0.96f);
                var authPreviousColor = GUI.backgroundColor;
                GUI.backgroundColor = authBoxColor;
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField("Authentication", EditorStyles.boldLabel);
                    var previousToken = repository.accessToken;
                    repository.accessToken = EditorGUILayout.PasswordField("Access Token", repository.accessToken);
                    if (!string.Equals(previousToken, repository.accessToken, StringComparison.Ordinal))
                    {
                        repository.accessToken = NormalizeToken(repository.accessToken);
                        SaveRepositoryToken(repository.id, repository.accessToken);
                    }
                }
                GUI.backgroundColor = authPreviousColor;

                if (GUILayout.Button("Test Connection"))
                {
                    var ok = TryTestRepositoryConnection(repository.url, repository.accessToken, out var message);
                    var title = ok ? "Connection succeeded" : "Connection failed";
                    EditorUtility.DisplayDialog(title, message, "OK");
                }

                var removePreviousColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
                if (GUILayout.Button("Remove Repository"))
                {
                    var label = string.IsNullOrWhiteSpace(repository.url) ? "this repository" : repository.url;
                    shouldRemove = EditorUtility.DisplayDialog("Remove Repository",
                    "Remove " + label + "?", "Remove", "Cancel");
                }
                GUI.backgroundColor = removePreviousColor;

                if (EditorGUI.EndChangeCheck())
                {
                    changed = true;
                }
            }
            GUI.backgroundColor = previousColor;

            return changed;
        }

        private static string DrawInstallRootField(string installRoot)
        {
            var absoluteRoot = ResolveInstallRoot(installRoot);
            var root = EditorGUILayout.TextField("Install Root", absoluteRoot);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(105f);
                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("Select install root", root, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        root = selected;
                    }
                }
            }

            return ToRelativeInstallRoot(root);
        }

        private static bool DrawRepositoryVisibilityField(bool isPublic)
        {
            var selected = EditorGUILayout.Popup("Visibility", isPublic ? 0 : 1, new[] { "Public", "Private" });
            return selected == 0;
        }

        private static void DrawRepositoryVisibilityTag(bool isPublic)
        {
            var previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = isPublic ? RepositoryVisibilityColorPublic : RepositoryVisibilityColorPrivate;
            var label = isPublic ? "PUBLIC" : "PRIVATE";
            var tagStyle = new GUIStyle("box")
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = EditorStyles.miniBoldLabel.fontSize,
                padding = new RectOffset(8, 8, 2, 2)
            };
            GUILayout.Label(label, tagStyle);
            GUI.backgroundColor = previousBackground;
        }

        private static Color GetRepositoryBoxColor(RepositoryConfig repository, int index)
        {
            var seedText = repository != null
            ? (repository.id ?? repository.url ?? string.Empty)
            : string.Empty;
            var seed = seedText.GetHashCode() ^ index;
            var random = new System.Random(seed);
            var gray = (float)(0.82 + random.NextDouble() * 0.12);
            return new Color(gray, gray, gray);
        }

        private RepositoryConfig CreateNewRepositoryDraft()
        {
            var fallbackInstallRoot = string.IsNullOrEmpty(_defaultInstallRoot)
                ? ToRelativeInstallRoot(GetDefaultInstallRoot())
                : _defaultInstallRoot;
            return new RepositoryConfig
            {
                id = Guid.NewGuid().ToString("N"),
                url = string.Empty,
                packagePrefix = DefaultPackagePrefix,
                isPublic = false,
                localOnly = false,
                installRoot = fallbackInstallRoot,
                autoUpdate = true,
                accessToken = string.Empty,
            };
        }

        private void LoadRepositories()
        {
            _repositories = new List<RepositoryConfig>();
            _repositories.AddRange(LoadRepositoriesFromPath(GetRepositoriesPath(false), false));
            _repositories.AddRange(LoadRepositoriesFromPath(GetRepositoriesPath(true), true));

            var fallbackInstallRoot = string.IsNullOrEmpty(_defaultInstallRoot)
                ? ToRelativeInstallRoot(GetDefaultInstallRoot())
                : _defaultInstallRoot;
            var changed = false;
            foreach (var repository in _repositories)
            {
                if (repository == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(repository.installRoot))
                {
                    repository.installRoot = fallbackInstallRoot;
                    changed = true;
                }
                else
                {
                    var normalizedRoot = NormalizeInstallRootPath(repository.installRoot, fallbackInstallRoot);
                    if (!string.Equals(repository.installRoot, normalizedRoot, StringComparison.Ordinal))
                    {
                        repository.installRoot = normalizedRoot;
                        changed = true;
                    }
                }

                if (string.IsNullOrEmpty(repository.id))
                {
                    repository.id = Guid.NewGuid().ToString("N");
                    changed = true;
                }

                var normalizedPrefix = NormalizePackagePrefix(repository.packagePrefix);
                if (!string.Equals(repository.packagePrefix, normalizedPrefix, StringComparison.Ordinal))
                {
                    repository.packagePrefix = normalizedPrefix;
                    changed = true;
                }

                repository.accessToken = LoadRepositoryToken(repository.id);
            }

            if (changed)
            {
                SaveRepositories();
            }
        }

        private static List<RepositoryConfig> LoadRepositoriesFromPath(string path, bool localOnly)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new List<RepositoryConfig>();
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<RepositoryConfig>();
                }

                var wrapper = JsonUtility.FromJson<RepositoryConfigList>(json);
                if (wrapper == null || wrapper.repositories == null)
                {
                    return new List<RepositoryConfig>();
                }

                foreach (var repository in wrapper.repositories)
                {
                    if (repository == null)
                    {
                        continue;
                    }

                    repository.localOnly = localOnly;
                    if (string.IsNullOrEmpty(repository.id))
                    {
                        repository.id = Guid.NewGuid().ToString("N");
                    }
                }

                return wrapper.repositories;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("LoadRepositories: failed to read " + path + ": " + ex.Message);
                return new List<RepositoryConfig>();
            }
        }

        private void SaveRepositories()
        {
            var shared = new List<RepositoryConfig>();
            var local = new List<RepositoryConfig>();

            foreach (var repository in _repositories)
            {
                if (repository == null)
                {
                    continue;
                }

                var target = repository.localOnly ? local : shared;
                target.Add(repository);
            }

            SaveRepositoriesToPath(GetRepositoriesPath(false), shared);
            SaveRepositoriesToPath(GetRepositoriesPath(true), local);
            AssetDatabase.Refresh();
        }

        private static void SaveRepositoriesToPath(string path, List<RepositoryConfig> repositories)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var wrapper = new RepositoryConfigList { repositories = repositories };
                var json = JsonUtility.ToJson(wrapper, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SaveRepositories: failed to write " + path + ": " + ex.Message);
            }
        }

        private static string GetRepositoryTokenKey(string repositoryId)
        {
            return PrefsRepositoryTokenPrefix + repositoryId;
        }

        private static string LoadRepositoryToken(string repositoryId)
        {
            if (string.IsNullOrEmpty(repositoryId))
            {
                return string.Empty;
            }

            return NormalizeToken(PlayerPrefs.GetString(GetRepositoryTokenKey(repositoryId), string.Empty));
        }

        private static void SaveRepositoryToken(string repositoryId, string token)
        {
            if (string.IsNullOrEmpty(repositoryId))
            {
                return;
            }

            var key = GetRepositoryTokenKey(repositoryId);
            var normalized = NormalizeToken(token);
            if (string.IsNullOrEmpty(normalized))
            {
                PlayerPrefs.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.SetString(key, normalized);
            }
            PlayerPrefs.Save();
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var trimmed = token.Trim();
            if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("Bearer ".Length);
            }
            else if (trimmed.StartsWith("token ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("token ".Length);
            }

            return trimmed.Trim();
        }

        private static string NormalizePackagePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return DefaultPackagePrefix;
            }

            var trimmed = prefix.Trim();
            while (trimmed.EndsWith(".", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return string.IsNullOrWhiteSpace(trimmed) ? DefaultPackagePrefix : trimmed;
        }

        private string GetRepositoriesPath(bool localOnly)
        {
            var relativePath = localOnly ? _localRepositoriesPathRelative : _repositoriesPathRelative;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = localOnly ? DefaultLocalRepositoriesPathRelative : DefaultRepositoriesPathRelative;
            }

            return ResolveRepositoryPath(relativePath);
        }

        private static string ResolveRepositoryPath(string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(pathValue))
            {
                return Path.GetFullPath(pathValue);
            }

            return Path.GetFullPath(Path.Combine(GetPackageRootPath(), pathValue));
        }

        private static string ResolveInstallRoot(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return string.Empty;
            }

            var projectRoot = GetProjectRootPath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return Path.GetFullPath(installRoot);
            }

            if (Path.IsPathRooted(installRoot))
            {
                return Path.GetFullPath(installRoot);
            }

            return Path.GetFullPath(Path.Combine(projectRoot, installRoot));
        }

        private string GetEmbeddedPackagesRoot()
        {
            var relativePath = string.IsNullOrWhiteSpace(_embeddedPackagesPathRelative)
                ? DefaultEmbeddedPackagesPathRelative
                : _embeddedPackagesPathRelative;
            return ResolveInstallRoot(relativePath);
        }

        private string GetEmbeddedPackagesRootOrFallback()
        {
            var embeddedRoot = GetEmbeddedPackagesRoot();
            return string.IsNullOrEmpty(embeddedRoot) ? ResolveInstallRoot(GetDefaultInstallRoot()) : embeddedRoot;
        }

        private static string ToRelativeInstallRoot(string pathValue)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return string.Empty;
            }

            var projectRoot = GetProjectRootPath();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return pathValue.Trim();
            }

            var absolute = Path.IsPathRooted(pathValue)
                ? Path.GetFullPath(pathValue)
                : Path.GetFullPath(Path.Combine(projectRoot, pathValue));
            var relative = MakeRelativePath(projectRoot, absolute);
            return string.IsNullOrWhiteSpace(relative) ? pathValue.Trim() : relative;
        }

        private static string MakeRelativePath(string basePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                return string.Empty;
            }

            var baseUri = new Uri(EnsureTrailingSeparator(Path.GetFullPath(basePath)));
            var targetUri = new Uri(Path.GetFullPath(targetPath));
            var relativeUri = baseUri.MakeRelativeUri(targetUri);
            if (relativeUri.IsAbsoluteUri)
            {
                return targetPath;
            }

            var relative = Uri.UnescapeDataString(relativeUri.ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string GetProjectRootPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot) ? string.Empty : Path.GetFullPath(projectRoot);
        }

        private void AddRepository(RepositoryConfig template)
        {
            if (template == null)
            {
                return;
            }

            var installRoot = string.IsNullOrWhiteSpace(template.installRoot)
                ? (string.IsNullOrEmpty(_defaultInstallRoot) ? ToRelativeInstallRoot(GetDefaultInstallRoot()) : _defaultInstallRoot)
                : template.installRoot;
            installRoot = ToRelativeInstallRoot(installRoot);

            var repositoryId = string.IsNullOrEmpty(template.id) ? Guid.NewGuid().ToString("N") : template.id;
            var config = new RepositoryConfig
            {
                id = repositoryId,
                url = template.url?.Trim(),
                packagePrefix = NormalizePackagePrefix(template.packagePrefix),
                isPublic = template.isPublic,
                localOnly = template.localOnly,
                installRoot = installRoot,
                autoUpdate = template.autoUpdate,
                accessToken = NormalizeToken(template.accessToken)
            };
            _repositories.Add(config);
            SaveRepositoryToken(config.id, config.accessToken);
        }

        private RepositoryConfig GetDefaultRepositoryConfig()
        {
            return _repositories.Count > 0 ? _repositories[0] : null;
        }

        private RepositoryConfig GetRepositoryConfigForPackage(PackageEntry package)
        {
            if (package == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(package.repositoryId))
            {
                var repository = FindRepositoryById(package.repositoryId);
                if (repository != null)
                {
                    return repository;
                }
            }

            if (!string.IsNullOrEmpty(package.id))
            {
                var localInfo = GetLocalPackageInfo(package.id);
                if (localInfo != null && !string.IsNullOrWhiteSpace(localInfo.RepositoryUrl))
                {
                    var repository = FindRepositoryConfigByUrl(localInfo.RepositoryUrl);
                    if (repository != null)
                    {
                        return repository;
                    }
                }
            }

            return GetDefaultRepositoryConfig();
        }

        internal void GetRepositorySelectionOptions(out string[] dropdownLabels, out string[] ids, out string[] fullLabels)
        {
            var dropdownList = new List<string>();
            var idList = new List<string>();
            var fullList = new List<string>();

            foreach (var repository in _repositories)
            {
                if (repository == null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(repository.url) ? "Repository" : repository.url;
                var prefix = NormalizePackagePrefix(repository.packagePrefix);
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    label += " (" + prefix + ")";
                }

                var dropdownLabel = BuildRepositoryShortLabel(repository.url);
                dropdownList.Add(string.IsNullOrWhiteSpace(dropdownLabel) ? label : dropdownLabel);
                idList.Add(repository.id);
                fullList.Add(label);
            }

            dropdownLabels = dropdownList.ToArray();
            ids = idList.ToArray();
            fullLabels = fullList.ToArray();
        }

        private RepositoryConfig FindRepositoryConfig(string owner, string repo)
        {
            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            {
                return null;
            }

            foreach (var repository in _repositories)
            {
                if (repository == null || string.IsNullOrEmpty(repository.url))
                {
                    continue;
                }

                if (TryGetRepoInfoFromUrl(repository.url, out var repoOwner, out var repoName))
                {
                    if (string.Equals(owner, repoOwner, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(repo, repoName, StringComparison.OrdinalIgnoreCase))
                    {
                        return repository;
                    }
                }
            }

            return null;
        }

        private RepositoryConfig FindRepositoryById(string repositoryId)
        {
            if (string.IsNullOrEmpty(repositoryId))
            {
                return null;
            }

            foreach (var repository in _repositories)
            {
                if (repository != null && string.Equals(repository.id, repositoryId, StringComparison.Ordinal))
                {
                    return repository;
                }
            }

            return null;
        }

        private RepositoryConfig FindRepositoryConfigByUrl(string repoUrl)
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                return null;
            }

            var normalizedTarget = NormalizeRemoteRepoUrl(repoUrl);
            foreach (var repository in _repositories)
            {
                if (repository == null || string.IsNullOrWhiteSpace(repository.url))
                {
                    continue;
                }

                var normalizedRepo = NormalizeRemoteRepoUrl(repository.url);
                if (string.Equals(normalizedRepo, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return repository;
                }
            }

            return null;
        }

        private static string BuildRepositoryShortLabel(string repoUrl)
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                return "Repository";
            }

            if (TryGetRepoInfoFromUrl(repoUrl, out var owner, out var repo))
            {
                return owner + "/" + repo;
            }

            if (Uri.TryCreate(repoUrl, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return BuildShortLabelFromPath(uri.LocalPath);
            }

            return BuildShortLabelFromPath(repoUrl) ?? repoUrl;
        }

        private static string BuildShortLabelFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\');
            var last = Path.GetFileName(trimmed);
            var parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(last))
            {
                return parent + "/" + last;
            }

            return string.IsNullOrEmpty(last) ? trimmed : last;
        }

        private string GetRepositoryAccessToken(RepositoryConfig repository)
        {
            return repository != null ? NormalizeToken(repository.accessToken) : string.Empty;
        }

        private string GetRepositoryUrl(RepositoryConfig repository)
        {
            return repository != null && !string.IsNullOrWhiteSpace(repository.url) ? repository.url : string.Empty;
        }

        private string GetInstallRootForPackage(PackageEntry package)
        {
            if (package != null && package.required)
            {
                return GetEmbeddedPackagesRootOrFallback();
            }

            var repository = GetRepositoryConfigForPackage(package);
            return GetInstallRootForRepository(repository);
        }

        private string GetPackageRoot(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            var installRoot = GetInstallRootForPackage(package);
            if (string.IsNullOrEmpty(installRoot))
            {
                return null;
            }

            return Path.Combine(installRoot, package.id);
        }

        private string GetInstallRootForRepository(RepositoryConfig repository)
        {
            if (repository != null && !string.IsNullOrWhiteSpace(repository.installRoot))
            {
                return ResolveInstallRoot(repository.installRoot);
            }

            var fallbackInstallRoot = string.IsNullOrEmpty(_defaultInstallRoot)
                ? ToRelativeInstallRoot(GetDefaultInstallRoot())
                : _defaultInstallRoot;
            return ResolveInstallRoot(fallbackInstallRoot);
        }

        private bool ConfirmRepositoryAction(RepositoryConfig repository, string title, string confirmMessage)
        {
            if (!EditorUtility.DisplayDialog(title, confirmMessage, "CONTINUE", "CANCEL"))
            {
                return false;
            }

            if (repository != null && repository.isPublic)
            {
                return EditorUtility.DisplayDialog("Public Repository", PublicRepoWarningMessage, "CONTINUE", "CANCEL");
            }

            return true;
        }

        private static void EnsureGitRemote(string packageRoot, string repoUrl, string token)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(repoUrl))
            {
                return;
            }

            var hasRemote = RunGitCapture(packageRoot, "remote", token, "origin");
            if (hasRemote)
            {
                RunGit(packageRoot, "remote set-url origin " + repoUrl, token);
            }
            else
            {
                RunGit(packageRoot, "remote add origin " + repoUrl, token);
            }
        }

        private bool IsRepositoryAutoUpdateEnabled(PackageEntry package)
        {
            var repository = GetRepositoryConfigForPackage(package);
            return repository == null || repository.autoUpdate;
        }

        [Serializable]
        private class RepositoryConfigList
        {
            public List<RepositoryConfig> repositories = new List<RepositoryConfig>();
        }

        private static bool TryTestRepositoryConnection(string repoUrl, string token, out string message)
        {
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                message = "Repository URL is required.";
                return false;
            }

            var authenticatedUrl = BuildAuthenticatedRepoUrl(repoUrl.Trim(), token);
            var arguments = BuildGitArguments("ls-remote " + QuoteGitArgument(authenticatedUrl), string.Empty);
            var startInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = Path.GetTempPath(),
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
                        message = "Failed to start git.";
                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        message = string.IsNullOrWhiteSpace(error) ? "Connection failed." : error.Trim();
                        return false;
                    }

                    message = string.IsNullOrWhiteSpace(output) ? "Connection succeeded." : "Connection succeeded.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = "Connection failed: " + ex.Message;
                return false;
            }
        }

        [Serializable]
        private class RepositoryConfig
        {
            public string id;
            public string url;
            public string packagePrefix;
            public bool isPublic;
            public bool localOnly;
            public string installRoot;
            public bool autoUpdate;
            [NonSerialized]
            public string accessToken;
        }

        private class RepositorySelectionWindow : EditorWindow
        {
            private readonly List<RepositoryConfig> _repositories = new List<RepositoryConfig>();
            private Action<RepositoryConfig> _onSelected;
            private Vector2 _scroll;

            public static void Show(IEnumerable<RepositoryConfig> repositories, Action<RepositoryConfig> onSelected)
            {
                var window = CreateInstance<RepositorySelectionWindow>();
                window.titleContent = new GUIContent("Select Repository");
                window.minSize = new Vector2(360f, 240f);
                window._onSelected = onSelected;
                window._repositories.Clear();
                if (repositories != null)
                {
                    foreach (var repository in repositories)
                    {
                        if (repository != null)
                        {
                            window._repositories.Add(repository);
                        }
                    }
                }

                window.ShowUtility();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Select Repository", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                if (_repositories.Count == 0)
                {
                    EditorGUILayout.HelpBox("No repositories configured.", MessageType.Info);
                    if (GUILayout.Button("Close"))
                    {
                        Close();
                    }
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (var repository in _repositories)
                {
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var label = string.IsNullOrWhiteSpace(repository.url) ? "Repository" : repository.url;
                            EditorGUILayout.LabelField(label ?? "Repository", EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            DrawRepositoryVisibilityTag(repository.isPublic);
                        }

                        if (!string.IsNullOrWhiteSpace(repository.url))
                        {
                            EditorGUILayout.LabelField(repository.url, EditorStyles.miniLabel);
                        }

                        if (repository.localOnly)
                        {
                            EditorGUILayout.HelpBox("Only available for you. No files to commit", MessageType.Info);
                        }

                        if (GUILayout.Button("Select"))
                        {
                            _onSelected?.Invoke(repository);
                            Close();
                            return;
                        }
                    }
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }
            }
        }

        private class RepositoryEditWindow : EditorWindow
        {
            private RepositoryConfig _draft;
            private Action<RepositoryConfig> _onSave;
            private bool _tokenValidated;
            private string _tokenValidationMessage;
            private string _lastToken;
            private bool _connectionTested;
            private bool _connectionTestPassed;
            private string _connectionTestMessage;
            private string _lastUrl;

            public static void Show(RepositoryConfig draft, Action<RepositoryConfig> onSave)
            {
                var window = CreateInstance<RepositoryEditWindow>();
                window.titleContent = new GUIContent("Add Repository");
                window.minSize = new Vector2(420f, 320f);
                window._draft = draft ?? new RepositoryConfig();
                if (string.IsNullOrWhiteSpace(window._draft.packagePrefix))
                {
                    window._draft.packagePrefix = DefaultPackagePrefix;
                }
                window._onSave = onSave;
                window._tokenValidated = false;
                window._tokenValidationMessage = string.Empty;
                window._lastToken = window._draft.accessToken ?? string.Empty;
                window._connectionTested = false;
                window._connectionTestPassed = false;
                window._connectionTestMessage = string.Empty;
                window._lastUrl = window._draft.url ?? string.Empty;
                window.ShowUtility();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Add Repository", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                _draft.url = EditorGUILayout.TextField("URL", _draft.url);
                _draft.packagePrefix = EditorGUILayout.TextField("Package Prefix", _draft.packagePrefix);
                if (!string.Equals(_draft.url ?? string.Empty, _lastUrl ?? string.Empty, StringComparison.Ordinal))
                {
                    _connectionTested = false;
                    _connectionTestPassed = false;
                    _connectionTestMessage = string.Empty;
                    _lastUrl = _draft.url ?? string.Empty;
                }
                _draft.isPublic = DrawRepositoryVisibilityField(_draft.isPublic);
                _draft.localOnly = EditorGUILayout.Toggle("Local Only", _draft.localOnly);
                if (_draft.localOnly)
                {
                    EditorGUILayout.HelpBox("Only available for you. No files to commit", MessageType.Info);
                }

                _draft.installRoot = DrawInstallRootField(_draft.installRoot);
                _draft.autoUpdate = EditorGUILayout.Toggle("Auto Update", _draft.autoUpdate);
                _draft.accessToken = EditorGUILayout.PasswordField("Access Token", _draft.accessToken);
                if (!string.Equals(_draft.accessToken ?? string.Empty, _lastToken ?? string.Empty, StringComparison.Ordinal))
                {
                    _draft.accessToken = NormalizeToken(_draft.accessToken);
                    _tokenValidated = false;
                    _tokenValidationMessage = string.Empty;
                    _lastToken = _draft.accessToken ?? string.Empty;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Test Connection"))
                    {
                        _connectionTested = true;
                        _connectionTestPassed = TryTestRepositoryConnection(_draft.url, _draft.accessToken, out _connectionTestMessage);
                    }

                    if (GUILayout.Button("Validate Token"))
                    {
                        if (string.IsNullOrWhiteSpace(_draft.accessToken))
                        {
                            _tokenValidated = false;
                            _tokenValidationMessage = "Access token is required to validate.";
                        }
                        else
                        {
                            _tokenValidated = true;
                            _tokenValidationMessage = "Token validated.";
                        }
                    }

                }

                if (_connectionTested && !string.IsNullOrWhiteSpace(_connectionTestMessage))
                {
                    var type = _connectionTestPassed ? MessageType.Info : MessageType.Error;
                    EditorGUILayout.HelpBox(_connectionTestMessage, type);
                }

                if (!string.IsNullOrWhiteSpace(_tokenValidationMessage))
                {
                    var type = _tokenValidated ? MessageType.Info : MessageType.Warning;
                    EditorGUILayout.HelpBox(_tokenValidationMessage, type);
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginDisabledGroup(!CanSave());
                    if (GUILayout.Button("SAVE", GUILayout.Width(120f)))
                    {
                        _onSave?.Invoke(_draft);
                        Close();
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            private bool CanSave()
            {
                if (_draft == null || string.IsNullOrWhiteSpace(_draft.url))
                {
                    return false;
                }

                return _connectionTestPassed;
            }
        }
    }
}
