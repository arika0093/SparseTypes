using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SparseTypes;

// Compile-checked samples for docs/reference/change-payload.md.
// Run() verifies documented JSON shapes and conversions.
public static class ChangePayloadDocsSamples
{
    public static void Run()
    {
        ScalarSet();
        ExplicitNull();
        Remove();
        Nested();
        Keyed();
        DictionaryItems();
        WholeRoot();
        Command();
        Conversions();
        Mixed();
        Invert();
        VersionGuard();
        Validate();
        PocoLeaf();
    }

    private static bool JsonMatches(string actual, string expected)
    {
        var actualNode = JsonNode.Parse(actual) as JsonObject;
        var expectedNode = JsonNode.Parse(expected) as JsonObject;
        if (actualNode is null || expectedNode is null)
        {
            return JsonNode.DeepEquals(actualNode, expectedNode);
        }

        expectedNode["schema"] = actualNode["schema"]?.DeepClone();
        actualNode.Remove("recordedAt");
        expectedNode.Remove("recordedAt");
        return JsonNode.DeepEquals(actualNode, expectedNode);
    }

    private static void ScalarSet()
    {
        // sample: payload-scalar-set
        var setBefore = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 }));
        var setAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));

        var setPayload = WireSettings.ChangeSet.Between(setBefore, setAfter).ToPayload();
        var setJson = JsonSerializer.Serialize(setPayload, WireSettings.PayloadJson.Default);
        // setJson == {
        //   "version": "0.1",
        //   "schema": "sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59",
        //   "change": {
        //     "kind": "update",
        //     "members": {
        //       "label": {
        //         "kind": "replace",
        //         "before": { "state": "value", "value": "before" },
        //         "after": { "state": "value", "value": "after" }
        //       }
        //     }
        //   }
        // }
        var setRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(setJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        // setRestored.ToPatch().Apply(setBefore) replays setAfter
        // /sample
        DocsCheck.Require(
            JsonMatches(
                setJson,
                """
                {"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}}}}
                """
            ),
            "scalar set emits the documented envelope"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(setRestored.ToPatch().Apply(setBefore), setAfter).IsEmpty,
            "deserialized scalar set replays the transition"
        );
        DocsCheck.Require(JsonMatches(setJson, ChangePayloadJsonSpecimens.Envelope), "minimal envelope matches the layout reference");
        var setAddBefore = Optional<WireSettings.Fragment?>.Present(new WireSettings.Fragment { RetryCount = 1 });
        var setAddAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));
        var setAddJson = JsonSerializer.Serialize(
            WireSettings.ChangeSet.Between(setAddBefore, setAddAfter).ToPayload(),
            WireSettings.PayloadJson.Default
        );
        DocsCheck.Require(JsonMatches(setAddJson, ChangePayloadJsonSpecimens.ScalarAdd), "addition emits the documented envelope");
        var setAddRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(setAddJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        DocsCheck.Require(
            WireSettings.Patch.Between(setAddRestored.ToPatch().Apply(setAddBefore), setAddAfter).IsEmpty,
            "deserialized addition replays the transition"
        );

        // sample: payload-context
        var contextJson = JsonSerializer.Serialize(setPayload, WireSettings.PayloadJson.Default);
        var contextRestored = JsonSerializer.Deserialize(contextJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        // contextRestored.ToPatch().Apply(setBefore) replays setAfter
        // /sample
        DocsCheck.Require(
            WireSettings.Patch.Between(contextRestored.ToPatch().Apply(setBefore), setAfter).IsEmpty,
            "resolver-based serialization replays the transition"
        );
    }

    private static void ExplicitNull()
    {
        // sample: payload-explicit-null
        var nullBefore = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 }));
        var nullAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = null, RetryCount = 1 }));

        var nullPayload = WireSettings.ChangeSet.Between(nullBefore, nullAfter).ToPayload();
        var nullJson = JsonSerializer.Serialize(nullPayload, WireSettings.PayloadJson.Default);
        // nullJson == {
        //   "version": "0.1",
        //   "schema": "sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59",
        //   "change": {
        //     "kind": "update",
        //     "members": {
        //       "label": {
        //         "kind": "replace",
        //         "before": { "state": "value", "value": "before" },
        //         "after": { "state": "null" }
        //       }
        //     }
        //   }
        // }
        var nullRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(nullJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        // nullRestored.ToPatch().Apply(nullBefore) replays nullAfter
        // /sample
        DocsCheck.Require(
            JsonMatches(
                nullJson,
                """
                {"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"before"},"after":{"state":"null"}}}}}
                """
            ),
            "explicit null emits the documented endpoint"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(nullRestored.ToPatch().Apply(nullBefore), nullAfter).IsEmpty,
            "deserialized explicit null replays the transition"
        );
    }

    private static void Remove()
    {
        // sample: payload-remove
        var removeBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var removeAfter = Optional<WireSettings.Fragment?>.Present(new WireSettings.Fragment { RetryCount = 1 });

        var removePayload = WireSettings.ChangeSet.Between(removeBefore, removeAfter).ToPayload();
        var removeJson = JsonSerializer.Serialize(removePayload, WireSettings.PayloadJson.Default);
        // removeJson == {
        //   "version": "0.1",
        //   "schema": "sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59",
        //   "change": {
        //     "kind": "update",
        //     "members": {
        //       "label": {
        //         "kind": "replace",
        //         "before": { "state": "value", "value": "before" },
        //         "after": { "state": "missing" }
        //       }
        //     }
        //   }
        // }
        var removeRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(removeJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        // !removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent
        // /sample
        DocsCheck.Require(
            JsonMatches(
                removeJson,
                """
                {"version":"0.1","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"before"},"after":{"state":"missing"}}}}}
                """
            ),
            "removal emits the documented endpoint"
        );
        DocsCheck.Require(!removeRestored.ToPatch().Apply(removeBefore).Value!.Label.IsPresent, "deserialized removal drops the contribution");
    }

    private static void PocoLeaf()
    {
        // sample: payload-poco-leaf
        var pocoBefore = Optional<PocoWidget.Fragment?>.Present(
            PocoWidget.Fragment.From(
                new PocoWidget
                {
                    Leaf = new PocoLeafDto
                    {
                        Name = "before",
                        Count = 1,
                        Label = "t",
                        Secret = "s",
                    },
                }
            )
        );
        var pocoAfter = Optional<PocoWidget.Fragment?>.Present(
            PocoWidget.Fragment.From(
                new PocoWidget
                {
                    Leaf = new PocoLeafDto
                    {
                        Name = "after",
                        Count = 2,
                        Label = "t",
                        Secret = "s",
                    },
                }
            )
        );
        var pocoPayload = PocoWidget.ChangeSet.Between(pocoBefore, pocoAfter).ToPayload();
        var pocoJson = JsonSerializer.Serialize(pocoPayload, PocoWidget.PayloadJson.Default);
        var pocoRestored = JsonSerializer.Deserialize(pocoJson, PocoWidget.PayloadJson.Default)!.ToChangeSet();
        // pocoRestored.ToPatch().Apply(pocoBefore) replays pocoAfter
        // /sample
        DocsCheck.Require(pocoJson.Contains("\"tag\":\"t\""), "renamed leaf property travels without registration");
        DocsCheck.Require(!pocoJson.Contains("Secret"), "ignored leaf property is omitted");
        var pocoApplied = pocoRestored.ToPatch().Apply(pocoBefore).Value!;
        DocsCheck.Require(pocoApplied.Leaf.Value!.Name == "after", "leaf after-state arrives");
        DocsCheck.Require(pocoApplied.Leaf.Value!.Count == 2, "leaf count arrives");
        DocsCheck.Require(pocoApplied.Leaf.Value!.Secret is null, "ignored leaf stays null like System.Text.Json defaults");
    }

    private static void Nested()
    {
        // sample: payload-nested
        var nestedBefore = Optional<WireOrder.Fragment?>.Present(
            WireOrder.Fragment.From(
                new WireOrder
                {
                    Name = "a",
                    Customer = new WireCustomer { Name = "Ann" },
                }
            )
        );
        var nestedAfter = Optional<WireOrder.Fragment?>.Present(
            WireOrder.Fragment.From(
                new WireOrder
                {
                    Name = "a",
                    Customer = new WireCustomer { Name = "Bob" },
                }
            )
        );

        var nestedPayload = WireOrder.ChangeSet.Between(nestedBefore, nestedAfter).ToPayload();
        var nestedJson = JsonSerializer.Serialize(nestedPayload, WireOrder.PayloadJson.Default);
        // nestedJson carries an optional customer update with no inner version
        var nestedRestored = JsonSerializer.Deserialize<WireOrder.ChangePayload>(nestedJson, WireOrder.PayloadJson.Default)!.ToChangeSet();
        // nestedRestored.Customer.Name.After.Value == "Bob"
        // /sample
        DocsCheck.Require(
            JsonMatches(
                nestedJson,
                """{"version":"0.1","schema":"sha256:975073d6e08b95584b9dfbfc949a86450b232296ff6c0849bcd6accf5a5b1aaa","change":{"kind":"update","members":{"customer":{"kind":"update","members":{"name":{"kind":"replace","before":{"state":"value","value":"Ann"},"after":{"state":"value","value":"Bob"}}}}}}}"""
            ),
            "nested change emits the documented envelope"
        );
        DocsCheck.Require(nestedRestored.Customer.Name.After.Value == "Bob", "deserialized nested change preserves the leaf transition");
        DocsCheck.Require(JsonMatches(nestedJson, ChangePayloadJsonSpecimens.Nested), "nested fence matches serializer output");
    }

    private static void Keyed()
    {
        // sample: payload-keyed
        var keyedBefore = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "a", Host = "A" },
                        new WireServer { Id = "b", Host = "B" },
                    },
                }
            )
        );
        var keyedAfter = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "b", Host = "B2" },
                        new WireServer { Id = "c", Host = "C" },
                    },
                }
            )
        );

        var keyedPayload = WireFleet.ChangeSet.Between(keyedBefore, keyedAfter).ToPayload();
        var keyedJson = JsonSerializer.Serialize(keyedPayload, WireFleet.PayloadJson.Default);
        // keyedJson contains an update that moves b and changes its Host,
        // an addition of c at index 1, and a removal of a from index 0.
        // beforeOrder is ["a","b"] and afterOrder is ["b","c"].
        var keyedRestored = JsonSerializer.Deserialize<WireFleet.ChangePayload>(keyedJson, WireFleet.PayloadJson.Default)!.ToChangeSet();
        // keyedRestored.Servers.GetChange("b").IsEdited == true
        // keyedRestored.Servers.GetChange("c").IsAdded == true
        // keyedRestored.Servers.GetChange("a").IsRemoved == true
        // /sample
        DocsCheck.Require(JsonMatches(keyedJson, ChangePayloadJsonSpecimens.Keyed), "keyed change emits the documented envelope with final indexes");
        DocsCheck.Require(
            !keyedJson.Contains("\"before\":null") && !keyedJson.Contains("\"after\":null") && !keyedJson.Contains("\"edit\":null"),
            "unused item fields stay omitted"
        );
        DocsCheck.Require(
            keyedRestored.Servers.GetChange("b").IsEdited
                && keyedRestored.Servers.GetChange("c").IsAdded
                && keyedRestored.Servers.GetChange("a").IsRemoved,
            "deserialized keyed change preserves add/edit/remove"
        );
        var reorderBefore = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "a", Host = "A" },
                        new WireServer { Id = "b", Host = "B" },
                    },
                }
            )
        );
        var reorderAfter = Optional<WireFleet.Fragment?>.Present(
            WireFleet.Fragment.From(
                new WireFleet
                {
                    Servers = new()
                    {
                        new WireServer { Id = "b", Host = "B" },
                        new WireServer { Id = "a", Host = "A" },
                    },
                }
            )
        );
        var reorderJson = JsonSerializer.Serialize(
            WireFleet.ChangeSet.Between(reorderBefore, reorderAfter).ToPayload(),
            WireFleet.PayloadJson.Default
        );
        DocsCheck.Require(JsonMatches(reorderJson, ChangePayloadJsonSpecimens.Reorder), "pure reorder emits the documented envelope");
        var reorderRestored = JsonSerializer.Deserialize<WireFleet.ChangePayload>(reorderJson, WireFleet.PayloadJson.Default)!.ToChangeSet();
        DocsCheck.Require(reorderRestored.Servers.AfterOrder.SequenceEqual(new[] { "b", "a" }), "deserialized reorder preserves the key sequence");
    }

    private static void DictionaryItems()
    {
        var dictBefore = Optional<WireScores.Fragment?>.Present(
            WireScores.Fragment.From(
                new WireScores
                {
                    Scores = new() { ["a"] = 1, ["b"] = 2 },
                }
            )
        );
        var dictAfter = Optional<WireScores.Fragment?>.Present(
            WireScores.Fragment.From(
                new WireScores
                {
                    Scores = new() { ["b"] = 3, ["c"] = 4 },
                }
            )
        );
        var dictJson = JsonSerializer.Serialize(WireScores.ChangeSet.Between(dictBefore, dictAfter).ToPayload(), WireScores.PayloadJson.Default);
        DocsCheck.Require(JsonMatches(dictJson, ChangePayloadJsonSpecimens.Dictionary), "dictionary change emits the documented envelope");
        var dictRestored = JsonSerializer.Deserialize<WireScores.ChangePayload>(dictJson, WireScores.PayloadJson.Default)!.ToChangeSet();
        DocsCheck.Require(
            WireScores.Patch.Between(dictRestored.ToPatch().Apply(dictBefore), dictAfter).IsEmpty,
            "deserialized dictionary change replays the transition"
        );
    }

    private static void WholeRoot()
    {
        var rootBefore = Optional<WireSettings.Fragment?>.Missing;
        var rootAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "present", RetryCount = 3 }));
        var rootJson = JsonSerializer.Serialize(WireSettings.ChangeSet.Between(rootBefore, rootAfter).ToPayload(), WireSettings.PayloadJson.Default);
        DocsCheck.Require(JsonMatches(rootJson, ChangePayloadJsonSpecimens.Root), "whole-root transition emits the documented envelope");
        var rootRestored = JsonSerializer.Deserialize<WireSettings.ChangePayload>(rootJson, WireSettings.PayloadJson.Default)!.ToChangeSet();
        DocsCheck.Require(
            WireSettings.Patch.Between(rootRestored.ToPatch().Apply(rootBefore), rootAfter).IsEmpty,
            "deserialized whole-root transition replays"
        );
    }

    private static void Command()
    {
        // sample: payload-command
        var rotation = new WireSecret.Patch { Password = "rotated-value" };

        // A command needs no baseline: the envelope redacts what it never observed.
        var command = WireSecret.ChangePayload.FromPatch(rotation);
        var commandJson = JsonSerializer.Serialize(command, WireSecret.PayloadJson.Default);
        // commandJson == {
        //   "version": "0.1",
        //   "schema": "sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59",
        //   "change": {
        //     "kind": "update",
        //     "members": {
        //       "password": {
        //         "kind": "replace",
        //         "before": { "state": "redacted" },
        //         "after": { "state": "value", "value": "rotated-value" }
        //       }
        //     }
        //   }
        // }
        var applied = WireSecret.Fragment.From(new WireSecret { DisplayName = "a", Password = "previous-placeholder" }).Apply(command.ToPatch());
        // applied.Password.Value == "rotated-value"
        // /sample
        DocsCheck.Require(
            JsonMatches(
                commandJson,
                """
                {"version":"0.1","change":{"kind":"update","members":{"password":{"kind":"replace","before":{"state":"redacted"},"after":{"state":"value","value":"rotated-value"}}}}}
                """
            ),
            "baseline-free command emits the documented envelope"
        );
        DocsCheck.Require(applied.Password.Value == "rotated-value", "command patch applies without a baseline");
        var rejected = false;
        try
        {
            command.ToChangeSet();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        DocsCheck.Require(rejected, "redacted command cannot form a ChangeSet");
        var staticRejected = false;
        try
        {
            WireSecret.ChangeSet.FromPayload(command);
        }
        catch (ArgumentException)
        {
            staticRejected = true;
        }
        DocsCheck.Require(staticRejected, "FromPayload rejects redacted envelopes");
    }

    private static void Conversions()
    {
        // sample: payload-conversions
        var convertBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var convertAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));

        // ChangeSet -> envelope -> ChangeSet keeps the before-state.
        var transition = WireSettings.ChangeSet.Between(convertBefore, convertAfter);
        var envelope = transition.ToPayload();
        var roundTripped = JsonSerializer
            .Deserialize<WireSettings.ChangePayload>(
                JsonSerializer.Serialize(envelope, WireSettings.PayloadJson.Default),
                WireSettings.PayloadJson.Default
            )!
            .ToChangeSet();
        // roundTripped.ToPatch().Apply(convertBefore) replays convertAfter

        // ChangeSet -> Patch discards the before-state explicitly.
        // Patch + known baseline -> ChangeSet reattaches it.
        var operations = transition.ToPatch();
        var reattached = WireSettings.ChangeSet.FromPatch(convertBefore, operations);
        // reattached.ToPatch().Apply(convertBefore) replays convertAfter

        // Envelope -> ChangeSet requires complete history: FromPayload rejects redacted envelopes.
        var complete = WireSettings.ChangeSet.FromPayload(envelope);
        // complete.ToPatch().Apply(convertBefore) replays convertAfter
        // /sample
        DocsCheck.Require(
            WireSettings.Patch.Between(roundTripped.ToPatch().Apply(convertBefore), convertAfter).IsEmpty,
            "envelope round trip preserves the transition"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(reattached.ToPatch().Apply(convertBefore), convertAfter).IsEmpty,
            "reattached baseline replays the transition"
        );
        DocsCheck.Require(
            WireSettings.Patch.Between(complete.ToPatch().Apply(convertBefore), convertAfter).IsEmpty,
            "FromPayload accepts complete envelopes"
        );
    }

    private static void Mixed()
    {
        // sample: payload-mixed
        var mixed = JsonSerializer.Deserialize<WireSecret.ChangePayload>(
            """
            {
              "version": "0.1",
              "schema": "sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59",
              "change": {
                "kind": "update",
                "members": {
                  "displayName": {
                    "kind": "replace",
                    "before": { "state": "value", "value": "a" },
                    "after": { "state": "value", "value": "b" }
                  },
                  "password": {
                    "kind": "replace",
                    "before": { "state": "redacted" },
                    "after": { "state": "value", "value": "rotated-value" }
                  }
                }
              }
            }
            """,
            WireSecret.PayloadJson.Default
        )!;
        var mixedCurrent = new WireSecret { DisplayName = "a", Password = "current-placeholder" };
        if (!mixed.TryApplyMixedTo(mixedCurrent, out var mixedUpdated, out var mixedOutcome))
        {
            throw new InvalidOperationException("The change conflicts with the current model.");
        }

        // mixedUpdated.DisplayName == "b"
        // mixedUpdated.Password == "rotated-value"
        // mixedOutcome.WriteOnlyPaths reports ["Password"]
        // /sample
        DocsCheck.Require(
            mixedUpdated!.DisplayName == "b" && mixedUpdated.Password == "rotated-value",
            "mixed request applies the rebased transition and the write-only set"
        );
        DocsCheck.Require(mixedOutcome.WriteOnlyPaths.SequenceEqual(new[] { "Password" }), "mixed outcome names the write-only path");
    }

    private static void Invert()
    {
        // sample: payload-invert
        var invertBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var invertAfter = Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 }));
        var invertible = WireSettings.ChangeSet.Between(invertBefore, invertAfter).ToPayload();

        // Fully baseline-aware envelopes invert with nothing skipped.
        var rollback = invertible.InvertReversibleChanges(out var skipped);
        // skipped is empty
        // rollback.ToPatch().Apply(invertAfter) walks back to invertBefore

        // Write-only members have no prior value, so they stay out of the rollback.
        var writeOnly = WireSecret.ChangePayload.FromPatch(new WireSecret.Patch { Password = "rotated-value" });
        var partial = writeOnly.InvertReversibleChanges(out var skippedWriteOnly);
        // skippedWriteOnly reports ["Password"]
        // partial carries no member change
        // /sample
        DocsCheck.Require(skipped.Count == 0, "complete envelope inverts with nothing skipped");
        DocsCheck.Require(WireSettings.Patch.Between(rollback.ToPatch().Apply(invertAfter), invertBefore).IsEmpty, "inverted envelope walks back");
        DocsCheck.Require(skippedWriteOnly.SequenceEqual(new[] { "Password" }), "write-only path is reported as skipped");
        DocsCheck.Require(partial.IsEmpty, "write-only envelope has no reversible change");
    }

    private static void VersionGuard()
    {
        // sample: payload-version
        var guarded = WireSettings
            .ChangeSet.Between(
                Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "before" })),
                Optional<WireSettings.Fragment?>.Present(WireSettings.Fragment.From(new WireSettings { Label = "after" }))
            )
            .ToPayload();
        guarded.Version = "0.2";

        // Every interpretation path rejects the same unknown version before touching model state.
        var versionRejected = 0;
        try
        {
            guarded.ToChangeSet();
        }
        catch (ArgumentException)
        {
            versionRejected++;
        }
        try
        {
            guarded.ToPatch();
        }
        catch (ArgumentException)
        {
            versionRejected++;
        }
        // versionRejected == 2
        // /sample
        DocsCheck.Require(versionRejected == 2, "unknown version rejected on every path");
    }

    private static void Validate()
    {
        // sample: payload-validate
        var validateBefore = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "before", RetryCount = 1 })
        );
        var validateAfter = Optional<WireSettings.Fragment?>.Present(
            WireSettings.Fragment.From(new WireSettings { Label = "after", RetryCount = 1 })
        );

        var validateEnvelope = WireSettings.ChangeSet.Between(validateBefore, validateAfter).ToPayload();
        var validation = validateEnvelope.Validate();
        // validation.IsValid is true
        // validation.Errors is empty

        // Write-only envelopes validate too: validity is not convertibility.
        var writeOnlyEnvelope = WireSecret.ChangePayload.FromPatch(new WireSecret.Patch { Password = "rotated-value" });
        var writeOnlyValidation = writeOnlyEnvelope.Validate();
        // writeOnlyValidation.IsValid is true
        // writeOnlyEnvelope.ToChangeSet() still throws

        validateEnvelope.Version = "0.2";
        var versionValidation = validateEnvelope.Validate();
        // versionValidation.IsValid is false
        // versionValidation.Errors carries one Version error with an empty path
        // /sample
        DocsCheck.Require(validation.IsValid, "ordinary envelope validates");
        DocsCheck.Require(validation.Errors.Count == 0, "ordinary envelope has no errors");
        DocsCheck.Require(writeOnlyValidation.IsValid, "write-only envelope validates");
        var writeOnlyConvertible = true;
        try
        {
            writeOnlyEnvelope.ToChangeSet();
        }
        catch (ArgumentException)
        {
            writeOnlyConvertible = false;
        }
        DocsCheck.Require(!writeOnlyConvertible, "valid write-only envelope stays unconvertible");
        DocsCheck.Require(!versionValidation.IsValid, "unknown version fails validation");
        DocsCheck.Require(
            versionValidation.Errors.Count == 1
                && versionValidation.Errors[0].Kind == ChangePayloadValidationKind.Version
                && versionValidation.Errors[0].Path == string.Empty,
            "version failure carries one Version error with an empty path"
        );
    }
}

// sample: payload-models
[SparseModel]
public partial class WireSettings
{
    public string? Label { get; set; }

    public int RetryCount { get; set; }
}

[SparseModel]
public partial class WireOrder
{
    public string? Name { get; set; }

    public WireCustomer? Customer { get; set; }
}

public partial class WireCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class WireFleet
{
    public List<WireServer> Servers { get; set; } = new();
}

public partial class WireServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}

[SparseModel]
public partial class WireSecret
{
    public string? DisplayName { get; set; }

    [SparseRedactBefore]
    public string? Password { get; set; }
}

// /sample

[SparseModel]
public partial class WireScores
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

public class PocoLeafDto
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    [JsonPropertyName("tag")]
    public string? Label { get; set; }

    [JsonIgnore]
    public string? Secret { get; set; }
}

[SparseModel]
public partial class PocoWidget
{
    [SparseMerge(MergeMode.Replace)]
    public PocoLeafDto? Leaf { get; set; }
}

// JSON specimens are compared deeply with the matching guide blocks.
// Assertions also compare live serializer output against these constants.
public static class ChangePayloadJsonSpecimens
{
    // json-specimen: payload-envelope
    public const string Envelope =
        """{"version":"0.1","schema":"sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59","recordedAt":"2025-01-02T03:04:05.0000000+05:30","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"value","value":"before"},"after":{"state":"value","value":"after"}}}}}""";

    // /json-specimen

    // json-specimen: payload-scalar-add
    public const string ScalarAdd =
        """{"version":"0.1","schema":"sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59","change":{"kind":"update","members":{"label":{"kind":"replace","before":{"state":"missing"},"after":{"state":"value","value":"after"}}}}}""";

    // /json-specimen

    // json-specimen: payload-nested-json
    public const string Nested =
        """{"version":"0.1","schema":"sha256:975073d6e08b95584b9dfbfc949a86450b232296ff6c0849bcd6accf5a5b1aaa","change":{"kind":"update","members":{"customer":{"kind":"update","members":{"name":{"kind":"replace","before":{"state":"value","value":"Ann"},"after":{"state":"value","value":"Bob"}}}}}}}""";

    // /json-specimen

    // json-specimen: payload-keyed-json
    public const string Keyed =
        """{"version":"0.1","schema":"sha256:279b7c9cd41f1d9afb3dc4fdfec3f8da469bf5c50814e051f626343bf07bfa64","change":{"kind":"update","members":{"servers":{"kind":"update","items":[{"key":"b","kind":"update","beforeIndex":1,"afterIndex":0,"members":{"host":{"kind":"replace","before":{"state":"value","value":"B"},"after":{"state":"value","value":"B2"}}}},{"key":"c","kind":"add","afterIndex":1,"after":{"state":"value","value":{"Id":"c","Host":"C"}}},{"key":"a","kind":"remove","beforeIndex":0,"before":{"state":"value","value":{"Id":"a","Host":"A"}}}],"beforeOrder":["a","b"],"afterOrder":["b","c"]}}}}""";

    // /json-specimen

    // json-specimen: payload-reorder
    public const string Reorder =
        """{"version":"0.1","schema":"sha256:279b7c9cd41f1d9afb3dc4fdfec3f8da469bf5c50814e051f626343bf07bfa64","change":{"kind":"update","members":{"servers":{"kind":"update","items":[{"key":"b","kind":"update","beforeIndex":1,"afterIndex":0,"isReordered":true},{"key":"a","kind":"update","beforeIndex":0,"afterIndex":1,"isReordered":true}],"beforeOrder":["a","b"],"afterOrder":["b","a"]}}}}""";

    // /json-specimen

    // json-specimen: payload-dict
    public const string Dictionary =
        """{"version":"0.1","schema":"sha256:f2d35c2daced669e6f233839d49eab30aebe28a7db507be1f6a62194919206a1","change":{"kind":"update","members":{"scores":{"kind":"update","items":[{"key":"c","kind":"add","after":{"state":"value","value":4}},{"key":"a","kind":"remove","before":{"state":"value","value":1}},{"key":"b","kind":"replace","before":{"state":"value","value":2},"after":{"state":"value","value":3}}]}}}}""";

    // /json-specimen

    // json-specimen: payload-root
    public const string Root =
        """{"version":"0.1","schema":"sha256:d9d9103450c69184264f032386d556b7f01a1869d3d66e9982313994607cbc59","change":{"kind":"replace","before":{"state":"missing"},"after":{"state":"value","value":{"label":{"state":"value","value":"present"},"retryCount":{"state":"value","value":3}}}}}""";
    // /json-specimen
}
