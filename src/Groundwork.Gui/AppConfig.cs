// Groundwork
// Copyright (C) 2026 Bob Long
//
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.IO;
using System.Text.Json;

namespace Groundwork.Gui;

/// <summary>
/// Provides persistent application configuration backed by a JSON file.
/// </summary>
public sealed class AppConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Groundwork"
    );

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "gui-config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Gets or sets the configured link descriptors.
    /// </summary>
    public List<string> Links { get; set; } = [];

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllBytes(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError(
                "Failed to load config from '{0}': {1}",
                ConfigPath,
                ex.Message
            );
            return new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        File.WriteAllBytes(ConfigPath, json);
    }
}
