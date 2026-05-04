using System;
using System.Collections.Generic;
using System.Linq;
using com.tgs.mcpforunity.editor.Helpers;
using com.tgs.mcpforunity.editor.Tools.Cameras;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace com.tgs.mcpforunity.editor.Resources.Scene
{
    [McpForUnityResource("get_cameras")]
    public static class CamerasResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                return CameraControl.ListCameras(@params ?? new JObject());
            }
            catch (Exception e)
            {
                McpLog.Error($"[CamerasResource] Error listing cameras: {e}");
                return new ErrorResponse($"Error listing cameras: {e.Message}");
            }
        }
    }
}
