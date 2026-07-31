#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.UI;

internal sealed class WeatherSettingsForm : SolisDialogForm
{
    private readonly QWeatherSettings _existing;
    private readonly Func<QWeatherSettings, WeatherMetricsReading> _testSettings;
    private readonly Action<QWeatherSettings, WeatherMetricsReading> _saveSettings;
    private readonly TextBox _apiHost;
    private readonly TextBox _apiKey;
    private readonly TextBox _coordinates;
    private readonly Button _saveButton;
    private readonly Button _testButton;
    private readonly Label _status;
    private QWeatherSettings? _testedSettings;
    private WeatherMetricsReading? _testedReading;

    public WeatherSettingsForm(
        QWeatherSettings existing,
        Func<QWeatherSettings, WeatherMetricsReading> testSettings,
        Action<QWeatherSettings, WeatherMetricsReading> saveSettings)
        : base("天气配置", new Size(700, 560))
    {
        _existing = existing ?? throw new ArgumentNullException(nameof(existing));
        _testSettings = testSettings ?? throw new ArgumentNullException(nameof(testSettings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));

        var root = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(CreateDialogHeader(
            "\uE753",
            "和风天气",
            "配置天气服务，并在保存前验证地区与实时数据。"), 0, 0);

        SolisDialogCard card = CreateCard(new Padding(18, 12, 18, 12));
        var fields = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 33.334f));
        _apiHost = AddField(fields, 0, "API Host", existing.ApiHost);
        _apiKey = AddField(fields, 1, "API Key（留空保持现有密钥）", string.Empty);
        _apiKey.UseSystemPasswordChar = true;
        _coordinates = AddCoordinateField(
            fields,
            2,
            "经纬度（经度,纬度）",
            FormatCoordinates(existing.Longitude, existing.Latitude));
        card.Controls.Add(fields);
        root.Controls.Add(card, 0, 1);

        var statusCard = new SolisDialogCard(Palette)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
            Padding = new Padding(16, 10, 16, 10)
        };
        _status = CreateLabel(
            "测试时会根据经纬度自动获取地区；测试成功后才能保存。",
            9,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleLeft);
        _status.AutoEllipsis = false;
        _status.BackColor = Palette.Surface;
        statusCard.Controls.Add(_status);
        root.Controls.Add(statusCard, 0, 2);

        _saveButton = CreateButton("保存", SolisButtonKind.Primary);
        _saveButton.Enabled = false;
        _saveButton.Click += SaveClick;
        _testButton = CreateButton("测试连接", SolisButtonKind.Secondary);
        _testButton.Click += TestClick;
        Button cancelButton = CreateButton("取消", SolisButtonKind.Ghost);
        cancelButton.DialogResult = DialogResult.Cancel;
        root.Controls.Add(CreateFooter(_saveButton, _testButton, cancelButton), 0, 3);

        AcceptButton = _testButton;
        CancelButton = cancelButton;
        Controls.Add(root);

        _apiHost.TextChanged += InputChanged;
        _apiKey.TextChanged += InputChanged;
        _coordinates.TextChanged += InputChanged;
    }

    private TextBox AddField(
        TableLayoutPanel layout,
        int row,
        string title,
        string value)
    {
        TableLayoutPanel field = CreateFieldLayout();
        field.Controls.Add(CreateFieldCaption(title), 0, 0);
        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 8),
            Text = value
        };
        StyleTextBox(textBox);
        field.Controls.Add(textBox, 0, 1);
        layout.Controls.Add(field, 0, row);
        return textBox;
    }

    private TextBox AddCoordinateField(
        TableLayoutPanel layout,
        int row,
        string title,
        string value)
    {
        TableLayoutPanel field = CreateFieldLayout();
        var heading = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        heading.Controls.Add(CreateFieldCaption(title), 0, 0);
        var pickerLink = new LinkLabel
        {
            AutoEllipsis = true,
            BackColor = Palette.Surface,
            Dock = DockStyle.Fill,
            LinkColor = Palette.Accent,
            Margin = Padding.Empty,
            Text = "中国大陆使用 GCJ-02，可在高德地图查询坐标",
            TextAlign = ContentAlignment.MiddleRight,
            VisitedLinkColor = Palette.Accent
        };
        pickerLink.LinkArea = new LinkArea(16, 8);
        pickerLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "https://lbs.amap.com/tools/picker")
        {
            UseShellExecute = true
        });
        heading.Controls.Add(pickerLink, 1, 0);

        var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 8),
            Text = value
        };
        StyleTextBox(textBox);
        field.Controls.Add(heading, 0, 0);
        field.Controls.Add(textBox, 0, 1);
        layout.Controls.Add(field, 0, row);
        return textBox;
    }

    private TableLayoutPanel CreateFieldLayout()
    {
        var field = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return field;
    }

    private Label CreateFieldCaption(string text)
    {
        Label label = CreateLabel(
            text,
            9,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleLeft);
        label.BackColor = Palette.Surface;
        return label;
    }

    private void InputChanged(object? sender, EventArgs e)
    {
        _testedSettings = null;
        _testedReading = null;
        _saveButton.Enabled = false;
        _status.ForeColor = Palette.Warning;
        _status.Text = "配置已更改，请重新测试连接。";
    }

    private async void TestClick(object? sender, EventArgs e)
    {
        if (!TryCreateSettings(out QWeatherSettings? settings, out string error))
        {
            _status.ForeColor = Palette.Danger;
            _status.Text = error;
            return;
        }

        _testButton.Enabled = false;
        SetInputsEnabled(false);
        _status.ForeColor = Palette.TextSecondary;
        _status.Text = "正在测试天气服务…";
        try
        {
            WeatherMetricsReading reading = await Task.Run(() => _testSettings(settings!));
            if (!reading.Available)
            {
                _status.ForeColor = Palette.Danger;
                _status.Text = $"测试失败：{DescribeError(reading.ErrorCategory)}";
                return;
            }

            _testedSettings = settings;
            _testedReading = reading;
            _saveButton.Enabled = true;
            _status.ForeColor = Palette.Success;
            _status.Text =
                $"测试成功：{reading.Location} · {reading.Description} · " +
                $"{reading.OutdoorLowC:0.#}–{reading.OutdoorHighC:0.#}°C；现在可以保存。";
        }
        catch (Exception exception)
        {
            _status.ForeColor = Palette.Danger;
            _status.Text = $"测试失败：{exception.Message}";
        }
        finally
        {
            SetInputsEnabled(true);
            _testButton.Enabled = true;
        }
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        if (_testedSettings is null || _testedReading is null)
        {
            _saveButton.Enabled = false;
            _status.ForeColor = Palette.Danger;
            _status.Text = "请先完成连接测试。";
            return;
        }

        try
        {
            _saveSettings(_testedSettings, _testedReading);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _status.ForeColor = Palette.Danger;
            _status.Text = $"保存失败：{exception.Message}";
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        _apiHost.Enabled = enabled;
        _apiKey.Enabled = enabled;
        _coordinates.Enabled = enabled;
    }

    private bool TryCreateSettings(out QWeatherSettings? settings, out string error)
    {
        settings = null;
        string host = _apiHost.Text.Trim();
        string apiKey = string.IsNullOrWhiteSpace(_apiKey.Text)
            ? _existing.ApiKey
            : _apiKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(host) ||
            host.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 ||
            Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            error = "API Host 只填写专属域名，不要包含 https:// 或路径。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            error = "请输入 API Key。";
            return false;
        }

        string[] coordinateParts = _coordinates.Text.Trim().Split(
            [',', '，'],
            StringSplitOptions.RemoveEmptyEntries);
        if (coordinateParts.Length != 2 ||
            !TryCoordinate(coordinateParts[0], -180, 180, out double longitude) ||
            !TryCoordinate(coordinateParts[1], -90, 90, out double latitude))
        {
            error = "经纬度格式或范围无效；请按“121.51,38.84”填写，经度在前。";
            return false;
        }

        settings = new QWeatherSettings(
            true,
            host,
            apiKey,
            string.Empty,
            null,
            longitude,
            latitude);
        error = string.Empty;
        return true;
    }

    private static bool TryCoordinate(
        string value,
        double minimum,
        double maximum,
        out double result) =>
        double.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        !double.IsNaN(result) &&
        !double.IsInfinity(result) &&
        result >= minimum &&
        result <= maximum;

    private static string FormatCoordinates(double? longitude, double? latitude)
    {
        if (!longitude.HasValue || !latitude.HasValue)
            return string.Empty;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:R},{1:R}",
            longitude.Value,
            latitude.Value);
    }

    private static string DescribeError(string? category) => category switch
    {
        "ApiKeyMissing" => "缺少 API Key",
        "ApiHostInvalid" => "API Host 无效",
        "CoordinatesInvalid" => "经纬度无效",
        "ApiRejected" => "API Key、Host 或权限被服务拒绝",
        "NetworkError" => "网络连接失败",
        "Timeout" => "请求超时",
        "InvalidJson" => "服务返回了无法解析的数据",
        _ when category?.StartsWith("HttpStatus", StringComparison.Ordinal) == true =>
            $"服务返回 HTTP {category.Substring("HttpStatus".Length)}",
        _ => category ?? "未知错误"
    };
}
