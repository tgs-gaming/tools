using System;
using Newtonsoft.Json;

namespace com.tgs.mcpforunity.editor.Models
{
    [Serializable]
    public class McpConfig
    {
        [JsonProperty("mcpServers")]
        public McpConfigServers mcpServers;
    }
}
