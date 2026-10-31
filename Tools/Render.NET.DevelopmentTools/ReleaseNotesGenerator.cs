using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Render.NET.DevelopmentTools
{
    public static partial class ReleaseNotesGenerator
    {
        public static async Task<string> GenerateAsync(string repository, string version, string commit, string packages, string workingDirectory)
        {
            string revision = await GitAsync(workingDirectory, "rev-parse", "--verify", $"{commit}^{{commit}}");
            string tags = await GitAsync(workingDirectory, "tag", "--merged", revision, "--sort=-version:refname");
            string? previous = tags.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(tag => VersionTag().IsMatch(tag) && tag.TrimStart('v') != version);
            string range = previous is null ? revision : $"{previous}..{revision}";
            string changes = await GitAsync(workingDirectory, "log", "--reverse", "--format=%H%x09%s", range);
            if (string.IsNullOrWhiteSpace(changes))
            {
                throw new InvalidOperationException("Refusing to publish release notes without any changes.");
            }
            string[] paths = Directory.GetFiles(packages, "*.nupkg").Order(StringComparer.Ordinal).ToArray();
            if (paths.Length == 0)
            {
                throw new InvalidOperationException("No NuGet packages found.");
            }
            StringBuilder lines = new($"## Render.NET {version}\n\n### Packages\n\n");
            foreach (string path in paths)
            {
                await using ZipArchive archive = await ZipFile.OpenReadAsync(path);
                ZipArchiveEntry nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
                await using Stream stream = await nuspec.OpenAsync();
                XDocument document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
                XElement metadata = document.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "metadata")
                    ?? throw new InvalidOperationException($"Package {path} has no metadata.");
                Dictionary<string, string> fields = metadata.Elements().ToDictionary(element => element.Name.LocalName, element => element.Value);
                string name = fields["id"];
                if (fields["version"] != version)
                {
                    throw new InvalidOperationException($"Package {name} does not match release version {version}.");
                }
                string notes = Whitespace().Replace(fields.GetValueOrDefault("releaseNotes", ""), " ").Trim();
                if (notes.Length == 0)
                {
                    throw new InvalidOperationException($"Package {name} has no release notes.");
                }
                lines.Append($"- [{name} {version}](https://www.nuget.org/packages/{name}/{version}): {notes}\n");
            }
            lines.Append("\n### Changes\n\n");
            foreach (string change in changes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = change.Split('\t', 2);
                lines.Append($"- {parts[1]} ([{parts[0][..7]}](https://github.com/{repository}/commit/{parts[0]}))\n");
            }
            string comparison = previous is null ? $"commits/{version}" : $"compare/{previous}...{version}";
            lines.Append($"\n**Full Changelog**: https://github.com/{repository}/{comparison}\n");
            return lines.ToString();
        }

        private static async Task<string> GitAsync(string directory, params string[] arguments)
        {
            ProcessResult result = await ProcessRunner.RunAsync(["git", .. arguments], directory);
            return result.Output.Trim();
        }

        [GeneratedRegex(@"^v?\d+\.\d+\.\d+$")]
        private static partial Regex VersionTag();
        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
