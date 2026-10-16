using System;
using System.Collections.Generic;
using System.Linq;
using OpenIVS2.Models;

namespace OpenIVS2.Services
{
    internal static class CameraStartupCheck
    {
        internal static List<CameraSettings> FindMissing(IEnumerable<CameraSettings> cameras,
            Func<IEnumerable<string>> enumerateCameraIds)
        {
            var hardwareCameras = cameras.Where(x => x.Enabled &&
                string.Equals(x.Mode, "hik", StringComparison.OrdinalIgnoreCase)).ToList();
            if (hardwareCameras.Count == 0) return new List<CameraSettings>();

            var availableIds = new HashSet<string>(enumerateCameraIds(), StringComparer.OrdinalIgnoreCase);
            return hardwareCameras.Where(x => string.IsNullOrWhiteSpace(x.CameraId)
                ? availableIds.Count == 0
                : !availableIds.Contains(x.CameraId)).ToList();
        }
    }
}
