using System;

namespace SparseTypes;

/// <summary>Excludes a property from generated transport payloads while preserving local tracking.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseTransportIgnoreAttribute : Attribute;
