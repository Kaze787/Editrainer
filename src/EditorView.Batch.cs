using static Editrainer.Strings;

namespace Editrainer;

/// <summary>The same change on many Pokémon, from the Tools menu.</summary>
public sealed partial class EditorView
{
    void BuildBatchTools()
    {
        AddTool(T("Aplicar a varios…"), OpenBatch);
        AddTool(T("Deshacer el último lote"), UndoBatch);
    }

    void OpenBatch()
    {
        using var dialog = new BatchDialog(document!, box);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Bounds = ToolWindow.Place(Window.Bounds, dialog.Size, dialog.MinimumSize, Screen.FromControl(Window).WorkingArea);
        dialog.ShowDialog(Window);
        AfterBatch(dialog.Outcome);
    }

    void UndoBatch()
    {
        if (!document!.CanUndoBatch) { Notify(T("No hay ningún lote que deshacer.")); return; }
        try { AfterBatch(document.UndoBatch()); }
        catch (InvalidOperationException ex) { Warn(ex.Message); }
    }

    // The selection and the open box stay: whoever is on screen may be one of those that changed.
    void AfterBatch(string? outcome)
    {
        if (current != null) BindEntry(current, true);
        resources.RefreshDraft();
        RefreshList();
        if (outcome != null) Notify(outcome); else UpdateState();
    }

    // Through the same menu and window a person uses. Leaves the document as it found it.
    void CheckBatch(Action<bool, string> Check)
    {
        var open = document!;
        void Tool(string text) => toolsMenu.Items.Cast<ToolStripItem>().First(i => i.Text == text).PerformClick();
        Tool(T("Deshacer el último lote"));
        Check(status.Text == T("No hay ningún lote que deshacer.") && !open.HasChanges, "With no batch, undo says so");
        using (var dialog = OffScreen(new BatchDialog(open, box))) dialog.RunInteractionChecks(Check);
        Check(!open.HasChanges, "The batch window leaves nothing behind");

        int expected = open.Pokemon.Count(p => p.Edit.IVs.Any(v => v != BatchDialog.ChecksValue));
        Check(expected != 0, "The save has someone for the batch to change");
        var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) => {
            timer.Dispose();
            var dialog = Application.OpenForms.OfType<BatchDialog>().Single();
            dialog.PressApplyToEveryone(); dialog.Close();
        };
        timer.Start();
        Tool(T("Aplicar a varios…"));
        Check(open.DirtyCount == expected && open.Pokemon.All(p => p.Edit.IVs.All(v => v == BatchDialog.ChecksValue)), $"The menu opens the batch window, and each Pokémon that changes gets its own pending change ({open.DirtyCount} of {expected})");
        Check(save.Enabled && Text.Contains($"{open.DirtyCount} ") && status.Text.Contains($"{open.DirtyCount} "), "A batch enables Save, counts in the title and reports in the status line");
        Check(open.ValidateEdits() == null && open.BuildOutput().Length == open.OriginalBytes.Length, "A batch can be saved");
        Tool(T("Deshacer el último lote"));
        Check(!open.HasChanges && !save.Enabled && status.Text == T("Lote deshecho: {0} Pokémon.", expected), "The Tools menu undoes the batch and says so");
    }

    void CheckBatchLayout(Action<bool, string> Check, string sizeLabel)
    {
        using var dialog = OffScreen(new BatchDialog(document!, box));
        dialog.ShowAllForChecks();
        foreach (var (label, size) in new[] { ("normal", dialog.Size), ("smallest", dialog.MinimumSize) })
        {
            dialog.Size = size; dialog.PerformLayout(); Application.DoEvents();
            var clipped = Clipped(dialog);
            Check(clipped.Length == 0, $"Every text fits in the batch window ({label}, {sizeLabel}) {clipped}");
            Check(dialog.Width <= Window.MinimumSize.Width && dialog.Height <= Window.MinimumSize.Height, $"The batch window fits a small screen ({label}, {dialog.Size})");
            Check(!dialog.NeedsScrolling && dialog.ButtonsFit, $"Everything in the batch window is in reach ({label}, {sizeLabel})");
            Check(dialog.ResultsFit, $"The skipped list needs no sideways scrolling ({label}, {sizeLabel})");
            var cut = Descendants(dialog).FirstOrDefault(c => c.Visible && dialog.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)) is var area && (area.Right > dialog.ClientSize.Width || area.Bottom > dialog.ClientSize.Height));
            Check(cut == null, $"Nothing leaves the batch window ({label}, {sizeLabel}: {cut?.GetType().Name} {cut?.Text})");
        }
    }

    // Each sentence a batch can leave in the status lines, at its longest, has to fit there.
    void CheckBatchMessages(Action<bool, string> Check, string reportPath)
    {
        var skips = document!.Pokemon.Select(p => new BatchSkip(p, "", "")).ToList();
        foreach (var message in new[] { new BatchResult(9999, 9999, skips).Summary, T("Lote deshecho: {0} Pokémon.", 9999), T("No hay ningún lote que deshacer.") })
        {
            status.Text = message;
            var size = TextRenderer.MeasureText(status.Text, status.Font, new Size(status.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak);
            Check(size.Height <= status.ClientSize.Height + 1, $"The status line holds: {message}");
        }
        status.Text = "";
        using var dialog = OffScreen(new BatchDialog(document, box));
        foreach (var message in dialog.LongestMessages())
            Check(dialog.Fits(message), $"The batch window's status line holds: {message}");
        dialog.ShowAllForChecks(); dialog.Refresh();
        using var picture = Theme.Capture(dialog);
        picture.Save($"{reportPath}.batch.png", System.Drawing.Imaging.ImageFormat.Png);
    }
}
