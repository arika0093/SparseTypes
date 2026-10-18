extern alias Playground;

using Playground::SparseTypes.Playground.Models;
using Playground::SparseTypes.Playground.Pages.Case1;
using Playground::SparseTypes.Playground.Pages.Case2;
using Playground::SparseTypes.Playground.Pages.Case4;
using Playground::SparseTypes.Playground.Pages.Case5;

namespace SparseTypes.Tests.Components;

// Headless tests for #331: page preview generations share intermediate
// fragments/models across panes and serialize each presentation once.
public sealed class PlaygroundPreviewMemoizationTests
{
    private sealed class Counter
    {
        public int Calls;

        public void Increment() => Calls++;
    }

    private static Case2Preview Case2Preview(
        FragmentEditState lower,
        FragmentEditState higher,
        PatchEditState patch,
        Counter? lowerCalls = null,
        Counter? higherCalls = null,
        Counter? patchCalls = null,
        Counter? snippetCalls = null
    ) =>
        new(
            () =>
            {
                lowerCalls?.Increment();
                return lower.Build();
            },
            () =>
            {
                higherCalls?.Increment();
                return higher.Build();
            },
            () =>
            {
                patchCalls?.Increment();
                return patch.Build();
            },
            () =>
            {
                snippetCalls?.Increment();
                return PlaygroundSnippets.PatchCSharp("patch", patch);
            }
        );

    private static Case1Preview Case1Preview(
        FragmentEditState lower,
        FragmentEditState higher,
        Counter? lowerCalls = null,
        Counter? higherCalls = null
    ) =>
        new(
            () =>
            {
                lowerCalls?.Increment();
                return lower.Build("lower");
            },
            () =>
            {
                higherCalls?.Increment();
                return higher.Build("higher");
            }
        );

    [Test]
    public void Case2SharesFragmentsAcrossRepresentations()
    {
        var lowerCalls = new Counter();
        var higherCalls = new Counter();
        var patchCalls = new Counter();
        var snippetCalls = new Counter();
        var preview = Case2Preview(
            FragmentEditState.LowerDefaults(),
            FragmentEditState.HigherDefaults(),
            PatchEditState.Defaults(),
            lowerCalls,
            higherCalls,
            patchCalls,
            snippetCalls
        );

        // Every pane reads intermediates, but each builds exactly once.
        _ = preview.MergedFragmentJson;
        _ = preview.MergedModelJson;
        _ = preview.MergedModelCSharp;
        _ = preview.PatchedFragmentJson;
        _ = preview.PatchedModelJson;
        _ = preview.PatchedModelCSharp;
        _ = preview.PatchCSharp;
        _ = preview.ChangeSetExportText;

        lowerCalls.Calls.ShouldBe(1);
        higherCalls.Calls.ShouldBe(1);
        patchCalls.Calls.ShouldBe(1);
        snippetCalls.Calls.ShouldBe(1);

        // Repeated reads reuse the cached text without re-serializing.
        ReferenceEquals(preview.MergedModelJson, preview.MergedModelJson).ShouldBeTrue();
        ReferenceEquals(preview.PatchedFragmentJson, preview.PatchedFragmentJson).ShouldBeTrue();
        ReferenceEquals(preview.ChangeSetExportText, preview.ChangeSetExportText).ShouldBeTrue();
        lowerCalls.Calls.ShouldBe(1);
        patchCalls.Calls.ShouldBe(1);
    }

    [Test]
    public void Case2OutputsMatchDirectComputation()
    {
        var lower = FragmentEditState.LowerDefaults();
        var higher = FragmentEditState.HigherDefaults();
        var patch = PatchEditState.Defaults();
        var preview = Case2Preview(lower, higher, patch);

        preview.MergedFragmentJson.ShouldBe(PlaygroundJson.WriteFragment(preview.MergedFragment));
        preview.MergedModelJson.ShouldBe(PlaygroundJson.WriteModel(preview.MergedModel));
        preview.PatchedModelJson.ShouldBe(PlaygroundJson.WriteModel(preview.PatchedModel));
        preview.PatchCSharp.ShouldBe(PlaygroundSnippets.PatchCSharp("patch", patch));
        preview.ChangeSetExportText.ShouldBe(
            PlaygroundJson.WriteSettingsChangeSet(
                PlaygroundSettings.ChangeSet.FromPatch(
                    Optional<PlaygroundSettings.Fragment?>.Present(preview.MergedFragment),
                    preview.TypedPatch
                )
            )
        );

        preview.MergedModel.Label.ShouldBeNull();
        preview.PatchedModel.Label.ShouldBe("patched!");
    }

    [Test]
    public void Case2CachesErrorsUntilInputsChange()
    {
        var failing = Case2Preview(
            FragmentEditState.LowerDefaults(),
            FragmentEditState.HigherDefaults(),
            PatchEditState.Defaults(),
            lowerCalls: new Counter()
        );
        // A fresh generation over changed inputs retries instead of
        // surfacing the retained error text.
        var broken = new Case2Preview(
            () => throw new InvalidOperationException("Lower is unavailable."),
            () => FragmentEditState.HigherDefaults().Build(),
            () => PatchEditState.Defaults().Build(),
            () => string.Empty
        );
        broken.MergedFragmentJson.ShouldStartWith("Error: InvalidOperationException");
        broken.MergedFragmentJson.ShouldBe(broken.MergedFragmentJson);

        failing.MergedFragmentJson.ShouldBe(PlaygroundJson.WriteFragment(failing.MergedFragment));
    }

    [Test]
    public void Case1SharesFragmentsAcrossRepresentations()
    {
        var lowerCalls = new Counter();
        var higherCalls = new Counter();
        var preview = Case1Preview(
            FragmentEditState.LowerDefaults(includeTags: true),
            FragmentEditState.HigherDefaults(includeTags: true),
            lowerCalls,
            higherCalls
        );

        _ = preview.LowerFragmentJson;
        _ = preview.HigherFragmentJson;
        _ = preview.MergedFragmentJson;
        _ = preview.MergeDiffJson;
        _ = preview.MergedOrigins;

        lowerCalls.Calls.ShouldBe(1);
        higherCalls.Calls.ShouldBe(1);

        ReferenceEquals(preview.MergedFragmentJson, preview.MergedFragmentJson).ShouldBeTrue();
        ReferenceEquals(preview.MergeDiffJson, preview.MergeDiffJson).ShouldBeTrue();
        lowerCalls.Calls.ShouldBe(1);
        higherCalls.Calls.ShouldBe(1);
    }

    [Test]
    public void Case1OutputsMatchDirectComputation()
    {
        var preview = Case1Preview(
            FragmentEditState.LowerDefaults(includeTags: true),
            FragmentEditState.HigherDefaults(includeTags: true)
        );

        preview.LowerFragmentJson.ShouldBe(PlaygroundJson.WriteFragment(preview.LowerFragment));
        preview.MergeDiffJson.ShouldBe(
            PlaygroundJson.WriteFragment(
                PlaygroundSettings.Fragment.Diff(preview.LowerModel, preview.MergedModel)
            )
        );
        preview.MergedOrigins.ShouldContain("lower");
        preview.MergedOrigins.ShouldContain("higher");
        preview.LowerModel.Label.ShouldBe("default");
        preview.MergedModel.Label.ShouldBeNull();
    }

    [Test]
    public void Case4SharesChangeSetAcrossPanes()
    {
        var model = new PlaygroundSettings
        {
            Enabled = true,
            RetryCount = 3,
            Label = "Draft",
        };
        var session = model.CreateEditSession();
        session.Observable.Label = "Edited";

        var summaryCalls = 0;
        var changeSetCalls = 0;
        var preview = new Case4Preview(
            () =>
            {
                summaryCalls++;
                return PlaygroundJson.WriteMaskedSettingsSummary(model);
            },
            () =>
            {
                changeSetCalls++;
                return session.CreateChangeSet();
            }
        );

        _ = preview.CurrentJson;
        _ = preview.ChangePayloadJson;
        _ = preview.ChangePayloadJson;
        ReferenceEquals(preview.CurrentJson, preview.CurrentJson).ShouldBeTrue();

        summaryCalls.ShouldBe(1);
        changeSetCalls.ShouldBe(1);
        preview.ChangePayloadJson.ShouldBe(
            PlaygroundJson.WriteSettingsChangeSet(session.CreateChangeSet())
        );
    }

    [Test]
    public void Case5InvalidatesPanesIndependently()
    {
        var entityCalls = 0;
        var baselineCalls = 0;
        var draftCalls = 0;
        var preview = new Case5Preview(
            () =>
            {
                entityCalls++;
                return """{"entity":1}""";
            },
            () =>
            {
                baselineCalls++;
                return """{"baseline":1}""";
            },
            () =>
            {
                draftCalls++;
                return """{"draft":1}""";
            }
        );

        _ = preview.EntityJson;
        _ = preview.BaselineJson;
        _ = preview.DraftJson;
        ReferenceEquals(preview.EntityJson, preview.EntityJson).ShouldBeTrue();
        entityCalls.ShouldBe(1);
        baselineCalls.ShouldBe(1);
        draftCalls.ShouldBe(1);

        // A draft edit refreshes only the draft pane.
        preview.InvalidateDraft();
        _ = preview.EntityJson;
        _ = preview.BaselineJson;
        _ = preview.DraftJson;
        entityCalls.ShouldBe(1);
        baselineCalls.ShouldBe(1);
        draftCalls.ShouldBe(2);

        // A reload refreshes only the entity pane.
        preview.InvalidateEntity();
        _ = preview.EntityJson;
        entityCalls.ShouldBe(2);
        baselineCalls.ShouldBe(1);
        draftCalls.ShouldBe(2);

        // Adopting a persisted DTO refreshes only the baseline pane.
        preview.InvalidateBaseline();
        _ = preview.BaselineJson;
        entityCalls.ShouldBe(2);
        baselineCalls.ShouldBe(2);
        draftCalls.ShouldBe(2);
    }
}
