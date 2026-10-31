using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Render.NET.DevelopmentTools;

namespace Render.NET.Tests
{
    public class ReleaseNotesTests
    {
        private string directory = null!;
        private string packages = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            directory = Path.Combine(Path.GetTempPath(), $"release-test-{Guid.NewGuid():N}");
            packages = Path.Combine(directory, "packages");
            Directory.CreateDirectory(packages);
            await GitAsync("init", "--quiet");
            await GitAsync("config", "user.name", "Release test");
            await GitAsync("config", "user.email", "release@example.invalid");
            await CommitAsync("Initial package");
            await PackageAsync();
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, recursive: true);

        [Test]
        public async Task DirectCommitAndPackageNotesAreIncluded()
        {
            await GitAsync("tag", "0.1.0");
            await CommitAsync("Render OBJ and 3MF");
            await GitAsync("tag", "0.2.0");
            string body = await NotesAsync();
            Assert.That(body, Does.Contain("Render OBJ and 3MF"));
            Assert.That(body, Does.Not.Contain("Initial package"));
            Assert.That(body, Does.Contain("Adds OBJ & 3MF support."));
            Assert.That(body, Does.Contain("compare/0.1.0...0.2.0"));
            Assert.That(body, Does.Contain("https://www.nuget.org/packages/Render.NET.Mesh/0.2.0"));
        }

        [Test]
        public async Task FirstReleaseIncludesInitialCommit()
        {
            string body = await NotesAsync();
            Assert.That(body, Does.Contain("Initial package"));
            Assert.That(body, Does.Contain("commits/0.2.0"));
        }

        [Test]
        public async Task UnmergedTagDoesNotHideChanges()
        {
            await GitAsync("tag", "0.1.0");
            string tree = await GitAsync("rev-parse", "HEAD^{tree}");
            string unrelated = await GitAsync("commit-tree", tree, "-p", "HEAD", "-m", "Unreleased work");
            await GitAsync("tag", "9.0.0", unrelated);
            await CommitAsync("Main fix");
            string body = await NotesAsync();
            Assert.That(body, Does.Contain("compare/0.1.0...0.2.0"));
            Assert.That(body, Does.Not.Contain("Unreleased work"));
        }

        [TestCase("0.2.0", "", "has no release notes")]
        [TestCase("0.1.0", "Package changes", "does not match release version")]
        public async Task InvalidPackageStopsPublication(string version, string notes, string error)
        {
            await PackageAsync(version, notes);
            await Assert.ThatAsync(async () => await NotesAsync(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains(error));
        }

        [Test]
        public async Task MissingPackagesStopPublication()
        {
            File.Delete(Path.Combine(packages, "Render.NET.Mesh.nupkg"));
            await Assert.ThatAsync(async () => await NotesAsync(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("No NuGet packages"));
        }

        [Test]
        public async Task NoChangesStopPublication()
        {
            await GitAsync("tag", "0.1.0");
            await Assert.ThatAsync(async () => await NotesAsync(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("without any changes"));
        }

        private Task<string> NotesAsync() => ReleaseNotesGenerator.GenerateAsync("bvdcode/Render.NET", "0.2.0", "HEAD", packages, directory);

        private async Task<string> GitAsync(params string[] arguments)
        {
            ProcessResult result = await ProcessRunner.RunAsync(["git", .. arguments], directory);
            return result.Output.Trim();
        }

        private async Task CommitAsync(string subject) => await GitAsync("commit", "--quiet", "--allow-empty", "-m", subject);

        private async Task PackageAsync(string version = "0.2.0", string notes = "Adds OBJ &amp; 3MF support.")
        {
            File.Delete(Path.Combine(packages, "Render.NET.Mesh.nupkg"));
            await using ZipArchive archive = await ZipFile.OpenAsync(Path.Combine(packages, "Render.NET.Mesh.nupkg"), ZipArchiveMode.Create);
            ZipArchiveEntry entry = archive.CreateEntry("Render.NET.Mesh.nuspec");
            await using Stream stream = await entry.OpenAsync();
            string xml = $"<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>Render.NET.Mesh</id><version>{version}</version><releaseNotes>{notes}</releaseNotes></metadata></package>";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(xml));
        }
    }
}
