namespace PackageShared.Consumer;

public sealed class Consumer
{
    public static global::System.DateTimeOffset? CopyRecordedAt(global::System.DateTimeOffset? recordedAt) =>
        PackageShared.Generated.ChangeSet.Create(recordedAt).WithRecordedAt(recordedAt).RecordedAt;
}
