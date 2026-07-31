// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Notifications;
using LibreHardwareMonitor.Utilities;

namespace LibreHardwareMonitor.UI;

public class SystemTray : IDisposable
{
    private IComputer _computer;
    private readonly PersistentSettings _settings;
    private readonly UnitManager _unitManager;
    private readonly List<SensorNotifyIcon> _sensorList = new List<SensorNotifyIcon>();
    private bool _mainIconEnabled;
    private readonly NotifyIconAdv _mainIcon;
    private readonly IUserNotificationService _notificationService;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _deviceStatusItem;
    private readonly ToolStripMenuItem _openDeviceWebUiItem;
    private readonly ToolStripMenuItem _silentStartupItem;

    public SystemTray(IComputer computer, PersistentSettings settings, UnitManager unitManager)
    {
        _computer = computer;
        _settings = settings;
        _unitManager = unitManager;
        _notificationService = new WindowsNotificationService();
        computer.HardwareAdded += HardwareAdded;
        computer.HardwareRemoved += HardwareRemoved;

        _mainIcon = new NotifyIconAdv();

        ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
        _deviceStatusItem = new ToolStripMenuItem("副屏：正在发现…")
        {
            Enabled = false
        };
        contextMenuStrip.Items.Add(_deviceStatusItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripItem openItem = new ToolStripMenuItem("打开设备控制台");
        openItem.Click += delegate
        {
            SendHideShowCommand();
        };
        contextMenuStrip.Items.Add(openItem);

        _openDeviceWebUiItem = new ToolStripMenuItem("打开副屏 WebUI")
        {
            Enabled = false
        };
        _openDeviceWebUiItem.Click += delegate
        {
            OpenDeviceWebUiRequested?.Invoke(this, EventArgs.Empty);
        };
        contextMenuStrip.Items.Add(_openDeviceWebUiItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        _silentStartupItem = new ToolStripMenuItem("静默启动");
        _silentStartupItem.Click += delegate
        {
            SilentStartupChangeRequested?.Invoke(!_silentStartupItem.Checked);
        };
        contextMenuStrip.Items.Add(_silentStartupItem);

        _autoStartItem = new ToolStripMenuItem("开机启动");
        _autoStartItem.Click += delegate
        {
            AutoStartChangeRequested?.Invoke(!_autoStartItem.Checked);
        };
        contextMenuStrip.Items.Add(_autoStartItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripItem exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += delegate
        {
            SendExitCommand();
        };
        contextMenuStrip.Items.Add(exitItem);
        _mainIcon.ContextMenuStrip = contextMenuStrip;
        _mainIcon.DoubleClick += delegate
        {
            SendHideShowCommand();
        };
        _mainIcon.Icon = EmbeddedResources.GetIcon("smallicon.ico");
        _mainIcon.Text = "Solis Monitor";
    }

    private void HardwareRemoved(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (ISensor sensor in hardware.Sensors)
            SensorRemoved(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareRemoved(subHardware);
    }

    private void HardwareAdded(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        if (_settings.GetValue(new Identifier(sensor.Identifier, "tray").ToString(), false))
            Add(sensor, false);
    }

    private void SensorRemoved(ISensor sensor)
    {
        if (Contains(sensor))
            Remove(sensor, false);
    }

    public void Dispose()
    {
        foreach (SensorNotifyIcon icon in _sensorList)
            icon.Dispose();
        _mainIcon.Dispose();
    }

    public void Redraw()
    {
        foreach (SensorNotifyIcon icon in _sensorList)
            icon.Update();
    }

    public bool Contains(ISensor sensor)
    {
        foreach (SensorNotifyIcon icon in _sensorList)
            if (icon.Sensor == sensor)
                return true;
        return false;
    }

    public void Add(ISensor sensor, bool balloonTip)
    {
        if (Contains(sensor))
            return;


        _sensorList.Add(new SensorNotifyIcon(this, sensor, _settings, _unitManager));
        UpdateMainIconVisibility();
        _settings.SetValue(new Identifier(sensor.Identifier, "tray").ToString(), true);
    }

    public void Remove(ISensor sensor)
    {
        Remove(sensor, true);
    }

    private void Remove(ISensor sensor, bool deleteConfig)
    {
        if (deleteConfig)
        {
            _settings.Remove(new Identifier(sensor.Identifier, "tray").ToString());
            _settings.Remove(new Identifier(sensor.Identifier, "traycolor").ToString());
        }
        SensorNotifyIcon instance = null;
        foreach (SensorNotifyIcon icon in _sensorList)
        {
            if (icon.Sensor == sensor)
                instance = icon;
        }
        if (instance != null)
        {
            _sensorList.Remove(instance);
            UpdateMainIconVisibility();
            instance.Dispose();
        }
    }

    public event EventHandler HideShowCommand;

    public void SendHideShowCommand()
    {
        HideShowCommand?.Invoke(this, null);
    }

    public event EventHandler ExitCommand;

    public event Action<bool> AutoStartChangeRequested;

    public event EventHandler OpenDeviceWebUiRequested;

    public event Action<bool> SilentStartupChangeRequested;

    public void SendExitCommand()
    {
        ExitCommand?.Invoke(this, null);
    }

    public bool AutoStart
    {
        get => _autoStartItem.Checked;
        set => _autoStartItem.Checked = value;
    }

    public bool SilentStartup
    {
        get => _silentStartupItem.Checked;
        set => _silentStartupItem.Checked = value;
    }

    public void UpdateDevice(DeviceTrayPresentation presentation)
    {
        if (presentation == null)
            throw new ArgumentNullException(nameof(presentation));

        _deviceStatusItem.Text = presentation.StatusText;
        _openDeviceWebUiItem.Enabled = presentation.CanOpenWebUi;
    }

    public void ShowDeviceOfflineNotification(string hostName)
    {
        string message = $"副屏 {hostName} 已离线超过 2 分钟。";
        if (_notificationService.TryShow("Solis Monitor", message))
            return;

        _mainIcon.ShowBalloonTip(
            10000,
            "Solis Monitor",
            message,
            ToolTipIcon.Warning);
    }

    public void ShowDeviceTokenMismatchNotification(string hostName)
    {
        string message =
            $"副屏 {hostName} 的设备令牌不匹配。请双击 GPIO21 开启物理授权并完成安全配对。";
        if (_notificationService.TryShow("Solis Monitor", message))
            return;

        _mainIcon.ShowBalloonTip(
            15000,
            "Solis Monitor",
            message,
            ToolTipIcon.Warning);
    }

    public void ShowWeatherFailureNotification(string message)
    {
        if (_notificationService.TryShow("Solis Monitor 天气", message))
            return;

        _mainIcon.ShowBalloonTip(
            15000,
            "Solis Monitor 天气",
            message,
            ToolTipIcon.Warning);
    }

    private void UpdateMainIconVisibility()
    {
        _mainIcon.Visible = _mainIconEnabled;
    }

    public bool IsMainIconEnabled
    {
        get { return _mainIconEnabled; }
        set
        {
            if (_mainIconEnabled != value)
            {
                _mainIconEnabled = value;
                UpdateMainIconVisibility();
            }
        }
    }
}
