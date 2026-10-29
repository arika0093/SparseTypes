using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseNavigationGuardTests
{
    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");

        protected override void NavigateToCore(string uri, bool forceLoad) { }

        protected override void SetNavigationLockState(bool value) { }
    }

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }

    [Test]
    public async Task ExternalConfirmationTracksUnsavedAndUninsertedDraftState()
    {
        var session = new BlazorDraftBoard().CreateEditSession();
        using var editor = session.CreateEditor();
        SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>? guard = null;
        var internalNavigationCalls = 0;

        using var tree = await Headless.Tree.CreateAsync(
            builder =>
            {
                builder.OpenComponent<SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>>(0);
                builder.AddAttribute(1, nameof(SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>.Editor), editor);
                builder.AddAttribute(2, nameof(SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>.EnableExternalConfirmation), true);
                builder.AddAttribute(
                    3,
                    nameof(SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>.BeforeInternalNavigation),
                    (Func<Microsoft.AspNetCore.Components.Routing.LocationChangingContext, Task>)(_ =>
                    {
                        internalNavigationCalls++;
                        return Task.CompletedTask;
                    })
                );
                builder.AddComponentReferenceCapture(
                    4,
                    component => guard = (SparseNavigationGuard<BlazorDraftBoard, BlazorDraftBoard.ChangeSet>)component
                );
                builder.CloseComponent();
            },
            services =>
            {
                services.AddSingleton<IJSRuntime, NoopJsRuntime>();
                services.AddSingleton<NavigationManager, TestNavigationManager>();
            }
        );

        guard.ShouldNotBeNull().ShouldConfirmExternalNavigation.ShouldBeFalse();
        await guard.HandleInternalNavigation(null!);
        internalNavigationCalls.ShouldBe(0);
        session.Observable.Name = "Changed";
        await tree.FlushAsync();
        guard.ShouldNotBeNull().ShouldConfirmExternalNavigation.ShouldBeTrue();
        await guard.HandleInternalNavigation(null!);
        internalNavigationCalls.ShouldBe(1);

        session.AcceptChanges();
        await tree.FlushAsync();
        guard.ShouldNotBeNull().ShouldConfirmExternalNavigation.ShouldBeFalse();
        await guard.HandleInternalNavigation(null!);
        internalNavigationCalls.ShouldBe(1);

        var draft = session
            .EditCollection<BlazorDraftLine, int>(BlazorDraftBoard.SparsePath.Drafts)
            .BeginAdd(
                BlazorDraftBoard.SparsePath.Drafts.At(0),
                new BlazorDraftLine { TemporaryId = Guid.NewGuid(), Title = "Not inserted" },
                session
            );
        await tree.FlushAsync();
        guard.ShouldNotBeNull().ShouldConfirmExternalNavigation.ShouldBeTrue();
        await guard.HandleInternalNavigation(null!);
        internalNavigationCalls.ShouldBe(2);
        draft.Dispose();
    }
}
