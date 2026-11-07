using System.ComponentModel;
using System.Globalization;

namespace Editrainer;

/// <summary>Editable move selector. Searching never changes the draft until a choice is confirmed.</summary>
sealed class MovePicker : ComboBox
{
    const int WidestRequest = 200;
    List<Choice> choices = [];
    Choice? committed;
    bool updating;
    public event EventHandler? MoveCommitted;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Choice? SelectedChoice => committed;

    public MovePicker()
    {
        Font = Theme.Font(10); DropDownStyle = ComboBoxStyle.DropDown; AutoSize = true;
        AutoCompleteMode = AutoCompleteMode.None; IntegralHeight = false;
        DropDownHeight = 270; Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(4, 7, 4, 7); Width = 300;
        Theme.Dress(this);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var preferred = base.GetPreferredSize(proposedSize);
        // The native edit field can grow taller than ComboBox's cached preferred
        // height after a font/DPI change. Let the containing row reserve that space.
        // Nor may its longest name widen the row: names change with the game and the language.
        return new Size(Math.Min(preferred.Width, WidestRequest), Math.Max(preferred.Height, Height));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Parent?.PerformLayout(this, nameof(PreferredSize));
    }

    public void SetChoices(IEnumerable<Choice> available, int selected)
    {
        choices = available.ToList();
        SelectMove(selected, false);
    }

    public void SelectMove(int id, bool notify = true)
    {
        var choice = choices.FirstOrDefault(x => x.Id == id);
        if (choice == null) return;
        bool changed = committed?.Id != choice.Id;
        committed = choice;
        updating = true; BeginUpdate();
        Items.Clear(); Items.AddRange(choices.Cast<object>().ToArray()); SelectedItem = choice;
        Text = choice.Label;
        EndUpdate(); updating = false; DroppedDown = false;
        SelectionStart = Text.Length; SelectionLength = 0;
        if (notify && changed) MoveCommitted?.Invoke(this, EventArgs.Empty);
    }

    static bool Matches(string name, string query) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, query,
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    internal void Search(string query)
    {
        if (updating) return;
        updating = true;
        var filtered = choices.Where(c => Matches(c.Label, query.Trim())).ToArray();
        BeginUpdate(); Items.Clear(); Items.AddRange(filtered.Cast<object>().ToArray()); SelectedIndex = -1;
        Text = query;
        EndUpdate(); updating = false;
        if (Focused) DroppedDown = filtered.Length > 0;
        SelectionStart = query.Length; SelectionLength = 0;
    }

    protected override void OnTextUpdate(EventArgs e)
    {
        base.OnTextUpdate(e);
        if (!updating) Search(Text);
    }

    protected override void OnSelectionChangeCommitted(EventArgs e)
    {
        var choice = SelectedItem as Choice;
        base.OnSelectionChangeCommitted(e);
        if (!updating && choice != null) SelectMove(choice.Id);
    }

    internal void ConfirmSearch()
    {
        var choice = SelectedItem as Choice ?? Items.Cast<Choice>().FirstOrDefault();
        if (choice != null) SelectMove(choice.Id);
        else CancelSearch();
    }

    internal void CancelSearch() { if (committed != null) SelectMove(committed.Id, false); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { ConfirmSearch(); e.Handled = e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.Escape) { CancelSearch(); e.Handled = e.SuppressKeyPress = true; return; }
        base.OnKeyDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter) { ConfirmSearch(); return true; }
        if (keyData == Keys.Escape) { CancelSearch(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnLeave(EventArgs e)
    {
        if (!updating)
        {
            var exact = choices.FirstOrDefault(c => CultureInfo.CurrentCulture.CompareInfo.Compare(c.Label, Text,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0);
            if (exact != null) SelectMove(exact.Id);
            else CancelSearch();
        }
        base.OnLeave(e);
    }
}
