using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class CommitPackageWindow : EditorWindow
    {
        private string _message;
        private string[] _files;
        private Vector2 _scroll;
        private ToolsPackageManagerWindow _owner;
        private PackageEntry _package;

        public static void Show(ToolsPackageManagerWindow owner, PackageEntry package, string[] files)
        {
            var window = CreateInstance<CommitPackageWindow>();
            window._owner = owner;
            window._package = package;
            window._files = files ?? new string[0];
            window.titleContent = new GUIContent("Commit Changes");
            window.minSize = new Vector2(420f, 300f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Files to Commit", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(180f));
            if (_files != null && _files.Length > 0)
            {
                foreach (var file in _files)
                {
                    EditorGUILayout.SelectableLabel(
                        file ?? string.Empty,
                        EditorStyles.miniLabel,
                        GUILayout.ExpandWidth(true),
                        GUILayout.MinWidth(0),
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
            else
            {
                EditorGUILayout.LabelField("No pending changes.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            _message = EditorGUILayout.TextField("Commit Message", _message);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_message) || _files == null || _files.Length == 0);
                if (GUILayout.Button("Commit"))
                {
                    _owner?.CommitPackage(_package, _message);
                    Close();
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
