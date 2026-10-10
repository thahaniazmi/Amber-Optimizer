using System.Windows.Media;
using SmartRamOptimizer.Services;

namespace SmartRamOptimizer.ViewModels;

/// <summary>One running app offered in the "Add application" window.</summary>
public sealed class CandidateRowViewModel : ObservableObject
{
    private bool _isAdded;

    public CandidateRowViewModel(AppCandidate candidate)
    {
        Candidate = candidate;

        var processes = candidate.ProcessCount == 1 ? "1 process" : $"{candidate.ProcessCount} processes";
        Detail = candidate.IsAntiCheatRisk
            ? "Anti-cheat game · not recommended"
            : $"{ByteFormat.Format(candidate.WorkingSetBytes)} · {processes}";
    }

    public AppCandidate Candidate { get; }

    public string Name => Candidate.Name;

    public string Detail { get; }

    public bool IsWarning => Candidate.IsAntiCheatRisk;

    public ImageSource? Icon => Candidate.Icon;

    public bool HasIcon => Candidate.Icon is not null;

    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public string AddLabel => $"Add {Name}";

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (SetProperty(ref _isAdded, value))
            {
                OnPropertyChanged(nameof(CanAdd));
                OnPropertyChanged(nameof(ButtonText));
            }
        }
    }

    public bool CanAdd => !_isAdded;

    public string ButtonText => _isAdded ? "Added" : "Add";

    public bool Matches(string search)
    {
        return search.Length == 0
            || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || Candidate.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
