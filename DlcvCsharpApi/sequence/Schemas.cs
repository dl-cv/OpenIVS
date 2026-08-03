using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DLCV.SequenceGraph
{
    public class ResourceItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("config")]
        public JObject Config { get; set; } = new JObject();
    }

    public class ResourceRegistry
    {
        [JsonProperty("items")]
        public List<ResourceItem> Items { get; set; } = new List<ResourceItem>();
    }

    public class PortDef
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("color")]
        public string Color { get; set; }

        [JsonProperty("dataType")]
        public string DataType { get; set; }
    }

    public class NodePorts
    {
        [JsonProperty("in")]
        public List<PortDef> In { get; set; } = new List<PortDef>();

        [JsonProperty("out")]
        public List<PortDef> Out { get; set; } = new List<PortDef>();
    }

    public class SequenceNodeInstance
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; } = "";

        [JsonProperty("pos")]
        public JToken Pos { get; set; }

        [JsonProperty("ports")]
        public NodePorts Ports { get; set; } = new NodePorts();

        [JsonProperty("props")]
        public JObject Props { get; set; } = new JObject();
    }

    public class SequenceEdge
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("sourceHandle")]
        public string SourceHandle { get; set; }

        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("targetHandle")]
        public string TargetHandle { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }
    }

    public class SequenceGraphDocument
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("resources")]
        public ResourceRegistry Resources { get; set; } = new ResourceRegistry();

        [JsonProperty("nodes")]
        public List<SequenceNodeInstance> Nodes { get; set; } = new List<SequenceNodeInstance>();

        [JsonProperty("edges")]
        public List<SequenceEdge> Edges { get; set; } = new List<SequenceEdge>();

        [JsonProperty("settings")]
        public JObject Settings { get; set; } = new JObject();
    }
}
