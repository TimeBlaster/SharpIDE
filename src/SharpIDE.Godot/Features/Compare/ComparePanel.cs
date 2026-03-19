using Godot;
using SharpIDE.Application.Features.Compare;
using SharpIDE.Application.Features.Git;
using SharpIDE.Godot.Features.Git;
using SharpIDE.Godot.Features.LeftSideBar;
using SharpIDE.Godot.Features.Problems;

namespace SharpIDE.Godot.Features.Compare;

public partial class ComparePanel : PanelContainer
{
    private static readonly Color DeletedFileColor = new(1f, 0.45f, 0.38f);

    private Label _titleLabel = null!;
    private Button _closeButton = null!;
    private Tree _filesTree = null!;
    private Label _emptyStateLabel = null!;

    private GitRefComparisonRequest? _gitRequest;
    private DirectoryCompareRequest? _directoryRequest;
    private bool _isBusy;
    private bool _updatingTree;
    private CancellationTokenSource? _refreshDebounceCts;

    [Inject] private readonly CompareService _compareService = null!;
    [Inject] private readonly GitService _gitService = null!;
    [Inject] private readonly GitRepositoryMonitor _gitRepositoryMonitor = null!;

    public override void _Ready()
    {
        _titleLabel = GetNode<Label>("%TitleLabel");
        _closeButton = GetNode<Button>("%CloseButton");
        _filesTree = GetNode<Tree>("%FilesTree");
        _emptyStateLabel = GetNode<Label>("%EmptyStateLabel");

        ConfigureTree();

        _closeButton.Pressed += ClosePanel;
        _filesTree.ItemActivated += OnFilesTreeItemActivated;
        GodotGlobalEvents.Instance.GitRefComparisonRequested.Subscribe(OnGitComparisonRequested);
        GodotGlobalEvents.Instance.DirectoryComparisonRequested.Subscribe(OnDirectoryComparisonRequested);
        _gitRepositoryMonitor.RepositoryChanged.Subscribe(OnRepositoryChanged);

        ShowEmptyState("No comparison selected.");
    }

    public override void _ExitTree()
    {
        _refreshDebounceCts?.Cancel();
        if (GodotGlobalEvents.Instance is not null)
        {
            GodotGlobalEvents.Instance.GitRefComparisonRequested.Unsubscribe(OnGitComparisonRequested);
            GodotGlobalEvents.Instance.DirectoryComparisonRequested.Unsubscribe(OnDirectoryComparisonRequested);
        }

        _gitRepositoryMonitor.RepositoryChanged.Unsubscribe(OnRepositoryChanged);
        _gitRepositoryMonitor.Stop();
    }

    private void ConfigureTree()
    {
        _filesTree.HideRoot = true;
        _filesTree.Columns = 2;
        _filesTree.SelectMode = Tree.SelectModeEnum.Row;
        _filesTree.ColumnTitlesVisible = false;
        _filesTree.SetColumnExpand(0, true);
        _filesTree.SetColumnExpand(1, false);
        _filesTree.SetColumnCustomMinimumWidth(1, 50);
    }

    private async Task OnGitComparisonRequested(GitRefComparisonRequest request)
    {
        _gitRequest = request;
        _directoryRequest = null;
        GodotGlobalEvents.Instance.CompareVisibilityChanged.InvokeParallelFireAndForget(true);
        GodotGlobalEvents.Instance.LeftDockExternallySelected.InvokeParallelFireAndForget(LeftDockType.Compare);
        await RefreshAsync();
    }

    private async Task OnDirectoryComparisonRequested(DirectoryCompareRequest request)
    {
        _directoryRequest = request;
        _gitRequest = null;
        _gitRepositoryMonitor.Stop();
        GodotGlobalEvents.Instance.CompareVisibilityChanged.InvokeParallelFireAndForget(true);
        GodotGlobalEvents.Instance.LeftDockExternallySelected.InvokeParallelFireAndForget(LeftDockType.Compare);
        await RefreshAsync();
    }

    private async Task OnRepositoryChanged()
    {
        if (_gitRequest is null) return;

        var previousRefreshDebounceCts = _refreshDebounceCts;
        _refreshDebounceCts = new CancellationTokenSource();
        if (previousRefreshDebounceCts is not null)
        {
            await previousRefreshDebounceCts.CancelAsync();
            previousRefreshDebounceCts.Dispose();
        }

        try
        {
            await Task.Delay(250, _refreshDebounceCts.Token);
            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync()
    {
        if ((_gitRequest is null && _directoryRequest is null) || _isBusy)
        {
            return;
        }

        _isBusy = true;
        await this.InvokeAsync(UpdateActionState);
        try
        {
            if (_gitRequest is not null)
            {
                await RefreshGitComparisonAsync(_gitRequest);
            }
            else if (_directoryRequest is not null)
            {
                await RefreshDirectoryComparisonAsync(_directoryRequest);
            }
        }
        catch (Exception ex)
        {
            await this.InvokeAsync(() => ShowEmptyState(ex.Message));
        }
        finally
        {
            _isBusy = false;
            await this.InvokeAsync(UpdateActionState);
        }
    }

    private async Task RefreshGitComparisonAsync(GitRefComparisonRequest request)
    {
        var snapshot = await _gitService.GetSnapshot(request.RepoRootPath, commitCount: 1);
        if (snapshot.Repository.IsRepositoryDiscovered)
        {
            _gitRepositoryMonitor.Start(snapshot.Repository.RepoRootPath, snapshot.Repository.GitDirectoryPath);
        }

        var result = await _gitService.GetRefComparison(request);
        var entries = result.Files
            .Select(file => new CompareTreeEntry
            {
                RelativePath = file.RepoRelativePath,
                DisplayPath = file.DisplayPath,
                StatusText = file.StatusCode,
                Color = GetStatusColor(file.StatusCode),
                ActivationPayload = new GitRefComparisonFileDiffRequest
                {
                    RepoRootPath = result.Request.RepoRootPath,
                    LeftTarget = result.Request.LeftTarget,
                    RightTarget = result.Request.RightTarget,
                    RepoRelativePath = file.RepoRelativePath,
                    OldRepoRelativePath = file.OldRepoRelativePath,
                    StatusCode = file.StatusCode
                }
            })
            .ToArray();

        await this.InvokeAsync(() =>
        {
            PopulateFilesTree(entries);
            _titleLabel.Text = BuildTitle(request.LeftTarget.DisplayName, request.RightTarget.DisplayName);
            _emptyStateLabel.Visible = entries.Length is 0;
            _emptyStateLabel.Text = "No file changes in this comparison.";
            _filesTree.Visible = true;
        });
    }

    private async Task RefreshDirectoryComparisonAsync(DirectoryCompareRequest request)
    {
        var result = await _compareService.CompareDirectories(request);
        var entries = result.Entries
            .Select(entry => new CompareTreeEntry
            {
                RelativePath = entry.RelativePath,
                DisplayPath = entry.DisplayPath,
                StatusText = entry.EntryStatus switch
                {
                    DirectoryCompareEntryStatus.Added => "A",
                    DirectoryCompareEntryStatus.Removed => "D",
                    _ => "M"
                },
                Color = entry.EntryStatus switch
                {
                    DirectoryCompareEntryStatus.Added => GitColours.GitNewFileColour,
                    DirectoryCompareEntryStatus.Removed => DeletedFileColor,
                    _ => GitColours.GitEditedFileColour
                },
                ActivationPayload = new FileCompareRequest
                {
                    LeftAbsolutePath = entry.LeftAbsolutePath ?? Path.Combine(request.LeftDirectoryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    RightAbsolutePath = entry.RightAbsolutePath ?? Path.Combine(request.RightDirectoryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    LeftDisplayName = request.LeftDisplayName,
                    RightDisplayName = request.RightDisplayName,
                    ComparisonKey = CompareService.BuildStableComparisonKey(
                        entry.LeftAbsolutePath ?? Path.Combine(request.LeftDirectoryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                        entry.RightAbsolutePath ?? Path.Combine(request.RightDirectoryPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
                }
            })
            .ToArray();

        await this.InvokeAsync(() =>
        {
            PopulateFilesTree(entries);
            _titleLabel.Text = BuildTitle(request.LeftDisplayName, request.RightDisplayName);
            _emptyStateLabel.Visible = entries.Length is 0;
            _emptyStateLabel.Text = "No file changes in this comparison.";
            _filesTree.Visible = true;
        });
    }

    private void PopulateFilesTree(IReadOnlyList<CompareTreeEntry> entries)
    {
        _updatingTree = true;
        try
        {
            _filesTree.Clear();
            var root = _filesTree.CreateItem();
            if (entries.Count is 0)
            {
                _filesTree.CreateItem(root);
                return;
            }

            var directoryMap = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase)
            {
                [string.Empty] = root
            };

            foreach (var entry in entries.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var directory = Path.GetDirectoryName(entry.RelativePath.Replace('/', Path.DirectorySeparatorChar))?
                    .Replace(Path.DirectorySeparatorChar, '/')
                    ?? string.Empty;
                var parent = EnsureDirectory(directoryMap, root, directory);
                var item = _filesTree.CreateItem(parent);
                item.SetText(0, Path.GetFileName(entry.RelativePath));
                item.SetText(1, entry.StatusText);
                item.SetTooltipText(0, entry.DisplayPath);
                item.SetCustomColor(0, entry.Color);
                item.SetCustomColor(1, entry.Color);
                item.SetTypedMetadata(0, entry);
            }
        }
        finally
        {
            _updatingTree = false;
        }
    }

    private void OnFilesTreeItemActivated()
    {
        if (_updatingTree) return;

        var entry = _filesTree.GetSelected()?.GetTypedMetadata<CompareTreeEntry>(0);
        if (entry?.ActivationPayload is null)
        {
            return;
        }

        switch (entry.ActivationPayload)
        {
            case GitRefComparisonFileDiffRequest gitRequest:
                GodotGlobalEvents.Instance.GitRefComparisonDiffRequested.InvokeParallelFireAndForget(gitRequest);
                break;
            case FileCompareRequest fileCompareRequest:
                GodotGlobalEvents.Instance.FileComparisonRequested.InvokeParallelFireAndForget(fileCompareRequest);
                break;
        }
    }

    private void ShowEmptyState(string message)
    {
        _filesTree.Visible = false;
        _filesTree.Clear();
        _filesTree.CreateItem();
        _titleLabel.Text = _gitRequest is not null
            ? BuildTitle(_gitRequest.LeftTarget.DisplayName, _gitRequest.RightTarget.DisplayName)
            : _directoryRequest is not null
                ? BuildTitle(_directoryRequest.LeftDisplayName, _directoryRequest.RightDisplayName)
                : "Compare";
        _emptyStateLabel.Visible = true;
        _emptyStateLabel.Text = message;
    }

    private void UpdateActionState()
    {
        _closeButton.Disabled = _isBusy;
    }

    private void ClosePanel()
    {
        _gitRequest = null;
        _directoryRequest = null;
        _gitRepositoryMonitor.Stop();
        ShowEmptyState("No comparison selected.");
        GodotGlobalEvents.Instance.CompareVisibilityChanged.InvokeParallelFireAndForget(false);
        GodotGlobalEvents.Instance.LeftDockExternallySelected.InvokeParallelFireAndForget(LeftDockType.Commit);
    }

    private static string BuildTitle(string leftDisplayName, string rightDisplayName) => $"{leftDisplayName} <> {rightDisplayName}";

    private static Color GetStatusColor(string statusCode)
    {
        return statusCode[..Math.Min(1, statusCode.Length)] switch
        {
            "A" => GitColours.GitNewFileColour,
            "D" => DeletedFileColor,
            _ => GitColours.GitEditedFileColour
        };
    }

    private static TreeItem EnsureDirectory(Dictionary<string, TreeItem> directoryMap, TreeItem root, string directory)
    {
        if (directoryMap.TryGetValue(directory, out var existing))
        {
            return existing;
        }

        var parentPath = Path.GetDirectoryName(directory.Replace('/', Path.DirectorySeparatorChar))?
            .Replace(Path.DirectorySeparatorChar, '/')
            ?? string.Empty;
        var parent = EnsureDirectory(directoryMap, root, parentPath);
        var created = root.GetTree().CreateItem(parent);
        created.SetText(0, Path.GetFileName(directory));
        created.Collapsed = false;
        directoryMap[directory] = created;
        return created;
    }

    private sealed class CompareTreeEntry
    {
        public required string RelativePath { get; init; }
        public required string DisplayPath { get; init; }
        public required string StatusText { get; init; }
        public required Color Color { get; init; }
        public required object ActivationPayload { get; init; }
    }
}
