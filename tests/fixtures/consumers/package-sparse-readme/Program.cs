ReadmeSamples.Run();

Console.WriteLine("SparseTypes README consumer passed.");

internal static class DocsCheck
{
    public static void Require(bool condition, string capability)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Failed: " + capability);
        }
    }
}
