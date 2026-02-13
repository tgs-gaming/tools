using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class EditDependenciesWindow : EditorWindow
    {
        private ToolsPackageManagerWindow _owner;
        private PackageEntry _package;
        private string _packageRoot;
        private readonly List<DependencySelection> _dependencies = new List<DependencySelection>();

        private class DependencySelection
        {
            public string PackageId;
            public string Version;
        }

        public static void Show(ToolsPackageManagerWindow owner, PackageEntry package, string packageRoot)
        {
            if (owner == null || package == null || string.IsNullOrEmpty(packageRoot))
            {
                return;
            }

            var window = CreateInstance<EditDependenciesWindow>();
            window._owner = owner;
            window._package = package;
            window._packageRoot = packageRoot;
            window.titleContent = new GUIContent("Edit Dependencies");
            window.minSize = new Vector2(420f, 260f);
            window.BuildDependencies();
            window.ShowUtility();
        }

        private void BuildDependencies()
        {
            _dependencies.Clear();
            if (_package == null)
            {
                return;
            }

            var dependencies = _package.dependencies ?? Array.Empty<string>();
            foreach (var dependency in dependencies)
            {
                if (!ToolsPackageManagerWindow.TryParseDependency(dependency, out var packageId, out var version))
                {
                    continue;
                }

                _dependencies.Add(new DependencySelection
                {
                    PackageId = packageId,
                    Version = version
                });
            }
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
            if (_package == null)
            {
                Close();
                return;
            }

            EditorGUILayout.LabelField("Edit Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            DrawDependenciesSection();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_packageRoot));
                if (GUILayout.Button("Save"))
                {
                    _owner.UpdatePackageDependencies(_package, _packageRoot, BuildDependencyStrings());
                    Close();
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
