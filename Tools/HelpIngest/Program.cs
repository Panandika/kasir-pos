using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Help.KnowledgeBase;

namespace Kasir.Tools.HelpIngest
{
    /// <summary>
    /// Ingest local FAQ markdown into a register's SQLite database. Run at install
    /// or whenever the bundled FAQ is updated.
    ///
    /// Usage:
    ///   HelpIngest --db &lt;path/to/kasir.db&gt; --src &lt;path/to/Assets/Help&gt; [--clear]
    ///
    /// --remote flag is reserved for v2 (push to Supabase Edge Function); not implemented here.
    /// </summary>
    internal class Program
    {
        private static int Main(string[] args)
        {
            string dbPath = null;
            string srcDir = null;
            bool clear = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--db": dbPath = args[++i]; break;
                    case "--src": srcDir = args[++i]; break;
                    case "--clear": clear = true; break;
                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown arg: " + args[i]);
                        PrintUsage();
                        return 2;
                }
            }

            if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(srcDir))
            {
                PrintUsage();
                return 2;
            }
            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine("Database not found: " + dbPath);
                return 1;
            }
            if (!Directory.Exists(srcDir))
            {
                Console.Error.WriteLine("Source directory not found: " + srcDir);
                return 1;
            }

            using (var db = new SqliteConnection("Data Source=" + dbPath))
            {
                db.Open();

                // Run pending migrations so help_faq tables exist on older DBs.
                MigrationRunner.Run(db);

                var repo = new HelpFaqRepository(db);
                if (clear)
                {
                    Console.WriteLine("Clearing existing help_faq rows…");
                    repo.Clear();
                }

                var ingester = new DocIngester();
                var chunks = ingester.ParseDirectory(srcDir);
                Console.WriteLine("Found " + chunks.Count + " chunks across markdown files in " + srcDir);

                int upserts = 0;
                foreach (var c in chunks)
                {
                    repo.Upsert(c);
                    upserts++;
                }
                Console.WriteLine("Upserted " + upserts + " FAQ entries. Total rows: " + repo.Count());
            }

            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("HelpIngest --db <path/to/kasir.db> --src <path/to/Assets/Help> [--clear]");
            Console.WriteLine();
            Console.WriteLine("  --db     Path to register SQLite database");
            Console.WriteLine("  --src    Directory containing FAQ markdown files (recursive)");
            Console.WriteLine("  --clear  Wipe existing help_faq rows before ingest");
        }
    }
}
