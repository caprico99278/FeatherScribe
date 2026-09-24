using System.Buffers.Binary;
using System.Xml.Linq;

namespace FeatherScribe.Tests;

public class AppIconAssetTests
{
    private static readonly int[] RequiredIconSizes = [16, 20, 24, 32, 48, 64, 128, 256];

    [Fact]
    public void Ico_HasValidHeaderAndAllRequiredSizes()
    {
        var bytes = File.ReadAllBytes(IconsPath("FeatherScribe.ico"));

        Assert.True(bytes.Length > 6, "FeatherScribe.ico is too small.");
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0))); // reserved
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2))); // type: icon
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        Assert.True(bytes.Length >= 6 + 16 * count, "ICO directory is truncated.");

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);

            Assert.Equal(width, height);
            Assert.Equal(32, bitCount);
            Assert.True(length > 0 && offset >= 6 + 16 * count && offset + length <= bytes.Length,
                $"ICO entry {width}x{height} points outside the file.");
            sizes.Add(width);
        }

        Assert.All(RequiredIconSizes, size => Assert.Contains(size, sizes));
    }

    [Fact]
    public void MasterPng_Is512Square()
    {
        var bytes = File.ReadAllBytes(IconsPath("FeatherScribe-512.png"));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        Assert.Equal(512, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16))); // IHDR width
        Assert.Equal(512, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20))); // IHDR height
    }

    [Fact]
    public void AppProject_UsesIconAsApplicationIconAndResource_WithoutNuGetDependencies()
    {
        var project = XDocument.Load(Path.Combine(AppProjectDirectory(), "FeatherScribe.App.csproj"));

        Assert.Contains(
            project.Descendants("ApplicationIcon"),
            element => element.Value == @"Assets\Icons\FeatherScribe.ico");
        Assert.Contains(
            project.Descendants("Resource"),
            element => element.Attribute("Include")?.Value == @"Assets\Icons\FeatherScribe.ico");
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void MainWindow_UsesAppIcon()
    {
        var window = XDocument.Load(Path.Combine(AppProjectDirectory(), "MainWindow.xaml")).Root!;

        Assert.Equal("Assets/Icons/FeatherScribe.ico", window.Attribute("Icon")?.Value);
    }

    [Fact]
    public void TrayIcon_UsesAppIconInsteadOfSystemIcon()
    {
        var code = File.ReadAllText(Path.Combine(AppProjectDirectory(), "TrayIconService.cs"));

        Assert.DoesNotContain("SystemIcons.Application", code);
        Assert.Contains("pack://application:,,,/Assets/Icons/FeatherScribe.ico", code);
        Assert.Contains("_trayIcon.Dispose();", code);
    }

    private static string IconsPath(string fileName)
        => Path.Combine(AppProjectDirectory(), "Assets", "Icons", fileName);

    private static string AppProjectDirectory()
        => Path.Combine(FindRepoRoot(), "src", "FeatherScribe.App");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FeatherScribe.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FeatherScribe repository root.");
    }
}
