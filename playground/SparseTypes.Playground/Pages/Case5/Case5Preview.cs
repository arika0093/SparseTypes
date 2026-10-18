namespace SparseTypes.Playground.Pages.Case5;

/// <summary>Lazy JSON panes for the Case 5 EF mapping playground.</summary>
/// <remarks>Entity, baseline and draft outputs have different invalidation sources: the tracked entity changes on explicit save/reload events, the baseline view changes when the persisted DTO is adopted, and the draft changes on every draft edit. Each pane serializes at most once between its own invalidations, so presentation-only renders reuse all three.</remarks>
public sealed class Case5Preview
{
    private readonly Func<string> _entity;
    private readonly Func<string> _baseline;
    private readonly Func<string> _draft;

    private string? _entityJson;
    private string? _baselineJson;
    private string? _draftJson;

    /// <summary>Initializes the panes over the given render factories.</summary>
    /// <param name="entity">Renders the tracked-entity JSON.</param>
    /// <param name="baseline">Renders the baseline DTO JSON.</param>
    /// <param name="draft">Renders the live draft JSON.</param>
    public Case5Preview(Func<string> entity, Func<string> baseline, Func<string> draft)
    {
        _entity = entity;
        _baseline = baseline;
        _draft = draft;
    }

    /// <summary>Gets the tracked-entity JSON, serialized once per invalidation.</summary>
    public string EntityJson => _entityJson ??= _entity();

    /// <summary>Gets the baseline DTO JSON, serialized once per invalidation.</summary>
    public string BaselineJson => _baselineJson ??= _baseline();

    /// <summary>Gets the live draft JSON, serialized once per invalidation.</summary>
    public string DraftJson => _draftJson ??= _draft();

    /// <summary>Drops the cached entity JSON after an explicit save or reload.</summary>
    public void InvalidateEntity() => _entityJson = null;

    /// <summary>Drops the cached baseline JSON after adopting a persisted DTO.</summary>
    public void InvalidateBaseline() => _baselineJson = null;

    /// <summary>Drops the cached draft JSON after a draft edit.</summary>
    public void InvalidateDraft() => _draftJson = null;
}
