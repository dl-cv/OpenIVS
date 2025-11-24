using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace DlcvModules
{
	public static class MaskRleUtils
	{
		/// <summary>
		/// 将单通道掩膜 Mat 按行优先展开为数字 RLE：首段永远为 0，随后每段在 0/1 间切换。
		/// 仅存储 width, height, runs（像素值非零视为 1）。
		/// </summary>
		public static JObject MatToMaskInfo(Mat mask)
		{
			int width = 0;
			int height = 0;
			var runs = new List<int>();

			if (mask != null && !mask.Empty())
			{
				height = Math.Max(0, mask.Rows);
				width = Math.Max(0, mask.Cols);
				if (width > 0 && height > 0)
				{
					Mat src = mask;
					try { if (!mask.IsContinuous()) src = mask.Clone(); } catch { }
					int total = width * height;
					var buf = new byte[total];
					try { Marshal.Copy(src.Data, buf, 0, total); } catch { }

					int currentValue = 0; // 首段固定为 0
					int count = 0;
					for (int i = 0; i < total; i++)
					{
						int bit = buf[i] != 0 ? 1 : 0;
						if (bit == currentValue)
						{
							count += 1;
						}
						else
						{
							runs.Add(count);
							currentValue = bit;
							count = 1;
						}
					}
					runs.Add(count);
				}
			}

			var obj = new JObject
			{
				["width"] = width,
				["height"] = height,
				["runs"] = new JArray(runs)
			};
			return obj;
		}

		/// <summary>
		/// 从数字 RLE 信息还原单通道掩膜（CV_8UC1），1 段写入 255，0 段写入 0。
		/// 期望字段：width(int), height(int), runs(int[])；首段为 0。
		/// </summary>
		public static Mat MaskInfoToMat(JToken maskInfo)
		{
			if (maskInfo == null) return new Mat();
			int width = 0;
			int height = 0;
			JArray runsArr = null;
			try
			{
				var obj = maskInfo as JObject;
				if (obj == null) return new Mat();
				width = obj.Value<int?>("width") ?? 0;
				height = obj.Value<int?>("height") ?? 0;
				runsArr = obj["runs"] as JArray;
			}
			catch
			{
				return new Mat();
			}

			if (width <= 0 || height <= 0 || runsArr == null) return new Mat();
			var dst = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
			int total = width * height;

			// 直接对 Mat 的底层数据指针进行分段填充（参考 C++ memset 实现）
			IntPtr basePtr = dst.Data;
			int idx = 0;
			int value = 0; // 首段为 0
			for (int i = 0; i < runsArr.Count && idx < total; i++)
			{
				int count = 0;
				try { count = runsArr[i].Value<int>(); } catch { count = 0; }
				if (count <= 0)
				{
					value ^= 1;
					continue;
				}
				int writeCount = Math.Min(count, total - idx);
				if (value == 1 && writeCount > 0)
				{
					try
					{
						IntPtr curPtr = IntPtr.Add(basePtr, idx);
						memset(curPtr, 0xFF, (UIntPtr)writeCount);
					}
					catch { }
				}
				idx += writeCount;
				value ^= 1;
			}
			return dst;
		}

		[DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr memset(IntPtr dest, int c, UIntPtr count);
	}
}
