using NicPad.Core;

namespace NicPad.UI;

internal enum FindAction
{
    FindNext,
    FindPrevious,
    Replace,
    ReplaceAll,
}

/// <summary>Modeless Find / Replace window. The owner does the work through <see cref="ActionRequested"/>.</summary>
internal sealed class FindReplaceDialog : Form
{
    private readonly TextBox findBox = new() { Width = 260, AccessibleName = "Find what" };
    private readonly TextBox replaceBox = new() { Width = 260, AccessibleName = "Replace with" };
    private readonly Label replaceLabel = new() { Text = "Re&place with:", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox matchCase = new() { Text = "Match &case", AutoSize = true };
    private readonly CheckBox wholeWord = new() { Text = "&Whole word", AutoSize = true };
    private readonly CheckBox wrap = new() { Text = "Wra&p around", AutoSize = true, Checked = true };
    private readonly Button replaceButton = new() { Text = "&Replace", AutoSize = true };
    private readonly Button replaceAllButton = new() { Text = "Replace &All", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public FindReplaceDialog()
    {
        Text = "Find";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(8);

        var findNext = new Button { Text = "Find &Next", AutoSize = true };
        var findPrev = new Button { Text = "Find Pre&vious", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        findNext.Click += (_, _) => Raise(FindAction.FindNext);
        findPrev.Click += (_, _) => Raise(FindAction.FindPrevious);
        replaceButton.Click += (_, _) => Raise(FindAction.Replace);
        replaceAllButton.Click += (_, _) => Raise(FindAction.ReplaceAll);
        close.Click += (_, _) => Hide();
        AcceptButton = findNext;
        CancelButton = close;

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "Fi&nd what:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(findBox, 1, 0);
        grid.Controls.Add(findNext, 2, 0);
        grid.Controls.Add(replaceLabel, 0, 1);
        grid.Controls.Add(replaceBox, 1, 1);
        grid.Controls.Add(findPrev, 2, 1);

        var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 6, 0, 0) };
        options.Controls.AddRange([matchCase, wholeWord, wrap]);
        grid.Controls.Add(options, 1, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        buttons.Controls.AddRange([replaceButton, replaceAllButton, close]);
        grid.Controls.Add(buttons, 2, 2);
        grid.Controls.Add(status, 1, 3);
        grid.SetColumnSpan(status, 2);
        Controls.Add(grid);

        foreach (var button in new[] { findNext, findPrev, replaceButton, replaceAllButton, close })
        {
            button.MinimumSize = new Size(110, 0);
        }
    }

    public event EventHandler<FindAction>? ActionRequested;

    public FindOptions Options => new(findBox.Text, matchCase.Checked, wholeWord.Checked, wrap.Checked);

    public string Replacement => replaceBox.Text;

    public void ShowFor(IWin32Window owner, bool replace, string? seed)
    {
        Text = replace ? "Replace" : "Find";
        replaceLabel.Visible = replaceBox.Visible = replaceButton.Visible = replaceAllButton.Visible = replace;
        if (!string.IsNullOrEmpty(seed) && !seed.Contains('\n', StringComparison.Ordinal))
        {
            findBox.Text = seed;
        }

        status.Text = "";
        if (!Visible)
        {
            Show(owner);
        }

        Activate();
        findBox.SelectAll();
        findBox.Focus();
    }

    public void SetStatus(string text) => status.Text = text;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private void Raise(FindAction action)
    {
        if (string.IsNullOrEmpty(findBox.Text))
        {
            status.Text = "Type something to find.";
            findBox.Focus();
            return;
        }

        ActionRequested?.Invoke(this, action);
    }
}

/// <summary>Asks for a 1-based line number.</summary>
internal sealed class GoToLineDialog : Form
{
    private readonly NumericUpDown number = new() { Minimum = 1, Width = 120, AccessibleName = "Line number" };

    public GoToLineDialog(int current, int max)
    {
        Text = "Go To Line";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(10);

        number.Maximum = Math.Max(1, max);
        number.Value = Math.Clamp(current, 1, Math.Max(1, max));

        var ok = new Button { Text = "&Go", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = $"&Line number (1 - {max}):", AutoSize = true });
        layout.Controls.Add(number);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Shown += (_, _) =>
        {
            number.Select(0, number.Text.Length);
            number.Focus();
        };
    }

    public int LineNumber => (int)number.Value;
}
