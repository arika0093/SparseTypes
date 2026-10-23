using System.Threading;

namespace SparseTypes.Generator.Shared;

internal static class SparseGeneratedOnceObservationCoordinator
{
    internal const string TypeName = SparseGeneratedOnceNames.ObservationCoordinator;

    internal static string HintName(string implementationNamespace) => "Generated.ObservationCoordinator.g.cs";

    internal static string BuildSource(string implementationNamespace, CancellationToken cancellationToken)
    {
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseGeneratedOnceNames.AppendGeneratedHeader(code, implementationNamespace, cancellationToken);
        code.AppendLine("internal sealed class " + TypeName + "<TView>");
        code.OpenBlock();
        code.AppendLine(
            "internal "
                + TypeName
                + "(global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler> subscribe, global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler> unsubscribe, global::System.Func<TView> capture)"
        );
        code.OpenBlock();
        code.AppendLine("_subscribe = subscribe;");
        code.AppendLine("_unsubscribe = unsubscribe;");
        code.AppendLine("_capture = capture;");
        code.CloseBlock();
        code.AppendLine(
            "internal TResult Query<TResult>(global::System.Func<TResult> liveQuery, global::System.Func<TView, TResult> snapshotQuery) => _hasView ? snapshotQuery(_view) : liveQuery();"
        );
        code.AppendLine("internal void Subscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)");
        code.OpenBlock();
        code.AppendLine("_handlers ??= new global::System.Collections.Generic.List<global::System.ComponentModel.PropertyChangedEventHandler>();");
        code.AppendLine("if (_handlers.Count == 0) _subscribe(OnInvalidated);");
        code.AppendLine("_handlers.Add(handler);");
        code.AppendLine("_handlerSnapshot = null;");
        code.CloseBlock();
        code.AppendLine("internal void Unsubscribe(global::System.ComponentModel.PropertyChangedEventHandler handler)");
        code.OpenBlock();
        code.AppendLine("if (_handlers is null || !_handlers.Remove(handler)) return;");
        code.AppendLine("_handlerSnapshot = null;");
        code.AppendLine("if (_handlers.Count == 0)");
        code.OpenBlock();
        code.AppendLine("_unsubscribe(OnInvalidated);");
        code.AppendLine("_handlers = null;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("private void OnInvalidated(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)");
        code.OpenBlock();
        code.AppendLine("if (_dispatching) { _pending = true; return; }");
        code.AppendLine("_dispatching = true;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("do");
        code.OpenBlock();
        code.AppendLine("_pending = false;");
        code.AppendLine("var handlers = _handlers;");
        code.AppendLine("if (handlers is null || handlers.Count == 0) break;");
        code.AppendLine("TView view;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("view = _capture();");
        code.CloseBlock();
        code.AppendLine("catch (global::System.InvalidOperationException)");
        code.OpenBlock();
        code.AppendLine("break;");
        code.CloseBlock();
        code.AppendLine("_view = view;");
        code.AppendLine("_hasView = true;");
        code.AppendLine("try");
        code.OpenBlock();
        code.AppendLine("var dispatch = _handlerSnapshot ??= handlers.ToArray();");
        code.AppendLine("foreach (var handler in dispatch) handler(sender, e);");
        code.CloseBlock();
        code.AppendLine("finally");
        code.OpenBlock();
        code.AppendLine("_hasView = false;");
        code.AppendLine("_view = default!;");
        code.CloseBlock();
        code.Unindent();
        code.AppendLine("} while (_pending);");
        code.CloseBlock();
        code.AppendLine("finally");
        code.OpenBlock();
        code.AppendLine("_dispatching = false;");
        code.CloseBlock();
        code.CloseBlock();
        code.AppendLine("private readonly global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler> _subscribe;");
        code.AppendLine("private readonly global::System.Action<global::System.ComponentModel.PropertyChangedEventHandler> _unsubscribe;");
        code.AppendLine("private readonly global::System.Func<TView> _capture;");
        code.AppendLine("private global::System.Collections.Generic.List<global::System.ComponentModel.PropertyChangedEventHandler>? _handlers;");
        code.AppendLine("private global::System.ComponentModel.PropertyChangedEventHandler[]? _handlerSnapshot;");
        code.AppendLine("private TView _view = default!;");
        code.AppendLine("private bool _hasView;");
        code.AppendLine("private bool _dispatching;");
        code.AppendLine("private bool _pending;");
        code.CloseBlock();
        code.CloseBlock();
        return code.ToString();
    }
}
