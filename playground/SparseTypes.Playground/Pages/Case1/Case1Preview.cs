using SparseTypes.Playground.Models;
using SparseTypes.Playground.Pages;

namespace SparseTypes.Playground.Pages.Case1;

/// <summary>One memoized preview generation for the Case 1 layers page.</summary>
/// <remarks>Lower/higher fragments build at most once per generation and every presentation derives from those shared instances, so reading several panes (or the same pane twice) never repeats model transforms or serialization. The page drops the whole generation on any input change; presentation-only renders reuse it.</remarks>
public sealed class Case1Preview
{
    private readonly Func<PlaygroundSettings.Fragment> _lower;
    private readonly Func<PlaygroundSettings.Fragment> _higher;

    private PlaygroundSettings.Fragment? _lowerFragment;
    private PlaygroundSettings.Fragment? _higherFragment;
    private PlaygroundSettings.Fragment? _mergedFragment;
    private PlaygroundSettings? _lowerModel;
    private PlaygroundSettings? _mergedModel;

    private string? _lowerFragmentJson;
    private string? _higherFragmentJson;
    private string? _mergedFragmentJson;
    private string? _mergeDiffJson;
    private string? _mergedOrigins;

    /// <summary>Initializes a preview generation over the given input factories.</summary>
    /// <param name="lower">Builds the lower fragment from the current edit state.</param>
    /// <param name="higher">Builds the higher fragment from the current edit state.</param>
    public Case1Preview(
        Func<PlaygroundSettings.Fragment> lower,
        Func<PlaygroundSettings.Fragment> higher
    )
    {
        _lower = lower;
        _higher = higher;
    }

    /// <summary>Gets the lower fragment, built once per generation.</summary>
    public PlaygroundSettings.Fragment LowerFragment => _lowerFragment ??= _lower();

    /// <summary>Gets the higher fragment, built once per generation.</summary>
    public PlaygroundSettings.Fragment HigherFragment => _higherFragment ??= _higher();

    /// <summary>Gets the merged fragment derived once from the layer fragments.</summary>
    public PlaygroundSettings.Fragment MergedFragment =>
        _mergedFragment ??= LowerFragment.Merge(HigherFragment);

    /// <summary>Gets the lower model converted once per generation.</summary>
    public PlaygroundSettings LowerModel => _lowerModel ??= LowerFragment.ToModel();

    /// <summary>Gets the merged model converted once per generation.</summary>
    public PlaygroundSettings MergedModel => _mergedModel ??= MergedFragment.ToModel();

    /// <summary>Gets the lower fragment JSON, serialized once per generation.</summary>
    public string LowerFragmentJson =>
        _lowerFragmentJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(LowerFragment)
        );

    /// <summary>Gets the higher fragment JSON, serialized once per generation.</summary>
    public string HigherFragmentJson =>
        _higherFragmentJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(HigherFragment)
        );

    /// <summary>Gets the merged fragment JSON, serialized once per generation.</summary>
    public string MergedFragmentJson =>
        _mergedFragmentJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(MergedFragment)
        );

    /// <summary>Gets the lower-to-merged diff JSON, derived once per generation.</summary>
    public string MergeDiffJson =>
        _mergeDiffJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(PlaygroundSettings.Fragment.Diff(LowerModel, MergedModel))
        );

    /// <summary>Gets the merged origin lines, enumerated once per generation.</summary>
    public string MergedOrigins =>
        _mergedOrigins ??= PlaygroundPageHelper.Try(() =>
            string.Join(
                System.Environment.NewLine,
                MergedFragment
                    .EnumerateOrigins()
                    .Select(entry => $"{entry.Path}: {entry.Origin ?? "Unknown"}")
            )
        );
}
