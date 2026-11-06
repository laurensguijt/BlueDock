using System;
using BlueDock.Services;
using Xunit;

namespace BlueDock.Tests
{
    /// <summary>
    /// The two decisions the updater makes before anything is downloaded: is this release
    /// actually newer, and is this URL somewhere we are willing to fetch an executable from.
    /// </summary>
    public class UpdateServiceTests
    {
        [Theory]
        [InlineData("v0.1.4", "0.1.4.0")]
        [InlineData("0.1.4", "0.1.4.0")]
        [InlineData("V2.0.0", "2.0.0.0")]
        [InlineData("v1.2", "1.2.0.0")]
        public void Reads_a_tag_with_or_without_its_v(string tag, string expected) =>
            Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(tag));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nightly")]
        [InlineData("v")]
        [InlineData(null)]
        public void Refuses_a_tag_that_is_not_a_version(string? tag) =>
            Assert.Null(UpdateService.ParseVersion(tag));

        [Fact]
        public void Versions_compare_the_way_releases_are_ordered()
        {
            var current = UpdateService.ParseVersion("v0.1.4")!;

            Assert.True(UpdateService.ParseVersion("v0.1.5") > current);
            Assert.True(UpdateService.ParseVersion("v0.2.0") > current);
            Assert.True(UpdateService.ParseVersion("v1.0.0") > current);
            // Equal or older must not be offered: re-publishing an old release should never
            // push users backwards.
            Assert.False(UpdateService.ParseVersion("v0.1.4") > current);
            Assert.False(UpdateService.ParseVersion("v0.1.3") > current);
        }

        [Theory]
        [InlineData("https://github.com/laurensguijt/BlueDock/releases/download/v1/x-setup.exe")]
        [InlineData("https://api.github.com/repos/laurensguijt/BlueDock/releases/latest")]
        [InlineData("https://objects.githubusercontent.com/a/b")]
        [InlineData("https://release-assets.githubusercontent.com/a/b")]
        public void Accepts_github_over_https(string url) => Assert.True(UpdateService.IsAllowed(url));

        [Theory]
        // Plain http would let anyone on the path hand us an executable.
        [InlineData("http://github.com/laurensguijt/BlueDock/x.exe")]
        [InlineData("https://example.com/x-setup.exe")]
        // The classic trap: the allowed name as a prefix of someone else's domain.
        [InlineData("https://github.com.evil.example/x-setup.exe")]
        [InlineData("https://evil.example/github.com/x-setup.exe")]
        [InlineData("file:///C:/x-setup.exe")]
        [InlineData("not a url")]
        [InlineData("")]
        public void Refuses_anything_else(string url) => Assert.False(UpdateService.IsAllowed(url));

        [Fact]
        public void The_running_version_is_known()
        {
            // Without this the updater cannot tell new from old, and would offer every release.
            Assert.True(UpdateService.CurrentVersion > new Version(0, 0, 0));
        }
    }
}
