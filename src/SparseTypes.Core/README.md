# SparseTypes.Core

`SparseTypes.Core` contains the shared value types, model attributes, and runtime helpers used by the SparseTypes products.

Most applications should install either `SparseTypes` or `SparseTypes.Fragments`. Both packages depend on Core and include their own source generator. Installing Core alone does not enable source generation. Add the package with `dotnet add package SparseTypes.Core`.

Core targets `netstandard2.0` and supports C# 9 or later. It has no dependency on either product runtime or generator.
