using System;
using System.Collections.Generic;
using System.Globalization;
using DlcvModules;
using Newtonsoft.Json.Linq;
using OpenCvSharp;

namespace DlcvCSharpTest
{
    internal static class AlgorithmModulesSelfTest
    {
        private const string SequenceType = "post_process/sequence_check";
        private const string LegacySequenceType = "post_process/wire_sequence_check";
        private const string Offset = "post_process/offset_distance";

        public static int Run()
        {
            new GraphExecutor(new List<Dictionary<string, object>>());
            int failures = 0;
            Action<string, Action> check = (name, test) =>
            {
                try { test(); Console.WriteLine("通过 " + name); }
                catch (Exception ex) { failures++; Console.WriteLine("失败 " + name + ": " + ex.Message); }
            };
            check("参考图像坐标排序、空输入与仅首张", TestReferenceImage);
            check("两个节点注册", () => { Require(ModuleRegistry.Get(SequenceType) != null, SequenceType + " 未注册"); Require(ModuleRegistry.Get(Offset) != null, Offset + " 未注册"); });
            check("旧流程类型兼容且输出新主标识与原因", TestLegacySequence);
            check("顺序水平竖直、重复及完整列表", TestSequence);
            check("中心排序、稳定同坐标与旋转框", TestCenters);
            check("四项旋转框距离小于阈值判 NG", () => TestFourValueRotatedDistance("<", "NG;距离：20，大于18"));
            check("四项旋转框距离大于阈值判 OK", () => TestFourValueRotatedDistance(">", "OK;距离：20，大于18"));
            check("四项旋转框按真实中心排序", TestFourValueRotatedSequence);
            check("独立角度有效性、哨兵值及非旋转框", TestIndependentAngles);
            check("各 local 独立逆变换、原图输出、不改输入", TestTransforms);
            check("不同原图身份、相同像素与索引不混合", TestOriginals);
            check("共享 wrapper 旧 origin 别名", TestSharedAlias);
            check("merge_results 共用图像两路结果", TestMerge);
            check("偏移两类 score top1、同分保留首项", TestTopScores);
            check("严格阈值、AND、原始距离及文化无关原因", TestRules);
            check("六位小数精确中点及大数中心", TestExactFormatting);
            check("OK/NG reason 文本与图像输出一致", TestReasons);
            check("空检测判定与普通检测合并、原因去重", TestDistanceStatusMerge);
            check("相同变换的空检测结果不复制其他图目标", TestMappedEmptyDistance);
            check("图片级距离扩展、默认构造与无效空值", TestDistanceExtraInfo);
            check("空输入、空检测及非 local", TestEmpty);
            check("无效类别分数框判 NG", TestInvalidDetections);
            check("无效属性、索引与变换显式拒绝", TestInvalidInputs);
            check("标准图像结果端口及 return_json", TestGraph);
            Console.WriteLine("算法节点自测失败数: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static ModuleIO Execute(string type, List<ModuleImage> images = null, JArray results = null, JObject properties = null)
        {
            Type implementation = ModuleRegistry.Get(type);
            Require(implementation != null, type + " 未注册");
            var module = (BaseModule)Activator.CreateInstance(implementation, 1, null,
                properties?.ToObject<Dictionary<string, object>>(), new DlcvModules.ExecutionContext());
            return module.Process(images, results);
        }

        private static Mat Image() { return new Mat(120, 160, MatType.CV_8UC1, Scalar.All(3)); }
        private static List<ModuleImage> Images(Mat image, int origin = 0)
        {
            return new List<ModuleImage> { new ModuleImage(null, image, new TransformationState(image.Width, image.Height), origin) };
        }
        private static JObject Det(string name, double x, double y = 10, double score = 0.9)
        {
            return new JObject { ["category_name"] = name, ["score"] = score, ["bbox"] = new JArray(x - 1, y - 1, 2, 2) };
        }
        private static JObject Entry(JArray dets, int index = 0, TransformationState state = null)
        {
            var entry = new JObject { ["type"] = "local", ["index"] = index, ["sample_results"] = dets };
            if (state != null) entry["transform"] = JObject.FromObject(state.ToDict());
            return entry;
        }
        private static JObject Sequence(params string[] names) { return new JObject { ["categories"] = new JArray(names) }; }
        private static string Outcome(ModuleIO output, int index = 0)
        {
            var entry = output.ResultList[index];
            Require(entry["ok"]?.Type == JTokenType.Boolean, "判定必须为布尔值");
            Require(entry["reason"]?.Type == JTokenType.String, "判定原因必须为字符串");
            Require(entry["sample_results"] is JArray detections && detections.Count == 0, "判定不生成检测目标");
            if (entry.Value<string>("originating_module") != Offset)
                return entry.Value<bool>("ok") ? "OK" : "NG";
            return (entry.Value<bool>("ok") ? "OK;" : "NG;") + entry.Value<string>("reason");
        }
        private static JObject Rule(string op, double value) { return new JObject { ["operator"] = op, ["value"] = value }; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Reject(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new InvalidOperationException("应拒绝非法输入");
        }

        private static void TestLegacySequence()
        {
            using (var image = Image())
            {
                foreach (bool empty in new[] { false, true })
                foreach (bool ok in new[] { true, false })
                {
                    var results = new JArray(Entry(empty ? new JArray() : new JArray(Det("蓝", 20), Det("红", 10))));
                    var properties = empty ? (ok ? Sequence() : Sequence("红")) : (ok ? Sequence("红", "蓝") : Sequence("蓝", "红"));
                    var output = Execute(LegacySequenceType, Images(image), results, properties);
                    var canonical = Execute(SequenceType, Images(image), results, properties);
                    string expected = empty ? (ok ? "（空）" : "红") : (ok ? "红 → 蓝" : "蓝 → 红");
                    string current = empty ? "（空）" : "红 → 蓝";
                    RequireReason(output, ok ? "OK" : "NG", "期望顺序：" + expected + "\n当前顺序：" + current);
                    Require(output.ResultList[0].Value<string>("originating_module") == SequenceType, "旧类型输出新主标识");
                    Require(JToken.DeepEquals(output.ResultList, canonical.ResultList), "新旧类型结果一致");
                    Require(ReferenceEquals(output.ImageList[0].OriginalImage, image), "旧类型输出原图");
                }
            }
        }

        private static void TestSequence()
        {
            using (var image = Image())
            {
                foreach (string direction in new[] { "horizontal", "vertical" })
                {
                    var properties = Sequence("红", "红", "蓝"); properties["direction"] = direction;
                    var dets = direction == "horizontal"
                        ? new JArray(Det("蓝", 80, 0), Det("红", 20, 100), Det("红", 50, 50))
                        : new JArray(Det("蓝", 0, 80), Det("红", 100, 20), Det("红", 50, 50));
                    Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(dets)), properties)) == "OK", direction);
                    foreach (var expected in new[] { Sequence("红", "蓝"), Sequence("蓝", "红", "红"), Sequence("红", "红", "蓝", "蓝"), Sequence() })
                    {
                        expected["direction"] = direction;
                        Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(dets)), expected)) == "NG", "完整列表不匹配");
                    }
                }
                Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(new JArray(Det(" A ", 10)))), Sequence("A"))) == "NG", "不得裁剪类别");
                Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(new JArray(Det("a", 10)))), Sequence("A"))) == "NG", "大小写敏感");
            }
        }

        private static void TestCenters()
        {
            using (var image = Image())
            {
                var wide = Det("B", 0); wide["bbox"] = new JArray(0, 0, 100, 20);
                var rotated = Det("C", 0); rotated["bbox"] = new JArray(20, 10, 4, 2, Math.PI / 4);
                Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(new JArray(wide, Det("A", 10), rotated))), Sequence("A", "C", "B"))) == "OK", "按中心而非左边缘");
                Require(Outcome(Execute(SequenceType, Images(image), new JArray(Entry(new JArray(Det("B", 10), Det("A", 10)))), Sequence("B", "A"))) == "OK", "同坐标稳定排序");
                foreach (JToken bbox in new JToken[] { new JArray(9, 9, 2, 2), new JArray(10, 10, 2, 2, 0.5),
                    new JObject { ["x1"] = 9, ["y1"] = 9, ["x2"] = 11, ["y2"] = 11 },
                    new JObject { ["cx"] = 10, ["cy"] = 10, ["w"] = 2, ["h"] = 2, ["theta"] = 0.5 } })
                {
                    var a = Det("A", 10); a["bbox"] = bbox;
                    Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(new JArray(a, Det("B", 13, 14)))))) == "OK;距离：5", "bbox 表示");
                }
            }
        }

        private static void TestFourValueRotatedDistance(string op, string expected)
        {
            using (var image = Image())
            {
                var a = Det("A", 0); a["bbox"] = new JArray(10, 20, 10, 2); a["with_angle"] = true; a["angle"] = 0;
                var b = Det("B", 0); b["bbox"] = new JArray(30, 20, 2, 2); b["with_angle"] = true; b["angle"] = 0;
                var properties = new JObject { ["rules"] = new JArray(Rule(op, 18)) };
                var output = Execute(Offset, Images(image), new JArray(Entry(new JArray(a, b))), properties);
                RequireReason(output, expected, "距离：20，大于18");
            }
        }

        private static void TestFourValueRotatedSequence()
        {
            using (var image = Image())
            {
                var a = Det("A", 0); a["bbox"] = new JArray(10, 20, 10, 2); a["with_angle"] = true; a["angle"] = 0;
                var b = Det("B", 0); b["bbox"] = new JArray(12, 20, 2, 2); b["with_angle"] = true; b["angle"] = 0;
                RequireReason(Execute(SequenceType, Images(image), new JArray(Entry(new JArray(b, a))), Sequence("A", "B")),
                    "OK", "期望顺序：A → B\n当前顺序：A → B");
            }
        }

        private static void TestIndependentAngles()
        {
            using (var image = Image())
            {
                var a = Det("A", 0); a["bbox"] = new JArray(10, 20, 10, 2);
                var b = Det("B", 0); b["bbox"] = new JArray(30, 20, 2, 2);
                var entries = new JArray(Entry(new JArray(a, b)));
                Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：16", "普通四项框仍为 XYWH");
                foreach (JToken flag in new JToken[] { new JValue(false), new JValue("true"), new JValue(1), JValue.CreateNull() })
                {
                    a["with_angle"] = flag; a["angle"] = JValue.CreateNull();
                    Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：16", "非布尔 true 不读取独立角度");
                }
                a["with_angle"] = true;
                foreach (JToken angle in new JToken[] { null, JValue.CreateNull(), new JValue(true), new JValue(double.NaN),
                    new JValue(double.PositiveInfinity), new JValue("bad") })
                {
                    if (angle == null) a.Remove("angle"); else a["angle"] = angle;
                    Require(Outcome(Execute(Offset, Images(image), entries)) == "NG;距离：NaN", "无效独立角度判 NG");
                    Reject(() => Execute(SequenceType, Images(image), entries));
                }
                foreach (double angle in new[] { -100.0, -99.0, -98.999, 0.0 })
                {
                    a["angle"] = angle;
                    Require(Outcome(Execute(Offset, Images(image), entries)) == (angle > -99 ? "OK;距离：21.023796" : "OK;距离：16"),
                        "独立角度大于 -99 才按旋转框中心");
                }
                a["bbox"] = new JArray(10, 20, 10, 2, 0); a["angle"] = JValue.CreateNull();
                b["bbox"] = new JArray(30, 20, 2, 2, 0);
                Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：20", "五项旋转框不读取独立角度");
            }
        }

        private static void TestTransforms()
        {
            using (var image = Image())
            {
                var first = new TransformationState(160, 120, affine2x3: new double[] { 2, 0, -20, 0, 2, -20 });
                var second = new TransformationState(160, 120, affine2x3: new double[] { 0, -1, 120, 1, 0, 0 });
                var wrappers = new List<ModuleImage> { new ModuleImage(null, image, new TransformationState(160, 120), 7), new ModuleImage(null, image, first, 8) };
                var entries = new JArray(Entry(new JArray(Det("B", 106, 13)), 1, second), Entry(new JArray(Det("A", 0, 0)), 0, first));
                JToken before = entries.DeepClone();
                foreach (string type in new[] { SequenceType, Offset })
                {
                    var output = Execute(type, wrappers, entries, type == SequenceType ? Sequence("A", "B") : null);
                    Require(Outcome(output) == (type == SequenceType ? "OK" : "OK;距离：5"), "每个结果独立变换");
                    Require(output.ImageList.Count == 1 && ReferenceEquals(output.ImageList[0].ImageObject, image) && ReferenceEquals(output.ImageList[0].OriginalImage, image), "输出原图");
                    var result = output.ResultList[0];
                    Require(result.Value<int>("origin_index") == 7 && result.Value<int>("index") == 0 && result.Value<bool>("ok"), "首个原图索引");
                    Require(result.Value<string>("reason") == (type == SequenceType ? "期望顺序：A → B\n当前顺序：A → B" : "距离：5") && result.Value<string>("originating_module") == type, "判定元数据");
                    Require(JToken.DeepEquals(result["transform"], JObject.FromObject(new TransformationState(160, 120).ToDict())), "输出恒等变换");
                    Require(((JArray)result["sample_results"]).Count == 0, "判定不生成全幅分类框");
                }
                Require(JToken.DeepEquals(entries, before) && wrappers[0].ImageObject == null && ReferenceEquals(wrappers[1].TransformState, first) && Cv2.Mean(image).Val0 == 3, "不得修改输入");
                Require(Outcome(Execute(Offset, wrappers, new JArray(Entry(new JArray(Det("A", 10), Det("B", 13, 14)))))) == "OK;距离：5", "空 transform 按原图坐标");
            }
        }

        private static void TestOriginals()
        {
            using (var one = Image()) using (var two = Image())
            {
                var wrappers = Images(one, 7); wrappers.AddRange(Images(two, 7));
                var alias = Entry(new JArray(Det("A", 10), Det("B", 16, 18)), 1); alias.Remove("index"); alias["image_index"] = 1;
                var entries = new JArray(Entry(new JArray(Det("A", 10), Det("B", 13, 14))), alias);
                var output = Execute(Offset, wrappers, entries);
                Require(output.ImageList.Count == 2 && Outcome(output) == "OK;距离：5" && Outcome(output, 1) == "OK;距离：10", "相同索引和像素也不能混合不同 Mat");
                Require(Outcome(Execute(SequenceType, wrappers, entries, Sequence("A", "B")), 1) == "OK", "顺序分组");
                var onlyOrigin = Entry(new JArray(Det("A", 10), Det("B", 13, 14))); onlyOrigin.Remove("index"); onlyOrigin["origin_index"] = 7;
                Require(Outcome(Execute(Offset, Images(one, 7), new JArray(onlyOrigin))) == "OK;距离：5", "origin 回退");
                Reject(() => Execute(Offset, wrappers, new JArray(onlyOrigin)));
            }
        }

        private static void TestSharedAlias()
        {
            using (var image = Image())
            {
                var shared = Images(image, 1)[0];
                var wrappers = new List<ModuleImage> { shared, shared };
                var a = Entry(new JArray(Det("A", 10))); a["origin_index"] = 0;
                var b = Entry(new JArray(Det("B", 13, 14)), 1); b["origin_index"] = 1;
                foreach (string type in new[] { SequenceType, Offset })
                    Require(Outcome(Execute(type, wrappers, new JArray(a, b), type == SequenceType ? Sequence("A", "B") : null)) == (type == SequenceType ? "OK" : "OK;距离：5"), "共享 wrapper 旧索引");
            }
        }

        private static void TestMerge()
        {
            using (var image = Image())
            {
                var shared = Images(image);
                var merge = new MergeResults(1);
                merge.ExtraInputsIn.Add(new ModuleChannel(shared, new JArray(Entry(new JArray(Det("B", 13, 14))))));
                var merged = merge.Process(shared, new JArray(Entry(new JArray(Det("A", 10)))));
                Require(merged.ImageList.Count == 2, "实际 merge 两路图像");
                Require(Outcome(Execute(Offset, merged.ImageList, merged.ResultList)) == "OK;距离：5", "merge 重编号后按原图身份归组");
            }
        }

        private static void TestTopScores()
        {
            using (var image = Image())
            {
                var entries = new JArray(Entry(new JArray(Det("A", 70, 70, 0.2), Det("A", 10, 10, 0.99), Det("A", 80, 80, 0.99))), Entry(new JArray(Det("B", 13, 14, 0.95), Det("B", 90, 90, 0.3))));
                Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：5", "top1 与同分首项");
                var invalidLow = Det("A", 0, 0, 0.1); invalidLow["bbox"] = JValue.CreateNull();
                ((JArray)entries[0]["sample_results"]).Add(invalidLow);
                Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：5", "只验证选中目标 bbox");
                Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(new JArray(Det("A", 10), Det("B", 10)))))) == "OK;距离：0", "同中心距离为零");
            }
        }

        private static void TestRules()
        {
            using (var image = Image())
            {
                var entries = new JArray(Entry(new JArray(Det("A", 10), Det("B", 13, 14))));
                foreach (string op in new[] { ">", "<" })
                    Require(Outcome(Execute(Offset, Images(image), entries, new JObject { ["rules"] = new JArray(Rule(op, 5)) })) == "NG;距离：5，等于5", "距离等于阈值时判 NG");
                Require(Outcome(Execute(Offset, Images(image), entries, new JObject { ["rules"] = new JArray(Rule(">", 4), Rule("<", 6)) })) == "OK;距离：5，大于4，小于6", "AND 成功");
                Require(Outcome(Execute(Offset, Images(image), entries, new JObject { ["rules"] = new JArray(Rule(">", 4), Rule("<", 5)) })) == "NG;距离：5，等于5", "失败只显示未通过规则");
                Require(Outcome(Execute(Offset, Images(image), entries, new JObject { ["rules"] = new JArray(Rule(">", 6), Rule("<", 4)) })) == "NG;距离：5，小于6，大于4", "失败关系按配置顺序");
                var rangeEntries = new JArray(Entry(new JArray(Det("A", 0, 0), Det("B", 45.115263, 0))));
                RequireReason(Execute(Offset, Images(image), rangeEntries,
                    new JObject { ["rules"] = new JArray(Rule(">", 0), Rule("<", 30)) }),
                    "NG;距离：45.115263，大于30", "距离：45.115263，大于30");
                RequireReason(Execute(Offset, Images(image), rangeEntries,
                    new JObject { ["rules"] = new JArray(Rule(">", 0), Rule("<", 50)) }),
                    "OK;距离：45.115263，大于0，小于50", "距离：45.115263，大于0，小于50");
                var culture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    var precise = new JArray(Entry(new JArray(Det("A", 0, 0), Det("B", 1.0000004, 0))));
                    Require(Outcome(Execute(Offset, Images(image), precise, new JObject { ["rules"] = new JArray(Rule(">", 1)) })) == "OK;距离：1，大于1", "比较未舍入距离");
                    Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(new JArray(Det("A", 0, 0), Det("B", 1, 1)))))) == "OK;距离：1.414214", "六位小数和小数点");
                }
                finally { CultureInfo.CurrentCulture = culture; }
            }
        }

        private static void TestExactFormatting()
        {
            using (var image = Image())
            {
                double midpoint = 1.0 / 128;
                double below = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(midpoint) - 1);
                double above = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(midpoint) + 1);
                double[] distances = { midpoint, below, above, 3.0 / 128, Math.Pow(2, 100) };
                string[] labels = { "OK;距离：0.007812", "OK;距离：0.007812", "OK;距离：0.007813", "OK;距离：0.023438", "OK;距离：1267650600228229401496703205376" };
                for (int i = 0; i < distances.Length; i++)
                {
                    var a = Det("A", 0); a["bbox"] = new JArray(0.0, 0.0, 2, 2, 0);
                    var b = Det("B", 0); b["bbox"] = new JArray(distances[i], 0.0, 2, 2, 0);
                    var entries = new JArray(Entry(new JArray(a, b)));
                    Require(Outcome(Execute(Offset, Images(image), entries)) == labels[i], "精确距离 " + distances[i].ToString("R", CultureInfo.InvariantCulture));
                    if (i == 0)
                    {
                        var properties = new JObject { ["rules"] = new JArray(Rule(">", 0.007812), Rule("<", 0.007813)) };
                        Require(Outcome(Execute(Offset, Images(image), entries, properties)) == labels[i] + "，大于0.007812，小于0.007813", "中点规则仍比较原值");
                        properties["rules"] = new JArray(Rule("<", midpoint));
                        Require(Outcome(Execute(Offset, Images(image), entries, properties)) == "NG;距离：0.007812，等于0.007812", "中点等阈值严格不通过");
                    }
                }
                // XYXY 对象中心先分别除二，避免两个有限端点相加溢出。
                var first = Det("A", 0); first["bbox"] = new JObject { ["x1"] = 1e308, ["y1"] = 0, ["x2"] = 1.1e308, ["y2"] = 2 };
                var second = Det("B", 0); second["bbox"] = new JObject { ["x1"] = 1.2e308, ["y1"] = 0, ["x2"] = 1.3e308, ["y2"] = 2 };
                var large = new JArray(Entry(new JArray(second, first)));
                Require(Outcome(Execute(SequenceType, Images(image), large, Sequence("A", "B"))) == "OK", "大数中心排序");
                var limits = new JObject { ["rules"] = new JArray(Rule(">", 1e307), Rule("<", 3e307)) };
                Require(Outcome(Execute(Offset, Images(image), large, limits)).StartsWith("OK;", StringComparison.Ordinal), "大数中心及距离不溢出");
            }
        }

        private static void TestReasons()
        {
            using (var image = Image())
            {
                var sequence = new JArray(Entry(new JArray(Det("蓝", 30), Det("红", 10), Det("绿", 20))));
                RequireReason(Execute(SequenceType, Images(image), sequence, Sequence("红", "绿", "蓝")), "OK", "期望顺序：红 → 绿 → 蓝\n当前顺序：红 → 绿 → 蓝");
                RequireReason(Execute(SequenceType, Images(image), sequence, Sequence("红", "蓝", "绿")), "NG", "期望顺序：红 → 蓝 → 绿\n当前顺序：红 → 绿 → 蓝");
                RequireReason(Execute(SequenceType, Images(image)), "OK", "期望顺序：（空）\n当前顺序：（空）");
                RequireReason(Execute(SequenceType, Images(image), null, Sequence("红")), "NG", "期望顺序：红\n当前顺序：（空）");
                RequireReason(Execute(SequenceType, Images(image), sequence), "NG", "期望顺序：（空）\n当前顺序：红 → 绿 → 蓝");
                var duplicate = new JArray(Entry(new JArray(Det(" 红 ", 10), Det(" 红 ", 20))));
                RequireReason(Execute(SequenceType, Images(image), duplicate, Sequence(" 红 ", " 红 ")), "OK", "期望顺序： 红  →  红 \n当前顺序： 红  →  红 ");
                // 使用精确中点检查距离与阈值的六位偶数舍入。
                var a = Det("A", 0); a["bbox"] = new JArray(0.0, 0.0, 2, 2, 0);
                var b = Det("B", 0); b["bbox"] = new JArray(1.0 / 128, 0.0, 2, 2, 0);
                var distance = new JArray(Entry(new JArray(a, b)));
                RequireReason(Execute(Offset, Images(image), distance), "OK;距离：0.007812", "距离：0.007812");
                var rules = new JObject { ["rules"] = new JArray(Rule("<", 1.0 / 128)) };
                RequireReason(Execute(Offset, Images(image), distance, rules), "NG;距离：0.007812，等于0.007812", "距离：0.007812，等于0.007812");
                RequireReason(Execute(Offset, Images(image)), "NG;距离：NaN", "距离：NaN");
            }
        }

        private static void RequireReason(ModuleIO output, string label, string reason)
        {
            bool ok = label.StartsWith("OK", StringComparison.Ordinal);
            var entry = output.ResultList[0];
            Require(Outcome(output) == label && entry.Value<bool>("ok") == ok, "reason 与判断一致");
            Require(entry["reason"]?.Type == JTokenType.String && entry.Value<string>("reason") == reason, "原图判定 reason 文本");
            var context = new DlcvModules.ExecutionContext();
            new ReturnJson(2, context: context).Process(output.ImageList, output.ResultList);
            var payload = context.Get<Dictionary<string, object>>("frontend_json");
            var last = JObject.FromObject(payload["last"]);
            Require(last.Value<string>("okng") == (ok ? "OK" : "NG") && last["by_image"][0].Value<bool>("ok") == ok, "图输出判断");
            Require(JToken.DeepEquals(last["by_image"][0]["reason"], entry["reason"]), "图输出 reason 类型与文本一致");
            Require(((JArray)last["by_image"][0]["results"]).Count == 0, "图输出保留空目标判定");
        }

        private static void TestEmpty()
        {
            foreach (string type in new[] { SequenceType, Offset }) Require(Execute(type).ResultList.Count == 0 && Execute(type).ImageList.Count == 0, "空输入");
            using (var image = Image())
            {
                Require(Outcome(Execute(SequenceType, Images(image))) == "OK", "空列表相等");
                Require(Outcome(Execute(SequenceType, Images(image), null, Sequence("A"))) == "NG", "缺检测");
                Require(Outcome(Execute(Offset, Images(image))) == "NG;距离：NaN", "空规则仍需两类");
                var entries = new JArray(new JObject { ["type"] = "aggregate" }, Entry(new JArray(Det("A", 10), Det("B", 13, 14))));
                Require(Outcome(Execute(Offset, Images(image), entries)) == "OK;距离：5", "忽略非 local");
            }
        }

        private static void TestDistanceStatusMerge()
        {
            using (var image = Image())
            foreach (bool statusFirst in new[] { true, false })
            foreach (bool useOrigin in new[] { true, false })
            {
                var detections = Entry(new JArray(Det("A", 10), Det("B", 13, 14)));
                var failed = Execute(Offset, Images(image), new JArray(detections),
                    new JObject { ["rules"] = new JArray(Rule("<", 5)) }).ResultList[0];
                var passed = Execute(Offset, Images(image), new JArray(detections)).ResultList[0];
                detections["extra_info"] = new JObject { ["source"] = "model" };
                var entries = statusFirst ? new JArray(failed, failed.DeepClone(), passed, detections)
                    : new JArray(detections, passed, failed, failed.DeepClone());
                foreach (JObject entry in entries)
                {
                    entry["origin_index"] = 0;
                    if (useOrigin) entry.Remove("index");
                }
                var context = new DlcvModules.ExecutionContext();
                new ReturnJson(2, context: context).Process(Images(image), entries);
                var payload = context.Get<Dictionary<string, object>>("frontend_json");
                var byImage = JObject.FromObject(payload["last"])["by_image"][0];
                Require(byImage.Value<bool?>("ok") == false, "普通检测不覆盖 NG，多个判定取 AND");
                var expectedReasons = statusFirst ? new JArray("距离：5，等于5", "距离：5")
                    : new JArray("距离：5", "距离：5，等于5");
                Require(JToken.DeepEquals(byImage["reason"], expectedReasons), "重复原因只保留一次");
                Require(byImage["extra_info"].Value<double>("distance") == 5 && byImage["extra_info"].Value<string>("source") == "model",
                    "合并图片扩展保留距离与其他字段");
                var results = (JArray)byImage["results"];
                Require(results.Count == 2 && results[0].Value<string>("category_name") == "A"
                    && results[1].Value<string>("category_name") == "B", "只保留真实检测目标");
            }
        }

        private static void TestMappedEmptyDistance()
        {
            using (var image = Image())
            foreach (bool useOrigin in new[] { true, false })
            {
                var state = new TransformationState(160, 120, affine2x3: new double[] { 1, 0, 0, 0, 1, 0 });
                var images = new List<ModuleImage> {
                    new ModuleImage(image, image, state, 0), new ModuleImage(image, image, state, 1)
                };
                var failed = Entry(new JArray(), 0, state);
                failed["ok"] = false;
                failed["reason"] = "距离：5";
                var entries = new JArray(failed, Entry(new JArray(Det("A", 10)), 1, state),
                    Entry(new JArray(Det("B", 13, 14)), 1, state));
                foreach (JObject entry in entries)
                {
                    entry["origin_index"] = entry["index"].DeepClone();
                    if (useOrigin) entry.Remove("index");
                }
                var context = new DlcvModules.ExecutionContext();
                new ReturnJson(2, context: context).Process(images, entries);
                var payload = context.Get<Dictionary<string, object>>("frontend_json");
                var byImage = (JArray)JObject.FromObject(payload["last"])["by_image"];
                Require(((JArray)byImage[0]["results"]).Count == 0, "明确空结果不按相同变换复制目标");
                Require(byImage[0].Value<bool?>("ok") == false &&
                    JToken.DeepEquals(byImage[0]["reason"], new JArray("距离：5")), "空检测仍保留判定和原因");
                var detections = (JArray)byImage[1]["results"];
                Require(byImage[1]["extra_info"]?["distance"] == null, "另一图不继承相同变换的距离");
                Require(detections.Count == 2 && detections[0].Value<string>("category_name") == "A"
                    && detections[1].Value<string>("category_name") == "B", "另一图只保留两个真实目标");
            }
        }

        private static void TestDistanceExtraInfo()
        {
            var objects = new List<dlcv_infer_csharp.Utils.CSharpObjectResult>();
            Require(new dlcv_infer_csharp.Utils.CSharpSampleResult(objects).ExtraInfo == null, "原单参数构造无默认扩展");
            Require(new dlcv_infer_csharp.Utils.CSharpSampleResult(objects, false, "测试").ExtraInfo == null, "原三参数构造无默认扩展");
            Require(new dlcv_infer_csharp.Utils.CSharpSampleResult().ExtraInfo == null, "默认结构无扩展");
            using (var image = Image())
            foreach (double? distance in new double?[] { 1.0000004, 0, null })
            {
                var detections = distance.HasValue ? new JArray(Det("A", 0, 0), Det("B", distance.Value, 0)) : new JArray();
                var entry = Entry(detections);
                entry["extra_info"] = new JObject { ["source"] = "sample", ["distance"] = -1, ["other"] = new JObject { ["value"] = 7 } };
                var output = Execute(Offset, Images(image), new JArray(entry));
                var extra = (JObject)output.ResultList[0]["extra_info"];
                Require(extra.Value<string>("source") == "sample" && extra["other"].Value<int>("value") == 7, "保留原有图片级扩展");
                if (distance.HasValue)
                    Require(extra["distance"].Type == JTokenType.Float && Math.Abs(extra.Value<double>("distance") - distance.Value) < 1e-12,
                        "距离为未舍入 double，零值有效");
                else Require(extra["distance"].Type == JTokenType.Null, "无效距离为 JSON null");
                Require(entry["extra_info"].Value<int>("distance") == -1, "输入扩展不变");
                var context = new DlcvModules.ExecutionContext();
                new ReturnJson(2, context: context).Process(output.ImageList, output.ResultList);
                var last = context.Get<Dictionary<string, object>>("frontend_json")["last"];
                Require(JToken.DeepEquals(JObject.FromObject(last)["by_image"][0]["extra_info"], extra), "空目标输出保留图片扩展");
                var sample = new dlcv_infer_csharp.Utils.CSharpSampleResult(objects, false, "测试", extra);
                var visual = dlcv_infer_csharp.Utils.ConvertToVisualizeFormat(new dlcv_infer_csharp.Utils.CSharpResult(
                    new List<dlcv_infer_csharp.Utils.CSharpSampleResult> { sample }));
                Require(JToken.DeepEquals(visual[0]["extra_info"], extra), "结构化转 JSON 保留图片扩展");
            }
            using (var image = Image())
            using (var second = image.Clone())
            using (var model = new AlgorithmFlowModel())
            {
                model.LoadNodes(JArray.Parse(@"[
                    {'id':1,'order':1,'type':'input/frontend_image','outputs':[{'type':'image_chan','links':[1]}]},
                    {'id':2,'order':2,'type':'post_process/offset_distance','inputs':[{'type':'image_chan','link':1}],
                     'outputs':[{'type':'image_chan','links':[2]},{'type':'result_chan','links':[3]}]},
                    {'id':3,'order':3,'type':'output/return_json','inputs':[{'type':'image_chan','link':2},{'type':'result_chan','link':3}]}
                ]"));
                var batch = model.InferBatch(new List<Mat> { image, second });
                Require(batch.SampleResults.Count == 2 && batch.SampleResults[0].ExtraInfo["distance"].Type == JTokenType.Null
                    && batch.SampleResults[1].ExtraInfo["distance"].Type == JTokenType.Null, "批量空目标距离分别保留 null");
                var json = (JObject)model.InferOneOutJson(image);
                Require(json["extra_info"]["distance"].Type == JTokenType.Null, "空目标 JSON 保留 null 距离");
            }
        }

        private static void TestInvalidDetections()
        {
            using (var image = Image())
            {
                foreach (var dets in new[] { new JArray(), new JArray(Det("A", 1)), new JArray(Det("A", 1), Det("A", 3)), new JArray(Det("A", 1), Det("B", 2), Det("C", 3)) })
                    Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(dets)))) == "NG;距离：NaN", "类别数必须为二");
                foreach (JToken category in new JToken[] { JValue.CreateNull(), new JValue(1), new JValue("") })
                {
                    var a = Det("A", 10); a["category_name"] = category;
                    Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(new JArray(a, Det("B", 13, 14)))))) == "NG;距离：NaN", "无效类别");
                }
                foreach (JToken bad in new JToken[] { JValue.CreateNull(), new JValue(true), new JValue(double.NaN), new JValue(double.PositiveInfinity), new JValue("bad") })
                {
                    var a = Det("A", 10); a["score"] = bad;
                    Require(Outcome(Execute(Offset, Images(image), new JArray(Entry(new JArray(a, Det("A", 10), Det("B", 13, 14)))))) == "NG;距离：NaN", "无效 score 不回退");
                }
                foreach (JToken bad in new JToken[] { JValue.CreateNull(), new JArray(), new JArray(1, 2, 0, 2), new JArray(1, 2, double.NaN, 2), new JArray(1, 2, 2, 2, double.NaN) })
                {
                    var a = Det("A", 10, 10, 1); a["bbox"] = bad;
                    var entries = new JArray(Entry(new JArray(a, Det("A", 10), Det("B", 13, 14))));
                    Require(Outcome(Execute(Offset, Images(image), entries)) == "NG;距离：NaN", "无效 top1 bbox");
                    Reject(() => Execute(SequenceType, Images(image), entries));
                }
            }
        }

        private static void TestInvalidInputs()
        {
            using (var image = Image())
            {
                foreach (JToken rules in new JToken[] { JValue.CreateNull(), new JObject(), new JValue("[]"), new JArray(new JObject()), new JArray(Rule(">=", 0)), new JArray(Rule(">", -1)), new JArray(Rule("<", double.NaN)), new JArray(new JObject { ["operator"] = ">", ["value"] = true }) })
                    Reject(() => Execute(Offset, Images(image), null, new JObject { ["rules"] = rules }));
                Reject(() => Execute(SequenceType, Images(image), null, new JObject { ["categories"] = "A" }));
                Reject(() => Execute(SequenceType, Images(image), null, new JObject { ["categories"] = new JArray(1) }));
                Reject(() => Execute(SequenceType, Images(image), null, new JObject { ["direction"] = "Horizontal" }));
                foreach (string type in new[] { SequenceType, Offset })
                {
                    var entry = Entry(new JArray(Det("A", 10), Det("B", 13, 14))); entry["image_index"] = 1;
                    Reject(() => Execute(type, Images(image), new JArray(entry)));
                    entry.Remove("image_index"); entry["origin_index"] = 8;
                    Reject(() => Execute(type, Images(image), new JArray(entry)));
                    foreach (double[] matrix in new[] { new double[6], new double[] { double.NaN, 0, 0, 0, 1, 0 } })
                        Reject(() => Execute(type, Images(image), new JArray(Entry(new JArray(), 0, new TransformationState(160, 120, affine2x3: matrix)))));
                    Reject(() => Execute(type, Images(image), new JArray(Entry(new JArray(), 0, new TransformationState(1, 1)))));
                }
            }
        }

        private static void TestReferenceImage()
        {
            using (var image = Image())
            foreach (bool vertical in new[] { false, true })
            {
                var props = Sequence("B", "A").ToObject<Dictionary<string, object>>();
                props["direction"] = vertical ? "vertical" : "horizontal";
                var module = new SequenceCheck(1, properties: props);
                var state = new TransformationState(160, 120, new[] { 30, 20, 100, 80 },
                    vertical ? new double[] { 0, 1, -20, -1, 0, 160 } : new double[] { -1, 0, 160, 0, 1, -20 });
                var reference = new ModuleImage(null, image, state);
                var entries = new JArray(Entry(new JArray(Det("A", 40))),
                    Entry(new JArray(Det("B", 10)), state: new TransformationState(160, 120,
                        affine2x3: new double[] { 1, 0, -100, 0, 1, 0 })));
                var before = entries.DeepClone();
                module.ExtraInputsIn.Add(new ModuleChannel(new List<ModuleImage> { reference, null }, null));
                var output = module.Process(Images(image), entries);
                Require(Outcome(output) == "OK", "参考图像应将 B 排在 A 前面");
                Require(output.ResultList[0].Value<string>("reason") == "期望顺序：B → A\n当前顺序：B → A", "参考顺序 reason");
                Require(ReferenceEquals(output.ImageList[0].OriginalImage, image), "仍输出原图");
                Require(JToken.DeepEquals(before, entries), "输入结果不变");
                module.ExtraInputsIn.Clear();
                module.ExtraInputsIn.Add(new ModuleChannel(null, null));
                module.Properties["categories"] = new JArray("A", "B");
                Require(Outcome(module.Process(Images(image), entries)) == "OK", "空参考输入沿用原图排序");
            }
        }

        private static void TestGraph()
        {
            using (var image = Image())
            {
                foreach (string type in new[] { SequenceType, LegacySequenceType, Offset })
                foreach (bool reference in type == Offset ? new[] { false } : new[] { false, true })
                foreach (bool ok in new[] { true, false })
                foreach (bool keepDetections in new[] { false, true })
                {
                    var context = new DlcvModules.ExecutionContext(); context.Set("frontend_image_mat", image);
                    var nodes = JArray.Parse(@"[
                        {'id':1,'type':'input/frontend_image','outputs':[{'type':'image_chan','links':[1]},{'type':'result_chan','links':[2]}]},
                        {'id':2,'type':'input/build_results','properties':{'category_name':'A','bbox_x':9,'bbox_y':9,'bbox_w':2,'bbox_h':2},'inputs':[{'type':'image_chan','link':1},{'type':'result_chan','link':2}],'outputs':[{'type':'image_chan','links':[3,5]},{'type':'result_chan','links':[4]}]},
                        {'id':3,'type':'input/build_results','properties':{'category_name':'B','bbox_x':12,'bbox_y':13,'bbox_w':2,'bbox_h':2},'inputs':[{'type':'image_chan','link':3},{'type':'result_chan','link':4}],'outputs':[{'type':'image_chan','links':[]},{'type':'result_chan','links':[6]}]},
                        {'id':4,'type':'post_process/merge_results','inputs':[{'type':'image_chan','link':5},{'type':'result_chan','link':4},{'type':'image_chan','link':5},{'type':'result_chan','link':6}],'outputs':[{'type':'image_chan','links':[7]},{'type':'result_chan','links':[8]}]},
                        {'id':5,'inputs':[{'type':'image_chan','link':7},{'type':'result_chan','link':8}],'outputs':[{'type':'image_chan','links':[9]},{'type':'result_chan','links':[10]}]},
                        {'id':6,'type':'output/return_json','inputs':[{'type':'image_chan','link':9},{'type':'result_chan','link':10}]}
                    ]");
                    nodes[4]["type"] = type;
                    nodes[4]["properties"] = type != Offset ? (ok ? Sequence("A", "B") : Sequence("B", "A"))
                        : new JObject { ["rules"] = ok ? new JArray() : new JArray(Rule("<", 5)) };
                    foreach (var node in nodes) node["order"] = node["id"].DeepClone();
                    if (reference)
                    {
                        ((JArray)nodes[0]["outputs"][0]["links"]).Add(11);
                        ((JArray)nodes[4]["inputs"]).Add(new JObject { ["name"] = "reference_image", ["type"] = "image_chan", ["link"] = 12 });
                        nodes[4]["properties"] = ok ? Sequence("B", "A") : Sequence("A", "B");
                        nodes.Add(JObject.Parse(@"{'id':7,'order':4,'type':'pre_process/image_flip',
                            'inputs':[{'type':'image_chan','link':11}], 'outputs':[{'type':'image_chan','links':[12]}]}"));
                    }
                    string reason = type != Offset ? (ok ? "期望顺序：A → B" : "期望顺序：B → A") + "\n当前顺序：A → B"
                        : (ok ? "距离：5" : "距离：5，等于5");
                    string label = (ok ? "OK" : "NG") + (type == Offset ? ";" + reason : "");
                    if (reference) reason = (ok ? "期望顺序：B → A" : "期望顺序：A → B") + "\n当前顺序：B → A";
                    var output = new GraphExecutor(nodes.ToObject<List<Dictionary<string, object>>>(), context).Run();
                    Require(Outcome(new ModuleIO((List<ModuleImage>)output[5]["image_list"], (JArray)output[5]["result_list"])) == label, "真实端口和共享图像支路");
                    var payload = context.Get<Dictionary<string, object>>("frontend_json");
                    Require(payload != null, "return_json 输出");
                    var last = JObject.FromObject(payload["last"]);
                    Require(last.Value<string>("okng") == (ok ? "OK" : "NG") && last["by_image"][0].Value<bool>("ok") == ok, "return_json 判定");
                    var inspection = ((JArray)output[5]["result_list"])[0];
                    Require(inspection.Value<string>("originating_module") == (type == Offset ? Offset : SequenceType), "图输出使用新主标识");
                    Require(inspection.Value<bool>("ok") == ok && inspection.Value<string>("reason") == reason, "节点 ok/reason");
                    Require(JToken.DeepEquals(last["by_image"][0]["reason"], inspection["reason"]), "return_json reason");
                    Require(((JArray)last["by_image"][0]["results"]).Count == 0, "return_json 不生成判定分类");
                    if (keepDetections)
                    {
                        ((JArray)nodes[3]["outputs"][0]["links"]).Add(13);
                        ((JArray)nodes[3]["outputs"][1]["links"]).Add(14);
                        nodes.Add(JObject.Parse(@"{'id':8,'order':8,'type':'output/return_json',
                            'inputs':[{'type':'image_chan','link':13},{'type':'result_chan','link':14}]}"));
                    }
                    using (var model = new AlgorithmFlowModel())
                    {
                        model.LoadNodes(nodes);
                        var structured = model.Infer(image);
                        Require(structured.SampleResults.Count == 1 && structured.SampleResults[0].Ok == ok,
                            "结构化结果保留显式判定");
                        Require(structured.SampleResults[0].Reason == reason, "结构化结果保留原因");
                        int expectedCount = keepDetections ? 2 : 0;
                        Require(structured.SampleResults[0].Results.Count == expectedCount, "结构化结果目标数量");
                        if (type == Offset)
                        {
                            Require(structured.SampleResults[0].ExtraInfo?.Value<double>("distance") == 5, "结构化图片距离");
                        }
                        var json = (JObject)model.InferOneOutJson(image);
                        Require(json.Value<bool?>("ok") == ok && JToken.DeepEquals(json["reason"], new JArray(reason)), "JSON 判定和原因");
                        Require(((JArray)json["result_list"]).Count == expectedCount, "JSON 目标数量");
                        if (type == Offset) Require(json["extra_info"].Value<double>("distance") == 5, "JSON 图片距离");
                        if (keepDetections)
                        {
                            var results = (JArray)json["result_list"];
                            Require(results[0].Value<string>("category_name") == "A" && results[1].Value<string>("category_name") == "B",
                                "真实检测与图片级判定分别保留");
                            Require(results[0]["extra_info"]?["distance"] == null && results[1]["extra_info"]?["distance"] == null,
                                "距离不写入真实目标扩展");
                        }
                        if (type == Offset && ok && !keepDetections)
                        {
                            nodes[2]["properties"]["bbox_x"] = 10.0000004;
                            nodes[2]["properties"]["bbox_y"] = 9;
                            model.LoadNodes(nodes);
                            var preciseJson = (JObject)model.InferOneOutJson(image);
                            Require(preciseJson["extra_info"]["distance"].Type == JTokenType.Float &&
                                Math.Abs(preciseJson["extra_info"].Value<double>("distance") - 1.0000004) < 1e-12, "完整 JSON 保留未舍入距离");
                            Require(Math.Abs(model.Infer(image).SampleResults[0].ExtraInfo.Value<double>("distance") - 1.0000004) < 1e-12,
                                "结构化单图保留未舍入距离");
                        }
                    }
                }
            }
        }

        private sealed class AlgorithmFlowModel : FlowGraphModel
        {
            internal void LoadNodes(JArray nodes)
            {
                var report = LoadFromRoot(new JObject { ["nodes"] = nodes.DeepClone() }, 0);
                Require(report.Value<int>("code") == 0, "合成流程加载");
            }
        }
    }
}
