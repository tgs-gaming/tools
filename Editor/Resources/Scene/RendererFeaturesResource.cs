using System;
using com.tgs.mcpforunity.editor.Helpers;
using com.tgs.mcpforunity.editor.Tools.Graphics;
using Newtonsoft.Json.Linq;

namespace com.tgs.mcpforunity.editor.Resources.Scene
{
    [McpForUnityResource("get_renderer_features")]
    public static class RendererFeaturesResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return RendererFeatureOps.ListFeatures(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[RendererFeaturesResource] Error: {e}");
                return new ErrorResponse($"Error listing renderer features: {e.Message}");
            }
        }
    }
}
