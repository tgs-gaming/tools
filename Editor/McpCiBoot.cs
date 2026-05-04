using System;
using com.tgs.mcpforunity.editor.Constants;
using com.tgs.mcpforunity.editor.Services.Transport.Transports;
using UnityEditor;

namespace com.tgs.mcpforunity.editor
{
    public static class McpCiBoot
    {
        public static void StartStdioForCi()
        {
            try 
            { 
                EditorPrefs.SetBool(EditorPrefKeys.UseHttpTransport, false); 
            }
            catch { /* ignore */ }

            StdioBridgeHost.StartAutoConnect();
        }
    }
}
