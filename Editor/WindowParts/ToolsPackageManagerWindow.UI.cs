using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    public partial class ToolsPackageManagerWindow
    {
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
            else if (_selectedTab == 1)
            {
                DrawRepositoriesSection();
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
                DrawAutoUpdateSettings();
                EditorGUILayout.Space();
                DrawRepositoriesPathSettings();
                EditorGUILayout.Space();
                DrawEmbeddedPackagesPathSettings();
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

        private void DrawRepositoriesPathSettings()
        {
            EditorGUILayout.LabelField("Repositories Paths", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Paths are relative to the package root unless absolute.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var repositoriesPath = EditorGUILayout.TextField("Repositories Path", _repositoriesPathRelative);
            var localRepositoriesPath = EditorGUILayout.TextField("Local Repositories Path", _localRepositoriesPathRelative);
            if (EditorGUI.EndChangeCheck())
            {
                _repositoriesPathRelative = repositoriesPath;
                _localRepositoriesPathRelative = localRepositoriesPath;
                EditorPrefs.SetString(PrefsRepositoriesPath, _repositoriesPathRelative ?? string.Empty);
                EditorPrefs.SetString(PrefsLocalRepositoriesPath, _localRepositoriesPathRelative ?? string.Empty);
                LoadRepositories();
            }
        }

        private void DrawEmbeddedPackagesPathSettings()
        {
            EditorGUILayout.LabelField("Embedded Packages", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Path is relative to the project root unless absolute.", MessageType.Info);

            var absoluteRoot = ResolveInstallRoot(string.IsNullOrWhiteSpace(_embeddedPackagesPathRelative)
                ? DefaultEmbeddedPackagesPathRelative
                : _embeddedPackagesPathRelative);
            EditorGUI.BeginChangeCheck();
            var embeddedPath = EditorGUILayout.TextField("Embedded Path", absoluteRoot);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(105f);
                if (GUILayout.Button("Browse", GUILayout.Width(80f)))
                {
                    var selected = EditorUtility.OpenFolderPanel("Select embedded packages root", embeddedPath, string.Empty);
                    if (!string.IsNullOrEmpty(selected))
                    {
                        embeddedPath = selected;
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _embeddedPackagesPathRelative = ToRelativeInstallRoot(embeddedPath);
                EditorPrefs.SetString(PrefsEmbeddedPackagesPath, _embeddedPackagesPathRelative ?? string.Empty);
                RefreshLocalCache();
            }
        }

        private void DrawPackageList()
        {
            EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);
            var snapshot = _usePackageListSnapshot ? _packageListSnapshot : null;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_isBusy);
                if (GUILayout.Button("Create Package"))
                {
                    CreatePackageWindow.Show(this);
                }
                EditorGUI.EndDisabledGroup();
                var hasAnyUpdate = snapshot != null
                    ? HasAnyUpdate(snapshot.Packages, snapshot.InstalledVersionsCache)
                    : HasAnyUpdate();
                if (hasAnyUpdate)
                {
                    EditorGUI.BeginDisabledGroup(_isBusy);
                    if (GUILayout.Button("Update All"))
                    {
                        StartOperation(UpdateAllPackages());
                    }
                    EditorGUI.EndDisabledGroup();
                }
                var refreshLocked = IsRefreshLocked();
                EditorGUI.BeginDisabledGroup(refreshLocked);
                if (GUILayout.Button("Refresh"))
                {
                    if (_isBusy && !refreshLocked)
                    {
                        ForceUnlockBusyState("Manual refresh");
                    }
                    BeginManualPackageRefresh();
                    StartOperation(LoadManifest());
                }
                EditorGUI.EndDisabledGroup();
            }

            var listItems = snapshot != null
                ? BuildPackageListItems(snapshot.Packages, snapshot.LocalPackagesCache, snapshot.InstalledVersionsCache,
                    snapshot.PackageUnityRequirements, snapshot.PackageCompatibility)
                : BuildPackageListItems(_packages);
            if ((listItems == null || listItems.Count == 0) && _repositories.Count == 0)
            {
                EditorGUILayout.HelpBox("No repositories configured.", MessageType.Info);
                DrawRepositoryAccessErrors();
                return;
            }
            var packageTab = GUILayout.Toolbar(_selectedPackageListTab, BuildPackageListTabLabels(listItems));
            if (packageTab != _selectedPackageListTab)
            {
                _selectedPackageListTab = packageTab;
                EditorPrefs.SetInt(PrefsPackageListTab, _selectedPackageListTab);
            }

            var showEmbedded = _selectedPackageListTab == 0;
            var showInstalled = _selectedPackageListTab == 1;
            var showAvailable = _selectedPackageListTab == 2;
            var showLocalOnly = _selectedPackageListTab == 3;
            var filteredItems = new List<PackageListItem>();
            foreach (var item in listItems)
            {
                if (item == null || item.Package == null)
                {
                    continue;
                }

                if (showLocalOnly)
                {
                    if (item.IsLocalOnly)
                    {
                        filteredItems.Add(item);
                    }
                    continue;
                }

                if (item.IsLocalOnly)
                {
                    continue;
                }

                if (item.Package.required)
                {
                    if (showEmbedded)
                    {
                        filteredItems.Add(item);
                    }
                    continue;
                }

                var isInstalled = item.IsInstalled || item.IsUpmInstalled;
                if (showInstalled)
                {
                    if (isInstalled)
                    {
                        filteredItems.Add(item);
                    }
                    continue;
                }

                if (showAvailable && !isInstalled)
                {
                    filteredItems.Add(item);
                }
            }

            if (filteredItems.Count == 0)
            {
                EditorGUILayout.HelpBox("No packages available.", MessageType.Info);
                DrawRepositoryAccessErrors();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in filteredItems)
            {
                var package = item.Package;
                if (package == null)
                {
                    continue;
                }

                var isUpmInstalled = item.IsUpmInstalled;
                var upmVersion = item.UpmVersion;
                var repositoryConfig = GetRepositoryConfigForPackage(package);
                var repositoryUrl = GetRepositoryUrl(repositoryConfig);
                if (item.IsLocalOnly)
                {
                    var localInfo = GetLocalPackageInfo(package.id, snapshot);
                    if (localInfo != null && !string.IsNullOrWhiteSpace(localInfo.RepositoryUrl))
                    {
                        repositoryUrl = localInfo.RepositoryUrl;
                        var matchedRepository = FindRepositoryConfigByUrl(repositoryUrl);
                        if (matchedRepository != null)
                        {
                            repositoryConfig = matchedRepository;
                        }
                        else
                        {
                            repositoryConfig = null;
                        }
                    }
                    else
                    {
                        repositoryUrl = string.Empty;
                        repositoryConfig = null;
                    }
                }

                var previousColor = GUI.backgroundColor;
                if (item.IsLocalOnly)
                {
                    GUI.backgroundColor = LocalOnlyPackageColor;
                }
                else if (item.IsInstalled || isUpmInstalled)
                {
                    GUI.backgroundColor = InstalledPackageColor;
                }

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    var lineRect = EditorGUILayout.GetControlRect(false, 2f);
                    EditorGUI.DrawRect(new Rect(lineRect.x, lineRect.y, lineRect.width, 1f),
                    new Color(0.35f, 0.35f, 0.35f, 1f));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = EditorStyles.boldLabel.fontSize + 2
                        };
                        EditorGUILayout.LabelField(package.displayName ?? package.id, titleStyle);
                        GUILayout.FlexibleSpace();
                        if (isUpmInstalled)
                        {
                            var previousBg = GUI.backgroundColor;
                            GUI.backgroundColor = Color.red;
                            var tagStyle = new GUIStyle("box")
                            {
                                alignment = TextAnchor.MiddleCenter,
                                fontSize = EditorStyles.miniBoldLabel.fontSize,
                                normal = { textColor = Color.white }
                            };
                            GUILayout.Label("UPM", tagStyle, GUILayout.Width(48f));
                            GUI.backgroundColor = previousBg;
                        }

                        if (repositoryConfig != null)
                        {
                            if (isUpmInstalled)
                            {
                                GUILayout.Space(4f);
                            }
                            DrawRepositoryVisibilityTag(repositoryConfig.isPublic);
                        }
                    }
                    if (!string.IsNullOrEmpty(package.author))
                    {
                        EditorGUILayout.LabelField("Author: " + package.author, EditorStyles.miniLabel);
                    }
                    if (!string.IsNullOrEmpty(repositoryUrl))
                    {
                        EditorGUILayout.LabelField("Repository: " + repositoryUrl, EditorStyles.miniLabel);
                    }
                    EditorGUILayout.LabelField(package.id ?? string.Empty, EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(package.description))
                    {
                        EditorGUILayout.LabelField(package.description, EditorStyles.wordWrappedMiniLabel);
                    }

                    var installedVersion = item.InstalledVersion;
                    var installedLabel = string.IsNullOrEmpty(installedVersion) ? "Not installed" : installedVersion;
                    if (isUpmInstalled)
                    {
                        installedLabel = string.IsNullOrEmpty(upmVersion) ? "UPM" : "UPM " + upmVersion;
                    }
                    EditorGUILayout.LabelField("Installed", installedLabel);
                    DrawDependenciesFoldout(package, isUpmInstalled, item.IsInstalled || isUpmInstalled);
                    if (IsGitInitialized(package, snapshot))
                    {
                        var gitHead = GetGitHeadCommit(package, snapshot);
                        var gitDetached = IsGitDetached(package, snapshot);
                        if (!string.IsNullOrEmpty(gitHead))
                        {
                            var gitMessage = GetGitHeadMessage(package, snapshot);
                            var gitLabel = gitDetached ? "Git: " + gitHead + " (Detached HEAD)" : "Git: " + gitHead;
                            if (!string.IsNullOrEmpty(gitMessage))
                            {
                                gitLabel += " - " + gitMessage;
                            }
                            EditorGUILayout.LabelField(gitLabel, EditorStyles.miniLabel);
                        }
                    }

                    if (item.IsLocalOnly)
                    {
                        EditorGUILayout.HelpBox("Local Only. Publish this package to share it with everyone.", MessageType.Info);
                    }

                    var canInstall = DrawPackageStatus(package, snapshot);
                    if (item.HasUpdate)
                    {
                        var latestVersion = GetLatestVersion(package);
                        EditorGUILayout.HelpBox("Updated available: " + latestVersion, MessageType.Info);
                    }
                    if (canInstall)
                    {
                        DrawVersionSelection(package, installedVersion, canInstall, isUpmInstalled, upmVersion, item.IsLocalOnly, item.IsInstalled);
                    }
                    if (!canInstall)
                    {
                        DrawAutoUpdateToggle(package, item.IsLocalOnly);
                    }

                    var buttonDivider = EditorGUILayout.GetControlRect(false, 1f);
                    EditorGUI.DrawRect(new Rect(buttonDivider.x, buttonDivider.y, buttonDivider.width, 1f),
                    new Color(0.2f, 0.2f, 0.2f, 1f));

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var isLocalRepo = IsLocalRepository(package);
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
                            if (HasPendingCommit(package, snapshot))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = new Color(0.95f, 0.85f, 0.35f);
                                if (GUILayout.Button("Git Commit"))
                                {
                                    CommitPackageWindow.Show(this, package, GetPendingCommitFiles(package));
                                }
                                GUI.backgroundColor = previousBgColor;
                            }

                            if (HasPendingPush(package, snapshot))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = Color.red;
                                if (GUILayout.Button("Git Push"))
                                {
                                    PushUpdate(package);
                                }
                                GUI.backgroundColor = previousBgColor;
                            }

                            if ((item.IsInstalled || isUpmInstalled) && TryGetPackageRoot(package, isUpmInstalled, out var packageRoot)
                            && IsGitInitializedAtPath(packageRoot))
                            {
                                var previousBgColor = GUI.backgroundColor;
                                GUI.backgroundColor = Color.red;
                                if (GUILayout.Button("TAG this version"))
                                {
                                    CreateVersionWindow.Show(this, package);
                                }
                                GUI.backgroundColor = previousBgColor;
                            }

                            if (item.IsInstalled || isUpmInstalled)
                            {
                                EditorGUI.BeginDisabledGroup(package.required);
                                if (GUILayout.Button("Uninstall"))
                                {
                                    if (item.IsInstalled)
                                    {
                                        UninstallPackageSafe(package);
                                    }
                                    else if (isUpmInstalled)
                                    {
                                        StartOperation(RemovePackageViaUpm(package));
                                    }
                                }
                                EditorGUI.EndDisabledGroup();
                            }
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

                            if (item.IsInstalled)
                            {
                                EditorGUI.BeginDisabledGroup(package.required);
                                if (GUILayout.Button("Uninstall"))
                                {
                                    UninstallPackageSafe(package);
                                }
                                EditorGUI.EndDisabledGroup();
                            }
                        }

                        if (item.IsInstalled || isUpmInstalled)
                        {
                            if (GUILayout.Button("Open Directory"))
                            {
                                OpenPackageDirectory(package, isUpmInstalled);
                            }
                        }

                        if ((item.IsInstalled || isUpmInstalled) && !item.IsLocalOnly)
                        {
                            var hasPackageRoot = TryGetPackageRoot(package, isUpmInstalled, out var packageRoot)
                            || TryGetPackageRoot(package, !isUpmInstalled, out packageRoot);
                            if (!hasPackageRoot)
                            {
                                if (GUILayout.Button("Initialize Git"))
                                {
                                    _statusMessage = "Package directory not found for " + package.id + ".";
                                }
                            }
                            else if (!IsGitInitializedAtPath(packageRoot))
                            {
                                if (GUILayout.Button("Initialize Git"))
                                {
                                    var ok = EditorUtility.DisplayDialog("Initialize Git",
                                    "This will initialize Git for this package. Any local changes may be lost. Continue?",
                                    "CONTINUE", "CANCEL");
                                    if (!ok)
                                    {
                                        return;
                                    }
                                    var reference = ResolveGitInitializationRef(package, installedVersion, isUpmInstalled, upmVersion);
                                    SetupGitForInstalledPackage(package, reference, packageRoot);
                                    RefreshLocalCache();
                                }
                            }
                            else
                            {
                                if (GUILayout.Button("Remove Git"))
                                {
                                    RemoveGitForInstalledPackage(package, packageRoot);
                                    RefreshLocalCache();
                                }
                            }
                        }

                        if (!item.IsLocalOnly)
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
            DrawRepositoryAccessErrors();
        }

        private static string[] BuildPackageListTabLabels(List<PackageListItem> listItems)
        {
            var embeddedCount = 0;
            var installedCount = 0;
            var availableCount = 0;
            var localCount = 0;

            if (listItems != null)
            {
                foreach (var item in listItems)
                {
                    if (item == null || item.Package == null)
                    {
                        continue;
                    }

                    if (item.IsLocalOnly)
                    {
                        localCount++;
                        continue;
                    }

                    if (item.Package.required)
                    {
                        embeddedCount++;
                        continue;
                    }

                    var isInstalled = item.IsInstalled || item.IsUpmInstalled;
                    if (isInstalled)
                    {
                        installedCount++;
                    }
                    else
                    {
                        availableCount++;
                    }
                }
            }

            return new[]
            {
                PackageListTabs[0] + " [" + embeddedCount + "]",
                PackageListTabs[1] + " [" + installedCount + "]",
                PackageListTabs[2] + " [" + availableCount + "]",
                PackageListTabs[3] + " [" + localCount + "]"
            };
        }

        private void DrawDependenciesFoldout(PackageEntry package, bool isUpmInstalled, bool isInstalled)
        {
            if (package == null)
            {
                return;
            }

            var dependencies = package.dependencies ?? Array.Empty<string>();
            var packageKey = package.id ?? package.displayName;
            if (string.IsNullOrEmpty(packageKey))
            {
                return;
            }

            var hasDependencies = dependencies.Length > 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                var isExpanded = GetDependencyFoldoutState(packageKey);
                var label = "Dependencies (" + dependencies.Length + ")";
                var labelContent = new GUIContent(label);
                var labelWidth = EditorStyles.foldout.CalcSize(labelContent).x + 8f;
                var foldoutRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight,
                GUILayout.Width(labelWidth));
                isExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, labelContent, true);
                _dependencyFoldouts[packageKey] = isExpanded;
                if (isInstalled)
                {
                    GUILayout.Space(4f);
                    if (GUILayout.Button("Edit", GUILayout.Width(48f)))
                    {
                        OpenEditDependenciesWindow(package, isUpmInstalled);
                    }
                }
                if (!isExpanded)
                {
                    return;
                }
            }

            if (!hasDependencies)
            {
                return;
            }

            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrEmpty(dependency))
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(16f);
                    EditorGUILayout.LabelField(FormatDependencyLabel(dependency), EditorStyles.miniLabel);
                }
            }
        }

        private bool GetDependencyFoldoutState(string packageId)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                return true;
            }

            return _dependencyFoldouts.TryGetValue(packageId, out var value) ? value : true;
        }

        private void OpenEditDependenciesWindow(PackageEntry package, bool isUpmInstalled)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            if (isUpmInstalled)
            {
                _statusMessage = "Dependencies can only be edited for locally installed packages.";
                return;
            }

            if (!TryGetPackageRoot(package, false, out var packageRoot))
            {
                _statusMessage = "Package directory not found for " + package.id + ".";
                return;
            }

            EditDependenciesWindow.Show(this, package, packageRoot);
        }

        private void DrawVersionSelection(PackageEntry package, string installedVersion, bool canInstall,
        bool isUpmInstalled, string upmVersion, bool isLocalOnly, bool isInstalled)
        {
            if (package == null || package.loadStatus != PackageLoadStatus.Loaded)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (package.versions == null || package.versions.Length == 0)
                {
                    EditorGUILayout.LabelField("Version", GUILayout.Width(60f));
                    EditorGUILayout.LabelField("None", GUILayout.Width(140f));
                }
                else
                {
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
                    EditorGUILayout.LabelField("Version", GUILayout.Width(60f));
                    selectedIndex = EditorGUILayout.Popup(selectedIndex, labels, GUILayout.Width(140f));
                    _selectedVersions[package.id] = selectedIndex;
                }

                if (!isLocalOnly && !package.required)
                {
                    EditorGUI.BeginChangeCheck();
                    var isEnabled = IsAutoUpdateEnabled(package.id);
                    var nextValue = EditorGUILayout.ToggleLeft("Auto Update", isEnabled, GUILayout.Width(110f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetAutoUpdateEnabled(package.id, nextValue);
                    }
                }
                else if (!isLocalOnly && package.required)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ToggleLeft("Auto Update", false, GUILayout.Width(110f));
                    EditorGUI.EndDisabledGroup();
                }

                if (isUpmInstalled)
                {
                    var selectedVersionLabel = GetSelectedVersionLabel(package);
                    var isSelectedInstalled = !string.IsNullOrEmpty(selectedVersionLabel)
                    && !string.IsNullOrEmpty(upmVersion)
                    && string.Equals(selectedVersionLabel, upmVersion, StringComparison.OrdinalIgnoreCase);
                    EditorGUI.BeginDisabledGroup(_isBusy || isSelectedInstalled || !canInstall);
                    if (GUILayout.Button("Update via UPM", GUILayout.Width(120f)))
                    {
                        var reference = !string.IsNullOrEmpty(selectedVersionLabel)
                        ? BuildVersionRef(package, selectedVersionLabel)
                        : BuildPackageBranchRef(package.id);
                        StartOperation(UpdatePackageViaUpm(package, reference, selectedVersionLabel));
                    }
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button("Open Unity Package Manager", GUILayout.Width(190f)))
                    {
                        if (!OpenUpmWindow(package.id))
                        {
                            _statusMessage = "ERROR: Unable to open Package Manager.";
                        }
                    }
                }

                if (!isUpmInstalled)
                {
                    var isLatestInstalled = IsLatestInstalled(package, installedVersion);
                    EditorGUI.BeginDisabledGroup(_isBusy || isLatestInstalled || !canInstall);
                    if (GUILayout.Button("Install Latest", GUILayout.Width(120f)))
                    {
                        var reference = ResolveLatestRef(package);
                        var operation = string.IsNullOrEmpty(installedVersion) ? "Installation" : "Update";
                        var targetVersion = GetLatestVersion(package);
                        StartOperation(InstallPackage(package, reference, operation, targetVersion));
                    }
                    EditorGUI.EndDisabledGroup();

                    var selectedVersion = GetSelectedVersion(package);
                    var isSelectedInstalled = selectedVersion != null
                    && !string.IsNullOrEmpty(installedVersion)
                    && string.Equals(selectedVersion.version, installedVersion, StringComparison.OrdinalIgnoreCase);
                    EditorGUI.BeginDisabledGroup(_isBusy || !canInstall || isSelectedInstalled);
                    if (GUILayout.Button("Install Selected Version", GUILayout.Width(180f)))
                    {
                        if (selectedVersion != null)
                        {
                            var reference = BuildVersionRef(package, selectedVersion.version);
                            var operation = string.IsNullOrEmpty(installedVersion) ? "Installation" : "Update";
                            StartOperation(InstallPackage(package, reference, operation, selectedVersion.version));
                        }
                    }
                    EditorGUI.EndDisabledGroup();

                    var canImportToGame = isInstalled || isUpmInstalled;
                    EditorGUI.BeginDisabledGroup(_isBusy || !canImportToGame);
                    if (GUILayout.Button("Import to Game", GUILayout.Width(140f)))
                    {
                        if (!TryGetPackageRoot(package, isUpmInstalled, out var packageRoot))
                        {
                            _statusMessage = "Package directory not found for " + package.id + ".";
                        }
                        else
                        {
                            ImportToGameWindow.Show(this, package, packageRoot);
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                }

                if (!isInstalled && !isUpmInstalled)
                {
                    EditorGUI.BeginDisabledGroup(isLocalOnly);
                    if (GUILayout.Button("Install via UPM", GUILayout.Width(130f)))
                    {
                        StartOperation(InstallPackageViaUpm(package));
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
        }

        private bool DrawPackageStatus(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (package == null)
            {
                return false;
            }

            switch (package.loadStatus)
            {
                case PackageLoadStatus.Loaded:
                return DrawCompatibilityStatus(package, snapshot);
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

        private bool DrawCompatibilityStatus(PackageEntry package, PackageListSnapshot snapshot)
        {
            if (package == null)
            {
                return false;
            }

            var isCompatible = snapshot != null
                ? IsPackageCompatible(package, snapshot.PackageCompatibility)
                : IsPackageCompatible(package);
            if (!isCompatible)
            {
                var requirement = snapshot != null
                    ? GetPackageUnityRequirement(package, snapshot.PackageUnityRequirements)
                    : GetPackageUnityRequirement(package);
                EditorGUILayout.HelpBox("Incompatible with current Unity (" + Application.unityVersion + "). Requires: " + requirement,
                MessageType.Warning);
                return false;
            }

            return true;
        }

        private bool OpenUpmWindow(string packageId)
        {
            if (TryOpenUpmWindow(packageId))
            {
                return true;
            }

            return EditorApplication.ExecuteMenuItem("Window/Package Manager");
        }

        private void OpenPackageDirectory(PackageEntry package, bool isUpmInstalled)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
            {
                return;
            }

            if (!TryGetPackageRoot(package, isUpmInstalled, out var packageRoot))
            {
                if (!TryGetPackageRoot(package, !isUpmInstalled, out packageRoot))
                {
                    _statusMessage = "Package directory not found for " + package.id + ".";
                    return;
                }
            }

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

        private void DrawAutoUpdateToggle(PackageEntry package, bool isLocalOnly)
        {
            if (package == null || string.IsNullOrEmpty(package.id) || isLocalOnly || package.required)
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

        private void DrawRepositoryAccessErrors()
        {
            if (_repositoryAccessErrors.Count == 0)
            {
                return;
            }

            var messages = new List<string>();
            foreach (var entry in _repositoryAccessErrors)
            {
                var repoLabel = string.IsNullOrEmpty(entry.Key) ? "Unknown repository" : entry.Key;
                var details = string.IsNullOrEmpty(entry.Value) ? "Unknown error." : entry.Value;
                messages.Add("Repository access error for " + repoLabel + ": " + details);
            }

            EditorGUILayout.HelpBox(string.Join("\n", messages), MessageType.Error);
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
    }
}
