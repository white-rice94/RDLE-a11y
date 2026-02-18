using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace RDEventEditorHelper
{
    public class PropertyData
    {
        public string name;
        public string displayName;
        public string value;
        public string type;
        public string[] options;
    }

    public class EditorForm : Form
    {
        private FlowLayoutPanel _panel;
        private Button _btnOK, _btnCancel;
        private string _eventType;
        private PropertyData[] _properties;
        private Dictionary<string, Control> _controls = new Dictionary<string, Control>();
        private bool _isClosingByButton = false;

        public event Action<Dictionary<string, string>> OnOK;
        public event Action OnCancel;

        public EditorForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "事件属性编辑器";
            this.Size = new Size(500, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = true;
            this.TopMost = true;

            _panel = new FlowLayoutPanel();
            _panel.Dock = DockStyle.Top;
            _panel.Height = 520;
            _panel.AutoScroll = true;
            _panel.FlowDirection = FlowDirection.TopDown;
            _panel.WrapContents = false;
            _panel.Padding = new Padding(10);
            this.Controls.Add(_panel);

            var btnPanel = new FlowLayoutPanel();
            btnPanel.Dock = DockStyle.Bottom;
            btnPanel.Height = 60;
            btnPanel.Padding = new Padding(10);

            _btnCancel = new Button { Text = "取消(&C)", Width = 100, Height = 35 };
            _btnOK = new Button { Text = "确定(&O)", Width = 100, Height = 35 };

            _btnOK.Click += (s, e) =>
            {
                _isClosingByButton = true;
                OnOK?.Invoke(GetCurrentUpdates());
                this.Close();
            };
            _btnCancel.Click += (s, e) =>
            {
                _isClosingByButton = true;
                OnCancel?.Invoke();
                this.Close();
            };

            btnPanel.Controls.Add(_btnOK);
            btnPanel.Controls.Add(_btnCancel);
            this.Controls.Add(btnPanel);

            this.CancelButton = _btnCancel;
            this.AcceptButton = _btnOK;

            this.FormClosing += (s, e) =>
            {
                if (_isClosingByButton) return;
                e.Cancel = true;
                _isClosingByButton = true;
                OnCancel?.Invoke();
                this.Close();
            };
        }

        public void SetData(string eventType, PropertyData[] properties)
        {
            _eventType = eventType;
            _properties = properties;
            this.Text = $"编辑事件: {eventType}";
            BuildUI();
        }

        private void BuildUI()
        {
            _panel.Controls.Clear();
            _controls.Clear();

            if (_properties == null || _properties.Length == 0)
            {
                var lbl = new Label
                {
                    Text = "该事件没有可编辑的属性",
                    AutoSize = true,
                    Padding = new Padding(10)
                };
                _panel.Controls.Add(lbl);
                return;
            }

            foreach (var prop in _properties)
            {
                string displayName = prop.displayName ?? prop.name;

                var group = new GroupBox
                {
                    Text = displayName,
                    Width = 440,
                    Height = 55,
                    Padding = new Padding(5),
                    AccessibleName = displayName
                };

                Control inputCtrl = null;

                switch (prop.type)
                {
                    case "Int":
                    case "Float":
                    case "String":
                        var txt = new TextBox
                        {
                            Text = prop.value ?? "",
                            Width = 400,
                            Top = 20,
                            Left = 10,
                            AccessibleName = displayName
                        };
                        inputCtrl = txt;
                        break;

                    case "Bool":
                        var chk = new CheckBox
                        {
                            Text = displayName,
                            Checked = prop.value == "true",
                            Top = 20,
                            Left = 10,
                            AutoSize = true,
                            AccessibleName = displayName
                        };
                        inputCtrl = chk;
                        break;

                    case "Enum":
                        var cmb = new ComboBox
                        {
                            Width = 400,
                            Top = 20,
                            Left = 10,
                            DropDownStyle = ComboBoxStyle.DropDownList,
                            AccessibleName = displayName
                        };
                        if (prop.options != null)
                            cmb.Items.AddRange(prop.options);
                        if (!string.IsNullOrEmpty(prop.value))
                            cmb.SelectedItem = prop.value;
                        else if (cmb.Items.Count > 0)
                            cmb.SelectedIndex = 0;
                        inputCtrl = cmb;
                        break;

                    case "Vector2":
                    case "Float2":
                        // 解析 "x,y" 格式
                        var parts2 = (prop.value ?? "0,0").Split(',');
                        string xVal = parts2.Length > 0 ? parts2[0].Trim() : "0";
                        string yVal = parts2.Length > 1 ? parts2[1].Trim() : "0";
                        
                        var vecPanel = new FlowLayoutPanel
                        {
                            FlowDirection = FlowDirection.LeftToRight,
                            Width = 420,
                            Height = 30,
                            Top = 20,
                            Left = 10,
                            Margin = new Padding(0)
                        };
                        
                        var lblX = new Label { Text = "X:", Width = 20, Top = 3 };
                        var txtX = new TextBox { Text = xVal, Width = 180, Name = "X" };
                        var lblY = new Label { Text = "Y:", Width = 20, Top = 3 };
                        var txtY = new TextBox { Text = yVal, Width = 180, Name = "Y" };
                        
                        vecPanel.Controls.AddRange(new Control[] { lblX, txtX, lblY, txtY });
                        inputCtrl = vecPanel;
                        break;

                    case "FloatExpression":
                        var exprTxt = new TextBox
                        {
                            Text = prop.value ?? "",
                            Width = 400,
                            Top = 20,
                            Left = 10,
                            AccessibleName = displayName
                        };
                        inputCtrl = exprTxt;
                        break;

                    case "FloatExpression2":
                        // 解析 "x,y" 格式的表达式
                        var exprParts = (prop.value ?? ",").Split(',');
                        string exprX = exprParts.Length > 0 ? exprParts[0].Trim() : "";
                        string exprY = exprParts.Length > 1 ? exprParts[1].Trim() : "";
                        
                        var exprPanel = new FlowLayoutPanel
                        {
                            FlowDirection = FlowDirection.LeftToRight,
                            Width = 420,
                            Height = 30,
                            Top = 20,
                            Left = 10,
                            Margin = new Padding(0)
                        };
                        
                        var lblExpr1 = new Label { Text = "X:", Width = 20, Top = 3 };
                        var txtExpr1 = new TextBox { Text = exprX, Width = 180, Name = "X" };
                        var lblExpr2 = new Label { Text = "Y:", Width = 20, Top = 3 };
                        var txtExpr2 = new TextBox { Text = exprY, Width = 180, Name = "Y" };
                        
                        exprPanel.Controls.AddRange(new Control[] { lblExpr1, txtExpr1, lblExpr2, txtExpr2 });
                        inputCtrl = exprPanel;
                        break;

                    case "Color":
                        var colorPanel = new FlowLayoutPanel
                        {
                            FlowDirection = FlowDirection.LeftToRight,
                            Width = 420,
                            Height = 30,
                            Top = 20,
                            Left = 10,
                            Margin = new Padding(0)
                        };
                        
                        var colorTxt = new TextBox 
                        { 
                            Text = prop.value ?? "#FFFFFF", 
                            Width = 300,
                            Name = "ColorText"
                        };
                        
                        var colorPreview = new Panel
                        {
                            Width = 30,
                            Height = 20,
                            BackColor = ParseColor(prop.value ?? "#FFFFFF")
                        };
                        
                        var btnPickColor = new Button
                        {
                            Text = "选择",
                            Width = 60,
                            Height = 23
                        };
                        
                        btnPickColor.Click += (s, e) =>
                        {
                            using (var colorDialog = new ColorDialog())
                            {
                                colorDialog.Color = colorPreview.BackColor;
                                if (colorDialog.ShowDialog() == DialogResult.OK)
                                {
                                    colorPreview.BackColor = colorDialog.Color;
                                    colorTxt.Text = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                                }
                            }
                        };
                        
                        colorTxt.TextChanged += (s, e) =>
                        {
                            try
                            {
                                colorPreview.BackColor = ParseColor(colorTxt.Text);
                            }
                            catch { }
                        };
                        
                        colorPanel.Controls.AddRange(new Control[] { colorTxt, colorPreview, btnPickColor });
                        inputCtrl = colorPanel;
                        break;

                    default:
                        var lbl = new Label
                        {
                            Text = $"不支持的类型: {prop.type}",
                            Width = 400,
                            Top = 20,
                            Left = 10,
                            AccessibleName = displayName
                        };
                        inputCtrl = lbl;
                        break;
                }

                if (inputCtrl != null)
                {
                    group.Controls.Add(inputCtrl);
                    _controls[prop.name] = inputCtrl;
                    _panel.Controls.Add(group);
                }
            }
        }

        private Dictionary<string, string> GetCurrentUpdates()
        {
            var updates = new Dictionary<string, string>();

            foreach (var kvp in _controls)
            {
                string propName = kvp.Key;
                Control ctrl = kvp.Value;
                string value = null;

                if (ctrl is TextBox txt)
                    value = txt.Text;
                else if (ctrl is CheckBox chk)
                    value = chk.Checked ? "true" : "false";
                else if (ctrl is ComboBox cmb)
                    value = cmb.SelectedItem?.ToString();
                else if (ctrl is FlowLayoutPanel panel)
                {
                    // 处理 Vector2, Float2, FloatExpression2, Color
                    var txtX = panel.Controls.Find("X", false).FirstOrDefault() as TextBox;
                    var txtY = panel.Controls.Find("Y", false).FirstOrDefault() as TextBox;
                    var colorTxt = panel.Controls.Find("ColorText", false).FirstOrDefault() as TextBox;
                    
                    if (txtX != null && txtY != null)
                    {
                        // Vector2, Float2, FloatExpression2
                        value = $"{txtX.Text},{txtY.Text}";
                    }
                    else if (colorTxt != null)
                    {
                        // Color
                        value = colorTxt.Text;
                    }
                }

                if (value != null)
                    updates[propName] = value;
            }

            return updates;
        }

        private Color ParseColor(string colorStr)
        {
            try
            {
                if (string.IsNullOrEmpty(colorStr))
                    return Color.White;

                // 支持 #RRGGBB 格式
                if (colorStr.StartsWith("#") && colorStr.Length == 7)
                {
                    int r = int.Parse(colorStr.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
                    int g = int.Parse(colorStr.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                    int b = int.Parse(colorStr.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
                    return Color.FromArgb(r, g, b);
                }

                // 尝试直接解析颜色名称
                return Color.FromName(colorStr);
            }
            catch
            {
                return Color.White;
            }
        }
    }
}
