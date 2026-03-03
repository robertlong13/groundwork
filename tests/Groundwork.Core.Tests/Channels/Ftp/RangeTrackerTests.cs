// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Groundwork.Core.Channels.Ftp;

namespace Groundwork.Core.Tests.Channels.Ftp;

public class RangeTrackerTests
{
    [Fact]
    public void Empty_IsNotComplete()
    {
        var tracker = new RangeTracker();
        Assert.False(tracker.IsComplete(100));
        Assert.Equal(0, tracker.TotalReceived);
    }

    [Fact]
    public void SingleRange_CoversEntireFile()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 100);

        Assert.True(tracker.IsComplete(100));
        Assert.Equal(100, tracker.TotalReceived);
        Assert.Null(tracker.FirstGap(100));
    }

    [Fact]
    public void GapAtStart()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(50, 50);

        Assert.False(tracker.IsComplete(100));
        var gap = tracker.FirstGap(100);
        Assert.NotNull(gap);
        Assert.Equal(0, gap.Value.Offset);
        Assert.Equal(50, gap.Value.Length);
    }

    [Fact]
    public void GapInMiddle()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 30);
        tracker.MarkReceived(60, 40);

        Assert.False(tracker.IsComplete(100));
        var gap = tracker.FirstGap(100);
        Assert.NotNull(gap);
        Assert.Equal(30, gap.Value.Offset);
        Assert.Equal(30, gap.Value.Length);
    }

    [Fact]
    public void GapAtEnd()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 50);

        Assert.False(tracker.IsComplete(100));
        var gap = tracker.FirstGap(100);
        Assert.NotNull(gap);
        Assert.Equal(50, gap.Value.Offset);
        Assert.Equal(50, gap.Value.Length);
    }

    [Fact]
    public void MergesAdjacentRanges()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 50);
        tracker.MarkReceived(50, 50);

        Assert.True(tracker.IsComplete(100));
        Assert.Equal(100, tracker.TotalReceived);
    }

    [Fact]
    public void MergesOverlappingRanges()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 60);
        tracker.MarkReceived(40, 60);

        Assert.True(tracker.IsComplete(100));
        Assert.Equal(100, tracker.TotalReceived);
    }

    [Fact]
    public void OutOfOrderInsertion()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(60, 40);
        tracker.MarkReceived(0, 30);
        tracker.MarkReceived(30, 30);

        Assert.True(tracker.IsComplete(100));
    }

    [Fact]
    public void GapCount_MultipleGaps()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(10, 10); // 10-20
        tracker.MarkReceived(40, 10); // 40-50
        tracker.MarkReceived(70, 10); // 70-80

        // Gaps: 0-10, 20-40, 50-70, 80-100
        Assert.Equal(4, tracker.GapCount(100));
    }

    [Fact]
    public void EnumerateGaps_ReturnsAllGaps()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(10, 10); // 10-20
        tracker.MarkReceived(40, 10); // 40-50

        var gaps = tracker.EnumerateGaps(60).ToList();

        Assert.Equal(3, gaps.Count);
        Assert.Equal((0, 10), gaps[0]);
        Assert.Equal((20, 20), gaps[1]);
        Assert.Equal((50, 10), gaps[2]);
    }

    [Fact]
    public void ZeroLengthMark_IsIgnored()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 0);

        Assert.Equal(0, tracker.TotalReceived);
        Assert.False(tracker.IsComplete(100));
    }

    [Fact]
    public void ZeroFileSize_IsAlwaysComplete()
    {
        var tracker = new RangeTracker();
        Assert.True(tracker.IsComplete(0));
        Assert.Null(tracker.FirstGap(0));
        Assert.Equal(0, tracker.GapCount(0));
    }

    [Fact]
    public void DuplicateRange_DoesNotDoubleCount()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 50);
        tracker.MarkReceived(0, 50);

        Assert.Equal(50, tracker.TotalReceived);
    }

    [Fact]
    public void MergesThreeOverlappingRanges()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 20);
        tracker.MarkReceived(40, 20);
        tracker.MarkReceived(80, 20);

        // Now bridge the first two.
        tracker.MarkReceived(15, 30);

        // (0,60) + (80,100) = 80 bytes, 1 gap at 60-80.
        Assert.Equal(80, tracker.TotalReceived);
        Assert.Equal(1, tracker.GapCount(100));
    }

    [Fact]
    public void EnumerateGapsFrom_StartsAtCursor()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(10, 10); // 10-20
        tracker.MarkReceived(40, 10); // 40-50
        // Gaps: 0-10, 20-40, 50-60

        var gaps = tracker.EnumerateGapsFrom(25, 60).ToList();

        // From cursor=25: (25,15), (50,10), then wrap: (0,10), (20,5)
        Assert.Equal(4, gaps.Count);
        Assert.Equal((25, 15), gaps[0]);
        Assert.Equal((50, 10), gaps[1]);
        Assert.Equal((0, 10), gaps[2]);
        Assert.Equal((20, 5), gaps[3]);
    }

    [Fact]
    public void EnumerateGapsFrom_ZeroCursor_SameAsEnumerateGaps()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(10, 10); // 10-20
        tracker.MarkReceived(40, 10); // 40-50

        var normal = tracker.EnumerateGaps(60).ToList();
        var fromZero = tracker.EnumerateGapsFrom(0, 60).ToList();

        Assert.Equal(normal, fromZero);
    }

    [Fact]
    public void EnumerateGapsFrom_PastAllGaps_WrapsToStart()
    {
        var tracker = new RangeTracker();
        tracker.MarkReceived(0, 50);
        // Gap: 50-100

        var gaps = tracker.EnumerateGapsFrom(100, 100).ToList();

        // Cursor at end, wraps to 0. Only gap is 50-100.
        Assert.Single(gaps);
        Assert.Equal((50, 50), gaps[0]);
    }
}
