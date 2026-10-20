using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PclDownloadPatcher
{
    internal static class SelfTests
    {
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Require(rejected, "Expected rejection: " + name);
        }

        internal static int Run(string reportPath)
        {
            string root = Path.Combine(Path.GetTempPath(), "pcl-patcher-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var log = new StringBuilder();
            try
            {
                // Exercise COPY, INSERT, backup, replacement, and hash validation.
                byte[] original = Encoding.UTF8.GetBytes("original PCL fixture");
                byte[] expected = Encoding.UTF8.GetBytes("original FIXED fixture");
                byte[] payload;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Encoding.ASCII.GetBytes("PCLFIX1\0"));
                    writer.Write(original.Length); writer.Write(expected.Length);
                    writer.Write(PatchEngine.Hash(original)); writer.Write(PatchEngine.Hash(expected));
                    writer.Write(3);
                    writer.Write((byte)0); writer.Write(0); writer.Write(9);
                    writer.Write((byte)1); writer.Write(5); writer.Write(Encoding.UTF8.GetBytes("FIXED"));
                    writer.Write((byte)0); writer.Write(12); writer.Write(original.Length - 12);
                    writer.Flush(); payload = stream.ToArray();
                }
                var recipe = PatchRecipe.Load(payload);
                Require(recipe.Build(original).SequenceEqual(expected), "Recipe reconstruction failed");
                log.AppendLine("PASS: recipe COPY/INSERT and complete output hash");

                string target = Path.Combine(root, "测试程序.exe");
                File.WriteAllBytes(target, original);
                var result = PatchEngine.Apply(target, recipe);
                Require(File.ReadAllBytes(target).SequenceEqual(expected), "Wrong patched file");
                Require(File.ReadAllBytes(result.BackupPath).SequenceEqual(original), "Backup is not original");
                Require(!result.AlreadyPatched, "First patch incorrectly skipped");
                Require(PatchEngine.Apply(target, recipe).AlreadyPatched, "Repeated patch was not detected");
                Require(Directory.GetFiles(root).Length == 2, "Repeated patch created files");
                log.AppendLine("PASS: Unicode path, atomic replacement, exact backup, idempotence");

                File.WriteAllBytes(target, original);
                byte[] sentinel = Encoding.UTF8.GetBytes("keep existing backup");
                File.WriteAllBytes(target + ".bak", sentinel);
                result = PatchEngine.Apply(target, recipe);
                Require(result.BackupPath != target + ".bak", "Existing backup overwritten");
                Require(File.ReadAllBytes(target + ".bak").SequenceEqual(sentinel), "Existing backup changed");
                Require(File.ReadAllBytes(result.BackupPath).SequenceEqual(original), "New backup is wrong");
                log.AppendLine("PASS: existing backup preservation");

                byte[] invalid = (byte[])original.Clone(); invalid[0] ^= 1;
                File.WriteAllBytes(target, invalid);
                int before = Directory.GetFiles(root).Length;
                Reject(() => PatchEngine.Apply(target, recipe), "unknown input");
                Require(File.ReadAllBytes(target).SequenceEqual(invalid), "Unsupported input changed");
                Require(Directory.GetFiles(root).Length == before, "Unsupported input created files");
                File.WriteAllBytes(target, original);
                File.SetAttributes(target, FileAttributes.ReadOnly);
                try { Reject(() => PatchEngine.Apply(target, recipe), "read-only target"); }
                finally { File.SetAttributes(target, FileAttributes.Normal); }
                Require(File.ReadAllBytes(target).SequenceEqual(original), "Read-only input changed");
                using (var guard = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
                    Reject(() => PatchEngine.Apply(target, recipe), "locked target");
                log.AppendLine("PASS: unsupported, read-only, and locked input protection");

                byte[] corrupt = (byte[])payload.Clone(); corrupt[corrupt.Length - 1] ^= 0x40;
                Reject(() => PatchRecipe.Load(corrupt).Build(original), "corrupt recipe");
                Reject(() => PatchRecipe.Load(payload.Take(50).ToArray()), "truncated recipe");
                Require(File.ReadAllBytes(target).SequenceEqual(original), "Failure changed target");
                Require(!Directory.GetFiles(root, ".pcl-fix-*.tmp").Any(), "Temporary files leaked");
                PatchRecipe.Embedded();
                using (var form = new MainForm(new string[0])) Require(form.Controls.Count > 0, "GUI construction failed");
                log.AppendLine("PASS: malformed data rejection, cleanup, embedded payload, Windows GUI construction");
                File.WriteAllText(reportPath, log.ToString(), Encoding.UTF8);
                return 0;
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL: " + ex);
                File.WriteAllText(reportPath, log.ToString(), Encoding.UTF8);
                return 1;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
