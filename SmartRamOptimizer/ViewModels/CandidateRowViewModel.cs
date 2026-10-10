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

        var size = ByteFormat.Format(candidate.WorkingSetBytes);
        var processes = candidate.ProcessCount == 1 ? "1 process" : $"{candidate.ProcessCount} processes";
        Detail = candidate.Advice.Suitability switch
        {
            Suitability.NotRecommended => candidate.Advice.Label,
            Suitability.Recommended => $"{candidate.Advice.Label} · {size}",
            _ => $"{size} · {processes}"
        };
    }

    public AppCandidate Candidate { get; }

    public string Name => Candidate.Name;

    public string Detail { get; }

    /// <summary>The longer explanation behind a recommendation or warning; null when there is none.</summary>
    public string? Advice => Candidate.Advice.Reason.Length > 0 ? Candidate.Advice.Reason : null;

    public bool IsWarning => Candidate.Advice.Suitability == Suitability.NotRecommended;

    public bool IsRecommended => Candidate.Advice.Suitability == Suitability.Recommended;

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
