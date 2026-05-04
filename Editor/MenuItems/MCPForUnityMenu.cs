using com.tgs.mcpforunity.editor.Setup;
using com.tgs.mcpforunity.editor.Windows;
using UnityEditor;
using UnityEngine;

namespace com.tgs.mcpforunity.editor.MenuItems
{
    public static class MCPForUnityMenu
    {
        [MenuItem("Window/MCP For Unity/Toggle MCP Window %#m", priority = 1)]
        public static void ToggleMCPWindow()
        {
            MCPForUnityEditorWindow.ShowWindow();
        }

        [MenuItem("Window/MCP For Unity/Local Setup Window", priority = 2)]
        public static void ShowSetupWindow()
        {
            SetupWindowService.ShowSetupWindow();
        }


        [MenuItem("Window/MCP For Unity/Edit EditorPrefs", priority = 3)]
        public static void ShowEditorPrefsWindow()
        {
            EditorPrefsWindow.ShowWindow();
        }
    }
}
