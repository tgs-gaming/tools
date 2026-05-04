using System;
using com.tgs.mcpforunity.editor.Helpers;
using com.tgs.mcpforunity.editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

namespace com.tgs.mcpforunity.editor.Resources.Scene
{
    [McpForUnityResource("get_rendering_stats")]
    public static class RenderingStatsResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return RenderingStatsOps.GetStats(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[RenderingStatsResource] Error: {e}");
                return new ErrorResponse($"Error getting rendering stats: {e.Message}");
            }
        }
    }
}
