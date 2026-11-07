using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Lets the user choose when several saves were found.</summary>
sealed class SavePicker : Form
{
    readonly ListBox list = new() { Dock = DockStyle.Fill, Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, ItemHeight = 26 };

    SavePicker(IEnumerable<FoundSave> saves)
    {
        Text = T("Elige una partida"); Font = Theme.Font(); BackColor = Theme.Background; ForeColor = Theme.Ink;
        Size = new Size(760, 440); MinimumSize = new Size(520, 300); StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
        list.Items.AddRange(saves.Cast<object>().ToArray()); list.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12) };
        layout.RowStyles.Add(new(SizeType.Absolute, 34)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 30)); layout.RowStyles.Add(new(SizeType.Absolute, 50));
        layout.Controls.Add(Theme.Label(T("Partidas encontradas, de la más reciente a la más antigua"), 11, true), 0, 0);
        layout.Controls.Add(list, 0, 1);
        var path = Theme.Label("", 8, color: Theme.Muted); path.AutoEllipsis = true; layout.Controls.Add(path, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var open = Theme.Button(T("Abrir partida"), true); var cancel = Theme.Button(T("Cancelar"));
        open.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(open); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout); AcceptButton = open; CancelButton = cancel;
        void ShowPath() => path.Text = (list.SelectedItem as FoundSave)?.Path ?? "";
        list.SelectedIndexChanged += (_, _) => ShowPath(); ShowPath();
        list.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }

    public static FoundSave? Pick(IWin32Window owner, IEnumerable<FoundSave> saves)
    {
        using var picker = new SavePicker(saves);
        return picker.ShowDialog(owner) == DialogResult.OK ? picker.list.SelectedItem as FoundSave : null;
    }
}
