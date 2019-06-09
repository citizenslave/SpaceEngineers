List<IMyTextSurface> cargoDisplays = new List<IMyTextSurface>();

List<IMyTextSurface> rsnDisplays = new List<IMyTextSurface>();
IMyRadioAntenna antenna;
string RSN_CHANNEL = "RSN";
Dictionary<long,string> rsnStatusData = new Dictionary<long,string>();
Dictionary<long,int> rsnAgeData = new Dictionary<long,int>();

List<IMyCargoContainer> storageContainers = new List<IMyCargoContainer>();

Dictionary<IMyRefinery,Dictionary<string,int>> RefineryPriorities = new Dictionary<IMyRefinery,Dictionary<string,int>>();
Dictionary<IMyRefinery,List<IMyTextSurface>> RefineryDisplays = new Dictionary<IMyRefinery,List<IMyTextSurface>>();

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    cargoDisplays.Add(Me.GetSurface(0));
    cargoDisplays[0].ContentType = ContentType.TEXT_AND_IMAGE;
    
    List<IMyRadioAntenna> tAntenna = new List<IMyRadioAntenna>();
    GridTerminalSystem.GetBlocksOfType(tAntenna, IsConnected);
    antenna = tAntenna[0];
    antenna.AttachedProgrammableBlock = Me.EntityId;
    IGC.RegisterBroadcastListener(RSN_CHANNEL).SetMessageCallback(RSN_CHANNEL);
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

        FindRSNDisplays();
        UpdateRSNDisplays();

        PrioritizeRefining();
    }
    if (updateSource != UpdateType.None) {
        if (argument.Equals(RSN_CHANNEL)) FetchPacket(RSN_CHANNEL);
        else if (argument.Split()[0].ToUpper().Contains("REFINERY")) ProcessRefineryCommands(argument);
    }
}

void ProcessRefineryCommands(string command) {
    MyCommandLine refineryCommand = new MyCommandLine();
    if (refineryCommand.TryParse(command)) {
        IMyRefinery refinery = GridTerminalSystem.GetBlockWithName(refineryCommand.Argument(2)) as IMyRefinery;
        if (refinery != null) {
            if (refineryCommand.Argument(1).ToUpper().Equals("CLEAR")) {
                SortBlockInventory(refinery, refinery.GetInventory(0), storageContainers);
                Echo($"{ refinery.CustomName } input inventory cleared...");
            } else Echo($"Unknown Refinery Command:\n\"{ refineryCommand.Argument(1) }\"");
        } else Echo($"Cannot find refinery with -name \"{ refineryCommand.Argument(2) }\"");
    }
}
void PrioritizeRefining() {
    List<IMyRefinery> refineries = new List<IMyRefinery>();
    GridTerminalSystem.GetBlocksOfType(refineries, block => {
        if (!IsConnected(block)) return false;
        IMyRefinery refinery = (IMyRefinery) block;
        MyIni refineryConfig = new MyIni();
        MyIniParseResult result = new MyIniParseResult();
        if (refineryConfig.TryParse(block.CustomData, out result)) {
            if (refineryConfig.ContainsSection("Priority")) {
                RefineryPriorities[refinery] = new Dictionary<string,int>();
                RefineryDisplays[refinery] = new List<IMyTextSurface>();
                GridTerminalSystem.GetBlocksOfType(RefineryDisplays[refinery], surface => {
                    IMyTextPanel panel = (IMyTextPanel) surface;
                    if (!panel.CustomData.Contains($"[LCD]\nRefineryDisplay=\"{ refinery.CustomName }\"")) return false;
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.FontColor = Color.DarkSlateGray;
                    return true;
                });
                Display(RefineryDisplays[refinery], $"{ refinery.CustomName }\nPRIORITIES\n", false);
                List<MyIniKey> priorityKeys = new List<MyIniKey>();
                refineryConfig.GetKeys("Priority", priorityKeys);
                foreach(MyIniKey priorityKey in priorityKeys.OrderBy(i => refineryConfig.Get(i).ToInt32(int.MaxValue))) {
                    string oreKey = priorityKey.ToString().Split('/')[1];
                    RefineryPriorities[refinery][oreKey] = refineryConfig.Get("Priority", oreKey).ToInt32(int.MaxValue);
                    Display(RefineryDisplays[refinery], $"{ oreKey }: { SortOre(oreKey, refinery) }\n");
                }
            }
        }
        return true;
    });
}

string SortOre(string oreKey, IMyRefinery refinery) {
    MyFixedPoint amount = 0;
    IMyInventory refineryInventory = refinery.GetInventory(0);
    MyItemType oreToFind = new MyItemType("MyObjectBuilder_Ore", oreKey);
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, block => {
        if (!block.HasInventory) return false;
        IMyInventory inventory = block.GetInventory(0);
        int itemSlot = FindItemSlot(inventory, oreToFind);
        if (itemSlot != -1) amount += ((MyInventoryItem) inventory.GetItemAt(itemSlot)).Amount;
        else return false;
        if (!IsConnected(block)) {
            int toSlot = FindItemSlot(refineryInventory, oreToFind);
            refineryInventory.TransferItemFrom(inventory, itemSlot, toSlot>=0?toSlot:refineryInventory.ItemCount, true);
        }
        while (itemSlot != -1) {
            itemSlot = FindItemSlot(inventory, oreToFind, itemSlot);
            if (itemSlot != -1) amount += ((MyInventoryItem) inventory.GetItemAt(itemSlot)).Amount;
        }
        return !RefineryPriorities.Keys.Contains(block) && !IsConnected(block);
    });
    foreach (IMyTerminalBlock block in blocks) {
        IMyInventory foundInventoryOnConstruct = block.GetInventory(0);
        int toSlot = FindItemSlot(refineryInventory, oreToFind);
        refineryInventory.TransferItemFrom(foundInventoryOnConstruct,
                FindItemSlot(foundInventoryOnConstruct, oreToFind), toSlot>=0?toSlot:refineryInventory.ItemCount, true);
    }
    return $"{ ((float) amount).ToString("n2") }kg";
}

int FindItemSlot(IMyInventory inventory, MyItemType itemType, int startIndex = -1) {
    for (int i=startIndex+1; i<inventory.ItemCount; i++) {
        if (((MyInventoryItem) inventory.GetItemAt(i)).Type.Equals(itemType)) return i;
    }
    return -1;
}

void FetchPacket(string channel) {
    IMyBroadcastListener listener = IGC.RegisterBroadcastListener(channel);
    if (!listener.HasPendingMessage) return;
    MyIGCMessage packet = listener.AcceptMessage();
    if (channel.Equals(RSN_CHANNEL)) {
        rsnStatusData[packet.Source] = (string) packet.Data;
        rsnAgeData[packet.Source] = 0;
    }
}

void FindRSNDisplays() {
    List<IMyTextPanel> tPanels = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(tPanels, block => {
        if (!IsConnected(block)) return false;
        if (block.CustomData.IndexOf("DisplayType=RSN") == -1) return false;
        IMyTextSurface surface = block;
        if (!rsnDisplays.Contains(surface)) {
            surface.ContentType = ContentType.TEXT_AND_IMAGE;
            surface.WriteText("", false);
            rsnDisplays.Add(surface);
        }
        return true;
    });
}

void UpdateRSNDisplays() {
    Display(rsnDisplays, "", false);
    foreach (KeyValuePair<long,string> item in rsnStatusData) {
        string age = (rsnAgeData[item.Key] < 2)?"":$"({ rsnAgeData[item.Key] }) ";
        Display(rsnDisplays, $"{ age }{ item.Value }\n\n");
        rsnAgeData[item.Key]++;
    }
}

void SortInventory(Boolean connected = false) {
    storageContainers.Clear();
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
