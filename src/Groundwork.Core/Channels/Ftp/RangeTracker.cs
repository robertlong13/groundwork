// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Groundwork.Core.Channels.Ftp;

/// <summary>
/// Tracks which byte ranges of a file have been received, using a sorted
/// list of non-overlapping intervals with merge-on-insert.
/// </summary>
internal sealed class RangeTracker
{
    // Sorted by Start. Invariant: no two ranges overlap or are adjacent.
    private readonly List<(int Start, int End)> _ranges = new();

    /// <summary>
    /// Gets the total number of bytes received.
    /// </summary>
    public int TotalReceived
    {
        get
        {
            int total = 0;
            foreach (var (start, end) in _ranges)
                total += end - start;
            return total;
        }
    }

    /// <summary>
    /// Marks a range of bytes as received, merging with adjacent/overlapping intervals.
    /// </summary>
    public void MarkReceived(int offset, int length)
    {
        if (length <= 0)
            return;

        int newStart = offset;
        int newEnd = offset + length;

        // Find the insertion point and any overlapping/adjacent ranges.
        int firstOverlap = -1;
        int lastOverlap = -1;

        for (int i = 0; i < _ranges.Count; i++)
        {
            var (start, end) = _ranges[i];

            // Adjacent or overlapping: ranges touch if newEnd >= start and newStart <= end.
            if (newEnd >= start && newStart <= end)
            {
                if (firstOverlap == -1)
                    firstOverlap = i;
                lastOverlap = i;
            }
        }

        if (firstOverlap == -1)
        {
            // No overlap -- binary search for insertion point.
            int insertAt = 0;
            for (int i = 0; i < _ranges.Count; i++)
            {
                if (_ranges[i].Start > newStart)
                    break;
                insertAt = i + 1;
            }

            _ranges.Insert(insertAt, (newStart, newEnd));
        }
        else
        {
            // Merge all overlapping ranges into one.
            newStart = Math.Min(newStart, _ranges[firstOverlap].Start);
            newEnd = Math.Max(newEnd, _ranges[lastOverlap].End);

            _ranges.RemoveRange(firstOverlap, lastOverlap - firstOverlap + 1);
            _ranges.Insert(firstOverlap, (newStart, newEnd));
        }
    }

    /// <summary>
    /// Returns the first gap in the range [0, fileSize), or null if complete.
    /// </summary>
    public (int Offset, int Length)? FirstGap(int fileSize)
    {
        if (fileSize <= 0)
            return null;

        int expected = 0;

        foreach (var (start, end) in _ranges)
        {
            if (start > expected)
                return (expected, Math.Min(start, fileSize) - expected);

            expected = Math.Max(expected, end);

            if (expected >= fileSize)
                return null;
        }

        return expected < fileSize ? (expected, fileSize - expected) : null;
    }

    /// <summary>
    /// Returns whether all bytes in [0, fileSize) have been received.
    /// </summary>
    public bool IsComplete(int fileSize) =>
        fileSize <= 0
        || (_ranges.Count == 1 && _ranges[0].Start == 0 && _ranges[0].End >= fileSize);

    /// <summary>
    /// Returns the number of gaps in [0, fileSize).
    /// </summary>
    public int GapCount(int fileSize)
    {
        if (fileSize <= 0)
            return 0;

        int gaps = 0;
        int expected = 0;

        foreach (var (start, end) in _ranges)
        {
            if (start > expected)
                gaps++;
            expected = Math.Max(expected, end);

            if (expected >= fileSize)
                return gaps;
        }

        if (expected < fileSize)
            gaps++;

        return gaps;
    }

    /// <summary>
    /// Enumerates all gaps in [0, fileSize).
    /// </summary>
    internal IEnumerable<(int Offset, int Length)> EnumerateGaps(int fileSize)
    {
        if (fileSize <= 0)
            yield break;

        int expected = 0;

        foreach (var (start, end) in _ranges)
        {
            if (start > expected)
                yield return (expected, Math.Min(start, fileSize) - expected);

            expected = Math.Max(expected, end);

            if (expected >= fileSize)
                yield break;
        }

        if (expected < fileSize)
            yield return (expected, fileSize - expected);
    }

    /// <summary>
    /// Enumerates gaps starting from <paramref name="cursor"/>, wrapping to 0
    /// after reaching <paramref name="fileSize"/>. Yields each gap at most once.
    /// </summary>
    internal IEnumerable<(int Offset, int Length)> EnumerateGapsFrom(int cursor, int fileSize)
    {
        if (fileSize <= 0)
            yield break;

        cursor = Math.Clamp(cursor, 0, fileSize);

        // Gaps from cursor to end.
        foreach (var (offset, length) in EnumerateGaps(fileSize))
        {
            var end = offset + length;
            if (end <= cursor)
                continue;

            // Trim gap start if it begins before cursor.
            var trimmedOffset = Math.Max(offset, cursor);
            yield return (trimmedOffset, end - trimmedOffset);
        }

        // Wrap: gaps from 0 to cursor.
        if (cursor > 0)
        {
            foreach (var (offset, length) in EnumerateGaps(fileSize))
            {
                if (offset >= cursor)
                    yield break;

                var end = Math.Min(offset + length, cursor);
                yield return (offset, end - offset);
            }
        }
    }
}
