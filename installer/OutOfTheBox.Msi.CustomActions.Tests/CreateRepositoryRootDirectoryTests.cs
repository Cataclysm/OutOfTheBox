// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System;
using System.IO;
using Xunit;

namespace OutOfTheBox.Msi.CustomActions.Tests
{
    public class CreateRepositoryRootDirectoryTests
    {
        [Fact]
        public void EnsureExists_creates_a_missing_directory()
        {
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));

            try
            {
                CreateRepositoryRootDirectoryAction.EnsureExists(path);

                Assert.True(Directory.Exists(path));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }

        [Fact]
        public void EnsureExists_does_not_touch_an_existing_directorys_contents()
        {
            var path = Path.Combine(Path.GetTempPath(), "OutOfTheBox-Test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var markerFile = Path.Combine(path, "marker.txt");
            File.WriteAllText(markerFile, "preserved");

            try
            {
                CreateRepositoryRootDirectoryAction.EnsureExists(path);

                Assert.True(File.Exists(markerFile));
                Assert.Equal("preserved", File.ReadAllText(markerFile));
            }
            finally
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
