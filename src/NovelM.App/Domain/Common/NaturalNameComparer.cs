using System.Text.RegularExpressions;

namespace NovelM_App.Domain.Common;

public sealed class NaturalNameComparer : IComparer<string>
{
    public static NaturalNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftParts = Regex.Matches(left, @"\d+|\D+");
        var rightParts = Regex.Matches(right, @"\d+|\D+");
        var sharedPartCount = Math.Min(leftParts.Count, rightParts.Count);

        for (var index = 0; index < sharedPartCount; index++)
        {
            var leftPart = leftParts[index].Value;
            var rightPart = rightParts[index].Value;
            int comparison;

            if (char.IsDigit(leftPart[0]) && char.IsDigit(rightPart[0]))
            {
                comparison = CompareNumericParts(leftPart, rightPart);
            }
            else
            {
                comparison = StringComparer.CurrentCultureIgnoreCase.Compare(
                    leftPart,
                    rightPart);
            }

            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Count.CompareTo(rightParts.Count);
    }

    private static int CompareNumericParts(string left, string right)
    {
        if (long.TryParse(left, out var leftNumber) &&
            long.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        var normalizedLeft = left.TrimStart('0');
        var normalizedRight = right.TrimStart('0');
        normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
        normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;

        var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
        return lengthComparison != 0
            ? lengthComparison
            : StringComparer.Ordinal.Compare(normalizedLeft, normalizedRight);
    }
}
