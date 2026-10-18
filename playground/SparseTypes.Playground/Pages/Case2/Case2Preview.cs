using SparseTypes;
using SparseTypes.Playground.Models;
using SparseTypes.Playground.Pages;

namespace SparseTypes.Playground.Pages.Case2;

/// <summary>One memoized preview generation for the Case 2 patch page.</summary>
/// <remarks>Lower/higher/patch fragments build at most once per generation and every presentation derives from those shared instances, so reading several panes (or the same pane twice) never repeats model transforms or serialization. The page drops the whole generation on any input change; presentation-only renders reuse it.</remarks>
public sealed class Case2Preview
{
    private readonly Func<PlaygroundSettings.Fragment> _lower;
    private readonly Func<PlaygroundSettings.Fragment> _higher;
    private readonly Func<PlaygroundSettings.Patch> _patch;
    private readonly Func<string> _patchCSharp;

    private PlaygroundSettings.Fragment? _lowerFragment;
    private PlaygroundSettings.Fragment? _higherFragment;
    private PlaygroundSettings.Fragment? _mergedFragment;
    private PlaygroundSettings.Patch? _typedPatch;
    private PlaygroundSettings.Fragment? _patchedFragment;
    private PlaygroundSettings? _mergedModel;
    private PlaygroundSettings? _patchedModel;
    private PlaygroundSettings.ChangeSet? _exportChanges;

    private string? _mergedFragmentJson;
    private string? _mergedModelJson;
    private string? _mergedModelCSharp;
    private string? _patchedFragmentJson;
    private string? _patchedModelJson;
    private string? _patchedModelCSharp;
    private string? _patchCSharpText;
    private string? _changeSetExportText;

    /// <summary>Initializes a preview generation over the given input factories.</summary>
    /// <param name="lower">Builds the lower fragment from the current edit state.</param>
    /// <param name="higher">Builds the higher fragment from the current edit state.</param>
    /// <param name="patch">Builds the typed patch from the current edit state.</param>
    /// <param name="patchCSharp">Renders the patch C# snippet from the current edit state.</param>
    public Case2Preview(
        Func<PlaygroundSettings.Fragment> lower,
        Func<PlaygroundSettings.Fragment> higher,
        Func<PlaygroundSettings.Patch> patch,
        Func<string> patchCSharp
    )
    {
        _lower = lower;
        _higher = higher;
        _patch = patch;
        _patchCSharp = patchCSharp;
    }

    /// <summary>Gets the lower fragment, built once per generation.</summary>
    public PlaygroundSettings.Fragment LowerFragment => _lowerFragment ??= _lower();

    /// <summary>Gets the higher fragment, built once per generation.</summary>
    public PlaygroundSettings.Fragment HigherFragment => _higherFragment ??= _higher();

    /// <summary>Gets the merged fragment derived once from the layer fragments.</summary>
    public PlaygroundSettings.Fragment MergedFragment =>
        _mergedFragment ??= LowerFragment.Merge(HigherFragment);

    /// <summary>Gets the typed patch, built once per generation.</summary>
    public PlaygroundSettings.Patch TypedPatch => _typedPatch ??= _patch();

    /// <summary>Gets the patched fragment derived once from the merged fragment.</summary>
    public PlaygroundSettings.Fragment PatchedFragment =>
        _patchedFragment ??= MergedFragment.Apply(TypedPatch);

    /// <summary>Gets the merged model converted once per generation.</summary>
    public PlaygroundSettings MergedModel => _mergedModel ??= MergedFragment.ToModel();

    /// <summary>Gets the patched model converted once per generation.</summary>
    public PlaygroundSettings PatchedModel => _patchedModel ??= PatchedFragment.ToModel();

    /// <summary>Gets the exported change set derived once per generation.</summary>
    public PlaygroundSettings.ChangeSet ExportChanges =>
        _exportChanges ??= PlaygroundSettings.ChangeSet.FromPatch(
            Optional<PlaygroundSettings.Fragment?>.Present(MergedFragment),
            TypedPatch
        );

    /// <summary>Gets the merged fragment JSON, serialized once per generation.</summary>
    public string MergedFragmentJson =>
        _mergedFragmentJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(MergedFragment)
        );

    /// <summary>Gets the merged model JSON, serialized once per generation.</summary>
    public string MergedModelJson =>
        _mergedModelJson ??= PlaygroundPageHelper.Try(() => PlaygroundJson.WriteModel(MergedModel));

    /// <summary>Gets the merged model C# snippet, rendered once per generation.</summary>
    public string MergedModelCSharp =>
        _mergedModelCSharp ??= PlaygroundPageHelper.Try(() =>
            PlaygroundSnippets.ModelCSharp("model", MergedModel)
        );

    /// <summary>Gets the patched fragment JSON, serialized once per generation.</summary>
    public string PatchedFragmentJson =>
        _patchedFragmentJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteFragment(PatchedFragment)
        );

    /// <summary>Gets the patched model JSON, serialized once per generation.</summary>
    public string PatchedModelJson =>
        _patchedModelJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteModel(PatchedModel)
        );

    /// <summary>Gets the patched model C# snippet, rendered once per generation.</summary>
    public string PatchedModelCSharp =>
        _patchedModelCSharp ??= PlaygroundPageHelper.Try(() =>
            PlaygroundSnippets.ModelCSharp("model", PatchedModel)
        );

    /// <summary>Gets the patch C# snippet, rendered once per generation.</summary>
    public string PatchCSharp => _patchCSharpText ??= _patchCSharp();

    /// <summary>Gets the exported ChangePayload JSON, serialized once per generation.</summary>
    public string ChangeSetExportText =>
        _changeSetExportText ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteSettingsChangeSet(ExportChanges)
        );
}
