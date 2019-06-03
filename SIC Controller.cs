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
        SortInventory();
        DisplayInventory();
    }
    if (updateSource != UpdateType.None) {
        // execute commands
    }
}

void SortInventory(Boolean connected = false) {
    List<IMyCargoContainer> storageContainers = new List<IMyCargoContainer>();
    GridTerminalSystem.GetBlocksOfType(storageContainers, IsConnected);

    List<IMyTerminalBlock> containers = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(containers, block => {
        if (!IsConnected(block) && !connected) return false;
        if (!block.HasInventory) return false;

        if (block.BlockDefinition.ToString().Contains("Reactor")) return false;
        if (block.BlockDefinition.ToString().Contains("OxygenGenerator")) return false;
        if (block.BlockDefinition.ToString().Contains("OxygenTank")) return false;

        if (block.BlockDefinition.ToString().Contains("Refinery")) {
            SortBlockInventory(block, block.GetInventory(1), storageContainers);
            return true;
        } else if (block.BlockDefinition.ToString().Contains("Assembler")) {
            if ((block as IMyAssembler).IsQueueEmpty) SortBlockInventory(block, block.GetInventory(0), storageContainers);
            SortBlockInventory(block, block.GetInventory(1), storageContainers);
            return true;
        } else if (block.BlockDefinition.ToString().Contains("Container") ||
                block.BlockDefinition.ToString().Contains("Connector")) {
            SortBlockInventory(block, block.GetInventory(0), storageContainers);
            return true;
        }

        else Echo($"{ block.BlockDefinition.ToString() } block inventories not sorted.");

        return true;
    });
}

void SortBlockInventory(IMyTerminalBlock block, IMyInventory inventory,
        List<IMyCargoContainer> storageContainers) {
    List<MyInventoryItem> items = new List<MyInventoryItem>();
    inventory.GetItems(items);
    foreach (MyInventoryItem item in items) {
        Boolean transferred = false;
        Boolean overflow = false;
        foreach (IMyCargoContainer container in storageContainers.OrderBy(i => GetPriority(i.CustomData, item.Type))) {
            IMyInventory destinationInventory = container.GetInventory(0);
            transferred = inventory.TransferItemTo(destinationInventory, item);
            overflow = GetPriority(container.CustomData, item.Type) > storageContainers.Count;
            if (transferred || inventory.Equals(destinationInventory)) break;
        }
        if ((overflow || !transferred) && !storageContainers.Contains(block))
            Echo($"Insufficient Storage for { FormatItemType(item.Type) } ({ item.Amount }) - { block.CustomName }");
    }
}

int GetPriority(string customData, MyItemType type) {
    string priorityType = type.ToString().Contains("Ice")?"iceStoragePriority":
            type.ToString().Contains("Ammo")?"ammoStoragePriority":
            $"{ FormatItemType(type).Split('/')[0].ToLower() }StoragePriority";
    string[] dataItems = customData.Split('\n');
    foreach (string dataItem in dataItems) if (dataItem.Contains(priorityType)) return int.Parse(dataItem.Split('=')[1]);
    return int.MaxValue;
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
    string typeString = type.ToString();
    return typeString.Substring(typeString.IndexOf("_")+1);
}

string FormatItemAmount(MyFixedPoint amount) {
    if ((float) amount == Math.Floor((float) amount)) return amount.ToString();
    return $"{ ((float) amount).ToString("n2") } kg";
}

void Display(List<IMyTextSurface> panels, string text, Boolean append = true) {
    foreach (IMyTextSurface panel in panels) panel.WriteText(text, append);
}
