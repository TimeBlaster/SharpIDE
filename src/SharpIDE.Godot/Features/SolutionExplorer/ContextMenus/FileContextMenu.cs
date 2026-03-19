using Godot;
using SharpIDE.Application.Features.Compare;
using SharpIDE.Application.Features.SolutionDiscovery;
using SharpIDE.Application.Features.SolutionDiscovery.VsPersistence;
using SharpIDE.Godot.Features.SolutionExplorer.ContextMenus.Dialogs;

namespace SharpIDE.Godot.Features.SolutionExplorer;

file enum FileContextMenuOptions
{
    Open = 0,
    RevealInFileExplorer = 1,
    CopyFullPath = 2,
    Rename = 3,
    Delete = 4,
    ShowDiff = 5
}

public partial class SolutionExplorerPanel
{
    private readonly PackedScene _renameFileDialogScene = GD.Load<PackedScene>("uid://b775b5j4rkxxw");
    private void OpenContextMenuFile(IReadOnlyList<SharpIdeFile> files)
    {
        var isSingleSelection = files.Count is 1;
        var canCompare = files.Count is 2;
        var menu = new PopupMenu();
        AddChild(menu);
        menu.AddItem("Open", (int)FileContextMenuOptions.Open);
        menu.AddItem("Reveal in File Explorer", (int)FileContextMenuOptions.RevealInFileExplorer);
        menu.AddSeparator();
        menu.AddItem("Copy Full Path", (int)FileContextMenuOptions.CopyFullPath);
        menu.AddSeparator();
        menu.AddItem("Compare Files", (int)FileContextMenuOptions.ShowDiff);
        menu.AddSeparator();
        menu.AddItem("Rename", (int)FileContextMenuOptions.Rename);
        menu.AddItem("Delete", (int)FileContextMenuOptions.Delete);
        SetMenuItemDisabled(menu, (int)FileContextMenuOptions.Open, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FileContextMenuOptions.RevealInFileExplorer, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FileContextMenuOptions.CopyFullPath, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FileContextMenuOptions.Rename, !isSingleSelection);
        SetMenuItemDisabled(menu, (int)FileContextMenuOptions.ShowDiff, !canCompare);
        menu.PopupHide += () => menu.QueueFree();
        menu.IdPressed += id =>
        {
            var actionId = (FileContextMenuOptions)id;
            if (actionId is FileContextMenuOptions.Open)
            {
                GodotGlobalEvents.Instance.FileSelected.InvokeParallelFireAndForget(files[0], null);
            }
            else if (actionId is FileContextMenuOptions.ShowDiff)
            {
                var compareRequest = BuildFileComparisonRequest(files[0],  files[1]);
                if (compareRequest is null) return;
                GodotGlobalEvents.Instance.FileComparisonRequested.InvokeParallelFireAndForget(compareRequest);
            }
            else if (actionId is FileContextMenuOptions.RevealInFileExplorer)
            {
                OS.ShellShowInFileManager(files[0].Path);
            }
            else if (actionId is FileContextMenuOptions.CopyFullPath)
            {
                DisplayServer.ClipboardSet(files[0].Path);
            }
            else if (actionId is FileContextMenuOptions.Rename)
            {
                var renameFileDialog = _renameFileDialogScene.Instantiate<RenameFileDialog>();
                renameFileDialog.File = files[0];
                AddChild(renameFileDialog);
                renameFileDialog.PopupCentered();
            }
            else if (actionId is FileContextMenuOptions.Delete)
            {
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var confirmationDialog = new ConfirmationDialog();
                confirmationDialog.Title = "Delete";
                confirmationDialog.DialogText = isSingleSelection 
                    ? $"Delete '{files[0].Name.Value}' file?" 
                    : $"Delete '{files.Count}'?";
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
                        foreach (var file in files)
                        {
                            await _ideFileOperationsService.DeleteFile(file);
                        }
                    }
                });
            }
        };
			
        var globalMousePosition = GetGlobalMousePosition();
        menu.Position = new Vector2I((int)globalMousePosition.X, (int)globalMousePosition.Y);
        menu.Popup();
    }

    private static void SetMenuItemDisabled(PopupMenu menu, int itemId, bool disabled)
    {
        for (var index = 0; index < menu.ItemCount; index++)
        {
            if (menu.GetItemId(index) != itemId)
            {
                continue;
            }

            menu.SetItemDisabled(index, disabled);
            return;
        }
    }
}
