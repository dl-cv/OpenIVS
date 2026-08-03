using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    public sealed class CameraCycleResult
    {
        public string Slot { get; set; }
        public bool Ok { get; set; }
        public string Reason { get; set; }
        public int DetectionCount { get; set; }
    }

    public sealed class CycleEvaluation
    {
        public bool Ok { get; set; }
        public List<CameraCycleResult> Cameras { get; set; }
    }

    public static class ResultEvaluator
    {
        public static CycleEvaluation Evaluate(AppSettings settings, Dictionary<string, object> results)
        {
            var evaluation = new CycleEvaluation { Ok = true, Cameras = new List<CameraCycleResult>() };
            foreach (var camera in settings.EnabledCameras())
            {
                var key = "display_" + camera.Slot.ToUpperInvariant();
                object raw;
                var item = results != null && results.TryGetValue(key, out raw)
                    ? ReadDisplayResult(camera.Slot, raw)
                    : new CameraCycleResult { Slot = camera.Slot, Ok = false, Reason = "未收到相机结果" };
                evaluation.Cameras.Add(item);
                if (!item.Ok) evaluation.Ok = false;
            }
            return evaluation;
        }

        private static CameraCycleResult ReadDisplayResult(string slot, object raw)
        {
            var result = new CameraCycleResult { Slot = slot, Ok = false, Reason = "结果格式无效" };
            var token = raw as JObject;
            if (token == null && raw != null)
            {
                try { token = JObject.FromObject(raw); } catch { }
            }
            if (token == null) return result;
            result.Ok = token["ok"] != null && token["ok"].ToObject<bool>();
            result.Reason = token["reason"] != null ? token["reason"].ToString() : "";
            result.DetectionCount = token["det_count"] != null ? token["det_count"].ToObject<int>() : 0;
            return result;
        }
    }
}
