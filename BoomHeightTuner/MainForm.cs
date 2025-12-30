using System;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;


namespace BoomHeightTuner
{
    public partial class MainForm : Form
    {
        private Button btnTapTest;
        private CheckBox chkShowLog;
        private int _savedSplitterDistance = -1;   // remembers chart/log split when hiding log
        private int _logWidth = 180;               // your preferred thin log width
        private int _faultPrev = 0;                // for detecting fault transitions

        private bool _armed = false;

        private SplitContainer centerPanel;
        private RichTextBox txtHelp;

        private void RequestStatusAndParams()
        {
            if (!_sp.IsOpen) return;

            SendLine("STATUS"); // get armed/fault immediately
            SendLine("GET");    // populate params table
        }

        private void RenderHelpMarkdown(string text)
        {
            txtHelp.Clear();
            txtHelp.SelectionStart = 0;

            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                string line = rawLine ?? "";

                // SECTION HEADER: ## Title
                if (line.StartsWith("## "))
                {
                    txtHelp.SelectionFont = new Font("Segoe UI", 13, FontStyle.Bold);
                    txtHelp.SelectionColor = Color.DarkSlateBlue;
                    txtHelp.AppendText(line.Substring(3).Trim() + Environment.NewLine + Environment.NewLine);
                    continue;
                }

                // Preserve indentation
                int indent = line.TakeWhile(c => c == ' ').Count();
                string content = line.TrimStart();

                txtHelp.SelectionFont = new Font("Segoe UI", 11, FontStyle.Regular);
                txtHelp.SelectionColor = Color.Black;

                // Apply indentation visually
                txtHelp.SelectionIndent = indent * 6;   // tweak if needed
                txtHelp.SelectionHangingIndent = 0;

                AppendWithEmphasis(content);
            }

            txtHelp.SelectionIndent = 0;
        }

        private void AppendWithEmphasis(string line)
        {
            bool bold = false;
            var parts = line.Split('*');

            foreach (var p in parts)
            {
                txtHelp.SelectionFont = bold
                    ? new Font("Segoe UI", 11, FontStyle.Bold)
                    : new Font("Segoe UI", 11, FontStyle.Regular);

                txtHelp.AppendText(p);
                bold = !bold;
            }

            txtHelp.AppendText(Environment.NewLine);
        }

        private string DefaultHelpText()
        {
            return
        @"HOW TO USE

• Main Form
• Tap a parameter to edit
• ENTER sends immediately when disarmed
• When ARMED, changes are marked PENDING
• DISARM to apply pending changes

SAFETY
• ESTOP immediately disables motion
• Loss of comms auto-disarms
• Sensor faults auto-disarm

TIPS
• Watch PRED vs TARGET
• Adjust hydraulicDelaySec if overshoot occurs
";
        }


        private void LoadHelpText()
        {
            try
            {
                // Put help.txt next to the EXE
                string exeDir = AppContext.BaseDirectory;
                string path = Path.Combine(exeDir, "help.txt");

                if (File.Exists(path))
                {
                    RenderHelpMarkdown(File.ReadAllText(path, Encoding.UTF8));

                }
                else
                {
                    // First run: use default, and optionally write out a starter file
                    txtHelp.Text = DefaultHelpText();
                    File.WriteAllText(path, txtHelp.Text, Encoding.UTF8);
                    Log($"help.txt created at: {path}");
                }
            }
            catch (Exception ex)
            {
                txtHelp.Text = DefaultHelpText();
                Log("HELP load failed: " + ex.Message);
            }
        }

        private void ShowKeypadForParamCell(int rowIndex)
        {
            if (rowIndex < 0) return;

            // Column 0 = Key, Column 1 = Value
            var key = gridParams.Rows[rowIndex].Cells[0].Value?.ToString();
            if (string.IsNullOrWhiteSpace(key)) return;

            var valueCell = gridParams.Rows[rowIndex].Cells[1];
            string oldValue = valueCell.Value?.ToString() ?? "";

            // Remember previous styling so we can restore on Cancel
            var oldBack = valueCell.Style.BackColor;
            var oldFore = valueCell.Style.ForeColor;
            var oldTip = valueCell.ToolTipText;

            // Highlight + clear immediately (your request)
            valueCell.Style.BackColor = Color.Khaki;
            valueCell.Style.ForeColor = Color.Black;
            valueCell.ToolTipText = "EDITING";
            valueCell.Value = "";
            gridParams.Refresh();

            using (var kp = new NumericKeypadForm("Set " + key, ""))
            {
                var dr = kp.ShowDialog(this);

                if (dr == DialogResult.OK)
                {
                    var newVal = kp.ValueText.Trim();

                    // Treat blank as "cancel" (restore)
                    if (string.IsNullOrWhiteSpace(newVal))
                    {
                        valueCell.Value = oldValue;
                        valueCell.Style.BackColor = oldBack;
                        valueCell.Style.ForeColor = oldFore;
                        valueCell.ToolTipText = oldTip;
                        return;
                    }

                    // write into grid
                    valueCell.Value = newVal;

                    if (_armed)
                    {
                        // ARMED -> do NOT send now; mark pending (orange)
                        MarkParamPending(key, newVal);
                        Log($"PENDING (ARMED): {key} = {newVal}");
                    }
                    else
                    {
                        // DISARMED -> send immediately
                        valueCell.Style.BackColor = Color.LightYellow;
                        valueCell.Style.ForeColor = Color.Black;
                        valueCell.ToolTipText = "SENT";

                        SendLine($"SET {key} {newVal}");
                    }
                }
                else
                {
                    // Cancel/close -> restore previous value and styling
                    valueCell.Value = oldValue;
                    valueCell.Style.BackColor = oldBack;
                    valueCell.Style.ForeColor = oldFore;
                    valueCell.ToolTipText = oldTip;
                }
            }
        }


        private void MainForm_Load(object sender, EventArgs e)
        {
            // Designer expects this method. We don’t need to do anything here.
        }

        // ---------------- Serial ----------------
        private readonly SerialPort _sp = new SerialPort();
        private readonly StringBuilder _rxAccum = new StringBuilder(2048);

        // ---------------- UI ----------------
        private ComboBox cbPorts = new ComboBox();
        private Button btnRefreshPorts = new Button();
        private Button btnConnect = new Button();

        private Button btnArm = new Button();
        private Button btnDisarm = new Button();
        private Button btnEstop = new Button();

        private Button btnStatus = new Button();
        private Button btnGet = new Button();
        private Button btnSave = new Button();
        private Button btnLoad = new Button();
        private Button btnDefaults = new Button();

        private CheckBox chkTelemetry = new CheckBox();
        private Label lblConn = new Label();

        private TextBox txtLog = new TextBox();

        private TextBox txtMph = new TextBox();
        private TextBox txtIps = new TextBox();
        private TextBox txtFilt = new TextBox();
        private TextBox txtPred = new TextBox();
        private TextBox txtErr = new TextBox();
        private TextBox txtCmd = new TextBox();
        private TextBox txtArmed = new TextBox();
        private TextBox txtFault = new TextBox();

        private DataGridView gridParams = new DataGridView();
        private Button btnApplySelected = new Button();
        private Button btnApplyAll = new Button();

        private Chart chart = new Chart();
        private SplitContainer rightPanel;

        // Pending parameter edits made while ARMED (apply on DISARM)
        private readonly Dictionary<string, string> _pendingParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Track arm transitions
        //private bool _armed;
        private bool _prevArmed = false;

        // Colors
        private readonly Color _pendingColor = Color.Orange;        // while armed
        private readonly Color _appliedColor = Color.LightGreen;   // after auto-apply

        // ---------------- Timers ----------------
        private readonly System.Windows.Forms.Timer _keepAliveTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _uiRefreshTimer = new System.Windows.Forms.Timer();


        // ---------------- Latest telemetry ----------------
        private readonly object _telemetryLock = new object();
        private Telemetry _lastT = new Telemetry();
        private StatusLine _lastStatus = new StatusLine();

        // Rolling chart buffers
        private int _sampleIndex = 0;
        private const int MaxPoints = 200;

        // Parameter keys we expose in grid
        private readonly string[] ParamKeys =
        {
            "targetIn",
            "deadbandIn",
            "sensorLeadIn",
            "hydraulicDelaySec",
            "medianSamples",
            "validMinIn",
            "validMaxIn",
            "minValveOnMs",
            "minValveOffMs",
            "pulseMinMs",
            "pulseMaxMs",
            "errForMaxPulseIn",
            "gps_mph"
        };

        public MainForm()
        {
            Text = "Boom Height Tuner (USB)";
            Width = 1200;
            Height = 780;

            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();

            this.Icon = new Icon("boom.ico");

            WireEvents();

            RefreshPorts();

            // Serial defaults
            _sp.BaudRate = 115200;
            _sp.NewLine = "\n";
            _sp.DtrEnable = true;
            _sp.RtsEnable = true;

            // Keepalive: prevents COMMS_LOSS_DISARM_MS
            _keepAliveTimer.Interval = 500; // 2 Hz is plenty
            _keepAliveTimer.Tick += (_, __) =>
            {
                if (_sp.IsOpen)
                    SendLine("STATUS");
            };

            // UI refresh (so we don’t update UI from DataReceived thread)
            _uiRefreshTimer.Interval = 100;
            _uiRefreshTimer.Tick += (_, __) => RefreshTelemetryUi();
            _uiRefreshTimer.Start();

            Shown += (s, e) =>
            {
                RefreshPorts();
                if (cbPorts.Items.Count > 0 && cbPorts.SelectedIndex < 0)
                    cbPorts.SelectedIndex = 0;

                // optional: auto-connect
                ToggleConnect();
                // Split chart/help evenly

                centerPanel.SplitterDistance = centerPanel.Height / 2;

                // Now sizes are real (after DPI/layout), so this is safe:
                rightPanel.Panel1MinSize = 400;
                rightPanel.Panel2MinSize = 180;

                int logWidth = 320;

                int max = rightPanel.Width - rightPanel.Panel2MinSize - rightPanel.SplitterWidth;
                int desired = rightPanel.Width - logWidth - rightPanel.SplitterWidth;

                // clamp
                if (desired < rightPanel.Panel1MinSize) desired = rightPanel.Panel1MinSize;
                if (desired > max) desired = max;

                // Only set if max is valid
                if (max >= rightPanel.Panel1MinSize)
                    rightPanel.SplitterDistance = desired;
            };

        }

        private int FindParamRow(string key)
        {
            for (int i = 0; i < gridParams.Rows.Count; i++)
            {
                var k = gridParams.Rows[i].Cells[0].Value?.ToString();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void MarkParamPending(string key, string value)
        {
            _pendingParams[key] = value;

            int row = FindParamRow(key);
            if (row >= 0)
            {
                var cell = gridParams.Rows[row].Cells[1];
                cell.Style.BackColor = _pendingColor;
                cell.Style.ForeColor = Color.Black;
                cell.ToolTipText = "PENDING (will apply when DISARMED)";
            }
        }

        private void ClearParamPendingHighlight(string key)
        {
            int row = FindParamRow(key);
            if (row >= 0)
            {
                var cell = gridParams.Rows[row].Cells[1];
                cell.Style.BackColor = Color.White;
                cell.Style.ForeColor = Color.Black;
                cell.ToolTipText = "";
            }
        }

        private void ApplyPendingIfDisarmed()
        {
            if (_pendingParams.Count == 0) return;

            if (_sp == null || !_sp.IsOpen)
            {
                Log($"PENDING NOT SENT (DISCONNECTED): {_pendingParams.Count} item(s)");
                return;
            }

            if (_armed) return; // only apply when disarmed

            Log($"Applying {_pendingParams.Count} pending param(s)...");

            foreach (var kv in _pendingParams.ToList())
            {
                string key = kv.Key;
                string val = kv.Value;

                SendLine($"SET {key} {val}");

                // Mark applied in the grid
                int row = FindParamRow(key);
                if (row >= 0)
                {
                    var cell = gridParams.Rows[row].Cells[1];
                    cell.Style.BackColor = _appliedColor;
                    cell.Style.ForeColor = Color.Black;
                    cell.ToolTipText = "APPLIED";
                }
            }

            _pendingParams.Clear();
        }


        // ================= UI BUILD =================
        private void BuildUi()
        {
            SuspendLayout();

            // ---------- TOP BAR ----------
            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,          // IMPORTANT
                Height = 72,               // try 68–80
                Padding = new Padding(8, 10, 8, 10),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };


            cbPorts.Width = 180;
          
            btnRefreshPorts.Text = "Refresh";
            btnConnect.Text = "Connect";

            lblConn.AutoSize = true;
            lblConn.Margin = new Padding(10, 10, 0, 0);
            lblConn.Padding = Padding.Empty;
            lblConn.Text = "Disconnected";

            int topH = 44;   // match your taller buttons (use whatever you picked)
            int padY = 8;

            // COM label
            var lblCom = new Label
            {
                Text = "COM:",
                AutoSize = true,
                Margin = new Padding(0, padY + 4, 0, 0)  // centers text vertically
            };

            // Ports dropdown
            cbPorts.DropDownStyle = ComboBoxStyle.DropDownList;
            cbPorts.Width = 180;
            cbPorts.Height = topH;                       // important
            cbPorts.Margin = new Padding(4, padY, 4, padY);

            // Refresh/Connect buttons match height
            btnRefreshPorts.Height = topH;
            btnConnect.Height = topH;
            btnRefreshPorts.Margin = new Padding(4, padY, 4, padY);
            btnConnect.Margin = new Padding(4, padY, 10, padY);

            // Connection label lines up vertically
            lblConn.AutoSize = true;
            lblConn.Margin = new Padding(10, padY + 6, 0, 0);


            btnArm.Text = "ARM";
            btnDisarm.Text = "DISARM";
            btnEstop.Text = "ESTOP";

            btnStatus.Text = "STATUS";
            btnGet.Text = "GET";
            btnSave.Text = "SAVE";
            btnLoad.Text = "LOAD";
            btnDefaults.Text = "DEFAULTS";

            chkTelemetry.Text = "Telemetry";
            chkTelemetry.Checked = true;
            chkTelemetry.Margin = new Padding(10, 10, 0, 0);

            var topButtons = new[]
            {
        btnRefreshPorts, btnConnect,
        btnArm, btnDisarm, btnEstop,
        btnStatus, btnGet, btnSave, btnLoad, btnDefaults
    };

            foreach (var b in topButtons)
            {
                b.AutoSize = false;
                b.Height = 40;
                b.Width = 90;
                b.Margin = new Padding(4, 8, 4, 8);
            }
            btnEstop.Width = 110;

            topPanel.Controls.Add(lblCom);
            topPanel.Controls.Add(cbPorts);
            topPanel.Controls.Add(btnRefreshPorts);
            topPanel.Controls.Add(btnConnect);
            topPanel.Controls.Add(lblConn);


            topPanel.Controls.Add(btnArm);
            topPanel.Controls.Add(btnDisarm);
            topPanel.Controls.Add(btnEstop);

            topPanel.Controls.Add(btnStatus);
            topPanel.Controls.Add(btnGet);
            topPanel.Controls.Add(btnSave);
            topPanel.Controls.Add(btnLoad);
            topPanel.Controls.Add(btnDefaults);

            chkShowLog = new CheckBox
            {
                Text = "Show Log",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(10, 10, 10, 0)
            };

            topPanel.Controls.Add(chkTelemetry);
            topPanel.Controls.Add(chkShowLog);

            btnTapTest = new Button
            {
                Text = "Tap Test",
                AutoSize = false,
                Height = 44,     // use the SAME height as your other top buttons
                Width = 110,     // a bit wider for the text
                Margin = new Padding(4, 6, 4, 6)
            };

            topPanel.Controls.Add(btnTapTest);



            // ---------- LEFT PANEL ----------
            var leftPanel = new SplitContainer
            {
                Dock = DockStyle.Left,
                Width = 650,
                Orientation = Orientation.Horizontal
            };

            // Make telemetry area "fixed/min height" so it never gets covered
            leftPanel.Panel1MinSize = 180;   // adjust if you want taller: 280–340
            leftPanel.Panel2MinSize = 320;

            // Set splitter AFTER DPI/layout is applied (prevents clipping/overlap)
            Shown += (s, e) =>
            {
                // lock the top telemetry panel to its minimum size
                leftPanel.SplitterDistance = leftPanel.Panel1MinSize;
            };

            // Telemetry grid
            var telemGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                Padding = new Padding(10)
            };

            telemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            telemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            telemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            telemGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // FIX: force row heights so big fonts don’t compress/overlap
            telemGrid.RowStyles.Clear();
            for (int r = 0; r < 4; r++)
                telemGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // adjust 55–70 if desired

            AddField(telemGrid, 0, 0, "MPH", txtMph);
            AddField(telemGrid, 0, 1, "IPS", txtIps);
            AddField(telemGrid, 0, 2, "FILT", txtFilt);
            AddField(telemGrid, 0, 3, "PRED", txtPred);
            AddField(telemGrid, 1, 0, "ERR", txtErr);
            AddField(telemGrid, 1, 1, "CMD", txtCmd);
            AddField(telemGrid, 1, 2, "ARM", txtArmed);
            AddField(telemGrid, 1, 3, "FAULT", txtFault);

            leftPanel.Panel1.Controls.Add(telemGrid);

            // Params grid + buttons
            gridParams.Dock = DockStyle.Fill;
            gridParams.AllowUserToAddRows = false;
            gridParams.AllowUserToDeleteRows = false;
            gridParams.RowHeadersVisible = false;
            gridParams.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridParams.MultiSelect = false;
            gridParams.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            gridParams.RowTemplate.Height = 48;                 // row height
            gridParams.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            gridParams.Font = new Font("Segoe UI", 17, FontStyle.Bold);

            if (gridParams.Columns.Count == 0)
            {
                gridParams.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", ReadOnly = true });
                gridParams.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value" });

                foreach (var k in ParamKeys)
                    gridParams.Rows.Add(k, "");
            }

            gridParams.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            gridParams.ColumnHeadersHeight = 56;   // try 52–64
            gridParams.ColumnHeadersDefaultCellStyle.Font =
                new Font("Segoe UI", 18, FontStyle.Bold);
            btnApplySelected.Text = "Apply Selected";
            btnApplyAll.Text = "Apply All";
            btnApplySelected.AutoSize = true;
            btnApplyAll.AutoSize = true;

            var paramButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 6, 8, 6),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            paramButtons.Controls.Add(btnApplySelected);
            paramButtons.Controls.Add(btnApplyAll);

            var paramsPanel = new Panel { Dock = DockStyle.Fill };
            paramsPanel.Controls.Add(gridParams);     // add fill first
            paramsPanel.Controls.Add(paramButtons);   // add bottom last

            leftPanel.Panel2.Controls.Add(paramsPanel);

            // ---------- RIGHT PANEL (chart + log) ----------
            // Make sure chart is initialized (if you already do this elsewhere, keep your existing init)
            if (chart.ChartAreas.Count == 0)
            {
                chart.Dock = DockStyle.Fill;
                chart.ChartAreas.Add(new ChartArea("main"));
                chart.Series.Add(new Series("filt") { ChartType = SeriesChartType.Line });
                chart.Series.Add(new Series("pred") { ChartType = SeriesChartType.Line });
                chart.Series.Add(new Series("err") { ChartType = SeriesChartType.Line });
            }

            chart.Series["filt"].BorderWidth = 4;  // try 3–5
            chart.Series["pred"].BorderWidth = 4;
            chart.Series["err"].BorderWidth = 3;

            chart.Series["filt"].MarkerStyle = MarkerStyle.None;
            chart.Series["pred"].MarkerStyle = MarkerStyle.None;
            chart.Series["err"].MarkerStyle = MarkerStyle.None;

            rightPanel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8
            };

            // ---------- CENTER PANEL (Chart + Help) ----------
            centerPanel = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6
            };

            centerPanel.Panel1MinSize = 200; // chart min
            centerPanel.Panel2MinSize = 120; // help min

            // Chart (top)
            chart.Dock = DockStyle.Fill;
            centerPanel.Panel1.Controls.Add(chart);

            // Help / How-To (bottom)
            txtHelp = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Segoe UI", 11),
                BackColor = SystemColors.Control,
                BorderStyle = BorderStyle.FixedSingle,
                DetectUrls = false,
                WordWrap = true
            };


            centerPanel.Panel2.Controls.Add(txtHelp);
            LoadHelpText();

            // Add center panel to RIGHT PANEL (instead of chart)
            rightPanel.Panel1.Controls.Add(centerPanel);


            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new System.Drawing.Font("Consolas", 9);
            rightPanel.Panel2.Controls.Add(txtLog);

            // ---------- ADD TO FORM ----------
            Controls.Clear();
            Controls.Add(rightPanel);
            Controls.Add(leftPanel);
            Controls.Add(topPanel);

            ResumeLayout(true);
        }

        private void AddField(TableLayoutPanel tlp, int row, int colPair, string label, TextBox tb)
        {
            int col = colPair * 2;

            var lbl = new Label
            {
                Text = label + ":",
                AutoSize = true,
                Font = new System.Drawing.Font(
                     "Segoe UI",
                      9,  // ← label size (try 13–15)
                      System.Drawing.FontStyle.Bold
                ),
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0, 12, 8, 0) // vertical + horizontal spacing
            };

            tlp.Controls.Add(lbl, col, row);

            tb.ReadOnly = true;
            tb.Dock = DockStyle.Fill;
            tb.TextAlign = HorizontalAlignment.Center;

            // ---- change these numbers to taste ----
            tb.Font = new System.Drawing.Font("Segoe UI", 22, System.Drawing.FontStyle.Bold);

            tlp.Controls.Add(tb, col + 1, row);
        }


        // ================= EVENTS =================
        private void WireEvents()
        {
        
            btnRefreshPorts.Click += (_, __) => RefreshPorts();
            btnConnect.Click += (_, __) => ToggleConnect();

            btnArm.Click += (_, __) => SendLine("ARM 1");
            btnDisarm.Click += (_, __) => SendLine("ARM 0");
            btnEstop.Click += (_, __) => SendLine("ESTOP");

            btnStatus.Click += (_, __) => SendLine("STATUS");
            btnGet.Click += (_, __) => SendLine("GET");
            btnSave.Click += (_, __) => SendLine("SAVE");
            btnLoad.Click += (_, __) => SendLine("LOAD");
            btnDefaults.Click += (_, __) => SendLine("DEFAULTS");

            if (btnTapTest != null)
            {
                btnTapTest.Click += (_, __) =>
                {
                    if (_armed)
                    {
                        MessageBox.Show("Tap Test requires DISARMED", "Tap Test");
                        return;
                    }

                    Log("Starting Tap Test...");
                    SendLine("TAPTEST");
                };
            }

            chkTelemetry.CheckedChanged += (_, __) =>
            {
                SendLine(chkTelemetry.Checked ? "TELEM 1" : "TELEM 0");
            };

            btnApplySelected.Click += (_, __) => ApplySelectedParam();
            btnApplyAll.Click += (_, __) => ApplyAllParams();

            FormClosing += (_, __) =>
            {
                try { _keepAliveTimer.Stop(); } catch { }
                try { if (_sp.IsOpen) _sp.Close(); } catch { }
            };

            _sp.DataReceived += (_, __) =>
            {
                try
                {
                    var data = _sp.ReadExisting();
                    if (string.IsNullOrEmpty(data)) return;

                    lock (_rxAccum)
                    {
                        _rxAccum.Append(data);

                        while (true)
                        {
                            var idx = _rxAccum.ToString().IndexOf('\n');
                            if (idx < 0) break;

                            var line = _rxAccum.ToString(0, idx).Trim('\r', '\n');
                            _rxAccum.Remove(0, idx + 1);

                            if (!string.IsNullOrWhiteSpace(line))
                                HandleLine(line);
                        }
                    }
                }
                catch { /* ignore */ }
            };

            gridParams.CellClick += (s, e) =>
            {
                // only respond to clicks on the Value column
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex != 1) return;

                // stop DataGridView from starting its own edit mode
                gridParams.EndEdit();

                ShowKeypadForParamCell(e.RowIndex);
            };

            if (chkShowLog != null)
            {
                chkShowLog.CheckedChanged += (s, e) =>
                {
                    SetLogVisible(chkShowLog.Checked, true);
                };
            }

        }

        // ================= SERIAL =================
        private void RefreshPorts()
        {
            var ports = System.IO.Ports.SerialPort.GetPortNames()
                                     .OrderBy(p => p)
                                     .ToArray();

            cbPorts.Items.Clear();
            cbPorts.Items.AddRange(ports);
            if (ports.Length > 0) cbPorts.SelectedIndex = 0;
        }

        private void ToggleConnect()
        {
            if (_sp.IsOpen)
            {
                Disconnect();
                return;
            }

            if (cbPorts.SelectedItem == null)
            {
                MessageBox.Show("Select a COM port first.");
                return;
            }

            try
            {
                _sp.PortName = cbPorts.SelectedItem.ToString()!;
                _sp.Open();

                lblConn.Text = $"Connected: {_sp.PortName}";
                btnConnect.Text = "Disconnect";

                // Start streaming + keepalive
                SendLine("TELEM 1");
                _keepAliveTimer.Start();

                // Auto-load status + params
                RequestStatusAndParams();

                Log($"[connect] {_sp.PortName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connect failed: " + ex.Message);
                Disconnect();
            }
        }


        private void Disconnect()
        {
            try { _keepAliveTimer.Stop(); } catch { }
            try { if (_sp.IsOpen) _sp.Close(); } catch { }

            lblConn.Text = "Disconnected";
            btnConnect.Text = "Connect";
            Log("[disconnect]");
        }

        private void SendLine(string cmd)
        {
            if (!_sp.IsOpen) return;

            try
            {
                _sp.Write(cmd + "\n");
            }
            catch (Exception ex)
            {
                Log("[send error] " + ex.Message);
            }
        }

        // ================= PARSING =================
        private void HandleLine(string line)
        {
            // Keep log readable: show non-telemetry always, telemetry optionally
            bool isTelem = line.StartsWith("T,", StringComparison.OrdinalIgnoreCase);
            bool isStatus = line.StartsWith("STATUS,", StringComparison.OrdinalIgnoreCase);

            if (!isTelem)
                BeginInvoke(new Action(() => Log(line)));

            if (isTelem && chkTelemetry.Checked)
                ParseTelemetryT(line);
            else if (isStatus)
                ParseStatus(line);
            else if (line.StartsWith("PARAMS BEGIN", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("PARAMS END", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains('='))
            {

                // PARAM line key=value (from GET)
                ParseKeyValueLine(line);
            }
            else if (line.StartsWith("TAPRESULT,", StringComparison.OrdinalIgnoreCase))
{
    var parts = line.Split(',');
    if (parts.Length == 2 &&
        float.TryParse(parts[1], CultureInfo.InvariantCulture, out float delay))
    {
        BeginInvoke(new Action(() =>
        {
            MessageBox.Show(
                $"Measured hydraulic delay:\n\n{delay:0.000} sec",
                "Tap Test Result");

            // Optional: suggest value
            Log($"Tap Test delay = {delay:0.000} sec");
        }));
    }
}

        }

        private static float ParseFloat(string s)
        {
            if (string.Equals(s, "nan", StringComparison.OrdinalIgnoreCase)) return float.NaN;
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        private void ParseTelemetryT(string line)
        {
            // T,mph,ips,filt,pred,err,cmd,armed,fault
            // Older versions may omit armed/fault; handle both
            try
            {
                var parts = line.Split(',');
                if (parts.Length < 7) return;

                var t = new Telemetry
                {
                    Mph = ParseFloat(parts[1]),
                    Ips = ParseFloat(parts[2]),
                    Filt = ParseFloat(parts[3]),
                    Pred = ParseFloat(parts[4]),
                    Err = ParseFloat(parts[5]),
                    Cmd = int.Parse(parts[6], CultureInfo.InvariantCulture),

                    Armed = (parts.Length >= 8) && (parts[7].Trim() == "1"),
                    Fault = (parts.Length >= 9) ? int.Parse(parts[8], CultureInfo.InvariantCulture) : 0
                };

                // Keep a simple bool too (used by keypad send gating)
                _armed = t.Armed;

                lock (_telemetryLock)
                    _lastT = t;

                BeginInvoke(new Action(() => PushChart(t)));
            }
            catch { /* ignore */ }
        }


        private void ParseStatus(string line)
        {
            // STATUS,armed,fault,mph,filt,pred,err,cmd
            try
            {
                var parts = line.Split(',');
                if (parts.Length < 8) return;

                var st = new StatusLine
                {
                    Armed = parts[1].Trim() == "1",
                    Fault = int.Parse(parts[2], CultureInfo.InvariantCulture),
                    Mph = ParseFloat(parts[3]),
                    Filt = ParseFloat(parts[4]),
                    Pred = ParseFloat(parts[5]),
                    Err = ParseFloat(parts[6]),
                    Cmd = int.Parse(parts[7], CultureInfo.InvariantCulture)
                };

                _armed = st.Armed;

                lock (_telemetryLock)
                {
                    _lastStatus = st;

                    // ensure the UI fault stays updated even if T-line fault is absent/zero
                    _lastT.Fault = st.Fault;

                    // optionally mirror Armed too (keeps UI consistent)
                    _lastT.Armed = st.Armed;
                }
            }
            catch { /* ignore */ }
        }


        private void ParseKeyValueLine(string line)
        {
            // key=value (from PARAMS dump)
            var idx = line.IndexOf('=');
            if (idx <= 0) return;

            var key = line.Substring(0, idx).Trim();
            var val = line[(idx + 1)..].Trim();

            BeginInvoke(new Action(() =>
            {
                foreach (DataGridViewRow row in gridParams.Rows)
                {
                    if (row.Cells[0].Value?.ToString() == key)
                    {
                        row.Cells[1].Value = val;
                        break;
                    }
                }
            }));
        }

        // ================= UI refresh =================
        private void RefreshTelemetryUi()
        {
            Telemetry t;
            StatusLine st;
            lock (_telemetryLock)
            {
                t = _lastT;
                st = _lastStatus;
            }

            // Display (same as you have)
            txtMph.Text = t.Mph.ToString("0.00", CultureInfo.InvariantCulture);
            txtIps.Text = t.Ips.ToString("0.0", CultureInfo.InvariantCulture);
            txtFilt.Text = FloatToText(t.Filt);
            txtPred.Text = FloatToText(t.Pred);
            txtErr.Text = FloatToText(t.Err);
            txtCmd.Text = t.Cmd.ToString(CultureInfo.InvariantCulture);
            txtArmed.Text = t.Armed ? "ARMED" : "DISARMED";
            txtFault.Text = t.Fault.ToString(CultureInfo.InvariantCulture);

            // Keep our local armed bool in sync
            _armed = t.Armed;

            // Detect transition: ARMED -> DISARMED
            if (_prevArmed && !_armed)
            {
                BeginInvoke(new Action(() => ApplyPendingIfDisarmed()));
            }
            _prevArmed = _armed;
        }

        private static string FloatToText(float v)
            => float.IsNaN(v) ? "nan" : v.ToString("0.00", CultureInfo.InvariantCulture);

        private void PushChart(Telemetry t)
        {
            var area = chart.ChartAreas["main"];
            area.AxisX.IsMarginVisible = false;   // removes extra empty margin at ends

            // Add point helper
            void AddPoint(string series, double y)
            {
                var s = chart.Series[series];
                s.Points.AddXY(_sampleIndex, y);
                while (s.Points.Count > MaxPoints)
                    s.Points.RemoveAt(0);
            }

            // Add points at current X
            if (!float.IsNaN(t.Filt)) AddPoint("filt", t.Filt);
            if (!float.IsNaN(t.Pred)) AddPoint("pred", t.Pred);
            if (!float.IsNaN(t.Err)) AddPoint("err", t.Err);

            // Advance X index after adding
            _sampleIndex++;

            // Force a sliding X window so it doesn’t compress on the left
            // Window is MaxPoints samples wide
            long minX = Math.Max(0, _sampleIndex - MaxPoints);
            long maxX = Math.Max(MaxPoints, _sampleIndex);

            area.AxisX.Minimum = minX;
            area.AxisX.Maximum = maxX;

            // Optional: reduce redraw jitter
            // (keeps labels stable while still scrolling)
            area.AxisX.Interval = 0; // auto
        }


        private void ApplySelectedParam()
        {
            if (!_sp.IsOpen) return;
            if (gridParams.SelectedRows.Count == 0) return;

            var row = gridParams.SelectedRows[0];
            var key = row.Cells[0].Value?.ToString();
            var val = row.Cells[1].Value?.ToString();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val))
                return;

            SendLine($"SET {key} {val}");
        }

        private void ApplyAllParams()
        {
            if (!_sp.IsOpen) return;

            foreach (DataGridViewRow row in gridParams.Rows)
            {
                var key = row.Cells[0].Value?.ToString();
                var val = row.Cells[1].Value?.ToString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val))
                    continue;

                SendLine($"SET {key} {val}");
            }
        }

        private void Log(string s)
        {
            // keep log from getting huge
            if (txtLog.Lines.Length > 500)
                txtLog.Clear();

            txtLog.AppendText(s + Environment.NewLine);
        }

        // ================= Data types =================
        private struct Telemetry
        {
            public float Mph;
            public float Ips;
            public float Filt;
            public float Pred;
            public float Err;
            public int Cmd;
            public bool Armed;
            public int Fault;
        }

        private struct StatusLine
        {
            public bool Armed;
            public int Fault;
            public float Mph;
            public float Filt;
            public float Pred;
            public float Err;
            public int Cmd;
        }

        private void SetLogVisible(bool visible, bool rememberSplit)
        {
            if (rightPanel == null) return;

            if (!visible)
            {
                // Save the current split so we can restore later
                if (rememberSplit && !rightPanel.Panel2Collapsed)
                    _savedSplitterDistance = rightPanel.SplitterDistance;

                rightPanel.Panel2Collapsed = true;
            }
            else
            {
                rightPanel.Panel2Collapsed = false;

                // SplitterDistance = width of Panel1 (chart area).
                int desired = rightPanel.Width - _logWidth - rightPanel.SplitterWidth;

                int min = rightPanel.Panel1MinSize;
                int max = rightPanel.Width - rightPanel.Panel2MinSize - rightPanel.SplitterWidth;

                if (desired < min) desired = min;
                if (desired > max) desired = max;

                if (_savedSplitterDistance > 0 && _savedSplitterDistance >= min && _savedSplitterDistance <= max)
                    rightPanel.SplitterDistance = _savedSplitterDistance;
                else if (max > min)
                    rightPanel.SplitterDistance = desired;
            }
        }

    }
}

