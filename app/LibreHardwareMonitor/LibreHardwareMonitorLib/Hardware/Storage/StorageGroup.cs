// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using StorageDIT = DiskInfoToolkit.Storage;

namespace LibreHardwareMonitor.Hardware.Storage;

internal class StorageGroup : IGroup
{
    private readonly List<StorageDevice> _hardware = new();

    public StorageGroup(ISettings settings)
        : this(settings, StorageDIT.GetDisks, StorageDIT.StopMonitoring)
    { }

    internal StorageGroup(
        ISettings settings,
        Func<List<DiskInfoToolkit.StorageDevice>> getDisks,
        Action stopMonitoring)
    {
        if (Software.OperatingSystem.IsUnix)
            return;

        try
        {
            AddHardware(settings, getDisks());
        }
        finally
        {
            stopMonitoring();
        }
    }

    public IReadOnlyList<IHardware> Hardware => _hardware;

    private void AddHardware(
        ISettings settings,
        IEnumerable<DiskInfoToolkit.StorageDevice> disks)
    {
        //Transform storage device to hardware
        _hardware.AddRange(disks
            .Where(HasUsableMedia)
            .Select(s => new StorageDevice(s, settings)));
    }

    private static bool HasUsableMedia(DiskInfoToolkit.StorageDevice storage) =>
        storage.DiskSizeBytes is > 0;

    public void Close() { }

    public string GetReport() => null;
}
