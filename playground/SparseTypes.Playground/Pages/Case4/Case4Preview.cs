using SparseTypes.Playground.Models;
using SparseTypes.Playground.Pages;

namespace SparseTypes.Playground.Pages.Case4;

/// <summary>One memoized preview generation for the undo-redo session page.</summary>
/// <remarks>The current-model summary and the pending change set derive at most once per generation, so reading several panes (or the same pane twice) never repeats serialization or change-set capture. The page drops the whole generation on every session notification; explicit submit snapshots stay independent and frozen.</remarks>
public sealed class Case4Preview
{
    private readonly Func<string> _currentJson;
    private readonly Func<PlaygroundSettings.ChangeSet> _changes;

    private string? _currentJsonText;
    private PlaygroundSettings.ChangeSet? _changeSet;
    private string? _changePayloadJson;

    /// <summary>Initializes a preview generation over the given input factories.</summary>
    /// <param name="currentJson">Serializes the current model summary.</param>
    /// <param name="changes">Captures the pending change set.</param>
    public Case4Preview(Func<string> currentJson, Func<PlaygroundSettings.ChangeSet> changes)
    {
        _currentJson = currentJson;
        _changes = changes;
    }

    /// <summary>Gets the current model summary JSON, serialized once per generation.</summary>
    public string CurrentJson => _currentJsonText ??= _currentJson();

    /// <summary>Gets the pending change set, captured once per generation.</summary>
    public PlaygroundSettings.ChangeSet ChangeSet => _changeSet ??= _changes();

    /// <summary>Gets the ChangePayload JSON, serialized once per generation.</summary>
    public string ChangePayloadJson =>
        _changePayloadJson ??= PlaygroundPageHelper.Try(() =>
            PlaygroundJson.WriteSettingsChangeSet(ChangeSet)
        );
}
