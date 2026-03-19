using Godot;
using SharpIDE.Application.Features.Compare;
using SharpIDE.Application.Features.SolutionDiscovery;

namespace SharpIDE.Godot.Features.SolutionExplorer;

public partial class SolutionExplorerPanel
{
    private sealed record SelectedTreeNode<TNode>(TNode Node, TreeItem TreeItem) where TNode : class;

    private IReadOnlyList<SharpIdeFile> GetSelectedFileContextItems(IReadOnlyList<TreeItem> selectedItems)
    {
        return selectedItems
            .Select(item => item.GetTypedMetadata<SharpIdeFile>(0))
            .Where(item => item != null)
            .DistinctBy(item => item.Path)
            .ToArray();
    }

    private IReadOnlyList<SharpIdeFolder> GetSelectedFolderContextItems(IReadOnlyList<TreeItem> selectedItems)
    {
        return selectedItems
            .Select(item => item.GetTypedMetadata<SharpIdeFolder>(0))
            .Where(item => item != null)
            .DistinctBy(item => item.Path)
            .ToArray();
    }

    private static FileCompareRequest? BuildFileComparisonRequest(SharpIdeFile leftFile, SharpIdeFile rightFile)
    {
        return new FileCompareRequest
        {
            LeftAbsolutePath = leftFile.Path,
            RightAbsolutePath = rightFile.Path,
            LeftDisplayName = leftFile.Name.Value,
            RightDisplayName = rightFile.Name.Value,
            ComparisonKey = CompareService.BuildStableComparisonKey(leftFile.Path, rightFile.Path)
        };
    }

    private static DirectoryCompareRequest? BuildDirectoryComparisonRequest(
        SharpIdeFolder leftFolder, SharpIdeFolder rightFolder)
    {
        return new DirectoryCompareRequest
        {
            LeftDirectoryPath = leftFolder.Path,
            RightDirectoryPath = rightFolder.Path,
            LeftDisplayName = leftFolder.Name.Value,
            RightDisplayName = rightFolder.Name.Value
        };
    }
}
