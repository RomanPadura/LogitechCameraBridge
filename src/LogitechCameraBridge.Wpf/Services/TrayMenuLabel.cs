using System.Windows.Forms;

namespace LogitechCameraBridge.Wpf.Services;

// ToolStripDropDownMenu measures menu-item text separately from generic labels.
// Use its menu-item sizing while keeping informational rows unselectable.
internal sealed class TrayMenuLabel : ToolStripMenuItem
{
    public TrayMenuLabel(string text) : base(text)
    {
        AccessibleRole = AccessibleRole.StaticText;
    }

    public override bool CanSelect => false;
}
