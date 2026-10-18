using System.ComponentModel;

namespace SparseTypes.Tests;

// Headless tests for #318: one session-owned coordinator shares a single
// captured snapshot across every watcher in a dispatch generation.
public sealed class ObservedFieldDispatchTests
{
    private static KeyedServerHolder Holder(int rows)
    {
        var holder = new KeyedServerHolder();
        for (var index = 0; index < rows; index++)
        {
            holder.Items.Add(new KeyedServer { Id = "id-" + index, Name = "n-" + index });
        }

        return holder;
    }

    private static List<SparseObservedField<object?>> WatchMembers(
        KeyedServerHolder.EditSession session,
        int count
    )
    {
        // One stable member path watched through the model-agnostic spelling.
        var path = SparsePath.Root<KeyedServerHolder>().Member(nameof(KeyedServerHolder.Items));
        var watchers = new List<SparseObservedField<object?>>(count);
        for (var index = 0; index < count; index++)
        {
            watchers.Add(session.ObserveField(path));
        }

        return watchers;
    }

    [Test]
    public void ManyWatchersShareOneSnapshotPerGeneration()
    {
        const int rows = 200;
        const int watchers = 100;
        var singleModel = Holder(rows);
        var singleSession = singleModel.CreateEditSession();
        using var single = singleSession.ObserveField(
            SparsePath.Root<KeyedServerHolder>().Member(nameof(KeyedServerHolder.Items))
        );

        var multiModel = Holder(rows);
        var multiSession = multiModel.CreateEditSession();
        var many = WatchMembers(multiSession, watchers);
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var singleBefore = GC.GetAllocatedBytesForCurrentThread();
            singleSession.Observable.Items[0].Name = "edit-0";
            var singleBytes = GC.GetAllocatedBytesForCurrentThread() - singleBefore;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var multiBefore = GC.GetAllocatedBytesForCurrentThread();
            multiSession.Observable.Items[0].Name = "edit-0";
            var multiBytes = GC.GetAllocatedBytesForCurrentThread() - multiBefore;

            // One shared root snapshot/diff serves all watchers: a hundred
            // independent diffs would scale the allocation a hundredfold.
            multiBytes.ShouldBeLessThan(singleBytes * 10);

            single.HasChanges.ShouldBeTrue();
            foreach (var watcher in many)
            {
                watcher.HasChanges.ShouldBeTrue();
            }
        }
        finally
        {
            foreach (var watcher in many)
            {
                watcher.Dispose();
            }
        }
    }

    [Test]
    public void ReentrantEditDispatchesFreshGeneration()
    {
        var session = new Settings().CreateEditSession();
        using var label = session.ObserveField(Settings.SparsePath.Label);
        using var count = session.ObserveField(Settings.SparsePath.RetryCount);
        var labelNotifications = 0;
        var countNotifications = 0;
        var reentered = false;
        ((INotifyPropertyChanged)label).PropertyChanged += (_, _) =>
        {
            labelNotifications++;
            if (!reentered)
            {
                reentered = true;
                session.Observable.RetryCount = 42;
            }
        };
        ((INotifyPropertyChanged)count).PropertyChanged += (_, _) => countNotifications++;

        session.Observable.Label = "dirty";

        // The nested edit schedules a fresh generation instead of handing
        // later observers a stale or mixed snapshot.
        label.IsChanged.ShouldBeTrue();
        label.After.ShouldBe(Optional<string?>.Present("dirty"));
        count.IsChanged.ShouldBeTrue();
        count.After.ShouldBe(Optional<int>.Present(42));
        labelNotifications.ShouldBeGreaterThan(0);
        countNotifications.ShouldBeGreaterThan(0);
    }

    [Test]
    public void DisposeDuringDispatchStaysSafe()
    {
        var session = new Settings().CreateEditSession();
        using var first = session.ObserveField(Settings.SparsePath.Label);
        var second = session.ObserveField(Settings.SparsePath.RetryCount);
        var secondNotifications = 0;
        ((INotifyPropertyChanged)second).PropertyChanged += (_, _) => secondNotifications++;
        ((INotifyPropertyChanged)first).PropertyChanged += (_, _) => second.Dispose();

        session.Observable.Label = "dirty";

        first.IsChanged.ShouldBeTrue();
        Should.Throw<ObjectDisposedException>(() => second.Refresh());

        var before = secondNotifications;
        session.Observable.Label = "dirtier";
        secondNotifications.ShouldBe(before);
        first.After.ShouldBe(Optional<string?>.Present("dirtier"));
    }

    [Test]
    public void SubscribeDuringDispatchJoinsNextGeneration()
    {
        var session = new Settings().CreateEditSession();
        using var first = session.ObserveField(Settings.SparsePath.Label);
        SparseObservedField<int>? late = null;
        ((INotifyPropertyChanged)first).PropertyChanged += (_, _) =>
        {
            late ??= session.ObserveField(Settings.SparsePath.RetryCount);
        };

        session.Observable.Label = "dirty";
        late.ShouldNotBeNull();
        late!.IsChanged.ShouldBeFalse();

        session.Observable.RetryCount = 7;
        late.IsChanged.ShouldBeTrue();
        late.After.ShouldBe(Optional<int>.Present(7));
        late!.Dispose();
    }

    [Test]
    public void ExplicitRefreshDuringDispatchSeesRawMutation()
    {
        var model = new Settings();
        var session = model.CreateEditSession();
        using var label = session.ObserveField(Settings.SparsePath.Label);
        using var count = session.ObserveField(Settings.SparsePath.RetryCount);
        Optional<int>? seenDuringDispatch = null;
        ((INotifyPropertyChanged)label).PropertyChanged += (_, _) =>
        {
            // A raw model write bypasses session notifications; an explicit
            // refresh still observes it instead of the dispatch snapshot.
            model.RetryCount = 99;
            count.Refresh();
            seenDuringDispatch = count.After;
        };

        session.Observable.Label = "dirty";

        seenDuringDispatch.ShouldBe(Optional<int>.Present(99));

        // The next notification converges the dispatch-driven watcher.
        session.Observable.Label = "dirtier";
        count.After.ShouldBe(Optional<int>.Present(99));
        label.After.ShouldBe(Optional<string?>.Present("dirtier"));
    }

    [Test]
    public void ManualRefreshAndFieldStateSeeRawMutations()
    {
        var model = new Settings();
        var session = model.CreateEditSession();
        using var watcher = session.ObserveField(Settings.SparsePath.Label);

        model.Label = "raw";

        watcher.Refresh();
        watcher.IsChanged.ShouldBeTrue();
        watcher.After.ShouldBe(Optional<string?>.Present("raw"));
        session
            .FieldState(Settings.SparsePath.Label)
            .After.ShouldBe(Optional<string?>.Present("raw"));
    }

    [Test]
    public void TransientKeyedStateSkipsOneGenerationOnly()
    {
        var model = Holder(("a", "a"), ("b", "b"));
        var session = model.CreateEditSession();
        using var label = session.ObserveField(
            SparsePath.Root<KeyedServerHolder>().Member(nameof(KeyedServerHolder.Items))
        );
        var notifications = 0;
        ((INotifyPropertyChanged)label).PropertyChanged += (_, _) => notifications++;

        // A duplicate key is invalid mid-swap: the dispatch shares one
        // capture, so the whole generation is skipped without notifying.
        model.Items.Add(new KeyedServer { Id = "a", Name = "duplicate" });
        session.Observable.Items[0].Name = "edit-while-duplicate";
        notifications.ShouldBe(0);

        // Removing the duplicate restores the next valid refresh.
        model.Items.RemoveAt(model.Items.Count - 1);
        session.Observable.Items[0].Name = "edit-after-fix";
        label.HasChanges.ShouldBeTrue();
        notifications.ShouldBeGreaterThan(0);
        session
            .FieldState(KeyedServerHolder.SparsePath.Items.Key("a").Name)
            .After.ShouldBe(Optional<string>.Present("edit-after-fix"));
    }

    private static KeyedServerHolder Holder(params (string Id, string Name)[] rows)
    {
        var holder = new KeyedServerHolder();
        foreach (var row in rows)
        {
            holder.Items.Add(new KeyedServer { Id = row.Id, Name = row.Name });
        }

        return holder;
    }
}
