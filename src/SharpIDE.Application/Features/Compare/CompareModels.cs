namespace SharpIDE.Application.Features.Compare;

public sealed class FileCompareRequest
{
    public required string LeftAbsolutePath { get; init; }
    public required string RightAbsolutePath { get; init; }
    public required string LeftDisplayName { get; init; }
    public required string RightDisplayName { get; init; }
    public required string ComparisonKey { get; init; }
}

public sealed class DirectoryCompareRequest
{
    public required string LeftDirectoryPath { get; init; }
    public required string RightDirectoryPath { get; init; }
    public required string LeftDisplayName { get; init; }
    public required string RightDisplayName { get; init; }
}

public enum DirectoryCompareEntryStatus
{
    Added,
    Removed,
    Modified
}

public sealed class DirectoryCompareEntry
{
    public required string RelativePath { get; init; }
    public required DirectoryCompareEntryStatus EntryStatus { get; init; }
    public string? LeftAbsolutePath { get; init; }
    public string? RightAbsolutePath { get; init; }
    public required string DisplayPath { get; init; }
}

public sealed class DirectoryCompareResult
{
    public required DirectoryCompareRequest Request { get; init; }
    public required IReadOnlyList<DirectoryCompareEntry> Entries { get; init; }
}
