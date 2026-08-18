using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DLCV.SequenceGraph
{
    public static class GraphValidator
    {
        public static void Validate(SequenceGraphDocument graph)
        {
            var errors = new List<ValidationIssue>();
            errors.AddRange(ValidateNoCycle(graph));
            errors.AddRange(ValidateDataFlowUpstream(graph));
            errors.AddRange(ValidateResultKeyUnique(graph));
            errors.AddRange(ValidatePortTypeConsistency(graph));
            errors.AddRange(ValidateReferences(graph));
            errors.AddRange(ValidateEdgeNodesExist(graph));
            if (errors.Count > 0)
                throw new SequenceGraphValidationException(errors);
        }

        public static IList<ValidationIssue> CollectIssues(SequenceGraphDocument graph)
        {
            var errors = new List<ValidationIssue>();
            errors.AddRange(ValidateNoCycle(graph));
            errors.AddRange(ValidateDataFlowUpstream(graph));
            errors.AddRange(ValidateResultKeyUnique(graph));
            errors.AddRange(ValidatePortTypeConsistency(graph));
            errors.AddRange(ValidateReferences(graph));
            errors.AddRange(ValidateEdgeNodesExist(graph));
            return errors;
        }

        private static string ParseOptionsFromType(string optionsFrom)
        {
            if (string.IsNullOrEmpty(optionsFrom) || !optionsFrom.StartsWith("resources."))
                return null;
            return optionsFrom.Substring("resources.".Length);
        }

        private static IList<ValidationIssue> ValidateReferences(SequenceGraphDocument graph)
        {
            var issues = new List<ValidationIssue>();
            var valid = new Dictionary<string, HashSet<string>>();
            foreach (var item in graph.Resources.Items)
            {
                if (!valid.ContainsKey(item.Type))
                    valid[item.Type] = new HashSet<string>();
                valid[item.Type].Add(item.Id);
            }

            foreach (var node in graph.Nodes)
            {
                var schema = NodeRegistry.GetPropertySchema(node.Type);
                if (schema == null)
                    continue;
                foreach (var kv in schema)
                {
                    var cat = ParseOptionsFromType(kv.Value.OptionsFrom);
                    if (cat == null)
                        continue;
                    var propVal = GetPropString(node.Props, kv.Key);
                    if (string.IsNullOrEmpty(propVal))
                        continue;
                    HashSet<string> ids;
                    if (!valid.TryGetValue(cat, out ids) || !ids.Contains(propVal))
                    {
                        issues.Add(new ValidationIssue(
                            "nodes[" + node.Id + "].props." + kv.Key,
                            "ref",
                            "??? " + cat + " ???: " + propVal));
                    }
                }
            }
            return issues;
        }

        private static string GetPropString(JObject props, string key)
        {
            if (props == null)
                return null;
            JToken token;
            if (!props.TryGetValue(key, out token) || token == null || token.Type == JTokenType.Null)
                return null;
            return token.ToString();
        }

        private static IList<ValidationIssue> ValidateNoCycle(SequenceGraphDocument graph)
        {
            var adj = new Dictionary<string, List<string>>();
            foreach (var edge in graph.Edges)
            {
                if (edge.Type != "control")
                    continue;
                List<string> list;
                if (!adj.TryGetValue(edge.Source, out list))
                {
                    list = new List<string>();
                    adj[edge.Source] = list;
                }
                list.Add(edge.Target);
            }

            const int White = 0, Gray = 1, Black = 2;
            var color = graph.Nodes.ToDictionary(n => n.Id, n => White);
            var path = new List<string>();
            var errors = new List<ValidationIssue>();

            System.Action<string> dfs = null;
            dfs = nodeId =>
            {
                color[nodeId] = Gray;
                path.Add(nodeId);
                List<string> nexts;
                if (adj.TryGetValue(nodeId, out nexts))
                {
                    foreach (var nxt in nexts)
                    {
                        int c;
                        if (!color.TryGetValue(nxt, out c))
                            continue;
                        if (c == Gray)
                        {
                            var cycleStart = path.IndexOf(nxt);
                            var cycle = string.Join(" ? ", path.Skip(cycleStart).Concat(new[] { nxt }));
                            errors.Add(new ValidationIssue(nodeId, "control_flow", "??????: " + cycle));
                        }
                        else if (c == White)
                        {
                            dfs(nxt);
                        }
                    }
                }
                path.RemoveAt(path.Count - 1);
                color[nodeId] = Black;
            };

            foreach (var n in graph.Nodes)
            {
                if (color[n.Id] == White)
                    dfs(n.Id);
            }
            return errors;
        }

        private static IList<ValidationIssue> ValidateDataFlowUpstream(SequenceGraphDocument graph)
        {
            var adj = new Dictionary<string, List<string>>();
            foreach (var edge in graph.Edges)
            {
                if (edge.Type != "control")
                    continue;
                List<string> list;
                if (!adj.TryGetValue(edge.Source, out list))
                {
                    list = new List<string>();
                    adj[edge.Source] = list;
                }
                list.Add(edge.Target);
            }

            var reachable = new Dictionary<string, HashSet<string>>();
            var visiting = new HashSet<string>();

            System.Func<string, HashSet<string>> dfs = null;
            dfs = nodeId =>
            {
                HashSet<string> cached;
                if (reachable.TryGetValue(nodeId, out cached))
                    return cached;
                if (visiting.Contains(nodeId))
                    return new HashSet<string>();
                visiting.Add(nodeId);
                var downstream = new HashSet<string> { nodeId };
                List<string> nexts;
                if (adj.TryGetValue(nodeId, out nexts))
                {
                    foreach (var nxt in nexts)
                        downstream.UnionWith(dfs(nxt));
                }
                visiting.Remove(nodeId);
                reachable[nodeId] = downstream;
                return downstream;
            };

            foreach (var n in graph.Nodes)
                dfs(n.Id);

            var errors = new List<ValidationIssue>();
            foreach (var edge in graph.Edges)
            {
                if (edge.Type != "data")
                    continue;
                HashSet<string> down;
                if (!reachable.TryGetValue(edge.Source, out down) || !down.Contains(edge.Target))
                {
                    errors.Add(new ValidationIssue(edge.Target, "data_flow",
                        "??? " + edge.Source + ":" + edge.SourceHandle + " ?????? " +
                        edge.Target + ":" + edge.TargetHandle + " ?????????????????"));
                }
            }
            return errors;
        }

                private static IList<ValidationIssue> ValidateResultKeyUnique(SequenceGraphDocument graph)
        {
            var seen = new Dictionary<string, string>();
            var errors = new List<ValidationIssue>();
            foreach (var node in graph.Nodes)
            {
                var key = node.Id;
                if (node.Type != "store_json")
                {
                    var propKey = GetPropString(node.Props, "result_key");
                    if (!string.IsNullOrEmpty(propKey))
                        key = propKey;
                }
                string existing;
                if (seen.TryGetValue(key, out existing))
                {
                    errors.Add(new ValidationIssue(node.Id, "result_key",
                        "result_key '" + key + "' duplicates node " + existing));
                }
                else
                {
                    seen[key] = node.Id;
                }
            }
            return errors;
        }

private static IList<ValidationIssue> ValidatePortTypeConsistency(SequenceGraphDocument graph)
        {
            var errors = new List<ValidationIssue>();
            var nodeMap = graph.Nodes.ToDictionary(n => n.Id);
            foreach (var edge in graph.Edges)
            {
                SequenceNodeInstance src, tgt;
                if (!nodeMap.TryGetValue(edge.Source, out src) || !nodeMap.TryGetValue(edge.Target, out tgt))
                    continue;
                var srcType = GetPortType(src, edge.SourceHandle);
                var tgtType = GetPortType(tgt, edge.TargetHandle);
                if (!string.IsNullOrEmpty(srcType) && !string.IsNullOrEmpty(tgtType) && srcType != tgtType)
                {
                    errors.Add(new ValidationIssue(edge.Target, "port_type",
                        "???????: " + edge.Source + ":" + edge.SourceHandle + "(" + srcType + ") ? " +
                        edge.Target + ":" + edge.TargetHandle + "(" + tgtType + ")"));
                }
            }
            return errors;
        }

        private static string GetPortType(SequenceNodeInstance node, string portId)
        {
            if (node.Ports == null)
                return null;
            if (node.Ports.In != null)
            {
                foreach (var p in node.Ports.In)
                    if (p.Id == portId) return p.Type;
            }
            if (node.Ports.Out != null)
            {
                foreach (var p in node.Ports.Out)
                    if (p.Id == portId) return p.Type;
            }
            return null;
        }

        private static IList<ValidationIssue> ValidateEdgeNodesExist(SequenceGraphDocument graph)
        {
            var nodeIds = new HashSet<string>(graph.Nodes.Select(n => n.Id));
            var errors = new List<ValidationIssue>();
            foreach (var edge in graph.Edges)
            {
                if (!nodeIds.Contains(edge.Source))
                    errors.Add(new ValidationIssue(edge.Source, "edge", "? " + edge.Id + ": source ???"));
                if (!nodeIds.Contains(edge.Target))
                    errors.Add(new ValidationIssue(edge.Target, "edge", "? " + edge.Id + ": target ???"));
            }
            return errors;
        }
    }
}

