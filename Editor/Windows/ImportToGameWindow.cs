using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using com.tgs.assetdependencymanager.editor;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class ImportToGameWindow : EditorWindow
    {
        private class GameDestinationOption
        {
            public string DisplayName;
            public string FullPath;
        }

        private ToolsPackageManagerWindow _owner;
        private PackageEntry _package;
        private string _packageRoot;
        private string _filterText = string.Empty;
        private readonly List<GameDestinationOption> _destinations = new List<GameDestinationOption>();
        private readonly List<GameDestinationOption> _filteredDestinations = new List<GameDestinationOption>();
        private string[] _destinationLabels = Array.Empty<string>();
        private int _selectedIndex;

        public static void Show(ToolsPackageManagerWindow owner, PackageEntry package, string packageRoot)
        {
            if (owner == null || package == null || string.IsNullOrEmpty(packageRoot))
            {
                return;
            }

            var window = CreateInstance<ImportToGameWindow>();
            window._owner = owner;
            window._package = package;
            window._packageRoot = packageRoot;
            window.titleContent = new GUIContent("Import to Game");
            window.minSize = new Vector2(420f, 220f);
            window.RefreshDestinations();
            window.ShowUtility();
        }

        private void RefreshDestinations()
        {
            _destinations.Clear();
            _destinationLabels = Array.Empty<string>();
            _selectedIndex = 0;

            var rootPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "studio_packages"));
            if (!Directory.Exists(rootPath))
            {
                return;
            }

            var normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
            var unique = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                var fullPath = Path.GetFullPath(directory);
                if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = fullPath.Substring(normalizedRoot.Length);
                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2)
                {
                    continue;
                }

                var displayName = Path.Combine(segments[0], segments[1]);
                var gameRoot = Path.Combine(rootPath, segments[0], segments[1]);
                if (!unique.ContainsKey(displayName) && Directory.Exists(gameRoot))
                {
                    unique.Add(displayName, gameRoot);
                }
            }

            foreach (var pair in unique)
            {
                _destinations.Add(new GameDestinationOption
                {
                    DisplayName = pair.Key,
                    FullPath = pair.Value
                });
            }

            _destinations.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _filteredDestinations.Clear();
            _destinationLabels = Array.Empty<string>();
            _selectedIndex = 0;

            var filter = _filterText?.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                _filteredDestinations.AddRange(_destinations);
            }
            else
            {
                foreach (var destination in _destinations)
                {
                    if (destination.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _filteredDestinations.Add(destination);
                    }
                }
            }

            _destinationLabels = new string[_filteredDestinations.Count + 1];
            _destinationLabels[0] = "Select game...";
            for (var i = 0; i < _filteredDestinations.Count; i++)
            {
                _destinationLabels[i + 1] = _filteredDestinations[i].DisplayName;
            }
        }

        private string GetPackageVersionLabel()
        {
            if (string.IsNullOrEmpty(_packageRoot))
            {
                return null;
            }

            var packageJsonPath = Path.Combine(_packageRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(packageJsonPath);
                var match = Regex.Match(json, "\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"",
                    RegexOptions.IgnoreCase);
                return match.Success ? match.Groups["version"].Value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnGUI()
        {
            if (_owner == null || _package == null)
            {
                Close();
                return;
            }

            EditorGUILayout.LabelField("Import to Game", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            var packageLabel = _package.displayName ?? _package.id ?? "Package";
            var versionLabel = GetPackageVersionLabel() ?? "unknown";
            EditorGUILayout.LabelField("Package", packageLabel + " " + versionLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _filterText = EditorGUILayout.TextField("Filter", _filterText ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFilter();
            }

            if (_filteredDestinations.Count == 0)
            {
                EditorGUILayout.HelpBox("No game folders found for the current filter.", MessageType.Warning);
            }
            else
            {
                _selectedIndex = EditorGUILayout.Popup("Destination Game", _selectedIndex, _destinationLabels);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var canImport = _selectedIndex > 0 && _selectedIndex - 1 < _filteredDestinations.Count;
                EditorGUI.BeginDisabledGroup(!canImport);
                if (GUILayout.Button("Import", GUILayout.Width(120f)))
                {
                    var destination = _filteredDestinations[_selectedIndex - 1];
                    DependencyManager.OpenWithImportDefaults(_packageRoot, destination.FullPath, "ExternalPackages");
                    Close();
                }
                EditorGUI.EndDisabledGroup();
                GUILayout.FlexibleSpace();
            }
        }
    }
}
