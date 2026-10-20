using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PclDownloadPatcher
{
    internal sealed class PatchRecipe
    {
        internal int InputLength, OutputLength;
        internal byte[] InputHash, OutputHash, Payload;

        internal static PatchRecipe Load(byte[] payload)
        {
            using (var reader = new BinaryReader(new MemoryStream(payload)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "PCLFIX1\0")
                    throw new InvalidDataException("补丁数据格式错误。");
                var recipe = new PatchRecipe
                {
                    InputLength = reader.ReadInt32(), OutputLength = reader.ReadInt32(),
                    InputHash = reader.ReadBytes(32), OutputHash = reader.ReadBytes(32), Payload = payload
                };
                if (recipe.InputLength < 0 || recipe.OutputLength < 0 ||
                    recipe.InputLength > 16 * 1024 * 1024 || recipe.OutputLength > 16 * 1024 * 1024 ||
                    recipe.InputHash.Length != 32 || recipe.OutputHash.Length != 32)
                    throw new InvalidDataException("补丁数据不完整。");
                return recipe;
            }
        }

        internal static PatchRecipe Embedded()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("patch.bin"))
            using (var buffer = new MemoryStream())
            {
                if (stream == null) throw new InvalidDataException("缺少内置补丁。");
                stream.CopyTo(buffer);
                var recipe = Load(buffer.ToArray());
                if (PatchEngine.Hex(PatchEngine.Hash(recipe.Payload)) != "ce3d1ec634222b4d34c5b3d312215ca5b77c038a067cf1442e0bee87c3c93396")
                    throw new InvalidDataException("内置补丁校验失败。");
                return recipe;
            }
        }

        internal byte[] Build(byte[] original)
        {
            if (original.Length != InputLength || !PatchEngine.Hash(original).SequenceEqual(InputHash))
                throw new InvalidOperationException("此文件不是已适配的官方 PCL 2.99.0.0，未作修改。");
            using (var reader = new BinaryReader(new MemoryStream(Payload)))
            using (var output = new MemoryStream(OutputLength))
            {
                reader.BaseStream.Position = 80;
                int count = reader.ReadInt32();
                if (count < 1 || count > 100000) throw new InvalidDataException("补丁指令数量错误。");
                for (int i = 0; i < count; ++i)
                {
                    byte op = reader.ReadByte();
                    if (op == 0)
                    {
                        int offset = reader.ReadInt32(), length = reader.ReadInt32();
                        if (offset < 0 || length < 0 || (long)offset + length > original.Length || output.Length + length > OutputLength)
                            throw new InvalidDataException("补丁复制范围错误。");
                        output.Write(original, offset, length);
                    }
                    else if (op == 1)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0 || output.Length + length > OutputLength || reader.BaseStream.Length - reader.BaseStream.Position < length)
                            throw new InvalidDataException("补丁写入范围错误。");
                        byte[] bytes = reader.ReadBytes(length);
                        output.Write(bytes, 0, bytes.Length);
                    }
                    else throw new InvalidDataException("未知补丁指令。");
                }
                byte[] result = output.ToArray();
                if (reader.BaseStream.Position != reader.BaseStream.Length || result.Length != OutputLength ||
                    !PatchEngine.Hash(result).SequenceEqual(OutputHash))
                    throw new InvalidDataException("修复结果校验失败，未作修改。");
                return result;
            }
        }
    }

    internal sealed class PatchResult
    {
        internal bool AlreadyPatched;
        internal string BackupPath;
    }

    internal static class PatchEngine
    {
        internal static byte[] Hash(byte[] bytes) { using (var sha = SHA256.Create()) return sha.ComputeHash(bytes); }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }

        private static byte[] ReadTarget(string path, PatchRecipe recipe)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length != recipe.InputLength && stream.Length != recipe.OutputLength)
                    throw new InvalidOperationException("此文件不是已适配的官方 PCL 2.99.0.0，未作修改。");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }

        private static void CheckRunning(string path)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
            {
                using (process)
                {
                    bool running;
                    try { running = string.Equals(Path.GetFullPath(process.MainModule.FileName), path, StringComparison.OrdinalIgnoreCase); }
                    catch { running = true; }
                    if (running) throw new InvalidOperationException("请先完全退出所选 PCL（包括托盘图标），再进行修复。");
                }
            }
        }

        internal static PatchResult Apply(string selectedPath, PatchRecipe recipe)
        {
            string path = Path.GetFullPath(selectedPath);
            byte[] original = ReadTarget(path, recipe);
            if (original.Length == recipe.OutputLength && Hash(original).SequenceEqual(recipe.OutputHash))
                return new PatchResult { AlreadyPatched = true };
            byte[] patched = recipe.Build(original);
            CheckRunning(path);
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                throw new InvalidOperationException("所选文件是只读文件，请先取消只读属性。");

            string backup = path + ".bak";
            if (File.Exists(backup) || Directory.Exists(backup))
                backup += "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N").Substring(0, 8);
            string temp = Path.Combine(Path.GetDirectoryName(path), ".pcl-fix-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(patched, 0, patched.Length);
                    stream.Flush(true);
                }
                if (!Hash(File.ReadAllBytes(temp)).SequenceEqual(recipe.OutputHash))
                    throw new IOException("临时文件校验失败，原文件未修改。");

                // Hold a handle that permits atomic replacement but prevents writes.
                using (var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var current = new MemoryStream())
                {
                    guard.CopyTo(current);
                    if (!Hash(current.ToArray()).SequenceEqual(recipe.InputHash))
                        throw new IOException("所选文件在修复过程中发生变化，请重新选择文件。");
                    File.Replace(temp, path, backup);
                }
                if (!Hash(File.ReadAllBytes(path)).SequenceEqual(recipe.OutputHash))
                {
                    try { File.Replace(backup, path, null); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("修复后校验失败，自动恢复未完成。原文件备份位于：" + backup, restoreError);
                    }
                    throw new IOException("修复后校验失败，已自动恢复原文件。");
                }
                return new PatchResult { BackupPath = backup };
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
    }
}
