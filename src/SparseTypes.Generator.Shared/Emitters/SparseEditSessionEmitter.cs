using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SparseTypes.Generator.Shared;

internal static class SparseEditSessionEmitter
{
    private const string CoreResourceName = "SparseTypes.Generator.Shared.Sessions.SparseEditSessionCore.template";
    private const string CurrentCoreResourceName = "SparseTypes.Generator.Shared.Sessions.SparseEditSessionWithCurrentCore.template";
    private static readonly Lazy<string> CoreTemplate = new(() => ReadTemplate(CoreResourceName));
    private static readonly Lazy<string> CurrentCoreTemplate = new(() => ReadTemplate(CurrentCoreResourceName));

    internal static bool HasChildSessionSurface(
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        bool hasChildModelMembers = true
    ) =>
        hasChildModelMembers
        && config.DescriptorDialect is not null
        && patchDialect.TypedPathInterfaceType is { Length: > 0 }
        && config.EditSessionWithChildrenInterfaceMetadataName is { Length: > 0 }
        && config.EditChildPathInterfaceMetadataName is { Length: > 0 }
        && config.EditChildSessionInterfaceMetadataName is { Length: > 0 }
        && config.EditChildSessionImplementationType is { Length: > 0 }
        && config.EditChildSessionStatusType is { Length: > 0 }
        && config.EditSessionWithChildStatusInterfaceMetadataName is { Length: > 0 }
        && config.EditChildDraftHostInterfaceMetadataName is { Length: > 0 }
        && config.EditChildDraftImplementationType is { Length: > 0 }
        && patchDialect.ChildConflictPathPrefixExpression is not null;

    internal static bool HasChildModelMembers(ImmutableArray<SparseMemberModel> members) =>
        members.Any(static member =>
            member.ChildModel is not null || member.Collection.ElementType.IsFragmentModel || member.Collection.ValueType?.IsFragmentModel == true
        );

    /// <summary>Emits the compilation-scoped reusable session cores once.</summary>
    /// <remarks>Generated-Once seam: capabilities are de-duplicated per compilation and prerequisites are validated before emission.</remarks>
    /// <param name="context">Generator output context.</param>
    /// <param name="config">Owning generator configuration.</param>
    public static void EmitCore(SourceProductionContext context, SparseGeneratorConfig config)
    {
        // Capability aggregation moves to the shared Generated-Once pipeline;
        // per-model callers keep requesting the full core pair until then.
        var capability = SparseEditSessionCapabilities.ForCompilation(hasSessionModels: true, needsCurrentView: true);
        var violations = SparseEditSessionCapabilities.ValidatePrerequisites(config, capability);
        if (violations.Length != 0)
        {
            throw new ArgumentException("Invalid edit-session capability plan: " + string.Join(" ", violations), nameof(config));
        }

        EmitCapability(context, config, capability);
    }

    /// <summary>Emits exactly the requested session-core subset.</summary>
    /// <param name="context">Generator output context.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="capability">De-duplicated capability set.</param>
    public static void EmitCapability(SourceProductionContext context, SparseGeneratorConfig config, SparseEditSessionCapability capability)
    {
        if (capability == SparseEditSessionCapability.None)
        {
            return;
        }

        var sessionDialect = config.EditSessionDialect ?? throw new ArgumentException("An edit-session dialect is required.", nameof(config));
        var runtimeDialect = config.RuntimeDialect ?? throw new ArgumentException("A runtime dialect is required.", nameof(config));
        var patchDialect = config.PatchDialect ?? throw new ArgumentException("A patch dialect is required.", nameof(config));
        var sources = RenderCoreSources(sessionDialect, runtimeDialect, patchDialect);

        if (SparseEditSessionCapabilities.NeedsCore(capability))
        {
            context.AddSource(sessionDialect.CoreHintName, SourceText.From(sources.Core, Encoding.UTF8));
        }

        if (SparseEditSessionCapabilities.NeedsWithCurrent(capability))
        {
            context.AddSource(sessionDialect.CurrentCoreHintName, SourceText.From(sources.CurrentCore, Encoding.UTF8));
        }
    }

    internal static (string Core, string CurrentCore) RenderCoreSources(
        SparseEditSessionDialect sessionDialect,
        SparseRuntimeDialect runtimeDialect,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        (
            RenderTemplate(CoreTemplate.Value, sessionDialect, runtimeDialect, patchDialect),
            RenderTemplate(CurrentCoreTemplate.Value, sessionDialect, runtimeDialect, patchDialect)
        );

    public static void AppendModelEditSession(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        string modelType,
        string accessibility,
        string observable,
        string readOnlyView,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config
    )
    {
        // Diagnose adapter binding at generation time: the connection contract
        // needs Fragment/Patch/ChangeSet/Observable families up front.
        var adapterViolations = SparseEditSessionAdapterContract.ValidateAdapterFeatures(config.EffectiveEmissionFeatures);
        if (adapterViolations.Length != 0)
        {
            throw new ArgumentException(
                "Invalid edit-session adapter plan for '" + modelType + "': " + string.Join(" ", adapterViolations),
                nameof(config)
            );
        }

        var sessionInterfaceMetadataName =
            config.EditSessionInterfaceMetadataName
            ?? throw new ArgumentException("An edit-session interface metadata name is required.", nameof(config));
        var modelAccessorInterfaceMetadataName = config.EditSessionModelAccessorInterfaceMetadataName;
        var sessionDialect = config.EditSessionDialect ?? throw new ArgumentException("An edit-session dialect is required.", nameof(config));
        var patchDialect = config.PatchDialect ?? throw new ArgumentException("A patch dialect is required.", nameof(config));
        var hasChildModelMembers = HasChildModelMembers(members);
        var modelName = SparseNaming.EscapeIdentifier(model.Name);
        var declaration = model.IsRecord ? "partial record " : "partial class ";
        var sessionInterface = "global::" + sessionInterfaceMetadataName;
        var core =
            "global::"
            + sessionDialect.Namespace
            + ".EditSessionCore<"
            + modelType
            + ", "
            + modelType
            + ".Fragment, "
            + modelType
            + ".Patch, "
            + modelType
            + ".ChangeSet, "
            + modelType
            + "."
            + observable
            + ", "
            + modelType
            + "."
            + readOnlyView
            + ">";
        var configuration =
            "global::"
            + sessionDialect.Namespace
            + ".EditSessionCoreConfiguration<"
            + modelType
            + ", "
            + modelType
            + ".Fragment, "
            + modelType
            + ".Patch, "
            + modelType
            + ".ChangeSet, "
            + modelType
            + "."
            + observable
            + ", "
            + modelType
            + "."
            + readOnlyView
            + ">";
        var canWriteInPlace = members.All(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly);
        var tryApply = canWriteInPlace
            ? "static (changes, current) => changes.TryApplyTo(current, out var updated, out var conflicts) ? (updated, null) : (null, conflicts)"
            : "static (changes, current) => { var candidate = "
                + modelType
                + ".Fragment.From(current).ToModel(); if (changes.TryApplyInPlace(candidate, out var conflicts)) return (candidate, null); return (null, conflicts); }";
        var writeModel = canWriteInPlace
            ? "static (current, updated) => " + modelType + ".Fragment.From(updated).WriteTo(current)"
            : "static (current, updated) => " + modelType + ".Fragment.From(updated).__SparseWriteWritableTo(current)";
        var conflictType = patchDialect.ConflictType;
        var rebaseResultType = patchDialect.RebaseResult(modelType + ".ChangeSet");
        SparseEditSessionHistoryEmitter.TryGetHistoryBinding(config, out var historyBinding);
        // A same-named member pushes the session behind a Sparse prefix,
        // mirroring observable/view collision resolution.
        var sessionSimple = SparseGeneratedPlacement.ResolveUiTypeName("EditSession", members.Select(static member => member.Property.Name));

        code.AppendLine(accessibility + " " + declaration + modelName);
        code.OpenBlock();
        code.AppendLine("/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases += ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
        var hasChildSessionSurface = HasChildSessionSurface(config, patchDialect, hasChildModelMembers);
        if (hasChildSessionSurface)
        {
            sessionBases +=
                ", global::" + config.EditSessionWithChildStatusInterfaceMetadataName + "<" + modelType + ", " + patchDialect.ConflictType + ">";
            if (config.EditChildDraftHostInterfaceMetadataName is { Length: > 0 } childDraftHostInterface)
            {
                sessionBases += ", global::" + childDraftHostInterface + "<" + sessionSimple + ", " + patchDialect.ConflictType + ">";
            }
        }
        else if (config.EditSessionWithPendingStateInterfaceMetadataName is { Length: > 0 } pendingStateInterface)
        {
            sessionBases += ", global::" + pendingStateInterface + "<" + modelType + ", " + patchDialect.ConflictType + ">";
        }
        if (config.EditSessionWithChangeSetInterfaceMetadataName is { Length: > 0 } changeSetInterface)
        {
            sessionBases += ", global::" + changeSetInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        }
        if (config.EditSessionWithBaselineRevisionInterfaceMetadataName is { Length: > 0 } baselineRevisionInterface)
        {
            sessionBases += ", global::" + baselineRevisionInterface;
        }
        sessionBases += SparseEditSessionPathsEmitter.GetPathsInterfaceBase(model, modelType, config, patchDialect, code.CancellationToken);
        sessionBases = SparseEditSessionCapabilityEmitter.AppendSessionCapabilityBases(
            sessionBases,
            modelType,
            config,
            patchDialect,
            historyBinding?.HasOwnedHistory == true
        );
        // Sessions already raise PropertyChanged with the standard signature.
        sessionBases += ", global::System.ComponentModel.INotifyPropertyChanged";
        code.AppendLine("public sealed class " + sessionSimple + " : " + sessionBases);
        code.OpenBlock();
        // Public surface first; internal constructors and private state follow.
        code.AppendLine("/// <summary>Gets the bindable proxy over the live model.</summary>");
        code.AppendLine("public " + modelType + "." + observable + " Observable => _session.Observable;");
        if (config.EditSessionWithBaselineRevisionInterfaceMetadataName is { Length: > 0 })
        {
            code.AppendLine("/// <summary>Gets the revision of the retained baseline.</summary>");
            code.AppendLine("public long BaselineRevision => _session.BaselineRevision;");
        }
        code.AppendLine("/// <summary>Gets the read-only view over the live model.</summary>");
        code.AppendLine("public " + modelType + "." + readOnlyView + " Current => _session.Current;");
        if (modelAccessorInterfaceMetadataName is not null)
        {
            code.AppendLine("/// <summary>Gets the live model for trusted framework use without affecting the change cache.</summary>");
            code.AppendLine("public " + modelType + " GetModelForFrameworkAccess() => _session.GetModelForFrameworkAccess();");
        }
        if (config.DescriptorDialect is { } descriptorDialect && config.EffectiveEmissionFeatures.EmitObservable)
        {
            code.AppendLine("/// <summary>Gets descriptors bound to this session's observable model.</summary>");
            code.AppendLine(
                "public "
                    + descriptorDialect.DescriptorSetInterface
                    + " Descriptors => __descriptors ?? (__descriptors = Observable."
                    + SparseObservableDescriptorEmitter.AccessorName(modelType)
                    + "("
                    + descriptorDialect.EffectivePathType(config.RuntimeDialect?.Namespace ?? patchDialect.RuntimeNamespace)
                    + ".Root(typeof("
                    + modelType
                    + "))));"
            );
        }
        code.AppendLine(
            "/// <summary>Gets whether "
                + (HasChildSessionSurface(config, patchDialect, hasChildModelMembers) ? "this session or any child session" : "the session")
                + " has pending changes.</summary>"
        );
        code.AppendLine(
            "public bool HasChanges => _session.HasChanges"
                + (HasChildSessionSurface(config, patchDialect, hasChildModelMembers) ? " || HasPendingChildSessions" : string.Empty)
                + ";"
        );
        if (hasChildSessionSurface)
        {
            code.AppendLine("/// <summary>Gets whether this session has changes apart from its registered child sessions.</summary>");
            code.AppendLine("public bool HasOwnChanges => _session.HasChanges;");
            var childPathType = config.DescriptorDialect!.EffectivePathType(config.RuntimeDialect?.Namespace ?? patchDialect.RuntimeNamespace);
            code.AppendLine("/// <summary>Resolves the model currently addressed by a child-session path.</summary>");
            code.AppendLine("public object? ResolveChildModel(" + childPathType + " path) => Descriptors.ResolveModel(path);");
        }
        SparseEditSessionPathsEmitter.AppendPathsProperty(code, model, modelType, config, patchDialect);
        code.AppendLine("/// <summary>Occurs when the session state or current view changes.</summary>");
        code.AppendLine(
            "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add => _session.PropertyChanged += value; remove => _session.PropertyChanged -= value; }"
        );
        code.AppendLine("/// <summary>Occurs when the session observes a committed transition.</summary>");
        code.AppendLine(
            "public event global::System.Action<"
                + modelType
                + ".ChangeSet>? TransitionObserved { add => _session.TransitionObserved += value; remove => _session.TransitionObserved -= value; }"
        );
        SparseEditSessionChangeSetEmitter.AppendMembers(code, modelType, config, patchDialect, hasChildModelMembers);
        code.AppendLine("/// <summary>Creates a patch describing the pending changes.</summary>");
        code.AppendLine("/// <returns>The pending patch.</returns>");
        code.AppendLine("public " + modelType + ".Patch CreatePatch() => CreateChangeSet().ToPatch();");
        code.AppendLine("/// <summary>Enumerates the changed member paths.</summary>");
        code.AppendLine("/// <returns>The changed paths in generated member order.</returns>");
        code.AppendLine(
            "public global::System.Collections.Generic.IReadOnlyList<"
                + SparseFragmentPatchEmitter.GetPathType(patchDialect)
                + "> EnumerateChangedPaths() => CreateChangeSet().EnumerateChangedPaths();"
        );
        // Path-first semantic state inspection: live field and root-collection
        // queries over the retained baseline.
        SparseEditSessionStateEmitter.AppendStateMembers(code, modelType, members, patchDialect);
        code.AppendLine("/// <summary>Applies a change set to the current model in place when possible.</summary>");
        code.AppendLine("/// <param name=\"changes\">The change set to apply.</param>");
        code.AppendLine("/// <param name=\"conflicts\">Structured conflicts when the change set cannot be applied in place.</param>");
        code.AppendLine("/// <returns><see langword=\"true\"/> when the model was updated; otherwise <see langword=\"false\"/>.</returns>");
        code.AppendLine(
            "public bool TryApplyInPlace("
                + modelType
                + ".ChangeSet changes, out global::System.Collections.Generic.IReadOnlyList<"
                + conflictType
                + ">? conflicts) => _session.TryApplyInPlace(changes, out conflicts);"
        );
        code.AppendLine("/// <summary>Applies a change set to the current model in place.</summary>");
        code.AppendLine("/// <param name=\"changes\">The change set to apply.</param>");
        code.AppendLine("public void ApplyInPlace(" + modelType + ".ChangeSet changes) => _session.ApplyInPlace(changes);");
        SparseEditSessionHistoryEmitter.AppendLifecycleMembers(
            code,
            modelType,
            conflictType,
            rebaseResultType,
            historyBinding,
            patchDialect.ReconcileConflictExpression?.Invoke(modelType, "error"),
            HasChildSessionSurface(config, patchDialect, hasChildModelMembers)
        );
        // Raw model access bypasses the change cache.
        code.AppendLine(modelType + " " + sessionInterface + "<" + modelType + ">.Model => _session.Model;");
        if (hasChildSessionSurface)
        {
            SparseEditSessionChildEmitter.AppendChildSessionApiMembers(
                code,
                modelType,
                sessionSimple,
                patchDialect,
                config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
                config
            );
        }
        else if (config.EditSessionWithPendingStateInterfaceMetadataName is { Length: > 0 } pendingStateInterface)
        {
            SparseEditSessionChildEmitter.AppendPendingStateProjection(code, modelType, conflictType, pendingStateInterface);
        }
        SparseEditSessionChildEmitter.AppendForkMergeMembers(code, core, conflictType, sessionSimple);
        if (hasChildSessionSurface)
        {
            SparseEditSessionChildEmitter.AppendChildDraftMembers(
                code,
                modelType,
                sessionSimple,
                patchDialect,
                config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
                config
            );
        }
        SparseEditSessionChildEmitter.AppendForkMergeCoreConstructor(code, core, sessionSimple);
        code.AppendLine(
            "internal " + sessionSimple + "(" + modelType + " model, global::System.Action? onChanged) : this(model, model, onChanged) { }"
        );
        code.AppendLine(
            "internal "
                + sessionSimple
                + "("
                + modelType
                + " baseline, "
                + modelType
                + " current, global::System.Action? onChanged) { _session = "
                + core
                + ".Create(baseline, current, __configuration, onChanged); }"
        );
        if (hasChildSessionSurface)
        {
            SparseEditSessionChildEmitter.AppendChildDraftPrivateMembers(
                code,
                modelType,
                sessionSimple,
                patchDialect,
                config.EffectiveEmissionFeatures.EmitObservable ? config.DescriptorDialect : null,
                config
            );
            // Private candidate projection trails every public member.
            SparseEditSessionChildEmitter.AppendCandidateBuilder(code, modelType, patchDialect.ConflictType, patchDialect);
        }
        code.AppendLine(
            "private static readonly "
                + configuration
                + " __configuration = new "
                + configuration
                + " { FromModel = "
                + modelType
                + ".Fragment.From, Between = "
                + modelType
                + ".ChangeSet.Between, ToPatch = static changes => changes.ToPatch(), IsEmpty = static changes => changes.IsEmpty, AdvanceBaseline = static (changes, baseline) => changes.ApplyToBaseline(baseline), ToObservable = (current, changed, rawModelAccess) => new "
                + modelType
                + "."
                + observable
                + "(current, changed, rawModelAccess), ToCurrent = static current => new "
                + modelType
                + "."
                + readOnlyView
                + "(current), TryApplyTo = "
                + tryApply
                + ", WriteModel = "
                + writeModel
                + ", Invert = static changes => changes.Invert(), Rebase = static (changes, server) => changes.RebaseOnto(server), EnumerateChangedPaths = static changes => changes.EnumerateChangedPaths(), RefreshObservable = static observable => observable.__SparseRefresh(), BaselineToModel = static fragment => fragment.ToModel() };"
        );
        code.AppendLine("private readonly " + core + " _session;");
        SparseEditSessionPathsEmitter.AppendPathsField(code, model, modelType, config, patchDialect);
        SparseEditSessionHistoryEmitter.AppendHistoryFields(code, modelType, conflictType, historyBinding);
        // Session-owned observation fan-out trails every public member.
        SparseEditSessionStateEmitter.AppendObservationCoordinatorMembers(code, patchDialect);
        if (config.DescriptorDialect is { } descriptorsDialect && config.EffectiveEmissionFeatures.EmitObservable)
        {
            // Descriptors are live views.
            code.AppendLine("private " + descriptorsDialect.DescriptorSetInterface + "? __descriptors;");
        }
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine();
    }

    private static string ReadTemplate(string resourceName)
    {
        var assembly = typeof(SparseEditSessionEmitter).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                "Embedded edit-session source template is missing: "
                    + resourceName
                    + " in "
                    + assembly.FullName
                    + ". Available resources: "
                    + string.Join(", ", assembly.GetManifestResourceNames())
            );
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RenderTemplate(
        string template,
        SparseEditSessionDialect sessionDialect,
        SparseRuntimeDialect runtimeDialect,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        new StringBuilder(template)
            .Replace("__SESSION_NAMESPACE__", sessionDialect.Namespace)
            .Replace("__OPTIONAL_TYPE__", runtimeDialect.OptionalType)
            .Replace("__CONFLICT_TYPE__", patchDialect.ConflictType)
            .Replace("__REBASE_RESULT_TYPE__", patchDialect.RebaseResult("TChangeSet"))
            .Replace("__PATH_TYPE__", SparseFragmentPatchEmitter.GetPathType(patchDialect))
            .ToString();
}
