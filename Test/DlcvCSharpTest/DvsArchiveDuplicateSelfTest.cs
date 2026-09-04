using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using DlcvModules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static class DvsArchiveDuplicateSelfTest
    {
        public static int Run()
        {
            try
            {
                VerifyIdenticalModelDataIsReused();
                VerifyDifferentModelDataIsRejected();
                VerifyIdenticalPipelineDataIsReused();
                VerifyDifferentPipelineDataIsRejected();
                Console.WriteLine("DVS 同名模型内容检查通过");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("DVS 同名模型内容检查失败：" + ex.Message);
                return 1;
            }
        }

        private static void VerifyIdenticalModelDataIsReused()
        {
            byte[] modelData = Encoding.ASCII.GetBytes("same-model");
            byte[] archive = BuildArchive(
                new[] { "pipeline.json", "model.dvt", "./MODEL.DVT" },
                new[] { Encoding.UTF8.GetBytes("{\"nodes\":[]}"), modelData, modelData });
            IDictionary entries = ReadArchiveEntries(archive);
            if (entries.Count != 1)
                throw new InvalidOperationException("相同内容未复用为一份模型数据");
        }

        private static void VerifyDifferentModelDataIsRejected()
        {
            byte[] archive = BuildArchive(
                new[] { "pipeline.json", "model.dvt", "./MODEL.DVT" },
                new[]
                {
                    Encoding.UTF8.GetBytes("{\"nodes\":[]}"),
                    Encoding.ASCII.GetBytes("model-a"),
                    Encoding.ASCII.GetBytes("model-b"),
                });
            try
            {
                ReadArchiveEntries(archive);
            }
            catch (InvalidDataException ex)
            {
                if (ex.Message.Contains("同名但内容不同"))
                    return;
                throw;
            }
            throw new InvalidOperationException("同名但内容不同的模型未报错");
        }

        private static void VerifyIdenticalPipelineDataIsReused()
        {
            byte[] pipeline = Encoding.UTF8.GetBytes("{\"nodes\":[]}");
            byte[] archive = BuildArchive(
                new[] { "pipeline.json", "./PIPELINE.JSON", "model.dvt" },
                new[] { pipeline, pipeline, Encoding.ASCII.GetBytes("model") });
            IDictionary entries = ReadArchiveEntries(archive);
            if (entries.Count != 1)
                throw new InvalidOperationException("相同流程内容未正确加载");
        }

        private static void VerifyDifferentPipelineDataIsRejected()
        {
            byte[] archive = BuildArchive(
                new[] { "pipeline.json", "./PIPELINE.JSON" },
                new[]
                {
                    Encoding.UTF8.GetBytes("{\"nodes\":[]}"),
                    Encoding.UTF8.GetBytes("{\"nodes\":[{}]}"),
                });
            try
            {
                ReadArchiveEntries(archive);
            }
            catch (InvalidDataException ex)
            {
                if (ex.Message.Contains("同名但内容不同"))
                    return;
                throw;
            }
            throw new InvalidOperationException("同名但内容不同的流程文件未报错");
        }

        private static byte[] BuildArchive(string[] fileNames, byte[][] fileData)
        {
            if (fileNames.Length != fileData.Length)
                throw new ArgumentException("文件名与数据数量不一致");

            var fileSizes = new JArray();
            foreach (byte[] data in fileData)
                fileSizes.Add(data.Length);
            var header = new JObject
            {
                ["file_list"] = new JArray(fileNames),
                ["file_size"] = fileSizes,
            };

            using (var stream = new MemoryStream())
            {
                WriteBytes(stream, Encoding.ASCII.GetBytes("DV\n"));
                WriteBytes(stream, Encoding.UTF8.GetBytes(header.ToString(Formatting.None) + "\n"));
                foreach (byte[] data in fileData)
                    WriteBytes(stream, data);
                return stream.ToArray();
            }
        }

        private static IDictionary ReadArchiveEntries(byte[] archive)
        {
            MethodInfo readArchive = typeof(DvsModel).GetMethod(
                "ReadArchive",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (readArchive == null)
                throw new MissingMethodException(typeof(DvsModel).FullName, "ReadArchive");

            using (var stream = new MemoryStream(archive, false))
            {
                object[] arguments = { stream, null, null };
                try
                {
                    readArchive.Invoke(null, arguments);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
                return (IDictionary)arguments[2];
            }
        }

        private static void WriteBytes(Stream stream, byte[] data)
        {
            stream.Write(data, 0, data.Length);
        }
    }
}
