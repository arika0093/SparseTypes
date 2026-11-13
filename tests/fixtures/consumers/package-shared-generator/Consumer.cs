namespace PackageShared.Consumer;

public sealed class Consumer
{
    public static void ExerciseKeyedGuard()
    {
        var map = new Dictionary<string, int>();
        PackageShared.Generated.KeyedGuardProbe.AddUnique(map, "key", 1);
    }

    public static global::System.DateTimeOffset? CopyRecordedAt(global::System.DateTimeOffset? recordedAt) =>
        PackageShared.Generated.ChangeSet.Create(recordedAt).WithRecordedAt(recordedAt).RecordedAt;
}
