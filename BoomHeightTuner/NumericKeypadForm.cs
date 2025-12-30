using System;
using System.Drawing;
using System.Windows.Forms;

namespace BoomHeightTuner
{
    public partial class NumericKeypadForm : Form
    {
        private readonly TextBox _txt = new TextBox();

        public string ValueText => _txt.Text;

        public NumericKeypadForm(string title, string initial = "")
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Width = 540;
            Height = 700;

            AutoScaleMode = AutoScaleMode.Dpi;

            _txt.Dock = DockStyle.Top;
            _txt.Font = new Font("Segoe UI", 22, FontStyle.Bold);
            _txt.TextAlign = HorizontalAlignment.Center;
            _txt.Height = 60;
            _txt.Text = initial ?? "";

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(14)   // was 10
            };

            for (int c = 0; c < 3; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int r = 0; r < 5; r++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

            Controls.Add(grid);
            Controls.Add(_txt);

            // Row 0
            AddBtn(grid, "7", 0, 0); AddBtn(grid, "8", 1, 0); AddBtn(grid, "9", 2, 0);
            // Row 1
            AddBtn(grid, "4", 0, 1); AddBtn(grid, "5", 1, 1); AddBtn(grid, "6", 2, 1);
            // Row 2
            AddBtn(grid, "1", 0, 2); AddBtn(grid, "2", 1, 2); AddBtn(grid, "3", 2, 2);
            // Row 3
            AddBtn(grid, "-", 0, 3); AddBtn(grid, "0", 1, 3); AddBtn(grid, ".", 2, 3);
            // Row 4
            AddBtn(grid, "CLR", 0, 4);
            AddBtn(grid, "BKSP", 1, 4);
            AddBtn(grid, "ENTER", 2, 4);

            // Allow Esc to cancel
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
        }

        private void AddBtn(TableLayoutPanel grid, string label, int col, int row)
        {
            var b = new Button
            {
                Text = label,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold), // BIG
                Margin = new Padding(8)
            };

            // Make special keys stand out
            if (label == "ENTER")
            {
                b.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
                b.BackColor = Color.LightGreen;
            }
            else if (label == "CLR")
            {
                b.BackColor = Color.Khaki;
            }
            else if (label == "BKSP")
            {
                b.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            }

            b.Click += (_, __) =>
            {
                switch (label)
                {
                    case "CLR":
                        _txt.Text = "";
                        break;

                    case "BKSP":
                        if (_txt.Text.Length > 0)
                            _txt.Text = _txt.Text.Substring(0, _txt.Text.Length - 1);
                        break;

                    case "ENTER":
                        DialogResult = DialogResult.OK;
                        Close();
                        break;

                    default:
                        _txt.Text += label;
                        break;
                }
            };

            grid.Controls.Add(b, col, row);
        }

    }
}


