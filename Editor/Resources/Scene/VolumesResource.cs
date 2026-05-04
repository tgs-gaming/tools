using System;
using com.tgs.mcpforunity.editor.Helpers;
using com.tgs.mcpforunity.editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

namespace com.tgs.mcpforunity.editor.Resources.Scene
{
    [McpForUnityResource("get_volumes")]
    public static class VolumesResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return VolumeOps.ListVolumes(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[VolumesResource] Error listing volumes: {e}");
                return new ErrorResponse($"Error listing volumes: {e.Message}");
            }
        }
    }
}
