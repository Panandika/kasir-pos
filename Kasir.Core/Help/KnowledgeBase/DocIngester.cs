using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Kasir.Models;

namespace Kasir.Help.KnowledgeBase
{
    /// <summary>
    /// Parses markdown FAQ files into HelpFaq chunks.
    /// Splits on H2/H3 headings; H1 becomes the doc title carried into each chunk's tags.
    /// </summary>
    public class DocIngester
    {
        private static readonly Regex HeadingRe = new Regex(
            @"^(?<hashes>\#{1,3})\s+(?<text>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // Front-matter: --- ... --- block at top of file (optional)
        private static readonly Regex FrontMatterRe = new Regex(
            @"\A---\s*\n(?<body>[\s\S]*?)\n---\s*\n",
            RegexOptions.Compiled);

        private static readonly Regex TagsLineRe = new Regex(
            @"^tags:\s*\[(?<list>[^\]]*)\]",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public List<HelpFaq> ParseFile(string path)
        {
            string text = File.ReadAllText(path);
            string relativePath = Path.GetFileName(path);
            return Parse(text, relativePath);
        }

        public List<HelpFaq> Parse(string markdown, string docPath)
        {
            var chunks = new List<HelpFaq>();
            string globalTags = null;

            // Strip + capture front matter
            var fm = FrontMatterRe.Match(markdown);
            if (fm.Success)
            {
                var tagsMatch = TagsLineRe.Match(fm.Groups["body"].Value);
                if (tagsMatch.Success)
                {
                    globalTags = NormalizeTagList(tagsMatch.Groups["list"].Value);
                }
                markdown = markdown.Substring(fm.Length);
            }

            // Find headings and slice between them
            var matches = HeadingRe.Matches(markdown);
            if (matches.Count == 0)
            {
                // No headings — single chunk for whole file
                string content = markdown.Trim();
                if (content.Length == 0) return chunks;
                chunks.Add(new HelpFaq
                {
                    DocPath = docPath,
                    Anchor = null,
                    Title = Path.GetFileNameWithoutExtension(docPath),
                    Content = content,
                    Tags = globalTags
                });
                return chunks;
            }

            string currentDocTitle = null;

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                int level = m.Groups["hashes"].Value.Length;
                string headingText = m.Groups["text"].Value.Trim();

                if (level == 1)
                {
                    currentDocTitle = headingText;
                    continue;
                }

                // Body runs from end of this heading line to start of next H2/H3 (or EOF)
                int bodyStart = m.Index + m.Length;
                int bodyEnd = markdown.Length;
                for (int j = i + 1; j < matches.Count; j++)
                {
                    int nextLevel = matches[j].Groups["hashes"].Value.Length;
                    if (nextLevel <= level)
                    {
                        bodyEnd = matches[j].Index;
                        break;
                    }
                    // H3 inside H2 stays inside this chunk's body
                }

                string body = markdown.Substring(bodyStart, bodyEnd - bodyStart).Trim();
                if (body.Length == 0) continue;

                chunks.Add(new HelpFaq
                {
                    DocPath = docPath,
                    Anchor = Slugify(headingText),
                    Title = headingText,
                    Content = AppendDocTitle(body, currentDocTitle),
                    Tags = globalTags
                });
            }

            return chunks;
        }

        public List<HelpFaq> ParseDirectory(string dir)
        {
            var all = new List<HelpFaq>();
            foreach (string path in Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories))
            {
                all.AddRange(ParseFile(path));
            }
            return all;
        }

        private static string AppendDocTitle(string body, string docTitle)
        {
            if (string.IsNullOrEmpty(docTitle)) return body;
            // Prepend the doc title in italic so FTS5 still indexes it.
            return docTitle + "\n\n" + body;
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            // Collapse multiple hyphens
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Length == 0 ? null : slug;
        }

        private static string NormalizeTagList(string raw)
        {
            // input: "diskon, kasir, hardware"  →  "diskon,kasir,hardware"
            var parts = raw.Split(',');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().Trim('"', '\'');
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(p);
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
