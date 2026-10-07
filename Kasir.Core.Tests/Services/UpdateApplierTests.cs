using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    // The staged new build replaces the installed files (port of the old Updater.exe).
    [TestFixture]
    public class UpdateApplierTests
    {
        private string _root, _source, _target;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "kasir-applier-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "install", UpdateApplier.StagingDir, "app");
            _target = Path.Combine(_root, "install");
            Directory.CreateDirectory(_source);

            // installed (old) version
            File.WriteAllText(Path.Combine(_target, "Kasir.Avalonia.exe"), "old exe");
            File.WriteAllText(Path.Combine(_target, "Kasir.Core.dll"), "old core");
            File.WriteAllText(Path.Combine(_target, "help.json"), "{\"MachinePassword\":\"keep\"}");
            Directory.CreateDirectory(Path.Combine(_target, "data"));
            File.WriteAllText(Path.Combine(_target, "data", "kasir.db"), "SALES DATA");

            // staged (new) version
            File.WriteAllText(Path.Combine(_source, "Kasir.Avalonia.exe"), "new exe");
            File.WriteAllText(Path.Combine(_source, "Kasir.Core.dll"), "new core");
            File.WriteAllText(Path.Combine(_source, "help.json"), "{\"public\":true}");
            File.WriteAllText(Path.Combine(_source, "version.txt"), "2.9.0");
            Directory.CreateDirectory(Path.Combine(_source, "Sql", "nested"));
            File.WriteAllText(Path.Combine(_source, "Sql", "nested", "a.sql"), "select 1;");
            Directory.CreateDirectory(Path.Combine(_source, "data"));
            File.WriteAllText(Path.Combine(_source, "data", "kasir.db"), "EMPTY");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private UpdateApplier.ApplyArgs Args() => new UpdateApplier.ApplyArgs
        {
            Source = _source, Target = _target, Pid = 12345, Exe = "Kasir.Avalonia.exe"
        };

        private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(parts));

        [Test]
        public void TryParseArgs_ReadsAllArguments()
        {
            var ok = UpdateApplier.TryParseArgs(new[]
            {
                "--apply-update", "--source", "S", "--target", "T", "--pid", "42", "--exe", "Kasir.Avalonia.exe"
            }, out var a);
            Assert.IsTrue(ok);
            Assert.AreEqual("S", a.Source);
            Assert.AreEqual("T", a.Target);
            Assert.AreEqual(42, a.Pid);
            Assert.AreEqual("Kasir.Avalonia.exe", a.Exe);
            Assert.IsFalse(UpdateApplier.TryParseArgs(new[] { "--apply-update", "--source", "S" }, out _));
            Assert.IsFalse(UpdateApplier.IsApplyInvocation(new[] { "--something" }));
        }

        [Test]
        public void Run_CopiesNewFiles_NeverTouchesDataOrExistingHelpJson()
        {
            string launched = null;
            int rc = UpdateApplier.Run(Args(), _ => true, p => launched = p);

            Assert.AreEqual(0, rc);
            Assert.AreEqual("new exe", Read(_target, "Kasir.Avalonia.exe"));
            Assert.AreEqual("new core", Read(_target, "Kasir.Core.dll"));
            Assert.AreEqual("select 1;", Read(_target, "Sql", "nested", "a.sql"), "nested folders are copied");
            Assert.AreEqual("SALES DATA", Read(_target, "data", "kasir.db"), "database must never be replaced");
            Assert.That(Read(_target, "help.json"), Does.Contain("keep"), "existing help.json kept");
            Assert.AreEqual("COPY_COMPLETE", Read(_target, UpdateApplier.StateFile));
            Assert.AreEqual("2.9.0", Read(_target, UpdateApplier.MarkerFile));
            Assert.AreEqual(Path.Combine(_target, "Kasir.Avalonia.exe"), launched);
            Assert.AreEqual("old core", Read(_target, UpdateApplier.BackupDir, "Kasir.Core.dll"), "replaced files are backed up");
        }

        [Test]
        public void Run_FreshInstallWithoutHelpJson_GetsPackagedHelpJson()
        {
            File.Delete(Path.Combine(_target, "help.json"));
            UpdateApplier.Run(Args(), _ => true, _ => { });
            Assert.That(Read(_target, "help.json"), Does.Contain("public"));
        }

        [Test]
        public void Run_ExistingHelpJson_GetsNewPublicKeys_KeepsInstalledValues()
        {
            File.WriteAllText(Path.Combine(_target, "help.json"),
                "{\"SupabaseUrl\":\"https://site.supabase.co\",\"RegisterId\":\"02\",\"MachinePassword\":\"keep\"}");
            File.WriteAllText(Path.Combine(_source, "help.json"),
                "{\"SupabaseUrl\":\"https://release.supabase.co\",\"RegisterId\":\"01\",\"DashboardUrl\":\"https://dash.example\"}");

            int rc = UpdateApplier.Run(Args(), _ => true, _ => { });

            Assert.AreEqual(0, rc);
            using var doc = JsonDocument.Parse(Read(_target, "help.json"));
            var root = doc.RootElement;
            Assert.AreEqual("https://dash.example", root.GetProperty("DashboardUrl").GetString(), "new public key added");
            Assert.AreEqual("https://site.supabase.co", root.GetProperty("SupabaseUrl").GetString(), "installed value wins");
            Assert.AreEqual("02", root.GetProperty("RegisterId").GetString(), "installed value wins");
            Assert.AreEqual("keep", root.GetProperty("MachinePassword").GetString(), "legacy login kept for unpaired registers");
            Assert.That(Read(_target, UpdateApplier.BackupDir, "help.json"), Does.Not.Contain("DashboardUrl"),
                "original help.json is backed up for rollback");
            Assert.IsFalse(File.Exists(Path.Combine(_target, "help.json.tmp")), "temp file cleaned up");
        }

        [Test]
        public void Run_MalformedInstalledHelpJson_LeftUntouched_UpdateStillSucceeds()
        {
            File.WriteAllText(Path.Combine(_target, "help.json"), "{ not json");
            File.WriteAllText(Path.Combine(_source, "help.json"), "{\"DashboardUrl\":\"https://dash.example\"}");

            int rc = UpdateApplier.Run(Args(), _ => true, _ => { });

            Assert.AreEqual(0, rc);
            Assert.AreEqual("{ not json", Read(_target, "help.json"));
            Assert.AreEqual("new core", Read(_target, "Kasir.Core.dll"));
            Assert.That(Read(_target, UpdateApplier.LogFile), Does.Contain("help.json"));
        }

        [Test]
        public void Run_MalformedPackagedHelpJson_InstalledLeftUntouched()
        {
            const string installed = "{\"SupabaseUrl\":\"https://site.supabase.co\"}";
            File.WriteAllText(Path.Combine(_target, "help.json"), installed);
            File.WriteAllText(Path.Combine(_source, "help.json"), "[1,2,3]");

            int rc = UpdateApplier.Run(Args(), _ => true, _ => { });

            Assert.AreEqual(0, rc);
            Assert.AreEqual(installed, Read(_target, "help.json"));
        }

        [Test]
        public void Run_CopyFails_RestoresOriginalHelpJson()
        {
            const string installed = "{\"SupabaseUrl\":\"https://site.supabase.co\"}";
            File.WriteAllText(Path.Combine(_target, "help.json"), installed);
            File.WriteAllText(Path.Combine(_source, "help.json"), "{\"DashboardUrl\":\"https://dash.example\"}");
            // version.txt is copied (alphabetically late) after help.json would be merged;
            // a directory in its place makes the copy throw.
            Directory.CreateDirectory(Path.Combine(_target, "version.txt"));

            int rc = UpdateApplier.Run(Args(), _ => true, _ => { });

            Assert.AreEqual(1, rc);
            Assert.AreEqual(installed, Read(_target, "help.json"), "rollback restores the pre-merge help.json");
        }

        [Test]
        public void MergeHelpJson_PackagedWithUtf8Bom_StillMerges()
        {
            // release.yml writes help.json via pwsh [Encoding]::UTF8, which emits a BOM.
            string installed = Path.Combine(_target, "help.json");
            File.WriteAllText(installed, "{\"SupabaseUrl\":\"https://site.supabase.co\"}");
            string packaged = Path.Combine(_source, "help.json");
            File.WriteAllText(packaged, "{\"DashboardUrl\":\"https://dash.example/?a=1&b=2\"}",
                new System.Text.UTF8Encoding(true));

            int added = UpdateApplier.MergeHelpJson(packaged, installed, _ => { });

            Assert.AreEqual(1, added);
            string text = File.ReadAllText(installed);
            Assert.That(text, Does.Contain("https://dash.example/?a=1&b=2"), "URL written unescaped");
            Assert.That(text, Does.Contain("https://site.supabase.co"));
        }

        [Test]
        public void MergeHelpJson_NothingNew_DoesNotRewriteFile()
        {
            string installed = Path.Combine(_target, "help.json");
            File.WriteAllText(installed, "{\"A\":\"1\",  \"B\":\"2\"}");
            File.WriteAllText(Path.Combine(_source, "help.json"), "{\"A\":\"x\"}");

            int added = UpdateApplier.MergeHelpJson(Path.Combine(_source, "help.json"), installed, _ => { });

            Assert.AreEqual(0, added);
            Assert.AreEqual("{\"A\":\"1\",  \"B\":\"2\"}", File.ReadAllText(installed));
        }

        [Test]
        public void Run_PosDoesNotExit_CancelsWithoutCopying()
        {
            int rc = UpdateApplier.Run(Args(), _ => false, _ => { });
            Assert.AreEqual(1, rc);
            Assert.AreEqual("old exe", Read(_target, "Kasir.Avalonia.exe"));
        }

        [Test]
        public void Run_CopyFails_RollsBackToOldFiles()
        {
            // A directory where a file must go makes File.Copy throw mid-update.
            Directory.CreateDirectory(Path.Combine(_target, "version.txt"));
            int rc = UpdateApplier.Run(Args(), _ => true, _ => { });

            Assert.AreEqual(1, rc);
            Assert.AreEqual("old exe", Read(_target, "Kasir.Avalonia.exe"));
            Assert.AreEqual("old core", Read(_target, "Kasir.Core.dll"));
            Assert.AreEqual("ROLLED_BACK", Read(_target, UpdateApplier.StateFile));
        }

        [Test]
        public void RecoverInterrupted_MidCopy_RestoresBackup_AndClearsStaging()
        {
            Directory.CreateDirectory(Path.Combine(_target, UpdateApplier.BackupDir));
            File.WriteAllText(Path.Combine(_target, UpdateApplier.BackupDir, "Kasir.Core.dll"), "old core");
            File.WriteAllText(Path.Combine(_target, "Kasir.Core.dll"), "half-written");
            File.WriteAllText(Path.Combine(_target, UpdateApplier.StateFile), "COPY_IN_PROGRESS");

            UpdateApplier.RecoverInterrupted(_target);

            Assert.AreEqual("old core", Read(_target, "Kasir.Core.dll"));
            Assert.IsFalse(Directory.Exists(Path.Combine(_target, UpdateApplier.StagingDir)));
        }

        [Test]
        public void RecoverInterrupted_AfterSuccess_ClearsStateAndStaging()
        {
            File.WriteAllText(Path.Combine(_target, UpdateApplier.StateFile), "COPY_COMPLETE");
            UpdateApplier.RecoverInterrupted(_target);
            Assert.IsFalse(File.Exists(Path.Combine(_target, UpdateApplier.StateFile)));
            Assert.IsFalse(Directory.Exists(Path.Combine(_target, UpdateApplier.StagingDir)));
        }
    }
}
