extern alias Playground;

using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Playground::SparseTypes.Playground.Components;
using Playground::SparseTypes.Playground.Models;

// A dependency-free test renderer builds render trees by hand, so the
// experimental RenderTree API surface it needs is acknowledged here.
#pragma warning disable BL0006

namespace SparseTypes.Tests.Components;

// Headless tests for #332: one page-wide coordinator owns the shared Monaco
// setTheme operation, so one effective theme change costs one interop call no
// matter how many editors are mounted.
public sealed class MonacoThemeCoordinatorTests
{
    private sealed class StubJsRuntime : IJSRuntime
    {
        private readonly object _gate = new();
        private readonly List<string> _appliedThemes = [];
        private int _attempts;
        private TaskCompletionSource<bool>? _themeGate;

        public Func<string, Exception?>? FailSetTheme { get; set; }

        public IReadOnlyList<string> AppliedThemes
        {
            get
            {
                lock (_gate)
                {
                    return _appliedThemes.ToList();
                }
            }
        }

        public int AttemptCount
        {
            get
            {
                lock (_gate)
                {
                    return _attempts;
                }
            }
        }

        public int SetThemeCount
        {
            get
            {
                lock (_gate)
                {
                    return _appliedThemes.Count;
                }
            }
        }

        public string? LastAppliedTheme
        {
            get
            {
                lock (_gate)
                {
                    return _appliedThemes.Count == 0 ? null : _appliedThemes[^1];
                }
            }
        }

        public void HoldSetTheme() =>
            _themeGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

        public void ReleaseSetTheme() => _themeGate?.TrySetResult(true);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeCoreAsync<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeCoreAsync<TValue>(identifier, args);

        private async ValueTask<TValue> InvokeCoreAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "blazorMonaco.editor.setTheme")
            {
                var theme = (string)args![0]!;
                lock (_gate)
                {
                    _attempts++;
                }

                var gate = _themeGate;
                if (gate is not null)
                {
                    await gate.Task;
                }

                var failure = FailSetTheme?.Invoke(theme);
                if (failure is not null)
                {
                    throw failure;
                }

                lock (_gate)
                {
                    _appliedThemes.Add(theme);
                }
            }

            return default!;
        }
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly object _gate = new();
        private readonly List<string> _identifiers = [];
        private int _models;

        public int CreateCount => Count("blazorMonaco.editor.createModel");

        public int SetThemeCount => Count("blazorMonaco.editor.setTheme");

        public TextModel CreateTrackedModel()
        {
            var number = Interlocked.Increment(ref _models);
            return new TextModel
            {
                JsRuntime = this,
                Uri = $"test://model-{number}",
                Id = $"model-{number}",
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            lock (_gate)
            {
                _identifiers.Add(identifier);
            }

            if (identifier == "blazorMonaco.editor.createModel")
            {
                return new ValueTask<TValue>((TValue)(object)CreateTrackedModel());
            }

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);

        private int Count(string identifier)
        {
            lock (_gate)
            {
                return _identifiers.Count(id => id == identifier);
            }
        }
    }

    private sealed class TestRenderer : Renderer
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CreateDefault();

        public TestRenderer(IServiceProvider services, ILoggerFactory loggers)
            : base(services, loggers) { }

        public List<Exception> Failures { get; } = [];

        public override Dispatcher Dispatcher => _dispatcher;

        public Task<int> RenderRootAsync(IComponent component) =>
            Dispatcher.InvokeAsync(async () =>
            {
                var id = AssignRootComponentId(component);
                await RenderRootComponentAsync(id);
                return id;
            });

        public Task ShowAsync(int rootId, ParameterView parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(rootId, parameters));

        public Task FlushAsync() => Dispatcher.InvokeAsync(() => { });

        public IReadOnlyList<Exception> TakeFailures()
        {
            lock (Failures)
            {
                return Failures.ToList();
            }
        }

        protected override void HandleException(Exception exception)
        {
            lock (Failures)
            {
                Failures.Add(exception);
            }
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }

    private sealed class TestServices : IServiceProvider
    {
        private readonly FakeJsRuntime _js;
        private readonly PlaygroundThemeState _themes = new();
        private readonly MonacoThemeCoordinator _coordinator;

        public TestServices(FakeJsRuntime js)
        {
            _js = js;
            _coordinator = new MonacoThemeCoordinator(_js, _themes);
        }

        public PlaygroundThemeState Themes => _themes;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IJSRuntime))
            {
                return _js;
            }

            if (serviceType == typeof(PlaygroundThemeState))
            {
                return _themes;
            }

            if (serviceType == typeof(MonacoThemeCoordinator))
            {
                return _coordinator;
            }

            return null;
        }
    }

    private sealed class TwoDiffHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MonacoDiffView>(0);
            builder.AddAttribute(1, nameof(MonacoDiffView.Original), "original-a");
            builder.AddAttribute(2, nameof(MonacoDiffView.Modified), "modified-a");
            builder.CloseComponent();
            builder.OpenComponent<MonacoDiffView>(3);
            builder.AddAttribute(4, nameof(MonacoDiffView.Original), "original-b");
            builder.AddAttribute(5, nameof(MonacoDiffView.Modified), "modified-b");
            builder.CloseComponent();
        }
    }

    private static async Task PollAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for the theme lifecycle.");
            }

            await Task.Delay(15);
        }
    }

    [Test]
    public async Task OneEffectiveChangeAppliesOnce()
    {
        var js = new StubJsRuntime();
        var themes = new PlaygroundThemeState();
        using var coordinator = new MonacoThemeCoordinator(js, themes);

        themes.SetMode(PlaygroundThemeMode.Dark);
        await PollAsync(() => js.SetThemeCount == 1);
        js.LastAppliedTheme.ShouldBe("pg-dark");

        // Repeating the same selected mode raises no notification at all.
        themes.SetMode(PlaygroundThemeMode.Dark);
        await Task.Delay(100);
        js.SetThemeCount.ShouldBe(1);
    }

    [Test]
    public async Task SameEffectiveThemeNeedsNoReapply()
    {
        var js = new StubJsRuntime();
        var themes = new PlaygroundThemeState();
        using var coordinator = new MonacoThemeCoordinator(js, themes);

        // Light is already effective; selecting System keeps pg-light.
        themes.SetMode(PlaygroundThemeMode.Light);
        await PollAsync(() => js.SetThemeCount == 1);
        themes.SetMode(PlaygroundThemeMode.System);
        await Task.Delay(100);
        js.SetThemeCount.ShouldBe(1);
        js.LastAppliedTheme.ShouldBe("pg-light");
    }

    [Test]
    public async Task SystemPreferenceChangeUpdatesOnce()
    {
        var js = new StubJsRuntime();
        var themes = new PlaygroundThemeState();
        using var coordinator = new MonacoThemeCoordinator(js, themes);

        themes.SetMode(PlaygroundThemeMode.System);
        await Task.Delay(100);
        // pg-light was never applied because nothing requested it yet.
        js.SetThemeCount.ShouldBe(0);

        coordinator.NotifyEditorReady();
        await PollAsync(() => js.SetThemeCount == 1);
        js.LastAppliedTheme.ShouldBe("pg-light");

        themes.SetSystemTheme(true);
        await PollAsync(() => js.SetThemeCount == 2);
        js.LastAppliedTheme.ShouldBe("pg-dark");
    }

    [Test]
    public async Task RapidTogglesApplyOnlyTheLatest()
    {
        var js = new StubJsRuntime();
        js.HoldSetTheme();
        var themes = new PlaygroundThemeState();
        using var coordinator = new MonacoThemeCoordinator(js, themes);

        themes.SetMode(PlaygroundThemeMode.Dark);
        await Task.Delay(50);
        themes.SetMode(PlaygroundThemeMode.Light);
        themes.SetMode(PlaygroundThemeMode.Dark);
        js.ReleaseSetTheme();

        // The in-flight attempt applies the latest theme; nothing older can
        // overwrite it afterwards.
        await PollAsync(() => js.SetThemeCount >= 1);
        await Task.Delay(200);
        js.AppliedThemes.Count.ShouldBeLessThanOrEqualTo(2);
        js.LastAppliedTheme.ShouldBe("pg-dark");
    }

    [Test]
    public async Task ThemeChangeBeforeMonacoLoadRetriesOnEditorReady()
    {
        var js = new StubJsRuntime();
        js.FailSetTheme = _ => new JSException("Monaco is not loaded yet.");
        var themes = new PlaygroundThemeState();
        using var coordinator = new MonacoThemeCoordinator(js, themes);

        // The failed attempt must not mark anything applied or throw.
        themes.SetMode(PlaygroundThemeMode.Dark);
        await PollAsync(() => js.AttemptCount == 1);
        await Task.Delay(100);
        js.AttemptCount.ShouldBe(1);
        js.SetThemeCount.ShouldBe(0);

        js.FailSetTheme = null;
        coordinator.NotifyEditorReady();
        await PollAsync(() => js.SetThemeCount == 1);
        js.LastAppliedTheme.ShouldBe("pg-dark");

        // Already applied: further ready signals stay silent.
        coordinator.NotifyEditorReady();
        await Task.Delay(100);
        js.AttemptCount.ShouldBe(2);
    }

    [Test]
    public async Task DisposeStopsThemeUpdates()
    {
        var js = new StubJsRuntime();
        var themes = new PlaygroundThemeState();
        var coordinator = new MonacoThemeCoordinator(js, themes);
        coordinator.Dispose();

        themes.SetMode(PlaygroundThemeMode.Dark);
        coordinator.NotifyEditorReady();
        await Task.Delay(100);
        js.SetThemeCount.ShouldBe(0);
    }

    [Test]
    public async Task TwoEditorsShareOneThemeCall()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new TwoDiffHost());

        await renderer.ShowAsync(rootId, ParameterView.Empty);
        await PollAsync(() => js.CreateCount == 4);
        await renderer.FlushAsync();
        // Both editors initialized, but the shared operation ran once.
        await PollAsync(() => js.SetThemeCount == 1);

        var before = js.SetThemeCount;
        services.Themes.SetMode(PlaygroundThemeMode.Dark);
        await PollAsync(() => js.SetThemeCount == before + 1);
        await Task.Delay(300);
        js.SetThemeCount.ShouldBe(before + 1);

        renderer.TakeFailures().ShouldBeEmpty();
    }
}
