using System.Collections.Generic;

namespace DLCV.SequenceGraph
{
    public class NodePropertyField
    {
        public string OptionsFrom { get; set; }
    }

    public static class NodeRegistry
    {
        private static readonly Dictionary<string, Dictionary<string, NodePropertyField>> Schemas =
            new Dictionary<string, Dictionary<string, NodePropertyField>>();

        static NodeRegistry()
        {
            Register("wait_camera_image", new Dictionary<string, NodePropertyField>
            {
                { "camera_id", new NodePropertyField { OptionsFrom = "resources.camera" } }
            });
            Register("camera_soft_trigger", new Dictionary<string, NodePropertyField>
            {
                { "camera_id", new NodePropertyField { OptionsFrom = "resources.camera" } }
            });
            Register("run_flow", new Dictionary<string, NodePropertyField>
            {
                { "flow_id", new NodePropertyField { OptionsFrom = "resources.flow" } }
            });
            Register("update_display", new Dictionary<string, NodePropertyField>
            {
                { "window_id", new NodePropertyField { OptionsFrom = "resources.window" } }
            });
            Register("modbus_tcp_input", new Dictionary<string, NodePropertyField>());
            Register("modbus_write_register", new Dictionary<string, NodePropertyField>());
            Register("wait_all", new Dictionary<string, NodePropertyField>());
            Register("wait_any", new Dictionary<string, NodePropertyField>());
            Register("store_json", new Dictionary<string, NodePropertyField>());
            Register("manual_trigger", new Dictionary<string, NodePropertyField>());
            Register("null_output", new Dictionary<string, NodePropertyField>());
        }

        public static void Register(string nodeType, Dictionary<string, NodePropertyField> schema)
        {
            Schemas[nodeType] = schema ?? new Dictionary<string, NodePropertyField>();
        }

        public static Dictionary<string, NodePropertyField> GetPropertySchema(string nodeType)
        {
            Dictionary<string, NodePropertyField> schema;
            return Schemas.TryGetValue(nodeType, out schema) ? schema : null;
        }

        public static bool IsKnownType(string nodeType)
        {
            return Schemas.ContainsKey(nodeType);
        }
    }
}

