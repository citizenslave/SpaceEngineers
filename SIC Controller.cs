List<IMyTextSurface> cargoDisplays = new List<IMyTextSurface>();

List<IMyTextSurface> rsnDisplays = new List<IMyTextSurface>();
IMyRadioAntenna antenna;
string RSN_CHANNEL = "RSN";
Dictionary<long,string> rsnStatusData = new Dictionary<long,string>();
Dictionary<long,int> rsnAgeData = new Dictionary<long,int>();

List<IMyCargoContainer> storageContainers = new List<IMyCargoContainer>();

Dictionary<IMyRefinery,Dictionary<string,int>> RefineryPriorities = new Dictionary<IMyRefinery,Dictionary<string,int>>();
Dictionary<IMyRefinery,List<IMyTextSurface>> RefineryDisplays = new Dictionary<IMyRefinery,List<IMyTextSurface>>();
string[] ORES = { "Stone", "Iron", "Nickel", "Silicon", "Cobalt", "Magnesium", "Gold", "Silver", "Uranium", "Platinum", "Scrap" };

Dictionary<string,int> MenuIndicies = new Dictionary<string,int>();
Dictionary<string,int> MenuSizes = new Dictionary<string,int>();

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    cargoDisplays.Add(Me.GetSurface(0));
    cargoDisplays[0].ContentType = ContentType.TEXT_AND_IMAGE;
    
    List<IMyRadioAntenna> tAntenna = new List<IMyRadioAntenna>();
    GridTerminalSystem.GetBlocksOfType(tAntenna, IsConnected);
    antenna = tAntenna[0];
    antenna.AttachedProgrammableBlock = Me.EntityId;
    IGC.RegisterBroadcastListener(RSN_CHANNEL).SetMessageCallback(RSN_CHANNEL);

    MenuIndicies["SIC Refinery 1 - Priority Menu"] = 0;
    MenuIndicies["SIC Refinery 2 - Priority Menu"] = 0;

    if (Storage.Length != 0) {
        MyIni SaveData = new MyIni();
        MyIniParseResult result = new MyIniParseResult();
        if (SaveData.TryParse(Storage, out result)) {
            if (SaveData.ContainsSection("Menus")) {
                List<MyIniKey> menuKeys = new List<MyIniKey>();
                SaveData.GetKeys("Menus", menuKeys);
                foreach (MyIniKey menuKey in menuKeys)
                    MenuIndicies[menuKey.ToString().Split('/')[1]] = SaveData.Get(menuKey).ToInt32(0);
            }
        }
    }
}

Boolean IsConnected(IMyTerminalBlock block) {
    return block.IsSameConstructAs(Me);
}

public void Save() {
    MyIni SaveData = new MyIni();
    foreach (KeyValuePair<string,int> menuIndex in MenuIndicies) SaveData.Set("Menus", menuIndex.Key, menuIndex.Value);
    Storage = SaveData.ToString();
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
        else if (argument.Split()[0].ToUpper().Contains("MENU")) ProcessMenuCommands(argument);
    }
}

void ProcessMenuCommands(string command) {
    MyCommandLine menuCommand = new MyCommandLine();
    if (menuCommand.TryParse(command)) {
        if (menuCommand.Argument(0).ToUpper().Equals("MENU+") ||
                menuCommand.Argument(0).ToUpper().Equals("MENU-")) {
            string menuName = menuCommand.Argument(1);
            if (MenuIndicies.Keys.Contains(menuName) &&
                    MenuSizes.Keys.Contains(menuName)) {
                Boolean isIncrease = menuCommand.Argument(0).Contains("+");
                MenuIndicies[menuName] = MenuIndicies[menuName] + (isIncrease?1:-1);
                if (MenuIndicies[menuName] > MenuSizes[menuName]-1)
                    MenuIndicies[menuName] = 0;
                else if (MenuIndicies[menuName] < 0)
                    MenuIndicies[menuName] = MenuSizes[menuName]-1;
            } else Echo($"Menu { menuName } is not configured properly.");
        } else Echo($"Invalid menu command: { menuCommand.Argument(0) }");
    }
}

void ProcessRefineryCommands(string command) {
    MyCommandLine refineryCommand = new MyCommandLine();
    if (refineryCommand.TryParse(command)) {
        IMyRefinery refinery = GridTerminalSystem.GetBlockWithName(refineryCommand.Argument(2)) as IMyRefinery;
        if (refinery != null && RefineryPriorities.Keys.Contains(refinery)) {
            if (refineryCommand.Argument(1).ToUpper().Equals("CLEAR")) {
                SortBlockInventory(refinery, refinery.GetInventory(0), storageContainers);
                Echo($"{ refinery.CustomName } input inventory cleared...");
            } else if (refineryCommand.Argument(1).ToUpper().Equals("PRIORITY+") ||
                    refineryCommand.Argument(1).ToUpper().Equals("PRIORITY-")) {
                AdjustRefineryPriority(refinery, refineryCommand);
            } else if (refineryCommand.Argument(1).ToUpper().Equals("SCAN")) {
                Dictionary<MyItemType,MyFixedPoint>.KeyCollection ores = ScanCargo(true, "Ore").Keys;
                foreach (MyItemType type in ores)
                    if (!type.SubtypeId.Equals("Ice") && !RefineryPriorities[refinery].Keys.Contains(type.SubtypeId)) {
                        RefineryPriorities[refinery][type.SubtypeId] = RefineryPriorities[refinery].Count+1;
                        Echo($"{ type.SubtypeId.ToString() } added to refinery:\n{ refinery.CustomName }");
                        SaveRefineryConfig(refinery);
                    }
            } else if (refineryCommand.Argument(1).ToUpper().Equals("DELETE")) {
                DeleteRefineryPriority(refinery, refineryCommand);
            } else Echo($"Unknown Refinery Command:\n\"{ refineryCommand.Argument(1) }\"");
        } else Echo($"Cannot find prioritized refinery \"{ refineryCommand.Argument(2) }\"");
    }
}

void DeleteRefineryPriority(IMyRefinery refinery, MyCommandLine refineryCommand) {
    string ore = refineryCommand.Argument(3);
    string menuName = $"{ refinery.CustomName } - Priority Menu";
    if (ore.ToUpper().Equals("MENU")) {
        ore = GetOreAtPriority(refinery, MenuIndicies[menuName]+1);
    }
    MenuSizes[menuName]--;
    if (MenuIndicies[menuName] >= MenuSizes[menuName]) MenuIndicies[menuName] = MenuSizes[menuName]-1;
    if (MenuIndicies[menuName] < 0) MenuIndicies[menuName] = 0;
    if (!RefineryPriorities[refinery].Keys.Contains(ore)) {
        Echo($"{ ore } is not prioritized for:\n{ refinery.CustomName }"); return;
    }
    int currentOrePriority = RefineryPriorities[refinery][ore];
    List<string> lowerPriorityOres = new List<string>();
    foreach (KeyValuePair<string,int> orePriority in RefineryPriorities[refinery]) {
        if (orePriority.Value > currentOrePriority) lowerPriorityOres.Add(orePriority.Key);
    }
    foreach (string lowerPriorityOre in lowerPriorityOres) RefineryPriorities[refinery][lowerPriorityOre]--;
    RefineryPriorities[refinery].Remove(ore);
    SaveRefineryConfig(refinery);
    Echo($"Deleted { ore } from:\n{ refinery.CustomName }");
}

void AdjustRefineryPriority(IMyRefinery refinery, MyCommandLine refineryCommand) {
    Boolean isIncrease = refineryCommand.Argument(1).Contains("+");
    string ore = refineryCommand.Argument(3);
    if (ore.ToUpper().Equals("MENU")) {
        string menuName = $"{ refinery.CustomName } - Priority Menu";
        ore = GetOreAtPriority(refinery, MenuIndicies[menuName]+1);
        MenuIndicies[menuName] = MenuIndicies[menuName] + (isIncrease?-1:1);
    }
    Boolean isInvalidCommand = true;
    if (!ORES.Contains(ore))
        Echo($"Invalid ore type for { refineryCommand.Argument(1) }: { ore }");
    else if (!RefineryPriorities[refinery].Keys.Contains(ore))
        RefineryPriorities[refinery][ore] = RefineryPriorities[refinery].Count+1;
    else if (isIncrease && RefineryPriorities[refinery][ore] == 1)
        Echo($"{ ore } is set to maximum priority in:\n{ refinery.CustomName }");
    else if (!isIncrease && RefineryPriorities[refinery][ore] == RefineryPriorities[refinery].Count)
        Echo($"{ ore } is set to minimum priority in:\n{ refinery.CustomName }");
    else isInvalidCommand = false;
    if (!isInvalidCommand) {
        int currentOrePriority = RefineryPriorities[refinery][ore];
        int newOrePriority = currentOrePriority+(isIncrease?-1:1);
        string oreAtNewPriority = GetOreAtPriority(refinery, newOrePriority);
        RefineryPriorities[refinery][oreAtNewPriority] = currentOrePriority;
        RefineryPriorities[refinery][ore] = newOrePriority;
        Echo($"{ ore } priority { (isIncrease?"increased":"decreased") }");
    }
    SaveRefineryConfig(refinery);
}

void SaveRefineryConfig(IMyRefinery refinery) {
    MyIni refineryConfig = new MyIni();
    foreach (KeyValuePair<string,int> orePriority in RefineryPriorities[refinery])
        refineryConfig.Set("Priority", orePriority.Key, orePriority.Value);
    Echo(refineryConfig.ToString());
    refinery.CustomData = refineryConfig.ToString();
}

string GetOreAtPriority(IMyRefinery refinery, int priority) {
    foreach (KeyValuePair<string,int> orePriority in RefineryPriorities[refinery]) {
        if (orePriority.Value == priority) return orePriority.Key;
    }
    return "";
}

void PrioritizeRefining() {
    List<IMyRefinery> refineries = new List<IMyRefinery>();
    GridTerminalSystem.GetBlocksOfType(refineries, block => {
        if (!IsConnected(block)) return false;
        if (!block.Enabled) return false;
        IMyRefinery refinery = (IMyRefinery) block;
        string menuName = $"{ refinery.CustomName } - Priority Menu";
        MenuSizes[menuName] = 0;
        MyIni refineryConfig = new MyIni();
        MyIniParseResult result = new MyIniParseResult();
        if (refineryConfig.TryParse(block.CustomData, out result)) {
            if (true || refineryConfig.ContainsSection("Priority")) {
                RefineryPriorities[refinery] = new Dictionary<string,int>();
                RefineryDisplays[refinery] = new List<IMyTextSurface>();
                GridTerminalSystem.GetBlocksOfType(RefineryDisplays[refinery], surface => {
                    IMyTextPanel panel = (IMyTextPanel) surface;
                    if (!panel.CustomData.Contains($"[LCD]\nRefineryDisplay=\"{ refinery.CustomName }\"")) return false;
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.FontColor = Color.DarkSlateGray;
                    surface.Alignment = TextAlignment.CENTER;
                    return true;
                });
                Display(RefineryDisplays[refinery], $"{ refinery.CustomName }\nPRIORITIES\n", false);
                List<MyIniKey> priorityKeys = new List<MyIniKey>();
                refineryConfig.GetKeys("Priority", priorityKeys);
                int priorityIndex = 0;
                foreach(MyIniKey priorityKey in priorityKeys.OrderBy(i => refineryConfig.Get(i).ToInt32(int.MaxValue))) {
                    MenuSizes[menuName]++;
                    string oreKey = priorityKey.ToString().Split('/')[1];
                    RefineryPriorities[refinery][oreKey] = refineryConfig.Get("Priority", oreKey).ToInt32(int.MaxValue);
                    string displayItem = (priorityIndex==MenuIndicies[menuName]?">":"");
                    displayItem += $"{ oreKey }: { SortOre(oreKey, refinery) }";
                    displayItem += (priorityIndex==MenuIndicies[menuName]?"<":"");
                    Display(RefineryDisplays[refinery], $"{ displayItem }\n");
                    priorityIndex++;
                }
                if (MenuSizes[menuName] == 0) Display(RefineryDisplays[refinery], "---SCAN FOR ORE---");
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
        return !RefineryPriorities.Keys.Contains(block) && IsConnected(block);
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
        } else if (!block.GetInventory(0).IsConnectedTo(storageContainers[0].GetInventory(0))) {
            return false;
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
    Dictionary<MyItemType, MyFixedPoint> cargo = ScanCargo(connected);

    Display(cargoDisplays, "", false);
    foreach (KeyValuePair<MyItemType, MyFixedPoint> entry in cargo.OrderBy(i => FormatItemType(i.Key))) {
        Display(cargoDisplays, $"{ FormatItemType(entry.Key) }: { FormatItemAmount(entry.Value) }\n");
    }
}

Dictionary<MyItemType,MyFixedPoint> ScanCargo(Boolean connected = false, string type = "", string subtype = "") {
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
                if (type.Length == 0 || item.Type.TypeId.Equals($"MyObjectBuilder_{ type }")) {
                    if (subtype.Length == 0 || item.Type.SubtypeId.Equals(subtype)) {
                        if (cargo.Keys.Contains(item.Type)) cargo[item.Type] += item.Amount;
                        else cargo[item.Type] = item.Amount;
                    }
                }
            }
        }

        return true;
    });

    return cargo;
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
