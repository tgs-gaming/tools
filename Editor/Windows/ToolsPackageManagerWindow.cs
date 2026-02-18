using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.Callbacks;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow : EditorWindow
    {
        private const string PrefsInstallRoot = "CTPM_InstallRoot";
        private const string PrefsSelectedTab = "CTPM_SelectedTab";
        private const string PrefsPackageListTab = "CTPM_PackageListTab";
        private const string PrefsAutoUpdatePrefix = "CTPM_AutoUpdate_";
        private const string PrefsAutoUpdateInterval = "CTPM_AutoUpdateIntervalInSeconds";
        private const string PrefsBusyRecoveryDelay = "CTPM_BusyRecoveryDelayInSeconds";
        private const string PrefsRepositoriesPath = "CTPM_RepositoriesPath";
        private const string PrefsLocalRepositoriesPath = "CTPM_LocalRepositoriesPath";
        private const string PrefsEmbeddedPackagesPath = "CTPM_EmbeddedPackagesPath";
        private const string PrefsRepositoryTokenPrefix = "CTPM_RepoToken_";
        private const string PrefsRunInBackground = "CTPM_RunInBackground";

        private const string UserAgent = "CompanyToolsPackageManager/1.0";
        private const string PackageBranchPrefix = "tool/";
        private const double DefaultAutoUpdateIntervalSeconds = 600.0;
        private const double BusyRecoveryDelaySeconds = 3.0;
        private const double LocalGitProbeIntervalSeconds = 20.0;
        private const double RefreshUnlockDelaySeconds = 2.0;
        private const string DefaultRepositoriesPathRelative = "../../repositories.json";
        private const string DefaultLocalRepositoriesPathRelative = "../../local-repositories.json";
        private const string DefaultEmbeddedPackagesPathRelative = "Assets/TGSPackageManager/embedded_packages";
        private const string DefaultPackagePrefix = "com.tgs";
        private const string PublicRepoWarningMessage = "This repository is PUBLIC. Be careful not to publish company code to public repositories.";
        private static ToolsPackageManagerWindow _backgroundInstance;

        private string _defaultInstallRoot;
        private string _repositoriesPathRelative;
        private string _localRepositoriesPathRelative;
        private string _embeddedPackagesPathRelative;
        private string _statusMessage;
        private string _lastUpmUrl;
        private bool _isBusy;
        private double _busyStartedAt;
        private double _refreshUnlockAt;
        private bool _refreshAfterCompilePending;
        private Vector2 _scroll;
        private Vector2 _repositoriesScroll;
        private int _selectedTab;
        private int _selectedPackageListTab;
        private double _nextAutoUpdateTime;
        private double _autoUpdateIntervalSeconds;
        private double _busyRecoveryDelaySeconds;
        private double _nextLocalGitProbeAt;

        private List<PackageEntry> _packages = new List<PackageEntry>();
        private GitHubContentsClient _client;
        private PackageInstaller _installer;
        private readonly Dictionary<string, int> _selectedVersions = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _packageUnityRequirements = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> _packageCompatibility = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> _installedVersionsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _pendingPushCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _pendingCommitCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _gitInitializedCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _gitHeadCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _gitHeadMessageCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _gitDetachedCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _remoteExistsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _remoteUrlCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _packageRemoteFingerprintCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageEntry> _packageMetadataCache = new Dictionary<string, PackageEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _packageUnityRequirementByRemoteCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _packageCompatibilityByRemoteCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LocalPackageInfo> _localPackagesCache = new List<LocalPackageInfo>();
        private readonly Dictionary<string, bool> _dependencyFoldouts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _repositoryAccessErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _availableInstallSelections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool _manualPackageRefresh;
        private bool _usePackageListSnapshot;
        private bool _lastPackageRefreshSucceeded;
        private bool _runInBackground;
        private PackageListSnapshot _packageListSnapshot;
        private readonly Dictionary<string, List<string>> _repositoryTags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private List<RepositoryConfig> _repositories = new List<RepositoryConfig>();
        private static readonly string[] Tabs = { "Packages", "Repositories", "Settings" };
        private static readonly string[] PackageListTabs = { "Embedded", "Installed", "Available", "Local" };
        private static readonly Color InstalledPackageColor = new Color(0.77f, 0.90f, 0.77f);
        private static readonly Color LocalOnlyPackageColor = new Color(0.92f, 0.86f, 0.56f);
        private static readonly Color RepositoryVisibilityColorPublic = new Color(0.35f, 0.72f, 0.35f);
        private static readonly Color RepositoryVisibilityColorPrivate = new Color(0.72f, 0.35f, 0.35f);

        [MenuItem("TGS/Package Manager", priority = -2000)]
        public static void Open()
        {
            var window = GetWindow<ToolsPackageManagerWindow>("TGS Package Manager");
            window.titleContent = new GUIContent("TGS Package Manager", GetPackageManagerIcon());
        }

        private void OnEnable()
        {
            _client = new GitHubContentsClient(UserAgent);
            _installer = new PackageInstaller(_client);
            titleContent = new GUIContent("TGS Package Manager", GetPackageManagerIcon());
            _autoUpdateIntervalSeconds = EditorPrefs.GetFloat(PrefsAutoUpdateInterval, (float)DefaultAutoUpdateIntervalSeconds);
            _busyRecoveryDelaySeconds = EditorPrefs.GetFloat(PrefsBusyRecoveryDelay, (float)BusyRecoveryDelaySeconds);
            _busyRecoveryDelaySeconds = Math.Max(0f, _busyRecoveryDelaySeconds);
            _nextAutoUpdateTime = EditorApplication.timeSinceStartup + _autoUpdateIntervalSeconds;
            EditorApplication.update += OnEditorUpdate;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            var storedInstallRoot = EditorPrefs.GetString(PrefsInstallRoot, string.Empty);
            var normalizedInstallRoot = string.IsNullOrWhiteSpace(storedInstallRoot)
                ? ToRelativeInstallRoot(GetDefaultInstallRoot())
                : ToRelativeInstallRoot(storedInstallRoot);
            _defaultInstallRoot = normalizedInstallRoot;
            if (!string.Equals(storedInstallRoot, normalizedInstallRoot, StringComparison.Ordinal))
            {
                EditorPrefs.SetString(PrefsInstallRoot, normalizedInstallRoot);
            }
            _repositoriesPathRelative = EditorPrefs.GetString(PrefsRepositoriesPath, DefaultRepositoriesPathRelative);
            _localRepositoriesPathRelative = EditorPrefs.GetString(PrefsLocalRepositoriesPath, DefaultLocalRepositoriesPathRelative);
            _embeddedPackagesPathRelative = EditorPrefs.GetString(PrefsEmbeddedPackagesPath, DefaultEmbeddedPackagesPathRelative);
            _runInBackground = IsRunInBackgroundChecked();
            _selectedTab = EditorPrefs.GetInt(PrefsSelectedTab, 0);
            _selectedPackageListTab = Mathf.Clamp(EditorPrefs.GetInt(PrefsPackageListTab, 2), 0, PackageListTabs.Length - 1);
            LoadRepositories();
            if (CanRunTasksForThisInstance())
            {
                AutoLoadManifest();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
        }

        private void OnEditorUpdate()
        {
            if (!CanRunTasksForThisInstance())
            {
                return;
            }

            if (TryRecoverFromStalledBusyState())
            {
                return;
            }

            TryRunPendingRefresh();
            if (_isBusy)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _nextAutoUpdateTime)
            {
                return;
            }

            _nextAutoUpdateTime = now + _autoUpdateIntervalSeconds;
            if (_repositories == null || _repositories.Count == 0)
            {
                return;
            }
            StartOperation(LoadManifest());
        }

        private void OnCompilationFinished(object _)
        {
            if (!CanRunTasksForThisInstance())
            {
                _refreshAfterCompilePending = false;
                return;
            }

            _refreshAfterCompilePending = true;
            TryRunPendingRefresh();
        }

        [InitializeOnLoadMethod]
        private static void OnEditorLaunched()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorApplication.delayCall += RequestBackgroundDuplicateCleanup;

            if (!IsBackgroundExecutionAllowedByPrefs())
            {
                return;
            }

            EditorApplication.delayCall += RequestBackgroundSynchronize;
        }

        private static void RequestBackgroundDuplicateCleanup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var window = GetOrCreateSyncWindow();
            window?.SynchronizeManagedPackageDuplicatesOnly();
        }

        private static void RequestBackgroundSynchronize()
        {
            if (!IsBackgroundExecutionAllowedByPrefs())
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var window = GetOrCreateSyncWindow();
            window?.SynchronizeAfterCompile();
        }

        private static ToolsPackageManagerWindow GetOrCreateSyncWindow()
        {
            var windows = Resources.FindObjectsOfTypeAll<ToolsPackageManagerWindow>();
            foreach (var window in windows)
            {
                if (window != null)
                {
                    return window;
                }
            }

            if (_backgroundInstance == null)
            {
                _backgroundInstance = CreateInstance<ToolsPackageManagerWindow>();
                _backgroundInstance.hideFlags = HideFlags.HideAndDontSave;
            }

            return _backgroundInstance;
        }

        private void SynchronizeAfterCompile()
        {
            if (!CanRunTasksForThisInstance())
            {
                return;
            }

            if (_isBusy || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SynchronizeManagedPackageDuplicates();

            if (_repositories == null || _repositories.Count == 0)
            {
                return;
            }
            StartOperation(LoadManifest());
        }

        private void SynchronizeManagedPackageDuplicatesOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SynchronizeManagedPackageDuplicates();
        }

        private bool IsRefreshLocked()
        {
            return _isBusy && EditorApplication.timeSinceStartup < _refreshUnlockAt;
        }

        private void MarkRefreshBlocked()
        {
            _refreshUnlockAt = EditorApplication.timeSinceStartup + RefreshUnlockDelaySeconds;
        }

        private void ForceUnlockBusyState(string reason)
        {
            if (!_isBusy)
            {
                return;
            }

            _isBusy = false;
            _busyStartedAt = 0d;
            EditorUtility.ClearProgressBar();
            if (_manualPackageRefresh)
            {
                FinalizeManualPackageRefresh();
            }
            Repaint();
        }

        private bool TryRecoverFromStalledBusyState()
        {
            if (!_isBusy)
            {
                return false;
            }

            var now = EditorApplication.timeSinceStartup;
            if (_busyStartedAt > 0d && now - _busyStartedAt < _busyRecoveryDelaySeconds)
            {
                return false;
            }

            if (EditorCoroutineRunner.HasRunningCoroutines)
            {
                return false;
            }

            Debug.LogWarning("TGS Package Manager: detected stale busy state, forcing refresh.");
            ForceUnlockBusyState("Busy watchdog");

            if (_repositories == null || _repositories.Count == 0)
            {
                _statusMessage = "Recovered from stale busy state.";
                return true;
            }

            BeginManualPackageRefresh();
            StartOperation(LoadManifest());
            return true;
        }

        private void TryRunPendingRefresh()
        {
            if (!CanRunTasksForThisInstance())
            {
                _refreshAfterCompilePending = false;
                return;
            }

            if (!_refreshAfterCompilePending)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (_repositories == null || _repositories.Count == 0)
            {
                _refreshAfterCompilePending = false;
                return;
            }

            if (IsRefreshLocked())
            {
                return;
            }

            if (_isBusy)
            {
                ForceUnlockBusyState("Compilation finished");
            }

            BeginManualPackageRefresh();
            StartOperation(LoadManifest());
            _refreshAfterCompilePending = false;
        }

        private static bool IsRunInBackgroundChecked()
        {
            return PlayerPrefs.GetInt(PrefsRunInBackground, 0) == 1;
        }

        private static bool IsBackgroundExecutionAllowedByPrefs()
        {
            return !IsRunInBackgroundChecked();
        }

        private bool IsBackgroundOnlyInstance()
        {
            return ReferenceEquals(this, _backgroundInstance)
                || (hideFlags & HideFlags.HideAndDontSave) != 0;
        }

        private bool CanRunTasksForThisInstance()
        {
            return !IsBackgroundOnlyInstance() || !_runInBackground;
        }

        private void SetRunInBackground(bool enabled)
        {
            _runInBackground = enabled;
            PlayerPrefs.SetInt(PrefsRunInBackground, enabled ? 1 : 0);
            PlayerPrefs.Save();

            if (enabled)
            {
                _refreshAfterCompilePending = false;
                if (_backgroundInstance != null && !ReferenceEquals(_backgroundInstance, this))
                {
                    DestroyImmediate(_backgroundInstance);
                    _backgroundInstance = null;
                }
            }
        }

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        private static string FormatDependencyLabel(string dependency)
        {
            if (!TryParseDependency(dependency, out var packageId, out var version))
            {
                return dependency;
            }

            if (string.IsNullOrEmpty(version))
            {
                return packageId + " (latest)";
            }

            return packageId + " (v" + version + ")";
        }

        internal static bool TryParseDependency(string dependency, out string packageId, out string version)
        {
            packageId = null;
            version = null;

            if (string.IsNullOrWhiteSpace(dependency))
            {
                return false;
            }

            var trimmed = dependency.Trim();
            var match = Regex.Match(trimmed, @"^(?<id>.+)-v(?<version>\d[\w\.-]*)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                packageId = match.Groups["id"].Value;
                version = match.Groups["version"].Value;
                return !string.IsNullOrEmpty(packageId);
            }

            packageId = trimmed;
            return true;
        }

        private static string[] ParseDependenciesFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var arrayMatch = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\[(?<body>[\\s\\S]*?)\\]",
                RegexOptions.IgnoreCase);
            if (arrayMatch.Success)
            {
                return ParseDependencyArray(arrayMatch.Groups["body"].Value);
            }

            var objectMatch = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{(?<body>[\\s\\S]*?)\\}",
                RegexOptions.IgnoreCase);
            if (objectMatch.Success)
            {
                return ParseDependencyObject(objectMatch.Groups["body"].Value);
            }

            return null;
        }

        private static string[] ParseDependencyArray(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return Array.Empty<string>();
            }

            var matches = Regex.Matches(body, "\"(?<value>(?:\\\\.|[^\"\\\\])*)\"");
            if (matches.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (Match match in matches)
            {
                var value = UnescapeJsonValue(match.Groups["value"].Value);
                if (!string.IsNullOrEmpty(value))
                {
                    list.Add(value);
                }
            }

            return list.Count > 0 ? list.ToArray() : Array.Empty<string>();
        }

        private static string[] ParseDependencyObject(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return Array.Empty<string>();
            }

            var matches = Regex.Matches(body, "\"(?<key>(?:\\\\.|[^\"\\\\])*)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"");
            if (matches.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (Match match in matches)
            {
                var key = UnescapeJsonValue(match.Groups["key"].Value);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var value = UnescapeJsonValue(match.Groups["value"].Value);
                list.Add(string.IsNullOrEmpty(value) ? key : key + "-v" + value);
            }

            return list.Count > 0 ? list.ToArray() : Array.Empty<string>();
        }

        private static string UnescapeJsonValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        

        internal void UpdatePackageDependencies(PackageEntry package, string packageRoot, string[] dependencies)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageJsonPath = Path.Combine(packageRoot, "package.json");
            if (!TryUpdatePackageJsonDependencies(packageJsonPath, dependencies, out var error))
            {
                _statusMessage = error;
                return;
            }

            package.dependencies = dependencies ?? Array.Empty<string>();
            RefreshLocalCache();
            _statusMessage = "Dependencies updated for " + package.id + ".";
        }

        private static bool TryUpdatePackageJsonDependencies(string packageJsonPath, string[] dependencies, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(packageJsonPath) || !File.Exists(packageJsonPath))
            {
                error = "package.json not found.";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(packageJsonPath);
            }
            catch (Exception ex)
            {
                error = "Failed to read package.json: " + ex.Message;
                return false;
            }

            var dependenciesJson = "  \"dependencies\": " + BuildDependenciesJson(dependencies, "  ");
            var regex = new Regex("\"dependencies\"\\s*:\\s*\\[[\\s\\S]*?\\]", RegexOptions.IgnoreCase);
            string updated;
            if (regex.IsMatch(json))
            {
                updated = regex.Replace(json, dependenciesJson, 1);
            }
            else
            {
                var authorRegex = new Regex("\"author\"\\s*:", RegexOptions.IgnoreCase);
                if (authorRegex.IsMatch(json))
                {
                    updated = authorRegex.Replace(json, dependenciesJson + ",\n  \"author\":", 1);
                }
                else
                {
                    var insertIndex = json.LastIndexOf('}');
                    if (insertIndex < 0)
                    {
                        error = "package.json missing closing brace.";
                        return false;
                    }

                    updated = json.Insert(insertIndex, ",\n" + dependenciesJson + "\n");
                }
            }

            try
            {
                File.WriteAllText(packageJsonPath, updated);
            }
            catch (Exception ex)
            {
                error = "Failed to update package.json: " + ex.Message;
                return false;
            }

            return true;
        }

        

        

        

        private bool IsPackageCompatible(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _packageCompatibility.TryGetValue(package.id, out var compatible) ? compatible : true;
        }

        private static bool IsPackageCompatible(PackageEntry package, Dictionary<string, bool> compatibility)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return compatibility != null && compatibility.TryGetValue(package.id, out var compatible)
                ? compatible
                : true;
        }

        private string GetPackageUnityRequirement(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return string.Empty;
            }

            return _packageUnityRequirements.TryGetValue(package.id, out var requirement) ? requirement : string.Empty;
        }

        private static string GetPackageUnityRequirement(PackageEntry package, Dictionary<string, string> requirements)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return string.Empty;
            }

            return requirements != null && requirements.TryGetValue(package.id, out var requirement)
                ? requirement
                : string.Empty;
        }

        private static string GetLatestVersion(PackageEntry package)
        {
            if (package == null || package.versions == null || package.versions.Length == 0)
            {
                return null;
            }

            return package.versions[package.versions.Length - 1].version;
        }

        private static bool IsUpdateAvailable(PackageEntry package, string installedVersion)
        {
            if (package == null || string.IsNullOrEmpty(installedVersion))
            {
                return false;
            }

            var latestVersion = GetLatestVersion(package);
            if (string.IsNullOrEmpty(latestVersion))
            {
                return false;
            }

            return !string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
        }

        

        

        

        


        private static string BuildVersionRef(PackageEntry package, string version)
        {
            if (package == null || string.IsNullOrEmpty(version))
            {
                return string.Empty;
            }

            var name = !string.IsNullOrEmpty(package.id) ? package.id : package.displayName;
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name + "-v" + version;
        }

        

        private static bool IsTagVersion(PackageEntry package, string version, List<string> tags)
        {
            if (package == null || string.IsNullOrEmpty(version) || tags == null || tags.Count == 0)
            {
                return false;
            }

            var expected = package.id + "-v" + version;
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        

        

        

        

        

        private static string NormalizeUpmRepoUrl(string repoUrl)
        {
            if (string.IsNullOrEmpty(repoUrl))
            {
                return string.Empty;
            }

            var trimmed = repoUrl.Trim();
            if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".git";
            }

            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }

                return trimmed.TrimEnd('/') + ".git";
            }

            return trimmed;
        }

        private UnityEditor.PackageManager.PackageInfo GetUpmPackageInfo(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + package.id);
        }

        

        private static bool TryOpenUpmWindow(string packageId)
        {
            var windowType = Type.GetType("UnityEditor.PackageManager.UI.Window,UnityEditor");
            if (windowType == null)
            {
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(packageId))
                {
                    var openWithPackage = windowType.GetMethod("Open", new[] { typeof(string) });
                    if (openWithPackage != null)
                    {
                        openWithPackage.Invoke(null, new object[] { packageId });
                        return true;
                    }
                }

                var openDefault = windowType.GetMethod("Open", Type.EmptyTypes);
                if (openDefault != null)
                {
                    openDefault.Invoke(null, null);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        

        

        

        

        

        

        

        

        

        

        

        private static bool TryUpdatePackageJsonVersion(string packageJsonPath, string version, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(packageJsonPath) || !File.Exists(packageJsonPath))
            {
                error = "package.json not found.";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(packageJsonPath);
            }
            catch (Exception ex)
            {
                error = "Failed to read package.json: " + ex.Message;
                return false;
            }

            var regex = new Regex("\"version\"\\s*:\\s*\"[^\"]*\"", RegexOptions.IgnoreCase);
            if (!regex.IsMatch(json))
            {
                error = "package.json missing version.";
                return false;
            }

            var replacement = "\"version\": \"" + EscapeJsonValue(version) + "\"";
            var updated = regex.Replace(json, replacement, 1);
            try
            {
                File.WriteAllText(packageJsonPath, updated);
            }
            catch (Exception ex)
            {
                error = "Failed to update package.json: " + ex.Message;
                return false;
            }

            return true;
        }

        private static bool TryUpdateChangelog(string changelogPath, string version, string releaseNotes, out string error)
        {
            error = null;
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var notes = FormatReleaseNotes(releaseNotes);
            if (string.IsNullOrEmpty(notes))
            {
                error = "Release notes are required.";
                return false;
            }

            string existing = string.Empty;
            if (File.Exists(changelogPath))
            {
                try
                {
                    existing = File.ReadAllText(changelogPath);
                }
                catch (Exception ex)
                {
                    error = "Failed to read CHANGELOG.md: " + ex.Message;
                    return false;
                }
            }

            var header = "# Changelog";
            var newline = Environment.NewLine;
            var body = existing ?? string.Empty;
            if (body.StartsWith(header, StringComparison.OrdinalIgnoreCase))
            {
                var firstLineEnd = body.IndexOf('\n');
                body = firstLineEnd >= 0 ? body.Substring(firstLineEnd + 1) : string.Empty;
            }
            body = body.TrimStart('\r', '\n');

            var entry = "## " + version + " - " + date + newline + notes + newline + newline;
            var final = header + newline + newline + entry + body;

            try
            {
                File.WriteAllText(changelogPath, final);
            }
            catch (Exception ex)
            {
                error = "Failed to update CHANGELOG.md: " + ex.Message;
                return false;
            }

            return true;
        }

        private static string FormatReleaseNotes(string releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
            {
                return string.Empty;
            }

            var lines = releaseNotes.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    builder.Append(trimmed);
                }
                else
                {
                    builder.Append("- ").Append(trimmed);
                }
                builder.Append(Environment.NewLine);
            }

            return builder.ToString();
        }

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        private static string EscapeGitMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return message.Replace("\"", "\\\"");
        }

        

        

        

        

        

        

        

        

        

        

        

        private static bool TryDeleteDirectory(string path)
        {
            const int maxAttempts = 3;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    ClearReadOnlyAttributes(path);
                    Directory.Delete(path, true);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(150);
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(150);
                }
            }

            return false;
        }

        private static void CopyDirectoryRecursive(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
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
                if (string.IsNullOrEmpty(dirName))
                {
                    continue;
                }

                var destinationDir = Path.Combine(destinationPath, dirName);
                CopyDirectoryRecursive(directory, destinationDir);
            }
        }

        private static void ClearReadOnlyAttributes(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                    // Ignore attribute failures and keep deleting.
                }
            }

            foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(dir, FileAttributes.Normal);
                }
                catch
                {
                    // Ignore attribute failures and keep deleting.
                }
            }
        }

        

        private static string GetLatestVersionFromTags(string packageId, List<string> tags)
        {
            var versions = ResolveVersionsFromTags(packageId, tags);
            if (versions.Length == 0)
            {
                return null;
            }

            return versions[versions.Length - 1];
        }

        private static bool IsLatestInstalled(PackageEntry package, string installedVersion)
        {
            if (package == null || package.versions == null || package.versions.Length == 0)
            {
                return false;
            }

            if (string.IsNullOrEmpty(installedVersion))
            {
                return false;
            }

            var latestVersion = GetLatestVersion(package);
            return string.Equals(installedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
        }

        

        

        

        

        

        

        

        

        

        private static Texture GetPackageManagerIcon()
        {
            var iconNames = new[]
            {
                "d_Package Manager",
                "Package Manager",
                "d_UnityEditor.PackageManager.UI.PackageManagerWindow",
                "UnityEditor.PackageManager.UI.PackageManagerWindow"
            };

            foreach (var iconName in iconNames)
            {
                var content = EditorGUIUtility.IconContent(iconName);
                if (content != null && content.image != null)
                {
                    return content.image;
                }
            }

            return null;
        }

        

        

        private static bool IsAutoUpdateEnabled(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return false;
            }

            return EditorPrefs.GetBool(PrefsAutoUpdatePrefix + packageId, false);
        }

        private static void SetAutoUpdateEnabled(string packageId, bool isEnabled)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return;
            }

            EditorPrefs.SetBool(PrefsAutoUpdatePrefix + packageId, isEnabled);
        }

        private static void SetConfigError(PackageEntry package, string error)
        {
            if (package == null)
            {
                return;
            }

            package.loadStatus = PackageLoadStatus.ConfigError;
            package.loadError = error;
        }

        

        private static string BuildPackageBranchRef(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return string.Empty;
            }

            return PackageBranchPrefix + packageId;
        }

        private static string BuildPackageId(string packageName, string packagePrefix)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return string.Empty;
            }

            var trimmed = packageName.Trim().ToLowerInvariant();
            var builder = new System.Text.StringBuilder();
            var lastWasDash = false;
            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    lastWasDash = false;
                }
                else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
                {
                    if (!lastWasDash && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasDash = true;
                    }
                }
            }

            var slug = builder.ToString().Trim('-');
            if (string.IsNullOrEmpty(slug))
            {
                return string.Empty;
            }

            var prefix = NormalizePackagePrefix(packagePrefix);
            return prefix + "." + slug;
        }

        private static void WritePackageFiles(string packageRoot, string packageId, string name, string author,
            string description, string version, string unityVersion, bool required, string[] dependencies,
            string repositoryUrl)
        {
            var readmePath = Path.Combine(packageRoot, "README.md");
            var changelogPath = Path.Combine(packageRoot, "CHANGELOG.md");
            var packageJsonPath = Path.Combine(packageRoot, "package.json");
            var gitignorePath = Path.Combine(packageRoot, ".gitignore");
            var licensePath = Path.Combine(packageRoot, "License.txt");
            var editorAsmdefPath = Path.Combine(packageRoot, "Editor", packageId + ".editor.asmdef");
            var runtimeAsmdefPath = Path.Combine(packageRoot, "Runtime", packageId + ".asmdef");

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");

            File.WriteAllText(readmePath, "# " + name + Environment.NewLine + Environment.NewLine + "## Usage" +
                Environment.NewLine);
            File.WriteAllText(changelogPath, "# Changelog" + Environment.NewLine + Environment.NewLine + "## " +
                version + " - " + date + Environment.NewLine + "- Initial release." + Environment.NewLine);

            var safeDescription = description ?? string.Empty;
            var safeUnityVersion = string.IsNullOrEmpty(unityVersion)
                ? GetDefaultUnityVersion()
                : unityVersion;
            var repositoryJson = string.Empty;
            if (!string.IsNullOrEmpty(repositoryUrl))
            {
                repositoryJson = "  \"repository\": {\n" +
                                 "    \"type\": \"git\",\n" +
                                 "    \"url\": \"" + EscapeJsonValue(repositoryUrl) + "\"\n" +
                                 "  },\n";
            }

            var json = "{\n" +
                       "  \"name\": \"" + packageId + "\",\n" +
                       "  \"version\": \"" + version + "\",\n" +
                       "  \"displayName\": \"" + name + "\",\n" +
                       "  \"description\": \"" + EscapeJsonValue(safeDescription) + "\",\n" +
                        "  \"unity\": \"" + EscapeJsonValue(safeUnityVersion) + "\",\n" +
                        "  \"required\": " + (required ? "true" : "false") + ",\n" +
                        "  \"dependencies\": " + BuildDependenciesJson(dependencies, "  ") + ",\n" +
                        repositoryJson +
                       "  \"author\": {\n" +
                       "    \"name\": \"" + EscapeJsonValue(author) + "\"\n" +
                       "  }\n" +
                       "}\n";
            File.WriteAllText(packageJsonPath, json);

            var gitignore = "Library/\nTemp/\nLogs/\nObj/\nBuild/\nBuilds/\nUserSettings/\n";
            File.WriteAllText(gitignorePath, gitignore);

            CopyPackageLicense(licensePath);

            var runtimeNamespace = BuildRootNamespace(packageId, false);
            var runtimeAsmdef = "{\n" +
                                "  \"name\": \"" + packageId + "\",\n" +
                                "  \"rootNamespace\": \"" + runtimeNamespace + "\"\n" +
                                "}\n";
            File.WriteAllText(runtimeAsmdefPath, runtimeAsmdef);

            var editorNamespace = BuildRootNamespace(packageId, true);
            var editorAsmdef = "{\n" +
                               "  \"name\": \"" + packageId + ".editor\",\n" +
                               "  \"rootNamespace\": \"" + editorNamespace + "\",\n" +
                               "  \"references\": [\n" +
                               "    \"" + packageId + "\"\n" +
                               "  ],\n" +
                               "  \"includePlatforms\": [\n" +
                               "    \"Editor\"\n" +
                               "  ]\n" +
                               "}\n";
            File.WriteAllText(editorAsmdefPath, editorAsmdef);
        }

        private static void CopyPackageLicense(string licensePath)
        {
            var sourceLicense = Path.Combine(GetPackageRootPath(), "License.txt");
            if (!File.Exists(sourceLicense))
            {
                Debug.LogWarning("CreatePackage: license template not found at " + sourceLicense);
                return;
            }

            File.Copy(sourceLicense, licensePath, true);
        }

        private static string GetPackageRootPath()
        {
            var scriptPath = Path.GetFullPath(new System.Diagnostics.StackTrace(true).GetFrame(0)?.GetFileName() ?? string.Empty);
            var startDir = !string.IsNullOrEmpty(scriptPath) ? Path.GetDirectoryName(scriptPath) : null;
            var current = string.IsNullOrEmpty(startDir) ? null : new DirectoryInfo(startDir);
            while (current != null)
            {
                var packageJsonPath = Path.Combine(current.FullName, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "TGSPackageManager", "packages", "com.tgs.package-manager"));
        }

        private static string BuildRootNamespace(string packageId, bool isEditor)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                var prefix = DefaultPackagePrefix;
                return isEditor ? prefix + ".editor" : prefix;
            }

            var parts = packageId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                var token = SanitizeNamespaceToken(parts[i]);
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('.');
                }
                builder.Append(token);
            }

            if (isEditor)
            {
                builder.Append(".editor");
            }

            return builder.ToString();
        }

        private static string SanitizeNamespaceToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var ch in token)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private static string EscapeJsonValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string BuildDependenciesJson(string[] dependencies, string indent)
        {
            if (dependencies == null || dependencies.Length == 0)
            {
                return "[]";
            }

            var list = new List<string>();
            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrEmpty(dependency))
                {
                    continue;
                }

                list.Add(dependency);
            }

            if (list.Count == 0)
            {
                return "[]";
            }

            var builder = new StringBuilder();
            var entryIndent = indent + "  ";
            builder.Append("[\n");
            for (var i = 0; i < list.Count; i++)
            {
                builder.Append(entryIndent)
                    .Append("\"")
                    .Append(EscapeJsonValue(list[i]))
                    .Append("\"");
                if (i < list.Count - 1)
                {
                    builder.Append(",");
                }
                builder.Append("\n");
            }
            builder.Append(indent).Append("]");
            return builder.ToString();
        }

        

        

        

        

        

        private static bool IsUnityCompatible(string requiredUnity)
        {
            if (string.IsNullOrEmpty(requiredUnity))
            {
                return true;
            }

            if (!TryParseUnityVersion(requiredUnity, out var required))
            {
                return true;
            }

            if (!TryParseUnityVersion(Application.unityVersion, out var current))
            {
                return true;
            }

            return current >= required;
        }

        private static bool TryParseUnityVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var buffer = new System.Text.StringBuilder();
            var dotCount = 0;
            foreach (var ch in value)
            {
                if (char.IsDigit(ch))
                {
                    buffer.Append(ch);
                    continue;
                }

                if (ch == '.')
                {
                    if (buffer.Length == 0 || dotCount >= 2)
                    {
                        break;
                    }

                    buffer.Append(ch);
                    dotCount++;
                    continue;
                }

                break;
            }

            var parsed = buffer.ToString().TrimEnd('.');
            return Version.TryParse(parsed, out version);
        }

        private static string[] ResolveVersionsFromTags(string packageId, List<string> tags)
        {
            if (string.IsNullOrEmpty(packageId) || tags == null || tags.Count == 0)
            {
                return new string[0];
            }

            var prefix = packageId + "-v";
            var versions = new List<string>();
            foreach (var tag in tags)
            {
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var versionPart = tag.Substring(prefix.Length);
                if (string.IsNullOrEmpty(versionPart))
                {
                    continue;
                }

                versions.Add(versionPart);
            }

            versions.Sort(CompareVersionStrings);
            return versions.ToArray();
        }

        private static int CompareVersionStrings(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (TryParseUnityVersion(left, out var leftVersion) && TryParseUnityVersion(right, out var rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static PackageVersion[] BuildVersionEntries(string[] versions)
        {
            if (versions == null || versions.Length == 0)
            {
                return new PackageVersion[0];
            }

            var list = new List<PackageVersion>(versions.Length);
            foreach (var version in versions)
            {
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }

                list.Add(new PackageVersion { version = version });
            }

            return list.ToArray();
        }

        private class PackageListItem
        {
            public PackageEntry Package { get; }
            public string InstalledVersion { get; }
            public bool IsInstalled { get; }
            public bool HasUpdate { get; }
            public bool IsLocalOnly { get; }
            public bool IsUpmInstalled { get; }
            public string UpmVersion { get; }

            public PackageListItem(PackageEntry package, string installedVersion, bool isInstalled, bool hasUpdate,
                bool isLocalOnly, bool isUpmInstalled, string upmVersion)
            {
                Package = package;
                InstalledVersion = installedVersion;
                IsInstalled = isInstalled;
                HasUpdate = hasUpdate;
                IsLocalOnly = isLocalOnly;
                IsUpmInstalled = isUpmInstalled;
                UpmVersion = upmVersion;
            }
        }

        private class LocalPackageInfo
        {
            public string Id;
            public string DisplayName;
            public string Description;
            public string Unity;
            public string Version;
            public string RootPath;
            public bool Required;
            public string[] Dependencies;
            public string RepositoryUrl;
        }

        private class PackageListSnapshot
        {
            public List<PackageEntry> Packages;
            public Dictionary<string, string> PackageUnityRequirements;
            public Dictionary<string, bool> PackageCompatibility;
            public List<LocalPackageInfo> LocalPackagesCache;
            public Dictionary<string, string> InstalledVersionsCache;
            public Dictionary<string, bool> PendingPushCache;
            public Dictionary<string, bool> PendingCommitCache;
            public Dictionary<string, bool> GitInitializedCache;
            public Dictionary<string, string> GitHeadCache;
            public Dictionary<string, string> GitHeadMessageCache;
            public Dictionary<string, bool> GitDetachedCache;
            public Dictionary<string, bool> RemoteExistsCache;
            public Dictionary<string, string> RemoteUrlCache;
            public string PackageListSignature;
        }

        

        

        

        

        private static bool IsGitPackAccessDenied(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            return message.IndexOf(".git/objects/pack", StringComparison.OrdinalIgnoreCase) >= 0
                   && message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        

        

        

        

        

        private void StartOperation(IEnumerator routine)
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            _busyStartedAt = EditorApplication.timeSinceStartup;
            EditorCoroutineRunner.StartCoroutine(WrapOperation(routine));
        }

        private void AutoLoadManifest()
        {
            if (_isBusy)
            {
                return;
            }

            if (_repositories == null || _repositories.Count == 0)
            {
                return;
            }

            StartOperation(LoadManifest());
        }

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        

        private IEnumerator WrapOperation(IEnumerator routine)
        {
            if (routine != null)
            {
                var stack = new Stack<IEnumerator>();
                stack.Push(routine);

                while (stack.Count > 0)
                {
                    var currentRoutine = stack.Peek();
                    bool movedNext;
                    try
                    {
                        movedNext = currentRoutine.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        _statusMessage = "Operation failed: " + ex.Message;
                        break;
                    }

                    if (!movedNext)
                    {
                        stack.Pop();
                        continue;
                    }

                    var current = currentRoutine.Current;
                    if (current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }

                    yield return current;
                }
            }

            if (_manualPackageRefresh)
            {
                FinalizeManualPackageRefresh();
            }

            _isBusy = false;
            _busyStartedAt = 0d;
            EditorUtility.ClearProgressBar();
            Repaint();
        }

        private static string GetDefaultInstallRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Assets", "TGSPackageManager", "packages");
        }

        internal static string GetDefaultUnityVersion()
        {
            if (TryParseUnityVersion(Application.unityVersion, out var version))
            {
                return version.Major + "." + version.Minor;
            }

            return Application.unityVersion;
        }

        

        

        

        

        [Serializable]
        private class PackageJsonInfo
        {
            public string name;
            public string version;
            public string displayName;
            public string description;
            public string pathInRepo;
            public string unity;
            public bool required;
            public string[] dependencies;
            public PackageJsonAuthor author;
            public PackageJsonRepository repository;
        }

        [Serializable]
        private class PackageJsonAuthor
        {
            public string name;
        }

        [Serializable]
        private class PackageJsonRepository
        {
            public string type;
            public string url;
        }
    }

    public class CreatePackageData
    {
        public string Name;
        public string Author;
        public string Description;
        public string Version;
        public string UnityVersion;
        public bool Required;
        public string[] Dependencies;
        public string RepositoryId;
    }

}
