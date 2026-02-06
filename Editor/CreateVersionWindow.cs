using UnityEditor;
using UnityEngine;

namespace com.tgs.packagemanager.editor
{
    internal class CreateVersionWindow : EditorWindow
    {
        private const string Placeholder = "1.5.0";
        private string _version;
        private string _releaseNotes;
        private ToolsPackageManagerWindow _owner;
        private PackageEntry _package;

        public static void Show(ToolsPackageManagerWindow owner, PackageEntry package)
        {
            var window = CreateInstance<CreateVersionWindow>();
            window._owner = owner;
            window._package = package;
            window._version = "";
            window._releaseNotes = string.Empty;
            window.titleContent = new GUIContent("Create Version");
            window.minSize = new Vector2(360f, 220f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create Version Tag", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _version = EditorGUILayout.TextField("Version", _version);
            EditorGUILayout.LabelField("Format: " + Placeholder, EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Release Notes", EditorStyles.miniLabel);
            _releaseNotes = EditorGUILayout.TextArea(_releaseNotes, GUILayout.MinHeight(80f));

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel"))
                {
                    Close();
                }

                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_version) || string.IsNullOrWhiteSpace(_releaseNotes));
                if (GUILayout.Button("Create"))
                {
                    _owner?.CreateVersionTag(_package, _version, _releaseNotes);
                    Close();
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
