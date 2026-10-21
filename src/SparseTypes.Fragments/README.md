# SparseTypes.Fragments

`SparseTypes.Fragments` generates presence-aware `Fragment` and `Patch` types for models annotated with `[SparseModel]`. Its generator is included in the package.

Add the package with `dotnet add package SparseTypes.Fragments`.

The package provides Fragment conversion, merging, diffing, change application, and deep cloning. Patches support application, `Between`, `Compose`, and baseline-relative `Invert`, including nested and supported collection members.

The package does not generate `ChangeSet`, patch `Rebase`, JSON payloads or converters, observable views, edit sessions, or history APIs. It is intended for in-process use.

Choose either `SparseTypes` or `SparseTypes.Fragments` for a consumer compilation. Do not reference both products in the same compilation. Generated types from the two products have no interoperability guarantee, and mixed-product model graphs are unsupported.

`SparseTypes.Fragments` targets `netstandard2.0`, supports C# 9 or later, and depends on `SparseTypes.Core`. The source generator requires Roslyn 4.3.1 or later in the build environment.
