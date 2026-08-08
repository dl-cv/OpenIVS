using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DLCV.SequenceGraph
{
    public static class SequenceGraphLoader
    {
        public static SequenceGraphDocument FromJson(string json)
        {
            return JsonConvert.DeserializeObject<SequenceGraphDocument>(json);
        }

        public static SequenceGraphDocument FromFile(string path)
        {
            var json = File.ReadAllText(path);
            return FromJson(json);
        }

        public static string ToJson(SequenceGraphDocument graph, bool indented = true)
        {
            return JsonConvert.SerializeObject(graph, indented ? Formatting.Indented : Formatting.None);
        }
    }
}

