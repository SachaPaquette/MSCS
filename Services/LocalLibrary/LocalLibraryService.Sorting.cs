using System;
using System.Collections.Generic;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
        private sealed class NaturalSortComparer : IComparer<string>
        {
            public static readonly NaturalSortComparer Instance = new();

            public int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x is null)
                {
                    return -1;
                }

                if (y is null)
                {
                    return 1;
                }

                var indexX = 0;
                var indexY = 0;

                while (indexX < x.Length && indexY < y.Length)
                {
                    var isDigitX = char.IsDigit(x[indexX]);
                    var isDigitY = char.IsDigit(y[indexY]);

                    if (isDigitX && isDigitY)
                    {
                        var startX = indexX;
                        var startY = indexY;

                        while (indexX < x.Length && char.IsDigit(x[indexX]))
                        {
                            indexX++;
                        }

                        while (indexY < y.Length && char.IsDigit(y[indexY]))
                        {
                            indexY++;
                        }

                        var numberX = x.Substring(startX, indexX - startX);
                        var numberY = y.Substring(startY, indexY - startY);

                        var trimmedX = numberX.TrimStart('0');
                        var trimmedY = numberY.TrimStart('0');

                        trimmedX = trimmedX.Length > 0 ? trimmedX : "0";
                        trimmedY = trimmedY.Length > 0 ? trimmedY : "0";

                        var lengthComparison = trimmedX.Length.CompareTo(trimmedY.Length);
                        if (lengthComparison != 0)
                        {
                            return lengthComparison;
                        }

                        var digitsComparison = string.CompareOrdinal(trimmedX, trimmedY);
                        if (digitsComparison != 0)
                        {
                            return digitsComparison;
                        }

                        var originalLengthComparison = (indexX - startX).CompareTo(indexY - startY);
                        if (originalLengthComparison != 0)
                        {
                            return originalLengthComparison;
                        }

                        continue;
                    }

                    if (isDigitX)
                    {
                        return -1;
                    }

                    if (isDigitY)
                    {
                        return 1;
                    }

                    var charX = char.ToUpperInvariant(x[indexX]);
                    var charY = char.ToUpperInvariant(y[indexY]);

                    var charComparison = charX.CompareTo(charY);
                    if (charComparison != 0)
                    {
                        return charComparison;
                    }

                    indexX++;
                    indexY++;
                }

                if (indexX == x.Length && indexY == y.Length)
                {
                    return 0;
                }

                return indexX == x.Length ? -1 : 1;
            }
        }
    }
}