using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace com.tgs.packagemanager.editor
{
    public class ToolsPackageManagerWindow : EditorWindow
    {
        private const string PrefsSource = "CTPM_Source";
        private const string PrefsManifestPath = "CTPM_ManifestPath";
        private const string PrefsManifestUrl = "CTPM_ManifestUrl";
        private const string PrefsInstallRoot = "CTPM_InstallRoot";
        private const string PrefsToken = "CTPM_GitHubToken";
        private const string PrefsRepoUrl = "CTPM_RepoUrl";
        private const string PrefsSelectedTab = "CTPM_SelectedTab";
        private const string PrefsAutoUpdatePrefix = "CTPM_AutoUpdate_";
        private const string PrefsAutoUpdateInterval = "CTPM_AutoUpdateIntervalSeconds";

        private const string DefaultRepoUrl = "https://github.com/tgs-gaming/tools";
        private const string DefaultManifestUrl = "https://github.com/tgs-gaming/tools/main/manifest.json";
        private const string TokenHelpUrl = "https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens";
        private const string UserAgent = "CompanyToolsPackageManager/1.0";
        private const string PackageBranchPrefix = "tool/";
        private const double DefaultAutoUpdateIntervalSeconds = 60.0;

        private enum ManifestSource
        {
            LocalFile,
            RemoteUrl
        }

        private ManifestSource _source;
        private string _manifestPath;
        private string _manifestUrl;
        private string _installRoot;
        private string _gitHubToken;
        private string _repoUrl;
        private string _statusMessage;
        private bool _isBusy;
        private Vector2 _scroll;
        private int _selectedTab;
        private double _nextAutoUpdateTime;
        private double _autoUpdateIntervalSeconds;

        private ToolsManifest _manifest;
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
        private readonly Dictionary<string, bool> _remoteExistsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _remoteUrlCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LocalPackageInfo> _localPackagesCache = new List<LocalPackageInfo>();
        private List<string> _repoTags = new List<string>();
        private static readonly string[] Tabs = { "Packages", "Settings" };
        private static readonly Color InstalledPackageColor = new Color(0.77f, 0.90f, 0.77f);
        private static readonly Color LocalOnlyPackageColor = new Color(0.92f, 0.86f, 0.56f);

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
            _nextAutoUpdateTime = EditorApplication.timeSinceStartup + _autoUpdateIntervalSeconds;
            EditorApplication.update += OnEditorUpdate;

            _source = (ManifestSource)EditorPrefs.GetInt(PrefsSource, (int)ManifestSource.RemoteUrl);
            _manifestPath = EditorPrefs.GetString(PrefsManifestPath, GetDefaultManifestPath());
            _manifestUrl = EditorPrefs.GetString(PrefsManifestUrl, DefaultManifestUrl);
            _installRoot = EditorPrefs.GetString(PrefsInstallRoot, GetDefaultInstallRoot());
            _gitHubToken = EditorPrefs.GetString(PrefsToken, string.Empty);
            _repoUrl = EditorPrefs.GetString(PrefsRepoUrl, DefaultRepoUrl);
            _selectedTab = EditorPrefs.GetInt(PrefsSelectedTab, 0);
            AutoLoadManifest();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
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
            StartOperation(LoadManifest());
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("TGS Package Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawTabs();
            EditorGUILayout.Space();

            if (_selectedTab == 0)
            {
                DrawPackageList();
            }
            else
            {
                DrawSettingsSection();
            }
            EditorGUILayout.Space();
            DrawStatus();
        }

        private void DrawSettingsSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawManifestSection();
                EditorGUILayout.Space();
                DrawRepositorySettings();
                EditorGUILayout.Space();
                DrawInstallSettings();
                EditorGUILayout.Space();
                DrawAutoUpdateSettings();
                EditorGUILayout.Space();
                DrawGitHubToken();
            }
        }

        private void DrawTabs()
        {
            var newTab = GUILayout.Toolbar(_selectedTab, Tabs);
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                EditorPrefs.SetInt(PrefsSelectedTab, _selectedTab);
            }
        }

        private void DrawManifestSection()
        {
            EditorGUILayout.LabelField("Manifest", EditorStyles.boldLabel);

            var newSource = (ManifestSource)EditorGUILayout.EnumPopup("Source", _source);
            if (newSource != _source)
            {
                _source = newSource;
                EditorPrefs.SetInt(PrefsSource, (int)_source);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_source == ManifestSource.LocalFile)
                {
                    EditorGUI.BeginChangeCheck();
                    _manifestPath = EditorGUILayout.TextField("Path", _manifestPath);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetString(PrefsManifestPath, _manifestPath);
                        AutoLoadManifest();
                    }
                    if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                    {
                        var selected = EditorUtility.OpenFilePanel("Select manifest.json", Path.GetDirectoryName(_manifestPath), "json");
                        if (!string.IsNullOrEmpty(selected))
                        {
                            _manifestPath = selected;
                            EditorPrefs.SetString(PrefsManifestPath, _manifestPath);
                            AutoLoadManifest();
                        }
                    }

                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    _manifestUrl = EditorGUILayout.TextField("URL", _manifestUrl);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetString(PrefsManifestUrl, _manifestUrl);
                        AutoLoadManifest();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_isBusy);
                if (GUILayout.Button("Load Manifest"))
                {
                    StartOperation(LoadManifest());
                }
                EditorGUI.EndDisabledGroup();

                if (_manifest != null)
                {
                    GUILayout.Label("Loaded", EditorStyles.miniLabel);
                }
            }

            if (_manifest != null && _manifest.repository != null)
            {
                EditorGUILayout.LabelField("Repository", _manifest.repository.owner + "/" + _manifest.repository.name);
                if (!string.IsNullOrEmpty(_manifest.repository.defaultBranch))
                {
                    EditorGUILayout.LabelField("Default Branch", _manifest.repository.defaultBranch);
                }
            }
        }

        private void DrawRepositorySettings()
        {
            EditorGUILayout.LabelField("Repository", EditorStyles.boldLabel);

            _repoUrl = EditorGUILayout.TextField("GitHub Repo URL", _repoUrl);
            EditorPrefs.SetString(PrefsRepoUrl, _repoUrl);

            if (TryGetRepoInfoFromUrl(_repoUrl, out var owner, out var repo))
            {
                EditorGUILayout.LabelField("Parsed", owner + "/" + repo);
            }
            else
            {
                EditorGUILayout.HelpBox("Unable to parse repository URL. Expected https://github.com/{owner}/{repo}.", MessageType.Warning);
            }
        }

        private void DrawInstallSettings()
        {
            EditorGUILayout.LabelField("Install Settings", EditorStyles.boldLabel);

            _installRoot = EditorGUILayout.TextField("Install Root", _installRoot);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(105f);
                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("Select install root", _installRoot, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        _installRoot = selected;
                    }
                }
            }
            EditorPrefs.SetString(PrefsInstallRoot, _installRoot);
        }

        private void DrawAutoUpdateSettings()
        {
            EditorGUILayout.LabelField("Auto Update", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var interval = EditorGUILayout.FloatField("Interval (seconds)", (float)_autoUpdateIntervalSeconds);
            if (EditorGUI.EndChangeCheck())
            {
                _autoUpdateIntervalSeconds = Math.Max(0, interval);
                EditorPrefs.SetFloat(PrefsAutoUpdateInterval, (float)_autoUpdateIntervalSeconds);
                _nextAutoUpdateTime = EditorApplication.timeSinceStartup + _autoUpdateIntervalSeconds;
            }
        }

        private void DrawGitHubToken()
        {
            EditorGUILayout.LabelField("GitHub Token", EditorStyles.boldLabel);

            _gitHubToken = EditorGUILayout.PasswordField("Token (Optional)", _gitHubToken);
            EditorPrefs.SetString(PrefsToken, _gitHubToken);

            EditorGUILayout.HelpBox("Token is optional for public repositories. Use it to avoid rate limits or access private repos.", MessageType.Info);
            if (GUILayout.Button("How to create a GitHub token"))
            {
                Application.OpenURL(TokenHelpUrl);
            }
        }

        private void DrawPackageList()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(_isBusy);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Package"))
                {
                    CreatePackageWindow.Show(this);
                }
                if (HasAnyUpdate())
                {
                    EditorGUI.BeginDisabledGroup(_isBusy);
                    if (GUILayout.Button("Update All"))
                    {
                        StartOperation(UpdateAllPackages());
                    }
                    EditorGUI.EndDisabledGroup();
                }
                if (GUILayout.Button("Refresh"))
                {
                    StartOperation(LoadManifest());
                }
            }
            EditorGUI.EndDisabledGroup();

            if (_manifest == null)
            {
                EditorGUILayout.HelpBox("No manifest loaded.", MessageType.Info);
                return;
            }

            var isLocalRepo = IsLocalRepository();
            var listItems = BuildPackageListItems(_packages);
            if (listItems.Count == 0)
            {
                EditorGUILayout.HelpBox("No packages available.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in listItems)
            {
                var package = item.Package;
                if (package == null)
                {
                    continue;
                }

                var previousColor = GUI.backgroundColor;
                if (item.IsLocalOnly)
                {
                    GUI.backgroundColor = LocalOnlyPackageColor;
                }
                else if (item.IsInstalled)
                {
                    GUI.backgroundColor = InstalledPackageColor;
                }

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    var lineRect = EditorGUILayout.GetControlRect(false, 2f);
                    EditorGUI.DrawRect(new Rect(lineRect.x, lineRect.y, lineRect.width, 1f),
                        new Color(0.35f, 0.35f, 0.35f, 1f));

                    var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = EditorStyles.boldLabel.fontSize + 2
                    };
                    EditorGUILayout.LabelField(package.displayName ?? package.id, titleStyle);
                    if (!string.IsNullOrEmpty(package.author))
                        EditorGUILayout.LabelField("Author: " + package.author, EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(package.id ?? string.Empty, EditorStyles.miniLabel);
                    if (package.required)
                    {
                        EditorGUILayout.LabelField("Required", EditorStyles.miniLabel);
                    }

                    if (!string.IsNullOrEmpty(package.description))
                    {
                        EditorGUILayout.LabelField(package.description, EditorStyles.wordWrappedMiniLabel);
                    }

                    var installedVersion = item.InstalledVersion;
                    EditorGUILayout.LabelField("Installed", string.IsNullOrEmpty(installedVersion) ? "Not installed" : installedVersion);

                    if (item.IsLocalOnly)
                    {
                        EditorGUILayout.HelpBox("Local Only. Publish this package to share it with everyone.", MessageType.Info);
                    }

                    var canInstall = DrawPackageStatus(package);
                    if (item.HasUpdate)
                    {
                        var latestVersion = GetLatestVersion(package);
                        EditorGUILayout.HelpBox("Updated available: " + latestVersion, MessageType.Info);
                    }
                    if (canInstall)
                    {
                        DrawVersionSelection(package);
                    }
                    DrawAutoUpdateToggle(package, item.IsLocalOnly);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (isLocalRepo)
                        {
                            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(installedVersion));
                            if (GUILayout.Button("Publish"))
                            {
                                PublishPackage(package);
                            }
                            EditorGUI.EndDisabledGroup();
                            
                        }
                        else if (!item.IsLocalOnly)
                        {
                            if (HasPendingCommit(package))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = new Color(0.95f, 0.85f, 0.35f);
                                if (GUILayout.Button("Commit"))
                                {
                                    CommitPackageWindow.Show(this, package, GetPendingCommitFiles(package));
                                }
                                GUI.backgroundColor = previousBgColor;
                            }
                            
                            if (HasPendingPush(package))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = Color.red;
                                if (GUILayout.Button("Push Update"))
                                {
                                    PushUpdate(package);
                                }
                                GUI.backgroundColor = previousBgColor;
                            }

                            if (IsGitInitialized(package))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = Color.red;
                                if (GUILayout.Button("Create Version"))
                                {
                                    CreateVersionWindow.Show(this, package);
                                }
                                GUI.backgroundColor = previousBgColor;
                            }
                            
                            var isLatestInstalled = IsLatestInstalled(package, installedVersion);
                            EditorGUI.BeginDisabledGroup(_isBusy || isLatestInstalled || !canInstall);
                            if (GUILayout.Button("Install Latest"))
                            {
                                var reference = ResolveLatestRef(package);
                                var operation = string.IsNullOrEmpty(installedVersion) ? "Installation" : "Update";
                                var targetVersion = GetLatestVersion(package);
                                StartOperation(InstallPackage(package, reference, operation, targetVersion));
                            }
                            EditorGUI.EndDisabledGroup();
                            EditorGUI.BeginDisabledGroup(_isBusy || !canInstall);
                            if (GUILayout.Button("Install Selected Version"))
                            {
                                var selectedVersion = GetSelectedVersion(package);
                                if (selectedVersion != null)
                                {
                                    var reference = BuildVersionRef(package, selectedVersion.version);
                                    var operation = string.IsNullOrEmpty(installedVersion) ? "Installation" : "Update";
                                    StartOperation(InstallPackage(package, reference, operation, selectedVersion.version));
                                }
                            }
                            EditorGUI.BeginDisabledGroup(package.required);
                            if (GUILayout.Button("Uninstall"))
                            {
                                UninstallPackageSafe(package);
                            }
                            EditorGUI.EndDisabledGroup();
                            EditorGUI.EndDisabledGroup();
                        }

                        if (item.IsLocalOnly)
                        {
                            var previousBgColor = GUI.backgroundColor;
                            GUI.backgroundColor = Color.red;
                            if (GUILayout.Button("Publish"))
                            {
                                PublishPackage(package);
                            }
                            GUI.backgroundColor = previousBgColor;
                            
                            EditorGUI.BeginDisabledGroup(package.required);
                            if (GUILayout.Button("Uninstall"))
                            {
                                UninstallPackageSafe(package);
                            }
                            EditorGUI.EndDisabledGroup();
                        }

                        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(installedVersion));
                        if (GUILayout.Button("Open Directory"))
                        {
                            OpenPackageDirectory(package);
                        }
                        EditorGUI.EndDisabledGroup();

                        if (item.IsInstalled && !item.IsLocalOnly && !IsGitInitialized(package))
                        {
                            if (GUILayout.Button("Initialize Git"))
                            {
                                SetupGitForInstalledPackage(package, null);
                                RefreshLocalCache();
                            }
                        }

                        if (RemoteExists(package))
                        {
                            if (GUILayout.Button("Open Remote"))
                            {
                                OpenRemoteRepository(package);
                            }
                        }
                    }
                }

                GUI.backgroundColor = previousColor;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawVersionSelection(PackageEntry package)
        {
            if (package == null || package.loadStatus != PackageLoadStatus.Loaded)
            {
                return;
            }

            if (package.versions == null || package.versions.Length == 0)
            {
                EditorGUILayout.LabelField("Versions", "None");
                return;
            }

            var installedVersion = GetInstalledVersion(package);

            var labels = new string[package.versions.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = package.versions[i].version;
            }

            var selectedIndex = GetSelectedIndex(package.id);
            if (selectedIndex < 0)
            {
                selectedIndex = GetDefaultSelectedIndex(package, installedVersion);
            }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, labels.Length - 1);
            selectedIndex = EditorGUILayout.Popup("Version", selectedIndex, labels);
            _selectedVersions[package.id] = selectedIndex;
        }

        private bool DrawPackageStatus(PackageEntry package)
        {
            if (package == null)
            {
                return false;
            }

            switch (package.loadStatus)
            {
                case PackageLoadStatus.Loaded:
                    return DrawCompatibilityStatus(package);
                case PackageLoadStatus.BranchNotFound:
                    var branchLabel = BuildPackageBranchRef(package.id);
                    EditorGUILayout.HelpBox("Branch Not found: " + branchLabel, MessageType.Warning);
                    return false;
                case PackageLoadStatus.ConfigError:
                    var errorMessage = string.IsNullOrEmpty(package.loadError) ? "Configuration error" : "Configuration error: " + package.loadError;
                    EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
                    return false;
                case PackageLoadStatus.Loading:
                    EditorGUILayout.LabelField("Status", "Loading...");
                    return false;
                case PackageLoadStatus.Pending:
                    EditorGUILayout.LabelField("Status", "Pending...");
                    return false;
                default:
                    return false;
            }
        }

        private bool DrawCompatibilityStatus(PackageEntry package)
        {
            if (package == null)
            {
                return false;
            }

            var isCompatible = IsPackageCompatible(package);
            if (!isCompatible)
            {
                var requirement = GetPackageUnityRequirement(package);
                EditorGUILayout.HelpBox("Incompatible with current Unity (" + Application.unityVersion + "). Requires: " + requirement,
                    MessageType.Warning);
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

        private string GetPackageUnityRequirement(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return string.Empty;
            }

            return _packageUnityRequirements.TryGetValue(package.id, out var requirement) ? requirement : string.Empty;
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

        private bool HasAnyUpdate()
        {
            if (_packages == null || _packages.Count == 0)
            {
                return false;
            }

            foreach (var package in _packages)
            {
                if (package == null || string.IsNullOrEmpty(package.id))
                {
                    continue;
                }

                var installedVersion = GetInstalledVersionCached(package);
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

                var installedVersion = GetInstalledVersionCached(package);
                if (string.IsNullOrEmpty(installedVersion) || !IsUpdateAvailable(package, installedVersion))
                {
                    continue;
                }

                var reference = ResolveLatestRef(package);
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                var targetVersion = GetLatestVersion(package);
                yield return InstallPackage(package, reference, "Update", targetVersion);
            }
        }

        private List<PackageListItem> BuildPackageListItems(List<PackageEntry> packages)
        {
            var items = new List<PackageListItem>();
            if (packages == null)
            {
                packages = new List<PackageEntry>();
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

                var installedVersion = GetInstalledVersionCached(package);
                var isInstalled = !string.IsNullOrEmpty(installedVersion);
                var hasUpdate = isInstalled && IsUpdateAvailable(package, installedVersion);
                items.Add(new PackageListItem(package, installedVersion, isInstalled, hasUpdate, false));
            }

            foreach (var localItem in BuildLocalOnlyPackages(remoteIds))
            {
                items.Add(localItem);
            }

            items.Sort((left, right) =>
            {
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

                if (left.IsInstalled != right.IsInstalled)
                {
                    return right.IsInstalled.CompareTo(left.IsInstalled);
                }

                if (left.IsInstalled && left.HasUpdate != right.HasUpdate)
                {
                    return right.HasUpdate.CompareTo(left.HasUpdate);
                }

                var leftName = left.Package.displayName ?? left.Package.id ?? string.Empty;
                var rightName = right.Package.displayName ?? right.Package.id ?? string.Empty;
                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });

            return items;
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

        private string GetInstalledVersion(PackageEntry package)
        {
            if (package == null)
            {
                return null;
            }

            var packageJsonPath = Path.Combine(_installRoot, package.id, "package.json");
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

        private void OpenPackageDirectory(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (Directory.Exists(packageRoot))
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    System.Diagnostics.Process.Start("explorer.exe", packageRoot);
                }
                else
                {
                    EditorUtility.RevealInFinder(packageRoot);
                }
            }
            else
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
            }
        }

        private List<PackageListItem> BuildLocalOnlyPackages(HashSet<string> remoteIds)
        {
            var items = new List<PackageListItem>();
            if (_localPackagesCache.Count == 0)
            {
                return items;
            }

            foreach (var info in _localPackagesCache)
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
                    versions = BuildVersionEntries(new[] { info.Version }),
                    loadStatus = PackageLoadStatus.Loaded
                };

                if (!string.IsNullOrEmpty(info.Unity))
                {
                    _packageUnityRequirements[info.Id] = info.Unity;
                    _packageCompatibility[info.Id] = IsUnityCompatible(info.Unity);
                }

                var installedVersion = info.Version ?? string.Empty;
                items.Add(new PackageListItem(entry, installedVersion, !string.IsNullOrEmpty(installedVersion), false, true));
            }

            return items;
        }

        private bool IsLocalRepository()
        {
            if (string.IsNullOrEmpty(_repoUrl))
            {
                return false;
            }

            if (Directory.Exists(_repoUrl))
            {
                return true;
            }

            if (Path.IsPathRooted(_repoUrl))
            {
                return Directory.Exists(_repoUrl);
            }

            if (Uri.TryCreate(_repoUrl, UriKind.Absolute, out var uri) && uri.IsFile)
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

            var ok = EditorUtility.DisplayDialog("Publish Package",
                "Are you sure? This package will be available for the entire team.", "CONTINUE", "CANCEL");
            if (!ok)
            {
                return;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("PublishPackage: directory not found for " + package.id);
                return;
            }

            Debug.Log("PublishPackage: publishing " + package.id + " from " + packageRoot);
            TrySetupGit(packageRoot, _repoUrl, BuildPackageBranchRef(package.id), _gitHubToken);
            RunGit(packageRoot, "add -A", _gitHubToken);
            RunGit(packageRoot, "commit -m \"Publish package\" --allow-empty", _gitHubToken, logErrors: true);
            RunGit(packageRoot, "push -u origin " + BuildPackageBranchRef(package.id), _gitHubToken, logErrors: true);
            _statusMessage = "Published " + package.id + ".";
            RefreshLocalCache();
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

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("CommitPackage: directory not found for " + package.id);
                return;
            }

            Debug.Log("CommitPackage: committing " + package.id + " from " + packageRoot);
            RunGit(packageRoot, "add -A", _gitHubToken);
            RunGit(packageRoot, "commit -m \"" + EscapeGitMessage(message) + "\"", _gitHubToken, logErrors: true);
            _statusMessage = "Committed " + package.id + ".";
            RefreshLocalCache();
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

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("CreateVersion: directory not found for " + package.id);
                return;
            }

            var tag = package.id + "-v" + version;
            if (TagExists(packageRoot, tag))
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
            RunGit(packageRoot, "add \"package.json\" \"CHANGELOG.md\"", _gitHubToken);
            RunGit(packageRoot, "commit -m \"" + EscapeGitMessage(commitMessage) + "\"", _gitHubToken, logErrors: true);
            RunGit(packageRoot, "push", _gitHubToken, logErrors: true);

            Debug.Log("CreateVersion: tagging " + tag + " for " + package.id);
            RunGit(packageRoot, "tag " + tag, _gitHubToken, logErrors: true);
            RunGit(packageRoot, "push origin " + tag, _gitHubToken, logErrors: true);
            _statusMessage = "Created tag " + tag + ".";
            RefreshLocalCache();
        }

        private bool TagExists(string packageRoot, string tag)
        {
            if (string.IsNullOrEmpty(packageRoot) || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var localMatch = RunGitGetOutput(packageRoot, "tag -l " + tag, _gitHubToken);
            if (!string.IsNullOrEmpty(localMatch))
            {
                return true;
            }

            var remoteMatch = RunGitGetOutput(packageRoot, "ls-remote --tags origin " + tag, _gitHubToken);
            return !string.IsNullOrEmpty(remoteMatch);
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

        private bool HasPendingPush(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _pendingPushCache.TryGetValue(package.id, out var pending) && pending;
        }

        private bool HasPendingCommit(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _pendingCommitCache.TryGetValue(package.id, out var pending) && pending;
        }

        private bool IsGitInitialized(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return _gitInitializedCache.TryGetValue(package.id, out var initialized) && initialized;
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
            return _remoteUrlCache.TryGetValue(package.id, out var url) ? url : null;
        }

        private void RefreshLocalCache()
        {
            ClearLocalCaches();
            if (string.IsNullOrEmpty(_installRoot) || !Directory.Exists(_installRoot))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(_installRoot))
            {
                var packageJsonPath = Path.Combine(directory, "package.json");
                if (!File.Exists(packageJsonPath))
                {
                    continue;
                }

                PackageJsonInfo info;
                try
                {
                    var json = File.ReadAllText(packageJsonPath);
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
                _installedVersionsCache[id] = version ?? string.Empty;
                _localPackagesCache.Add(new LocalPackageInfo
                {
                    Id = id,
                    DisplayName = info != null ? info.displayName : null,
                    Description = info != null ? info.description : null,
                    Unity = info != null ? info.unity : null,
                    Version = version ?? string.Empty,
                    RootPath = directory,
                    Required = info != null && info.required
                });

                var gitInitialized = Directory.Exists(Path.Combine(directory, ".git"));
                _gitInitializedCache[id] = gitInitialized;

                if (gitInitialized)
                {
                    var pending = RunGitCapture(directory, "status -sb", _gitHubToken, "ahead");
                    _pendingPushCache[id] = pending;

                    var pendingCommit = RunGitCapture(directory, "status --porcelain", _gitHubToken, string.Empty);
                    _pendingCommitCache[id] = pendingCommit;

                    var hasRemote = RunGitCapture(directory, "remote", _gitHubToken, "origin");
                    _remoteExistsCache[id] = hasRemote;
                    if (hasRemote)
                    {
                        var remoteUrl = RunGitGetOutput(directory, "remote get-url origin", _gitHubToken);
                        if (!string.IsNullOrEmpty(remoteUrl))
                        {
                            _remoteUrlCache[id] = remoteUrl;
                        }
                    }
                }
            }
        }

        private void ClearLocalCaches()
        {
            _installedVersionsCache.Clear();
            _pendingPushCache.Clear();
            _pendingCommitCache.Clear();
            _gitInitializedCache.Clear();
            _remoteExistsCache.Clear();
            _remoteUrlCache.Clear();
            _localPackagesCache.Clear();
        }

        private static string BuildRemoteBranchUrl(string remoteUrl, string branch)
        {
            if (string.IsNullOrEmpty(remoteUrl) || string.IsNullOrEmpty(branch))
            {
                return remoteUrl;
            }

            var url = remoteUrl.Trim();
            if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://github.com/" + url.Substring("git@github.com:".Length);
            }

            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Substring(0, url.Length - 4);
            }

            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url + "/tree/" + branch;
            }

            return url;
        }

        private string GetInstalledVersionCached(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return null;
            }

            return _installedVersionsCache.TryGetValue(package.id, out var version) ? version : null;
        }

        private static string EscapeGitMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return message.Replace("\"", "\\\"");
        }

        private string[] GetPendingCommitFiles(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return new string[0];
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                return new string[0];
            }

            var output = RunGitGetOutput(packageRoot, "status --porcelain", _gitHubToken);
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

        private void PushUpdate(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var ok = EditorUtility.DisplayDialog("Push Update",
                "Are you sure? This package will be available for the entire team.", "CONTINUE", "CANCEL");
            if (!ok)
            {
                return;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                Debug.LogWarning("PushUpdate: directory not found for " + package.id);
                return;
            }

            Debug.Log("PushUpdate: pushing " + package.id + " from " + packageRoot);
            // TrySetupGit(packageRoot, _repoUrl, BuildPackageBranchRef(package.id), _gitHubToken);
            RunGit(packageRoot, "push -u origin " + BuildPackageBranchRef(package.id), _gitHubToken);
            _statusMessage = "Pushed " + package.id + ".";
            RefreshLocalCache();
        }

        private void UninstallPackageSafe(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
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

            AssetDatabase.Refresh();
            _statusMessage = "Uninstalled " + package.id + ".";
            RefreshLocalCache();
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

        private void SetupGitForInstalledPackage(PackageEntry package, string reference)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
            if (!Directory.Exists(packageRoot))
            {
                Debug.LogWarning("SetupGitForInstalledPackage: directory not found for " + package.id);
                return;
            }

            if (string.IsNullOrEmpty(_repoUrl))
            {
                Debug.LogWarning("SetupGitForInstalledPackage: repo url missing.");
                return;
            }

            var refToUse = string.IsNullOrEmpty(reference) ? BuildPackageBranchRef(package.id) : reference;
            TrySetupGit(packageRoot, _repoUrl, BuildPackageBranchRef(package.id), _gitHubToken);
            RunGit(packageRoot, "fetch --all --tags", _gitHubToken);

            if (IsTagRef(package.id, refToUse))
            {
                RunGit(packageRoot, "checkout -f " + refToUse, _gitHubToken);
            }
            else
            {
                RunGit(packageRoot, "checkout -B " + refToUse + " origin/" + refToUse, _gitHubToken);
            }
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

        private string ResolveLatestRef(PackageEntry package)
        {
            if (package == null)
            {
                return string.Empty;
            }

            var latestVersion = GetLatestVersionFromTags(package.id, _repoTags);
            if (!string.IsNullOrEmpty(latestVersion))
            {
                return BuildVersionRef(package, latestVersion);
            }

            return BuildPackageBranchRef(package.id);
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

        private IEnumerator LoadManifest()
        {
            _statusMessage = "Loading manifest...";
            _manifest = null;
            _packages = new List<PackageEntry>();
            _packageUnityRequirements.Clear();
            _packageCompatibility.Clear();
            _repoTags = new List<string>();
            ClearLocalCaches();

            if (_source == ManifestSource.LocalFile)
            {
                if (string.IsNullOrEmpty(_manifestPath) || !File.Exists(_manifestPath))
                {
                    _statusMessage = "Manifest path is invalid.";
                    yield break;
                }

                string json;
                try
                {
                    json = File.ReadAllText(_manifestPath);
                }
                catch (Exception ex)
                {
                    _statusMessage = "Failed to read manifest: " + ex.Message;
                    yield break;
                }

                TryParseManifest(json);
            }
            else
            {
                if (string.IsNullOrEmpty(_manifestUrl))
                {
                    _statusMessage = "Manifest URL is empty.";
                    yield break;
                }

                var url = NormalizeManifestUrl(_manifestUrl);
                using (var request = UnityWebRequest.Get(AddCacheBuster(url)))
                {
                    request.SetRequestHeader("User-Agent", UserAgent);
                    request.SetRequestHeader("Cache-Control", "no-cache");
                    request.SetRequestHeader("Pragma", "no-cache");
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        _statusMessage = "Failed to download manifest: " + request.error;
                        yield break;
                    }

                    TryParseManifest(request.downloadHandler.text);
                }
            }

            if (_manifest != null)
            {
                yield return LoadPackageMetadata();
            }
        }

        private void TryParseManifest(string json)
        {
            try
            {
                _manifest = JsonUtility.FromJson<ToolsManifest>(json);
            }
            catch (Exception ex)
            {
                _manifest = null;
                _packages = new List<PackageEntry>();
                _statusMessage = "Manifest parse error: " + ex.Message;
                return;
            }

            if (_manifest == null)
            {
                _packages = new List<PackageEntry>();
                _packageUnityRequirements.Clear();
                _packageCompatibility.Clear();
                _repoTags = new List<string>();
                ClearLocalCaches();
                _statusMessage = "Manifest parse error: empty result.";
                return;
            }

            var errors = _manifest.Validate();
            if (errors.Count > 0)
            {
                _statusMessage = "Manifest validation failed: " + string.Join(" | ", errors);
                _manifest = null;
                _packages = new List<PackageEntry>();
                _packageUnityRequirements.Clear();
                _packageCompatibility.Clear();
                _repoTags = new List<string>();
                ClearLocalCaches();
                return;
            }

            _packages = new List<PackageEntry>();
            _statusMessage = "Manifest loaded.";
        }

        private IEnumerator LoadPackageMetadata()
        {
            if (_manifest == null)
            {
                yield break;
            }

            var repository = ResolveRepository();
            if (repository == null)
            {
                foreach (var package in _packages)
                {
                    SetConfigError(package, "Repository metadata is missing.");
                }

                _statusMessage = "Manifest repository information is missing.";
                yield break;
            }

            yield return LoadRepositoryTags(repository);
            yield return LoadPackageBranches(repository);
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

                yield return LoadPackageMetadata(repository, package);
                Repaint();
            }

            yield return EnsureAutoUpdatedPackagesInstalled();
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

                if (!ShouldAutoUpdate(package))
                {
                    continue;
                }

                var installedVersion = GetInstalledVersionCached(package);
                var needsInstall = string.IsNullOrEmpty(installedVersion) || IsUpdateAvailable(package, installedVersion);
                if (!needsInstall)
                {
                    continue;
                }

                var reference = ResolveLatestRef(package);
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                var operation = string.IsNullOrEmpty(installedVersion) ? "Installation" : "Update";
                var targetVersion = GetLatestVersion(package);
                yield return InstallPackage(package, reference, operation, targetVersion);
            }
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

        private void DrawAutoUpdateToggle(PackageEntry package, bool isLocalOnly)
        {
            if (package == null || string.IsNullOrEmpty(package.id) || isLocalOnly)
            {
                return;
            }

            EditorGUI.BeginChangeCheck();
            var isEnabled = IsAutoUpdateEnabled(package.id);
            var nextValue = EditorGUILayout.ToggleLeft("Auto Update", isEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                SetAutoUpdateEnabled(package.id, nextValue);
            }
        }

        private bool ShouldAutoUpdate(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            return package.required || IsAutoUpdateEnabled(package.id);
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

        private IEnumerator LoadPackageMetadata(RepositoryInfo repository, PackageEntry package)
        {
            var branchRef = BuildPackageBranchRef(package.id);
            GitHubContentItem[] packageItems = null;
            GitHubRequestError packageError = null;

            yield return _client.GetContents(repository.owner, repository.name, "package.json", branchRef, _gitHubToken,
                result => packageItems = result, err => packageError = err);

            if (packageError != null)
            {
                if (packageError.statusCode == 404)
                {
                    yield return ResolveMissingPackageJson(repository, package, branchRef);
                    yield break;
                }

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
            yield return _client.DownloadText(packageItem.download_url, _gitHubToken,
                text => packageJson = text, err => downloadError = err);

            if (downloadError != null)
            {
                SetConfigError(package, downloadError.ToString());
                yield break;
            }

            ApplyPackageJson(package, packageJson);
            ApplyVersionsFromTags(package, _repoTags);
        }

        private IEnumerator ResolveMissingPackageJson(RepositoryInfo repository, PackageEntry package, string branchRef)
        {
            GitHubContentItem[] branchItems = null;
            GitHubRequestError branchError = null;

            yield return _client.GetContents(repository.owner, repository.name, string.Empty, branchRef, _gitHubToken,
                result => branchItems = result, err => branchError = err);

            if (branchError != null && branchError.statusCode == 404)
            {
                package.loadStatus = PackageLoadStatus.BranchNotFound;
                package.loadError = "Branch not found: " + branchRef;
                yield break;
            }

            if (branchError != null)
            {
                SetConfigError(package, branchError.ToString());
                yield break;
            }

            SetConfigError(package, "package.json not found.");
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

        private IEnumerator LoadRepositoryTags(RepositoryInfo repository)
        {
            _repoTags = new List<string>();
            GitHubTag[] tags = null;
            GitHubRequestError tagError = null;

            yield return _client.GetTags(repository.owner, repository.name, _gitHubToken, result => tags = result,
                err => tagError = err);

            if (tagError != null)
            {
                _statusMessage = "Failed to load tags: " + tagError;
                Debug.LogWarning("LoadRepositoryTags: " + tagError);
                yield break;
            }

            if (tags == null || tags.Length == 0)
            {
                yield break;
            }

            foreach (var tag in tags)
            {
                if (tag != null && !string.IsNullOrEmpty(tag.name))
                {
                    _repoTags.Add(tag.name);
                }
            }
        }

        private IEnumerator LoadPackageBranches(RepositoryInfo repository)
        {
            _packages = new List<PackageEntry>();
            GitHubBranch[] branches = null;
            GitHubRequestError branchError = null;

            yield return _client.GetBranches(repository.owner, repository.name, _gitHubToken, result => branches = result,
                err => branchError = err);

            if (branchError != null)
            {
                _statusMessage = "Failed to load branches: " + branchError;
                Debug.LogWarning("LoadPackageBranches: " + branchError);
                yield break;
            }

            if (branches == null || branches.Length == 0)
            {
                yield break;
            }

            foreach (var branch in branches)
            {
                if (branch == null || string.IsNullOrEmpty(branch.name))
                {
                    continue;
                }

                if (!branch.name.StartsWith(PackageBranchPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var packageId = branch.name.Substring(PackageBranchPrefix.Length);
                if (string.IsNullOrEmpty(packageId))
                {
                    continue;
                }

                GitHubContentItem[] packageItems = null;
                GitHubRequestError packageError = null;

                yield return _client.GetContents(repository.owner, repository.name, "package.json", branch.name, _gitHubToken,
                    result => packageItems = result, err => packageError = err);

                if (packageError != null)
                {
                    if (packageError.statusCode == 404)
                    {
                        continue;
                    }

                    _statusMessage = "Failed to read package.json for branch " + branch.name + ": " + packageError;
                    Debug.LogWarning("LoadPackageBranches: package.json error for " + branch.name + ": " + packageError);
                    continue;
                }

                var packageItem = FindPackageJsonItem(packageItems);
                if (packageItem == null)
                {
                    continue;
                }

                _packages.Add(new PackageEntry { id = packageId, loadStatus = PackageLoadStatus.Pending });
            }
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

        private static void SetConfigError(PackageEntry package, string error)
        {
            if (package == null)
            {
                return;
            }

            package.loadStatus = PackageLoadStatus.ConfigError;
            package.loadError = error;
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

        private static string BuildPackageBranchRef(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return string.Empty;
            }

            return PackageBranchPrefix + packageId;
        }

        private static string BuildPackageId(string packageName)
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
            return string.IsNullOrEmpty(slug) ? string.Empty : "com.tgs." + slug;
        }

        private static void WritePackageFiles(string packageRoot, string packageId, string name, string author,
            string description, string version)
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
            var json = "{\n" +
                       "  \"name\": \"" + packageId + "\",\n" +
                       "  \"version\": \"" + version + "\",\n" +
                       "  \"displayName\": \"" + name + "\",\n" +
                       "  \"description\": \"" + EscapeJsonValue(safeDescription) + "\",\n" +
                        "  \"unity\": \"2020.3\",\n" +
                        "  \"required\": false,\n" +
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
            if (!string.IsNullOrEmpty(scriptPath))
            {
                var editorDir = Path.GetDirectoryName(scriptPath);
                if (!string.IsNullOrEmpty(editorDir))
                {
                    var packageRoot = Path.GetFullPath(Path.Combine(editorDir, ".."));
                    return packageRoot;
                }
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "TGSPackageManager", "packages", "com.tgs.package-manager"));
        }

        private static string BuildRootNamespace(string packageId, bool isEditor)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return isEditor ? "com.tgs.editor" : "com.tgs";
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
            var gitArgs = BuildGitArguments(arguments, token);
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
            var gitArgs = BuildGitArguments(arguments, token);
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
            if (string.IsNullOrEmpty(token))
            {
                return arguments;
            }

            var raw = "x-access-token:" + token;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            return "-c http.extraHeader=\"Authorization: Basic " + encoded + "\" " + arguments;
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

            public PackageListItem(PackageEntry package, string installedVersion, bool isInstalled, bool hasUpdate, bool isLocalOnly)
            {
                Package = package;
                InstalledVersion = installedVersion;
                IsInstalled = isInstalled;
                HasUpdate = hasUpdate;
                IsLocalOnly = isLocalOnly;
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
        }

        private IEnumerator InstallPackage(PackageEntry package, string reference, string operation, string targetVersion)
        {
            var repository = ResolveRepository();
            if (repository == null)
            {
                _statusMessage = "Manifest repository information is missing.";
                yield break;
            }

            var hadError = false;
            var errorMessage = string.Empty;
            yield return _installer.InstallPackage(repository, package, reference, _installRoot, _gitHubToken,
                OnInstallProgress, message =>
                {
                    hadError = true;
                    errorMessage = message;
                    _statusMessage = message;
                });

            if (!hadError)
            {
                _statusMessage = "Installed " + package.id + ".";
                RefreshLocalCache();
                yield break;
            }

            if (IsGitPackAccessDenied(errorMessage) && TryCleanReinstall(package))
            {
                hadError = false;
                errorMessage = string.Empty;
                yield return _installer.InstallPackage(repository, package, reference, _installRoot, _gitHubToken,
                    OnInstallProgress, message =>
                    {
                        hadError = true;
                        errorMessage = message;
                        _statusMessage = message;
                    });

                if (!hadError)
                {
                    _statusMessage = "Installed " + package.id + ".";
                    RefreshLocalCache();
                    yield break;
                }
            }

            SetOperationError(operation, package, targetVersion, errorMessage);
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

        private bool TryCleanReinstall(PackageEntry package)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return false;
            }

            var packageRoot = Path.Combine(_installRoot, package.id);
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

            if (string.IsNullOrEmpty(data.Name) || string.IsNullOrEmpty(data.Author))
            {
                _statusMessage = "Name and author are required.";
                Debug.LogWarning("CreatePackage: name/author missing.");
                yield break;
            }

            var packageId = BuildPackageId(data.Name);
            if (string.IsNullOrEmpty(packageId))
            {
                _statusMessage = "Invalid package name.";
                Debug.LogWarning("CreatePackage: invalid package name.");
                yield break;
            }

            var version = string.IsNullOrEmpty(data.Version) ? "1.0.0" : data.Version;
            var packageRoot = Path.Combine(_installRoot, packageId);
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
                WritePackageFiles(packageRoot, packageId, data.Name, data.Author, data.Description, version);
            }
            catch (Exception ex)
            {
                _statusMessage = "Failed to write package files: " + ex.Message;
                Debug.LogError("CreatePackage: failed to write files. " + ex);
                yield break;
            }

            TrySetupGit(packageRoot, _repoUrl, BuildPackageBranchRef(packageId), _gitHubToken);
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

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                var messageType = _statusMessage.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                    ? MessageType.Error
                    : MessageType.Info;
                EditorGUILayout.HelpBox(_statusMessage, messageType);
            }
        }

        private void StartOperation(IEnumerator routine)
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            EditorCoroutineRunner.StartCoroutine(WrapOperation(routine));
        }

        private void AutoLoadManifest()
        {
            if (_isBusy)
            {
                return;
            }

            if (_source == ManifestSource.LocalFile)
            {
                if (string.IsNullOrEmpty(_manifestPath) || !File.Exists(_manifestPath))
                {
                    return;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(_manifestUrl))
                {
                    return;
                }
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

            _isBusy = false;
            EditorUtility.ClearProgressBar();
            Repaint();
        }

        private static string GetDefaultManifestPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Assets", "TGSPackageManager", "manifest.json");
        }

        private static string GetDefaultInstallRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Assets", "TGSPackageManager", "packages");
        }

        private static string NormalizeManifestUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            if (url.IndexOf("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return url;
            }

            if (url.IndexOf("github.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var normalized = url.Replace("https://github.com/", "https://raw.githubusercontent.com/")
                    .Replace("http://github.com/", "https://raw.githubusercontent.com/")
                    .Replace("/blob/", "/");
                return normalized;
            }

            return url;
        }

        private static string AddCacheBuster(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "t=" + DateTime.UtcNow.Ticks;
        }

        private RepositoryInfo ResolveRepository()
        {
            var fallback = _manifest != null ? _manifest.repository : null;

            if (TryGetRepoInfoFromUrl(_repoUrl, out var owner, out var repo))
            {
                return new RepositoryInfo
                {
                    owner = owner,
                    name = repo,
                    defaultBranch = fallback != null ? fallback.defaultBranch : "main",
                    description = fallback != null ? fallback.description : null
                };
            }

            return fallback;
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
                if (repoUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                {
                    var path = repoUrl.Substring("git@github.com:".Length).Trim('/');
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

            return false;
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
            public PackageJsonAuthor author;
        }

        [Serializable]
        private class PackageJsonAuthor
        {
            public string name;
        }
    }

    internal class CreatePackageWindow : EditorWindow
    {
        private string _name;
        private string _author;
        private string _description;
        private string _version;
        private ToolsPackageManagerWindow _owner;

        public static void Show(ToolsPackageManagerWindow owner)
        {
            var window = CreateInstance<CreatePackageWindow>();
            window._owner = owner;
            window.titleContent = new GUIContent("Create Package");
            window.minSize = new Vector2(360f, 220f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create Package", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _name = EditorGUILayout.TextField("Package Name", _name);
            _author = EditorGUILayout.TextField("Author", _author);
            _description = EditorGUILayout.TextField("Description", _description);
            _version = EditorGUILayout.TextField("Version", _version);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_name) || string.IsNullOrEmpty(_author));
                if (GUILayout.Button("Create"))
                {
                    var data = new CreatePackageData
                    {
                        Name = _name,
                        Author = _author,
                        Description = _description,
                        Version = _version
                    };
                    _owner?.BeginCreatePackage(data);
                    Close();
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }

    public class CreatePackageData
    {
        public string Name;
        public string Author;
        public string Description;
        public string Version;
    }

    internal static class EditorCoroutineRunner
    {
        private class CoroutineState
        {
            public IEnumerator Routine;
            public object Current;
        }

        private static readonly List<CoroutineState> Routines = new List<CoroutineState>();
        private static bool _isHooked;

        public static void StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            Routines.Add(new CoroutineState { Routine = routine, Current = null });
            if (!_isHooked)
            {
                _isHooked = true;
                EditorApplication.update += Update;
            }
        }

        private static void Update()
        {
            for (var i = Routines.Count - 1; i >= 0; i--)
            {
                var state = Routines[i];
                if (!MoveNext(state))
                {
                    Routines.RemoveAt(i);
                }
            }

            if (Routines.Count == 0 && _isHooked)
            {
                _isHooked = false;
                EditorApplication.update -= Update;
            }
        }

        private static bool MoveNext(CoroutineState state)
        {
            if (state.Current is UnityWebRequestAsyncOperation webOp)
            {
                if (!webOp.isDone)
                {
                    return true;
                }

                state.Current = null;
            }
            else if (state.Current is AsyncOperation asyncOp)
            {
                if (!asyncOp.isDone)
                {
                    return true;
                }

                state.Current = null;
            }
            else if (state.Current is EditorWaitForSeconds wait)
            {
                if (!wait.IsDone)
                {
                    return true;
                }

                state.Current = null;
            }

            if (!state.Routine.MoveNext())
            {
                return false;
            }

            state.Current = state.Routine.Current;
            return true;
        }

        internal class EditorWaitForSeconds
        {
            private readonly double _endTime;

            public EditorWaitForSeconds(float seconds)
            {
                _endTime = EditorApplication.timeSinceStartup + seconds;
            }

            public bool IsDone => EditorApplication.timeSinceStartup >= _endTime;
        }
    }
}
