using System;
using Newtonsoft.Json;

namespace com.tgs.mcpforunity.editor.Models
{
    [Serializable]
    public class McpConfigServers
    {
        [JsonProperty("unityMCP")]
        public McpConfigServer unityMCP;
    }
}
