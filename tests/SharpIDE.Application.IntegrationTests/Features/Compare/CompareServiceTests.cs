using SharpIDE.Application.Features.Compare;
using SharpIDE.Application.Features.Git;

namespace SharpIDE.Application.IntegrationTests.Features.Compare;

public class CompareServiceTests
{
    private readonly CompareService _compareService = new();

    [Fact]
    public async Task GetFileDiffView_TwoTextFiles_ProducesChangedRows()
    {
        using var workspace = new TemporaryDirectory();
        var leftPath = workspace.WriteFile("left.txt", """
            alpha
            bravo
            """);
        var rightPath = workspace.WriteFile("right.txt", """
            alpha
            charlie
            """);

        var diffView = await _compareService.GetFileDiffView(new FileCompareRequest
        {
            LeftAbsolutePath = leftPath,
            RightAbsolutePath = rightPath,
            LeftDisplayName = "Left",
            RightDisplayName = "Right",
            ComparisonKey = CompareService.BuildStableComparisonKey(leftPath, rightPath)
        }, TestContext.Current.CancellationToken);

        diffView.BaseLabel.Should().Be("Left");
        diffView.CurrentLabel.Should().Be("Right");
        diffView.Rows.Should().Contain(row =>
            row.Kind == GitDiffDisplayRowKind.ModifiedRight &&
            row.LeftText == "bravo" &&
            row.RightText == "charlie");
        diffView.CanEditCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task GetFileDiffView_MissingRightFile_UsesEmptyCurrentSide()
    {
        using var workspace = new TemporaryDirectory();
        var leftPath = workspace.WriteFile("left.txt", """
            alpha
            bravo
            """);
        var missingRightPath = Path.Combine(workspace.RootPath, "missing.txt");

        var diffView = await _compareService.GetFileDiffView(new FileCompareRequest
        {
            LeftAbsolutePath = leftPath,
            RightAbsolutePath = missingRightPath,
            LeftDisplayName = "Left",
            RightDisplayName = "Right",
            ComparisonKey = CompareService.BuildStableComparisonKey(leftPath, missingRightPath)
        }, TestContext.Current.CancellationToken);

        diffView.CurrentDisplayText.Should().BeEmpty();
        diffView.Rows.Where(row => row.ChunkBackgroundKind is not GitDiffChunkBackgroundKind.None)
            .Should()
            .OnlyContain(row => row.Kind == GitDiffDisplayRowKind.Removed);
    }

    [Fact]
    public async Task CompareDirectories_RecursiveComparison_ReportsOnlyChangedFiles()
    {
        using var workspace = new TemporaryDirectory();
        var leftRoot = workspace.CreateDirectory("left");
        var rightRoot = workspace.CreateDirectory("right");
        Directory.CreateDirectory(Path.Combine(leftRoot, "nested"));
        Directory.CreateDirectory(Path.Combine(rightRoot, "nested"));

        File.WriteAllText(Path.Combine(leftRoot, "same.txt"), "same");
        File.WriteAllText(Path.Combine(rightRoot, "same.txt"), "same");
        File.WriteAllText(Path.Combine(leftRoot, "nested", "modified.txt"), "before");
        File.WriteAllText(Path.Combine(rightRoot, "nested", "modified.txt"), "after");
        File.WriteAllText(Path.Combine(leftRoot, "only-left.txt"), "left");
        File.WriteAllText(Path.Combine(rightRoot, "only-right.txt"), "right");

        var result = await _compareService.CompareDirectories(new DirectoryCompareRequest
        {
            LeftDirectoryPath = leftRoot,
            RightDirectoryPath = rightRoot,
            LeftDisplayName = "left",
            RightDisplayName = "right"
        }, TestContext.Current.CancellationToken);

        result.Entries.Should().HaveCount(3);
        result.Entries.Should().Contain(entry => entry.RelativePath == "nested/modified.txt" && entry.EntryStatus == DirectoryCompareEntryStatus.Modified);
        result.Entries.Should().Contain(entry => entry.RelativePath == "only-left.txt" && entry.EntryStatus == DirectoryCompareEntryStatus.Removed);
        result.Entries.Should().Contain(entry => entry.RelativePath == "only-right.txt" && entry.EntryStatus == DirectoryCompareEntryStatus.Added);
        result.Entries.Should().NotContain(entry => entry.RelativePath == "same.txt");
    }

    [Fact]
    public async Task GetFileDiffView_BinaryFile_ProducesBinaryPreviewInsteadOfThrowing()
    {
        using var workspace = new TemporaryDirectory();
        var leftPath = Path.Combine(workspace.RootPath, "left.bin");
        var rightPath = Path.Combine(workspace.RootPath, "right.bin");
        await File.WriteAllBytesAsync(leftPath, [0, 1, 2, 3], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(rightPath, [0, 1, 9, 3], TestContext.Current.CancellationToken);

        var diffView = await _compareService.GetFileDiffView(new FileCompareRequest
        {
            LeftAbsolutePath = leftPath,
            RightAbsolutePath = rightPath,
            LeftDisplayName = "Left",
            RightDisplayName = "Right",
            ComparisonKey = CompareService.BuildStableComparisonKey(leftPath, rightPath)
        }, TestContext.Current.CancellationToken);

        diffView.BaseDisplayText.Should().Contain("Binary or unsupported text file.");
        diffView.CurrentDisplayText.Should().Contain("Binary or unsupported text file.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"sharpide-compare-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string WriteFile(string relativePath, string contents)
        {
            var absolutePath = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, contents);
            return absolutePath;
        }

        public string CreateDirectory(string relativePath)
        {
            var absolutePath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(absolutePath);
            return absolutePath;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
