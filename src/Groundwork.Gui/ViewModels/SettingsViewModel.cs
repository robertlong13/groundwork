// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Groundwork.Core.Channels;
using Groundwork.Core.Connections;

namespace Groundwork.Gui.ViewModels;

/// <summary>
/// Provides the view model for the Application Settings page.
/// </summary>
public partial class SettingsViewModel : PageViewModelBase
{
    private readonly AppConfig _config;
    private readonly LinkManager _links;

    public override string Title => "Settings";

    // Material Symbols: Settings (filled, weight 400, grade 0, optical size 24)
    public override string IconPathData =>
        "M421-80q-14 0-25-9t-13-23l-15-94q-19-7-40-19t-37-25l-86 40q-14 6-28 1.5T155-226L97-330q-8-13-4.5-27t15.5-23l80-59q-2-9-2.5-20.5T185-480q0-9 .5-20.5T188-521l-80-59q-12-9-15.5-23t4.5-27l58-104q8-13 22-17.5t28 1.5l86 40q16-13 37-25t40-18l15-95q2-14 13-23t25-9h118q14 0 25 9t13 23l15 94q19 7 40.5 18.5T669-710l86-40q14-6 27.5-1.5T804-734l59 104q8 13 4.5 27.5T852-580l-80 57q2 10 2.5 21.5t.5 21.5q0 10-.5 21t-2.5 21l80 58q12 8 15.5 22.5T863-330l-58 104q-8 13-22 17.5t-28-1.5l-86-40q-16 13-36.5 25.5T592-206l-15 94q-2 14-13 23t-25 9H421Zm59-270q54 0 92-38t38-92q0-54-38-92t-92-38q-54 0-92 38t-38 92q0 54 38 92t92 38Z";

    /// <summary>
    /// Gets the list of configured link entries.
    /// </summary>
    public ObservableCollection<LinkEntry> Links { get; } = [];

    [ObservableProperty]
    private string _newLinkDescriptor = string.Empty;

    [ObservableProperty]
    private string? _linkError;

    public SettingsViewModel(AppConfig config, LinkManager links)
    {
        _config = config;
        _links = links;

        foreach (var descriptor in config.Links)
            Links.Add(new LinkEntry(descriptor));
    }

    /// <summary>
    /// Marks the link entry at the given config index as connected.
    /// </summary>
    public void MarkConnected(int configIndex, MavChannel channel)
    {
        if (configIndex >= 0 && configIndex < Links.Count)
        {
            Links[configIndex].Channel = channel;
            Links[configIndex].Connected = true;
        }
    }

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        var descriptor = NewLinkDescriptor.Trim();
        if (string.IsNullOrEmpty(descriptor))
            return;

        LinkError = null;

        MavChannel channel;
        try
        {
            channel = await _links.AddAsync(descriptor);
        }
        catch (FormatException ex)
        {
            LinkError = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            LinkError = $"Connection failed: {ex.Message}";
            return;
        }

        Links.Add(new LinkEntry(descriptor) { Channel = channel, Connected = true });
        _config.Links.Add(descriptor);
        _config.Save();
        NewLinkDescriptor = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveLinkAsync(LinkEntry? entry)
    {
        if (entry is null)
            return;

        var index = Links.IndexOf(entry);
        if (index < 0)
            return;

        if (entry.Channel is not null)
            await _links.RemoveAsync(entry.Channel);

        Links.RemoveAt(index);
        _config.Links.RemoveAt(index);
        _config.Save();
    }
}

/// <summary>
/// Represents a configured link with its connection status.
/// </summary>
public partial class LinkEntry : ObservableObject
{
    public LinkEntry(string descriptor)
    {
        Descriptor = descriptor;
    }

    /// <summary>
    /// Gets the connection descriptor string.
    /// </summary>
    public string Descriptor { get; }

    /// <summary>
    /// Gets or sets the channel for this link when connected.
    /// </summary>
    public MavChannel? Channel { get; set; }

    /// <summary>
    /// Gets or sets whether this link is currently connected.
    /// </summary>
    [ObservableProperty]
    private bool _connected;
}
