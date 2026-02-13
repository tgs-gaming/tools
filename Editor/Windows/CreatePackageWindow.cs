using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class CreatePackageWindow : EditorWindow
    {
        private string _name;
        private string _author;
        private string _description;
        private string _version;
        private string _unityVersion;
        private bool _required;
        private int _selectedRepositoryIndex = -1;
        private string[] _repositoryLabels = Array.Empty<string>();
        private string[] _repositoryIds = Array.Empty<string>();
        private string[] _repositoryFullLabels = Array.Empty<string>();
        private readonly List<DependencySelection> _dependencies = new List<DependencySelection>();
        private ToolsPackageManagerWindow _owner;

        private class DependencySelection
        {
            public string PackageId;
            public string Version;
        }

        public static void Show(ToolsPackageManagerWindow owner)
        {
            var window = CreateInstance<CreatePackageWindow>();
            window._owner = owner;
            window._unityVersion = ToolsPackageManagerWindow.GetDefaultUnityVersion();
            window.titleContent = new GUIContent("Create Package");
            window.minSize = new Vector2(360f, 270f);
            window.Show();
        }

        private void DrawDependenciesSection()
        {
            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+", GUILayout.Width(26f)))
                {
                    _dependencies.Add(new DependencySelection());
                }

                EditorGUI.BeginDisabledGroup(_dependencies.Count == 0);
                if (GUILayout.Button("-", GUILayout.Width(26f)))
                {
                    _dependencies.RemoveAt(_dependencies.Count - 1);
                }
                EditorGUI.EndDisabledGroup();
            }

            var availablePackages = _owner != null ? _owner.GetAvailablePackagesSnapshot() : new List<PackageEntry>();
            if (availablePackages.Count == 0)
            {
                EditorGUILayout.HelpBox("Load packages to select package dependencies.", MessageType.Info);
                return;
            }

            var packageIds = new string[availablePackages.Count];
            for (var i = 0; i < availablePackages.Count; i++)
            {
                packageIds[i] = availablePackages[i].id;
            }

            for (var i = 0; i < _dependencies.Count; i++)
            {
                var dependency = _dependencies[i];
                if (dependency == null)
                {
                    dependency = new DependencySelection();
                    _dependencies[i] = dependency;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var selectedPackageIndex = 0;
                    if (!string.IsNullOrEmpty(dependency.PackageId))
                    {
                        for (var index = 0; index < packageIds.Length; index++)
                        {
                            if (string.Equals(packageIds[index], dependency.PackageId, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedPackageIndex = index;
                                break;
                            }
                        }
                    }

                    selectedPackageIndex = EditorGUILayout.Popup(selectedPackageIndex, packageIds);
                    dependency.PackageId = packageIds[selectedPackageIndex];

                    var selectedPackage = availablePackages[selectedPackageIndex];
                    var versionOptions = BuildVersionOptions(selectedPackage);
                    var selectedVersionIndex = 0;
                    if (!string.IsNullOrEmpty(dependency.Version))
                    {
                        for (var index = 1; index < versionOptions.Length; index++)
                        {
                            if (string.Equals(versionOptions[index], dependency.Version, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedVersionIndex = index;
                                break;
                            }
                        }
                    }

                    selectedVersionIndex = EditorGUILayout.Popup(selectedVersionIndex, versionOptions, GUILayout.Width(120f));
                    dependency.Version = selectedVersionIndex == 0 ? null : versionOptions[selectedVersionIndex];
                }
            }
        }

        private static string[] BuildVersionOptions(PackageEntry package)
        {
            var versions = new List<string> { "(latest)" };
            if (package != null && package.versions != null)
            {
                foreach (var version in package.versions)
                {
                    if (version == null || string.IsNullOrEmpty(version.version))
                    {
                        continue;
                    }

                    versions.Add(version.version);
                }
            }

            return versions.ToArray();
        }

        private string[] BuildDependencyStrings()
        {
            if (_dependencies.Count == 0)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var dependency in _dependencies)
            {
                if (dependency == null || string.IsNullOrEmpty(dependency.PackageId))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(dependency.Version))
                {
                    list.Add(dependency.PackageId);
                }
                else
                {
                    list.Add(dependency.PackageId + "-v" + dependency.Version);
                }
            }

            return list.ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create Package", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_owner != null)
            {
                _owner.GetRepositorySelectionOptions(out _repositoryLabels, out _repositoryIds, out _repositoryFullLabels);
            }

            if (_repositoryLabels == null || _repositoryLabels.Length == 0)
            {
                EditorGUILayout.HelpBox("No repositories configured. Add one first.", MessageType.Warning);
            }
            else
            {
                if (_selectedRepositoryIndex < 0 || _selectedRepositoryIndex >= _repositoryLabels.Length)
                {
                    _selectedRepositoryIndex = 0;
                }

                var fullLabel = _selectedRepositoryIndex >= 0 && _selectedRepositoryIndex < _repositoryFullLabels.Length
                    ? _repositoryFullLabels[_selectedRepositoryIndex]
                    : _repositoryLabels[_selectedRepositoryIndex];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Repository", GUILayout.Width(EditorGUIUtility.labelWidth - 4f));
                    if (EditorGUILayout.DropdownButton(new GUIContent(fullLabel), FocusType.Keyboard))
                    {
                        var menu = new GenericMenu();
                        for (var i = 0; i < _repositoryLabels.Length; i++)
                        {
                            var index = i;
                            var label = _repositoryLabels[i];
                            menu.AddItem(new GUIContent(label), index == _selectedRepositoryIndex, () =>
                            {
                                _selectedRepositoryIndex = index;
                                Repaint();
                            });
                        }
                        menu.ShowAsContext();
                    }
                }
            }

            _name = EditorGUILayout.TextField("Package Name", _name);
            _author = EditorGUILayout.TextField("Author", _author);
            _description = EditorGUILayout.TextField("Description", _description);
            _version = EditorGUILayout.TextField("Version", _version);
            _unityVersion = EditorGUILayout.TextField("Unity Version", _unityVersion);
            _required = EditorGUILayout.Toggle("Embedded", _required);
            EditorGUILayout.HelpBox("Embedded packages will always be installed.", MessageType.Info);
            EditorGUILayout.Space();
            DrawDependenciesSection();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                var hasRepository = _repositoryIds != null
                                    && _selectedRepositoryIndex >= 0
                                    && _selectedRepositoryIndex < _repositoryIds.Length
                                    && !string.IsNullOrEmpty(_repositoryIds[_selectedRepositoryIndex]);
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_name) || string.IsNullOrEmpty(_author) || !hasRepository);
                if (GUILayout.Button("Create"))
                {
                    var repositoryId = hasRepository ? _repositoryIds[_selectedRepositoryIndex] : null;
                    var data = new CreatePackageData
                    {
                        Name = _name,
                        Author = _author,
                        Description = _description,
                        Version = _version,
                        UnityVersion = _unityVersion,
                        Required = _required,
                        Dependencies = BuildDependencyStrings(),
                        RepositoryId = repositoryId
                    };
                    _owner?.BeginCreatePackage(data);
                    Close();
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
