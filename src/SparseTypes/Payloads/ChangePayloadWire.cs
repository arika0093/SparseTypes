using System.ComponentModel;

namespace SparseTypes;

/// <summary>Serializer-agnostic ChangePayload version-1 wire tokens.</summary>
/// <remarks>The base runtime carries no <c>System.Text.Json</c> reference.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ChangePayloadWire
{
    /// <summary>The current wire version token.</summary>
    public const string Version = "0.1";

    /// <summary>The envelope version property name.</summary>
    public const string VersionProperty = "version";

    /// <summary>The envelope changes property name.</summary>
    public const string ChangesProperty = "changes";

    /// <summary>The polymorphic discriminator property name.</summary>
    public const string DiscriminatorProperty = "member";

    /// <summary>The whole-root discriminator value.</summary>
    public const string RootDiscriminator = "$root";

    /// <summary>The endpoint state property name.</summary>
    public const string StateProperty = "state";

    /// <summary>The endpoint value property name.</summary>
    public const string ValueProperty = "value";

    /// <summary>The <c>missing</c> endpoint state token.</summary>
    public const string MissingState = "missing";

    /// <summary>The <c>null</c> endpoint state token.</summary>
    public const string NullState = "null";

    /// <summary>The <c>value</c> endpoint state token.</summary>
    public const string ValueState = "value";

    /// <summary>The <c>redacted</c> endpoint state token.</summary>
    public const string RedactedState = "redacted";

    /// <summary>The <c>add</c> item kind token.</summary>
    public const string AddKind = "add";

    /// <summary>The <c>remove</c> item kind token.</summary>
    public const string RemoveKind = "remove";

    /// <summary>The <c>edit</c> item kind token.</summary>
    public const string EditKind = "edit";

    /// <summary>The <c>reorder</c> item kind token.</summary>
    public const string ReorderKind = "reorder";

    /// <summary>Returns the UTF-8 form of the endpoint state envelope name.</summary>
    /// <remarks>
    /// The string constants stay the single vocabulary source: this cached
    /// byte form exists so generated reader dispatch can compare without
    /// materializing name strings.
    /// </remarks>
    /// <returns>The lower-case state property name encoded as UTF-8.</returns>
    public static System.ReadOnlySpan<byte> StatePropertyUtf8() => Utf8StateProperty;

    /// <summary>Returns the UTF-8 form of the endpoint value envelope name.</summary>
    /// <remarks>See <see cref="StatePropertyUtf8"/> for the single-source rule.</remarks>
    /// <returns>The lower-case value property name encoded as UTF-8.</returns>
    public static System.ReadOnlySpan<byte> ValuePropertyUtf8() => Utf8ValueProperty;

    /// <summary>Returns the UTF-8 form of the wire token for an endpoint state.</summary>
    /// <remarks>The candidate enumeration mirrors <see cref="TryParseState"/>.</remarks>
    /// <param name="state">The endpoint state.</param>
    /// <returns>The lower-case state token encoded as UTF-8.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">An undefined state value.</exception>
    public static System.ReadOnlySpan<byte> StateTokenUtf8(ChangePayloadState state) =>
        state switch
        {
            ChangePayloadState.Missing => Utf8MissingState,
            ChangePayloadState.Null => Utf8NullState,
            ChangePayloadState.Value => Utf8ValueState,
            ChangePayloadState.Redacted => Utf8RedactedState,
            _ => throw new System.ArgumentOutOfRangeException(nameof(state), state, null),
        };

    /// <summary>Returns the UTF-8 form of the wire token for an item kind.</summary>
    /// <remarks>The candidate enumeration mirrors <see cref="TryParseKind"/>.</remarks>
    /// <param name="kind">The item kind.</param>
    /// <returns>The lower-case kind token encoded as UTF-8.</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">An undefined kind value.</exception>
    public static System.ReadOnlySpan<byte> KindTokenUtf8(ChangePayloadItemKind kind) =>
        kind switch
        {
            ChangePayloadItemKind.Add => Utf8AddKind,
            ChangePayloadItemKind.Remove => Utf8RemoveKind,
            ChangePayloadItemKind.Edit => Utf8EditKind,
            ChangePayloadItemKind.Reorder => Utf8ReorderKind,
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private static readonly byte[] Utf8StateProperty = System.Text.Encoding.UTF8.GetBytes(StateProperty);

    private static readonly byte[] Utf8ValueProperty = System.Text.Encoding.UTF8.GetBytes(ValueProperty);

    private static readonly byte[] Utf8MissingState = System.Text.Encoding.UTF8.GetBytes(MissingState);

    private static readonly byte[] Utf8NullState = System.Text.Encoding.UTF8.GetBytes(NullState);

    private static readonly byte[] Utf8ValueState = System.Text.Encoding.UTF8.GetBytes(ValueState);

    private static readonly byte[] Utf8RedactedState = System.Text.Encoding.UTF8.GetBytes(RedactedState);

    private static readonly byte[] Utf8AddKind = System.Text.Encoding.UTF8.GetBytes(AddKind);

    private static readonly byte[] Utf8RemoveKind = System.Text.Encoding.UTF8.GetBytes(RemoveKind);

    private static readonly byte[] Utf8EditKind = System.Text.Encoding.UTF8.GetBytes(EditKind);

    private static readonly byte[] Utf8ReorderKind = System.Text.Encoding.UTF8.GetBytes(ReorderKind);

    /// <summary>Maps an endpoint state to its wire token.</summary>
    /// <param name="state">The state to map.</param>
    /// <returns>The lower-case wire token.</returns>
    public static string ToToken(ChangePayloadState state) =>
        state switch
        {
            ChangePayloadState.Missing => MissingState,
            ChangePayloadState.Null => NullState,
            ChangePayloadState.Value => ValueState,
            ChangePayloadState.Redacted => RedactedState,
            _ => throw new System.ArgumentOutOfRangeException(nameof(state), state, null),
        };

    /// <summary>Maps an item kind to its wire token.</summary>
    /// <param name="kind">The kind to map.</param>
    /// <returns>The lower-case wire token.</returns>
    public static string ToToken(ChangePayloadItemKind kind) =>
        kind switch
        {
            ChangePayloadItemKind.Add => AddKind,
            ChangePayloadItemKind.Remove => RemoveKind,
            ChangePayloadItemKind.Edit => EditKind,
            ChangePayloadItemKind.Reorder => ReorderKind,
            _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    /// <summary>Parses a wire token into an endpoint state.</summary>
    /// <param name="token">The lower-case token, or null.</param>
    /// <param name="state">The parsed state on success.</param>
    /// <returns>Whether the token is a known state.</returns>
    public static bool TryParseState(string? token, out ChangePayloadState state)
    {
        switch (token)
        {
            case MissingState:
                state = ChangePayloadState.Missing;
                return true;
            case NullState:
                state = ChangePayloadState.Null;
                return true;
            case ValueState:
                state = ChangePayloadState.Value;
                return true;
            case RedactedState:
                state = ChangePayloadState.Redacted;
                return true;
            default:
                state = default;
                return false;
        }
    }

    /// <summary>Parses a wire token into an item kind.</summary>
    /// <param name="token">The lower-case token, or null.</param>
    /// <param name="kind">The parsed kind on success.</param>
    /// <returns>Whether the token is a known kind.</returns>
    public static bool TryParseKind(string? token, out ChangePayloadItemKind kind)
    {
        switch (token)
        {
            case AddKind:
                kind = ChangePayloadItemKind.Add;
                return true;
            case RemoveKind:
                kind = ChangePayloadItemKind.Remove;
                return true;
            case EditKind:
                kind = ChangePayloadItemKind.Edit;
                return true;
            case ReorderKind:
                kind = ChangePayloadItemKind.Reorder;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}
