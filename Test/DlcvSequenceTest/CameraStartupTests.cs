using System;
using System.Collections.Generic;
using OpenIVS2.Models;
using OpenIVS2.Services;

namespace DlcvSequenceTest
{
    internal static class CameraStartupTests
    {
        internal static void Run(Action<bool, string> assert)
        {
            var camera = new CameraSettings { Slot = "A", Name = "相机A", Enabled = true, Mode = "hik", CameraId = "missing" };
            var cameras = new[] { camera };
            var missing = CameraStartupCheck.FindMissing(cameras, () => new string[0]);
            assert(missing.Count == 1 && object.ReferenceEquals(missing[0], camera), "未发现设备时返回缺失相机，不抛异常");
            assert(camera.Enabled && camera.CameraId == "missing", "启动检查不删除或禁用原配置");
            missing = CameraStartupCheck.FindMissing(cameras, () => new[] { "other", "其他相机", "其他相机 (other)" });
            assert(missing.Count == 1, "存在其他相机时不错误替代指定相机");

            var ids = new[] { "serial-1", "camera-user", "camera-user (serial-1)" };
            foreach (var id in ids)
            {
                camera.CameraId = id.ToUpperInvariant();
                assert(CameraStartupCheck.FindMissing(cameras, () => ids).Count == 0,
                    "序列号、用户ID和显示名称按原匹配规则忽略大小写：" + id);
            }
            camera.CameraId = " ";
            assert(CameraStartupCheck.FindMissing(cameras, () => ids).Count == 0 &&
                CameraStartupCheck.FindMissing(cameras, () => new string[0]).Count == 1,
                "空ID保持选择第一台设备语义，无设备时仍提示");

            camera.Enabled = false;
            var virtualCamera = new CameraSettings { Slot = "B", Enabled = true, Mode = "virtual", CameraId = "missing" };
            assert(CameraStartupCheck.FindMissing(new[] { camera, virtualCamera }, () =>
            {
                throw new Exception("纯虚拟相机配置不应枚举硬件");
            }).Count == 0, "禁用相机及虚拟相机不访问硬件SDK");

            camera.Enabled = true;
            camera.CameraId = "missing";
            virtualCamera.Mode = "HIK";
            var calls = 0;
            missing = CameraStartupCheck.FindMissing(new[] { camera, virtualCamera }, () =>
            {
                calls++;
                return new string[0];
            });
            assert(missing.Count == 2 && calls == 1, "一次枚举报告全部缺失相机");
            assert(CameraStartupCheck.FindMissing(cameras, () => new[] { "missing" }).Count == 0,
                "相机恢复后重新检查通过，不缓存缺失状态");

            var error = new InvalidOperationException("模拟SDK枚举失败");
            Exception observed = null;
            try { CameraStartupCheck.FindMissing(cameras, () => { throw error; }); }
            catch (Exception ex) { observed = ex; }
            assert(object.ReferenceEquals(error, observed), "SDK枚举错误保留给原有启动异常处理，不伪装成无设备");
        }
    }
}
