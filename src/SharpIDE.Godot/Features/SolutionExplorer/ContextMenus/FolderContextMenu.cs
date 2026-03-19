using Godot;
using SharpIDE.Application.Features.Compare;
using SharpIDE.Application.Features.FileWatching;
using SharpIDE.Application.Features.SolutionDiscovery;
using SharpIDE.Application.Features.SolutionDiscovery.VsPersistence;
using SharpIDE.Godot.Features.SolutionExplorer.ContextMenus.Dialogs;

namespace SharpIDE.Godot.Features.SolutionExplorer;

file enum FolderContextMenuOptions
{
    CreateNew = 1,
    RevealInFileExplorer = 2,
    Delete = 3,
    Rename = 4,
    CompareDirectories = 5
}

file enum CreateNewSubmenuOptions
{
    Directory = 1,
    CSharpFile = 2
}

public partial class SolutionExplorerPanel
{
    [Inject] private readonly IdeFileOperationsService _ideFileOperationsService = null!;
    
    private readonly PackedScene _newDirectoryDialogScene = GD.Load<PackedScene>("uid://bgi4u18y8pt4x");
    private readonly PackedScene _newCsharpFileDialogScene = GD.Load<PackedScene>("uid://chnb7gmcdg0ww");
    private readonly PackedScene _renameDirectoryDialogScene = GD.Load<PackedScene>("uid://btebkg8bo3b37");
    private void OpenContextMenuFolder(IReadOnlyList<SharpIdeFolder> folders)
    {
        var isSingleSelection = folders.Count is 1;
        var canCompare = folders.Count is 2;
        var menu = new PopupMenu();
        AddChild(menu);
        
        var createNewSubmenu = new PopupMenu();
        menu.AddSubmenuNodeItem("Add", createNewSubmenu, (int)FolderContextMenuOptions.CreateNew);
        createNewSubmenu.AddItem("Directory", (int)CreateNewSubmenuOptions.Directory);
        createNewSubmenu.AddItem("C# File", (int)CreateNewSubmenuOptions.CSharpFile);
        createNewSubmenu.IdPressed += id => OnCreateNewSubmenuPressed(id, folders[0]);
        
        menu.AddItem("Compare Directories", (int)FolderContextMenuOptions.CompareDirectories);
        menu.AddSeparator();
        menu.AddItem("Reveal in File Explorer", (int)FolderContextMenuOptions.RevealInFileExplorer);
        menu.AddItem("Delete", (int)FolderContextMenuOptions.Delete);
        menu.AddItem("Rename", (int)FolderContextMenuOptions.Rename);
        SetMenuItemDisabled(menu, (int)FolderContextMenuOptions.CreateNew, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FolderContextMenuOptions.RevealInFileExplorer, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FolderContextMenuOptions.Rename, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FolderContextMenuOptions.CompareDirectories, !canCompare);
        menu.PopupHide += () => menu.QueueFree();
        menu.IdPressed += id =>
        {
            var actionId = (FolderContextMenuOptions)id;
            if (actionId is FolderContextMenuOptions.RevealInFileExplorer)
            {
                OS.ShellOpen(folders[0].Path);
            }
            else if (actionId is FolderContextMenuOptions.CompareDirectories)
            {
                var compareRequest = BuildDirectoryComparisonRequest(folders[0], folders[1]);
                if (compareRequest is null) return;
                GodotGlobalEvents.Instance.DirectoryComparisonRequested.InvokeParallelFireAndForget(compareRequest);
            }
            else if (actionId is FolderContextMenuOptions.Delete)
            {
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var confirmationDialog = new ConfirmationDialog();
                confirmationDialog.Title = "Delete";
                confirmationDialog.DialogText = isSingleSelection 
                    ? $"Delete '{folders[0].Name.Value}' folder?" 
                    : $"Delete '{folders.Count}'?";
                confirmationDialog.Confirmed += () =>
                {
                    confirmedTcs.SetResult(true);
                };
                confirmationDialog.Canceled += () =>
                {
                    confirmedTcs.SetResult(false);
                };
                AddChild(confirmationDialog);
                confirmationDialog.PopupCentered();
                _ = Task.GodotRun(async () =>
                {
                    var confirmed = await confirmedTcs.Task;
                    if (confirmed)
                    {
                        foreach (var folder in folders)
                        {
                            await _ideFileOperationsService.DeleteDirectory(folder);
                        }
                    }
                });
            }
            else if (actionId is FolderContextMenuOptions.Rename)
            {
                var renameDirectoryDialog = _renameDirectoryDialogScene.Instantiate<RenameDirectoryDialog>();
                renameDirectoryDialog.Folder = folders[0];
                AddChild(renameDirectoryDialog);
                renameDirectoryDialog.PopupCentered();
            }
        };
			
        var globalMousePosition = GetGlobalMousePosition();
        menu.Position = new Vector2I((int)globalMousePosition.X, (int)globalMousePosition.Y);
        menu.Popup();
    }

    private void OnCreateNewSubmenuPressed(long id, IFolderOrProject folder)
    {
        var actionId = (CreateNewSubmenuOptions)id;
        if (actionId is CreateNewSubmenuOptions.Directory)
        {
            var newDirectoryDialog = _newDirectoryDialogScene.Instantiate<NewDirectoryDialog>();
            newDirectoryDialog.ParentFolder = folder;
            AddChild(newDirectoryDialog);
            newDirectoryDialog.PopupCentered();
        }
        else if (actionId is CreateNewSubmenuOptions.CSharpFile)
        {
            var newCsharpFileDialog = _newCsharpFileDialogScene.Instantiate<NewCsharpFileDialog>();
            newCsharpFileDialog.ParentNode = folder;
            AddChild(newCsharpFileDialog);
            newCsharpFileDialog.PopupCentered();
        }
    }
}
