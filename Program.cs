using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Plain Craft Launcher (PCL) myPCL 下载修复补丁")]
[assembly: AssemblyDescription("第三方一次性下载修复工具")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PclDownloadPatcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--self-test")
                return SelfTests.Run(args.Length > 1 ? args[1] : "patcher-tests.txt");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
            return 0;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox pathBox = new TextBox();
        private readonly Button browse = new Button();
        private readonly Button patch = new Button();
        private readonly Label status = new Label();
        private bool busy;

        internal MainForm(string[] args)
        {
            Text = "PCL myPCL 下载修复补丁";
            ClientSize = new Size(660, 265);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Icon = SystemIcons.Application;
            Controls.Add(new Label { Text = "PCL 下载与整合包安装修复", Font = new Font(Font.FontFamily, 15F, FontStyle.Bold), AutoSize = true, Location = new Point(20, 18) });
            Controls.Add(new Label { Text = "选择官方 EXE，修复一次，以后直接启动 PCL。", AutoSize = true, Location = new Point(22, 58) });
            Controls.Add(new Label { Text = "PCL 程序：", AutoSize = true, Location = new Point(22, 101) });
            pathBox.SetBounds(98, 97, 438, 27);
            browse.Text = "选择文件…"; browse.SetBounds(546, 96, 92, 29);
            browse.Click += (s, e) => Browse();
            patch.Text = "备份并修复"; patch.SetBounds(474, 140, 164, 38);
            patch.Click += async (s, e) => await Apply();
            status.Text = "支持已适配的官方 PCL 2.99.0.0；未知版本不会修改。";
            status.SetBounds(22, 190, 616, 26);
            Controls.Add(new Label { Text = "原文件会保存在同目录的 .bak 备份中。", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(22, 226) });
            Controls.AddRange(new Control[] { pathBox, browse, patch, status });
            FormClosing += (s, e) => { if (busy) e.Cancel = true; };
            if (args.Length > 0) pathBox.Text = args[0];
            else
            {
                string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe");
                if (File.Exists(candidate)) pathBox.Text = candidate;
            }
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog { Title = "选择需要修复的 PCL 程序", Filter = "可执行文件 (*.exe)|*.exe", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.FileName;
            }
        }

        private async Task Apply()
        {
            if (string.IsNullOrWhiteSpace(pathBox.Text)) { Browse(); if (string.IsNullOrWhiteSpace(pathBox.Text)) return; }
            string selected = pathBox.Text.Trim();
            busy = true; browse.Enabled = patch.Enabled = pathBox.Enabled = false;
            status.Text = "正在检查、备份并修复…";
            try
            {
                PatchResult result = await Task.Run(() => PatchEngine.Apply(selected, PatchRecipe.Embedded()));
                status.Text = result.AlreadyPatched ? "此文件已经修复，无需重复操作。" : "修复完成，以后直接运行所选 PCL 即可。";
                MessageBox.Show(this, result.AlreadyPatched ? status.Text : status.Text + Environment.NewLine + Environment.NewLine + "原文件备份：" + result.BackupPath,
                    "PCL myPCL 下载修复补丁", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                status.Text = "没有写入权限。";
                MessageBox.Show(this, "请把 PCL 移到有写入权限的文件夹后再试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                status.Text = "修复未完成。";
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { busy = false; browse.Enabled = patch.Enabled = pathBox.Enabled = true; }
        }
    }
}
