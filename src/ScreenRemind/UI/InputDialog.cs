namespace ScreenRemind.UI;

/// <summary>通用单行输入框（用于标注文字、自定义顺延分钟、设置密码）。</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _box;

    public string Value => _box.Text.Trim();

    private InputDialog(string title, string prompt, string defaultValue, bool passwordMode)
    {
        Text = title;
        Theme.ApplyForm(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 148);

        var lbl = new Label
        {
            Text = prompt,
            AutoSize = false,
            Bounds = new Rectangle(20, 18, 340, 22),
            ForeColor = Theme.Text
        };

        _box = new TextBox
        {
            Bounds = new Rectangle(20, 46, 340, 26),
            Text = defaultValue,
            UseSystemPasswordChar = passwordMode,
            Font = new Font(Theme.FontFamily, 10F)
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Confirm();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var ok = Theme.PrimaryButton("确定");
        ok.Bounds = new Rectangle(200, 92, 76, 34);
        ok.Click += (_, _) => Confirm();

        var cancel = Theme.GhostButton("取消");
        cancel.Bounds = new Rectangle(284, 92, 76, 34);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(new Control[] { lbl, _box, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Confirm()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    public static string? Prompt(IWin32Window? owner, string title, string prompt, string defaultValue = "", bool password = false)
    {
        using var dlg = new InputDialog(title, prompt, defaultValue, password);
        var result = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
        return result == DialogResult.OK ? dlg.Value : null;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.Focus();
        _box.SelectAll();
    }
}
