using System.Collections;

namespace SparseTypes;

/// <summary>Structural comparison for untyped values that expose non-generic collection interfaces.</summary>
/// <remarks>Generated typed comparison methods handle generic collection shapes without runtime type inspection.</remarks>
internal static class FragmentComparisonPrimitives
{
    internal static bool AreValuesEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left is string || right is string)
        {
            return Equals(left, right);
        }

        if (left is IDictionary || right is IDictionary)
        {
            return left is IDictionary leftDictionary
                && right is IDictionary rightDictionary
                && AreDictionariesEqual(leftDictionary, rightDictionary);
        }

        if (left is IList || right is IList)
        {
            return left is IList leftList
                && right is IList rightList
                && AreListsEqual(leftList, rightList);
        }

        if (left is IEnumerable leftItems && right is IEnumerable rightItems)
        {
            return AreSequencesEqual(leftItems, rightItems);
        }

        return Equals(left, right);
    }

    private static bool AreDictionariesEqual(IDictionary left, IDictionary right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (DictionaryEntry entry in left)
        {
            if (!right.Contains(entry.Key) || !AreValuesEqual(entry.Value, right[entry.Key]))
            {
                return false;
            }
        }

        foreach (DictionaryEntry entry in right)
        {
            if (!left.Contains(entry.Key))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreListsEqual(IList left, IList right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!AreValuesEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSequencesEqual(IEnumerable left, IEnumerable right)
    {
        var leftEnumerator = left.GetEnumerator();
        IEnumerator? rightEnumerator = null;
        try
        {
            rightEnumerator = right.GetEnumerator();
            while (true)
            {
                var leftMoved = leftEnumerator.MoveNext();
                var rightMoved = rightEnumerator.MoveNext();
                if (leftMoved != rightMoved)
                {
                    return false;
                }

                if (!leftMoved)
                {
                    return true;
                }

                if (!AreValuesEqual(leftEnumerator.Current, rightEnumerator.Current))
                {
                    return false;
                }
            }
        }
        finally
        {
            (rightEnumerator as IDisposable)?.Dispose();
            (leftEnumerator as IDisposable)?.Dispose();
        }
    }
}
