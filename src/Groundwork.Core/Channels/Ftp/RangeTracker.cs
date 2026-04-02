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
/// <remarks>All public members are thread-safe.</remarks>
internal sealed class RangeTracker
{
    private readonly object _lock = new();

    // Sorted by Start. Invariant: no two ranges overlap or are adjacent.
    private readonly List<(int Start, int End)> _ranges = new();

    /// <summary>
    /// Gets the highest byte offset received (exclusive end of last range), or 0 if empty.
    /// </summary>
    public int HighestReceived
    {
        get
        {
            lock (_lock)
                return _ranges.Count > 0 ? _ranges[^1].End : 0;
        }
    }

    /// <summary>
    /// Gets the total number of bytes received.
    /// </summary>
    public int TotalReceived
    {
        get
        {
            lock (_lock)
                return SumRanges();
        }
    }

    private int SumRanges()
    {
        int total = 0;
        foreach (var (start, end) in _ranges)
            total += end - start;
        return total;
    }

    /// <summary>
    /// Marks a range of bytes as received, merging with adjacent/overlapping intervals.
    /// </summary>
    public void MarkReceived(int offset, int length)
    {
        if (length <= 0)
            return;

        lock (_lock)
            MergeRange(offset, length);
    }

    private void MergeRange(int offset, int length)
    {
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
    /// Returns whether all bytes in [0, fileSize) have been received.
    /// </summary>
    public bool IsComplete(int fileSize)
    {
        lock (_lock)
            return fileSize <= 0
                || (_ranges.Count == 1 && _ranges[0].Start == 0 && _ranges[0].End >= fileSize);
    }

    private IEnumerable<(int Offset, int Length)> EnumerateGaps(int fileSize)
    {
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
    /// Returns a snapshot of gaps starting from <paramref name="cursor"/>,
    /// wrapping to 0 after reaching <paramref name="fileSize"/>.
    /// </summary>
    /// <remarks>Each gap appears at most once.</remarks>
    internal List<(int Offset, int Length)> GetGapsFrom(
        int cursor,
        int fileSize,
        int maxCount = int.MaxValue
    )
    {
        if (fileSize <= 0)
            return [];

        cursor = Math.Clamp(cursor, 0, fileSize);

        lock (_lock)
            return EnumerateGapsFrom(cursor, fileSize).Take(maxCount).ToList();
    }

    private IEnumerable<(int Offset, int Length)> EnumerateGapsFrom(int cursor, int fileSize)
    {
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
