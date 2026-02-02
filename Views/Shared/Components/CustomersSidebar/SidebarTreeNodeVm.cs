using PreLedgerORC.ViewComponents;

namespace PreLedgerORC.Views.Shared.Components.CustomersSidebar;

public class SidebarTreeNodeVm
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public CustomersSidebarViewComponent.TreeNodeVm Node { get; set; } = default!;
}
