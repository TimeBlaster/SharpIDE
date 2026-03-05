using System.Diagnostics;
using Godot;
using SharpIDE.Application.Features.Analysis;
using SharpIDE.Application.Features.Analysis.WorkspaceServices;
using SharpIDE.Godot.Features.ActivityListener;

namespace SharpIDE.Godot.Features.BottomBar;

public partial class RunningTasksDisplay : HBoxContainer
{
    [Inject] private readonly ActivityMonitor _activityMonitor = null!;
    
    private bool _isSolutionRestoring;
    private bool _isSolutionLoading;
    private bool _isSolutionDiagnosticsBeingRetrieved;
    private bool _isDecompilingAssembly;

    private Label _solutionRestoringLabel = null!;
    private Label _solutionLoadingLabel = null!;
    private Label _solutionDiagnosticsLabel = null!;
    private Label _decompilingAssemblyLabel = null!;
    
    public override void _Ready()
    {
        _solutionRestoringLabel = GetNode<Label>("%SolutionRestoringLabel");
        _solutionLoadingLabel = GetNode<Label>("%SolutionLoadingLabel");
        _solutionDiagnosticsLabel = GetNode<Label>("%SolutionDiagnosticsLabel");
        _decompilingAssemblyLabel = GetNode<Label>("%DecompilingAssemblyLabel");
        Visible = false;
        _activityMonitor.ActivityChanged.Subscribe(OnActivityChanged);
    }

    public override void _ExitTree()
    {
        _activityMonitor.ActivityChanged.Unsubscribe(OnActivityChanged);
    }

    private async Task OnActivityChanged(Activity activity)
    {
        var isOccurring = !activity.IsStopped;
        if (activity.DisplayName == $"{nameof(RoslynAnalysis)}.{nameof(RoslynAnalysis.UpdateSolutionDiagnostics)}")
        {
            _isSolutionDiagnosticsBeingRetrieved = isOccurring;
        }
        else if (activity.DisplayName == "OpenSolution")
        {
            _isSolutionLoading = isOccurring;
        }
        else if (activity.DisplayName == "RestoreSolution")
        {
            _isSolutionRestoring = isOccurring;
        }
        else if (activity.DisplayName == $"{nameof(PortablePdbWriter2)}.{nameof(PortablePdbWriter2.DecompiledAndWritePdb)}")
        {
            _isDecompilingAssembly = isOccurring;
        }
        else
        {
            return;
        }
        
        var visible = _isSolutionDiagnosticsBeingRetrieved || _isSolutionLoading || _isSolutionRestoring || _isDecompilingAssembly;
        await this.InvokeAsync(() =>
        {
            _solutionLoadingLabel.Visible = _isSolutionLoading;
            _solutionDiagnosticsLabel.Visible = _isSolutionDiagnosticsBeingRetrieved;
            _solutionRestoringLabel.Visible = _isSolutionRestoring;
            _decompilingAssemblyLabel.Visible = _isDecompilingAssembly;
            Visible = visible;
        });
    }
}