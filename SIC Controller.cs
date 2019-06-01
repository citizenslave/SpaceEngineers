List<IMyTextSurface> cargoDisplays = new List<IMyTextSurface>();

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    cargoDisplays.Add(Me.GetSurface(0));
    cargoDisplays[0].ContentType = ContentType.TEXT_AND_IMAGE;
}

Boolean IsConnected(IMyTerminalBlock block) {
    return block.IsSameConstructAs(Me);
}

public void Save() {

}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update100) != 0) {
        updateSource &= ~UpdateType.Update100;
        DisplayInventory();
    }
    if (updateSource != UpdateType.None) {
        // execute commands
    }
}

void DisplayInventory(Boolean connected = false) {
    Dictionary<MyItemType, MyFixedPoint> cargo = new Dictionary<MyItemType, MyFixedPoint>();
    List<IMyTerminalBlock> containers = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(containers, block => {
        if (!IsConnected(block) && !connected) return false;
        if (!block.HasInventory) return false;

        for (int i=0; i<block.InventoryCount; i++) {
            IMyInventory inventory = block.GetInventory(i);
            List<MyInventoryItem> items = new List<MyInventoryItem>();
            inventory.GetItems(items);
            foreach (MyInventoryItem item in items) {
                if (cargo.Keys.Contains(item.Type)) cargo[item.Type] += item.Amount;
                else cargo[item.Type] = item.Amount;
            }
        }

        return true;
    });

    Display(cargoDisplays, "", false);
    foreach (KeyValuePair<MyItemType, MyFixedPoint> entry in cargo.OrderBy(i => FormatItemType(i.Key))) {
        Display(cargoDisplays, $"{ FormatItemType(entry.Key) }: { FormatItemAmount(entry.Value) }\n");
    }
}

string FormatItemType(MyItemType type) {
    return type.ToString().Split('_')[1];
}

string FormatItemAmount(MyFixedPoint amount) {
    if ((float) amount == Math.Floor((float) amount)) return amount.ToString();
    return $"{ ((float) amount).ToString("n2") } kg";
}

void Display(List<IMyTextSurface> panels, string text, Boolean append = true) {
    foreach (IMyTextSurface panel in panels) panel.WriteText(text, append);
}
