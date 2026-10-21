using System;
using System.Collections.Immutable;

namespace SparseTypes.Generator.Shared;

/// <summary>Emits the per-model edit-session class nested in the model.</summary>
/// <remarks>Mirrors <see cref="SparseEditSessionEmitter"/> but takes the model-nested observable and read-only-view references explicitly.</remarks>
internal static class SparseRelocatedEditSessionCore
{
    /// <summary>Appends the model-nested edit-session class.</summary>
    /// <param name="code">Target builder.</param>
    /// <param name="model">Owning model.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="observableRef">Qualified observable reference.</param>
    /// <param name="readOnlyRef">Qualified read-only-view reference.</param>
    public static void Append(
        SharedIndentedBuilder code,
        SparseModelInfo model,
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        string observableRef,
        string readOnlyRef
    )
    {
        var adapterViolations = SparseEditSessionAdapterContract.ValidateAdapterFeatures(config.EffectiveEmissionFeatures);
        if (adapterViolations.Length != 0)
        {
            throw new ArgumentException(
                "Invalid edit-session adapter plan for '" + model.ModelTypeName + "': " + string.Join(" ", adapterViolations),
                nameof(config)
            );
        }

        var modelType = model.ModelTypeName;
        var sessionInterfaceMetadataName =
            config.EditSessionInterfaceMetadataName
            ?? throw new ArgumentException("An edit-session interface metadata name is required.", nameof(config));
        var modelAccessorInterfaceMetadataName = config.EditSessionModelAccessorInterfaceMetadataName;
        var sessionDialect = config.EditSessionDialect ?? throw new ArgumentException("An edit-session dialect is required.", nameof(config));
        var patchDialect = config.PatchDialect ?? throw new ArgumentException("A patch dialect is required.", nameof(config));
        var hasChildModelMembers = SparseEditSessionEmitter.HasChildModelMembers(members);
        var hasChildSessionSurface = SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect, hasChildModelMembers);
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
            + observableRef
            + ", "
            + readOnlyRef
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
            + observableRef
            + ", "
            + readOnlyRef
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

        code.AppendLine("/// <summary>A typed edit session for this model.</summary>");
        var sessionBases = sessionInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
        if (modelAccessorInterfaceMetadataName is not null)
        {
            sessionBases += ", global::" + modelAccessorInterfaceMetadataName + "<" + modelType + ">";
        }
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
        if (config.EditSessionPipelineInterfaceMetadataName is { Length: > 0 } pipelineInterface)
        {
            sessionBases += ", global::" + pipelineInterface + "<" + modelType + ", " + modelType + ".ChangeSet>";
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
        code.AppendLine("public " + observableRef + " Observable => _session.Observable;");
        if (config.EditSessionWithBaselineRevisionInterfaceMetadataName is { Length: > 0 })
        {
            code.AppendLine("/// <summary>Gets the revision of the retained baseline.</summary>");
            code.AppendLine("public long BaselineRevision => _session.BaselineRevision;");
        }
        code.AppendLine("/// <summary>Gets the read-only view over the live model.</summary>");
        code.AppendLine("public " + readOnlyRef + " Current => _session.Current;");
        if (modelAccessorInterfaceMetadataName is not null)
        {
            code.AppendLine("/// <summary>Gets the live model for trusted framework use without affecting the change cache.</summary>");
            code.AppendLine("public " + modelType + " GetModelForFrameworkAccess() => _session.GetModelForFrameworkAccess();");
        }
        if (config.DescriptorDialect is { } descriptorDialect && config.EffectiveEmissionFeatures.EmitObservable)
        {
            var factoryRef = SparseGeneratedPlacement.GetDescriptorFactoryReference(model, config, code.CancellationToken);
            code.AppendLine("/// <summary>Gets descriptors bound to this session's observable model.</summary>");
            code.AppendLine(
                "public "
                    + descriptorDialect.DescriptorSetInterface
                    + " Descriptors => __descriptors ?? (__descriptors = "
                    + factoryRef
                    + ".Create(Observable, "
                    + descriptorDialect.EffectivePathType(config.RuntimeDialect?.Namespace ?? patchDialect.RuntimeNamespace)
                    + ".Root(typeof("
                    + modelType
                    + "))));"
            );
        }
        code.AppendLine(
            "/// <summary>Gets whether "
                + (hasChildSessionSurface ? "this session or any child session" : "the session")
                + " has pending changes.</summary>"
        );
        code.AppendLine(
            "public bool HasChanges => _session.HasChanges"
                + (
                    SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect, hasChildModelMembers)
                        ? " || HasPendingChildSessions"
                        : string.Empty
                )
                + ";"
        );
        if (SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect, hasChildModelMembers))
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
        // Path-first semantic state inspection; shared with the
        // model-nested session placement so both surfaces stay identical.
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
            SparseEditSessionEmitter.HasChildSessionSurface(config, patchDialect, hasChildModelMembers)
        );
        if (config.EditSessionPipelineInterfaceMetadataName is { Length: > 0 } sessionPipelineInterface)
        {
            code.AppendLine(
                "bool global::"
                    + sessionPipelineInterface
                    + "<"
                    + modelType
                    + ", "
                    + modelType
                    + ".ChangeSet>.TryReconcileForPipeline("
                    + modelType
                    + ".ChangeSet submitted, "
                    + modelType
                    + " persisted, out global::System.Collections.Generic.IReadOnlyList<"
                    + conflictType
                    + ">? conflicts) => TryReconcileAndApply(submitted, persisted, out conflicts);"
            );
        }
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
                + observableRef
                + "(current, changed, rawModelAccess), ToCurrent = static current => new "
                + readOnlyRef
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
            code.AppendLine("private " + descriptorsDialect.DescriptorSetInterface + "? __descriptors;");
        }
        code.CloseBlock();
    }
}
