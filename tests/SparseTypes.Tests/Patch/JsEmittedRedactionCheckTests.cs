using System.Text.Json;

namespace SparseTypes.Tests.Patch;

// Temporary verification for issue 384: every redacted payload shape emitted
// by the JS codegen must be accepted by the C# reader.
public sealed class JsEmittedRedactionCheckTests
{
    [Test]
    public void JsDictEdit_IsAccepted()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"update","members":{"secrets":{"kind":"update","items":[{"key":"k","kind":"replace","before":{"state":"redacted"},"after":{"state":"value","value":"after-value"}}]}}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedSecretMap.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        var before = Optional<RedactedSecretMap.Fragment?>.Present(
            RedactedSecretMap.Fragment.From(new RedactedSecretMap { Secrets = new() { ["k"] = "before-value" } }));
        var after = Optional<RedactedSecretMap.Fragment?>.Present(
            RedactedSecretMap.Fragment.From(new RedactedSecretMap { Secrets = new() { ["k"] = "after-value" } }));
        RedactedSecretMap.Patch.Between(payload.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsDictRemove_IsAccepted()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"update","members":{"secrets":{"kind":"update","items":[{"key":"k","kind":"remove","before":{"state":"redacted"}}]}}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedSecretMap.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        var before = Optional<RedactedSecretMap.Fragment?>.Present(
            RedactedSecretMap.Fragment.From(new RedactedSecretMap { Secrets = new() { ["k"] = "before-value" } }));
        var after = Optional<RedactedSecretMap.Fragment?>.Present(
            RedactedSecretMap.Fragment.From(new RedactedSecretMap()));
        RedactedSecretMap.Patch.Between(payload.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsNestedUpdate_IsAccepted()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"update","members":{"profile":{"kind":"update","members":{"nickname":{"kind":"replace","before":{"state":"redacted"},"after":{"state":"value","value":"after-nickname"}},"token":{"kind":"replace","before":{"state":"redacted"},"after":{"state":"value","value":"after-token"}}}}}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedTeamHolder.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        Optional<RedactedTeamHolder.Fragment?> State(string nickname, string token) =>
            Optional<RedactedTeamHolder.Fragment?>.Present(
                RedactedTeamHolder.Fragment.From(
                    new RedactedTeamHolder { Profile = new RedactedProfile { Nickname = nickname, Token = token } }));
        var before = State("before-nickname", "before-token");
        var after = State("after-nickname", "after-token");
        RedactedTeamHolder.Patch.Between(payload.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsKeyedEdit_IsAccepted()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"update","members":{"devices":{"kind":"update","items":[{"key":"a","kind":"update","beforeIndex":0,"afterIndex":0,"members":{"name":{"kind":"replace","before":{"state":"redacted"},"after":{"state":"value","value":"after-device"}}}}],"beforeOrder":["a"],"afterOrder":["a"]}}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedDeviceHolder.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => payload.ToChangeSet());
        Optional<RedactedDeviceHolder.Fragment?> State(string name) =>
            Optional<RedactedDeviceHolder.Fragment?>.Present(
                RedactedDeviceHolder.Fragment.From(
                    new RedactedDeviceHolder { Devices = [new RedactedDevice { Id = "a", Name = name }] }));
        RedactedDeviceHolder.Patch.Between(payload.ToPatch().Apply(State("before-device")), State("after-device")).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsKeyedRemove_IsAccepted()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"update","members":{"devices":{"kind":"update","items":[{"key":"a","kind":"remove","beforeIndex":0,"before":{"state":"redacted"}}],"beforeOrder":["a"],"afterOrder":[]}}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedDeviceHolder.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        var before = Optional<RedactedDeviceHolder.Fragment?>.Present(
            RedactedDeviceHolder.Fragment.From(
                new RedactedDeviceHolder { Devices = [new RedactedDevice { Id = "a", Name = "before-device" }] }));
        var after = Optional<RedactedDeviceHolder.Fragment?>.Present(
            RedactedDeviceHolder.Fragment.From(new RedactedDeviceHolder()));
        RedactedDeviceHolder.Patch.Between(payload.ToPatch().Apply(before), after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void JsWholeModelBlindRemove_IsExplicit()
    {
        var json =
            """{"version":"0.1","schema":"sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","change":{"kind":"replace","before":{"state":"redacted"},"after":{"state":"missing"}}}""";
        var payload = PayloadJsonTestSupport.Deserialize<RedactedSecretMap.ChangePayload>(json);
        payload.Validate().IsValid.ShouldBeTrue();
        var present = Optional<RedactedSecretMap.Fragment?>.Present(
            RedactedSecretMap.Fragment.From(new RedactedSecretMap { Secrets = new() { ["k"] = "before-value" } }));
        var missing = Optional<RedactedSecretMap.Fragment?>.Missing;
        try
        {
            var projected = payload.ToPatch();
            RedactedSecretMap.Patch.Between(projected.Apply(present), missing).IsEmpty.ShouldBeTrue();
        }
        catch (ArgumentException)
        {
            // Blind whole-model removal without a patch projection is an
            // explicit, value-free rejection.
        }
    }
}
