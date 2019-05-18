IMyCockpit mainCockpit;
List<IMyTextSurface> oxygenStatusPanels = new List<IMyTextSurface>();
List<IMyTextSurface> powerStatusPanels = new List<IMyTextSurface>();
List<IMyTextSurface> cargoStatusPanels = new List<IMyTextSurface>();

List<IMyPowerProducer> powerProducers = new List<IMyPowerProducer>();

static MyItemType URANIUM_I = new MyItemType("MyObjectBuilder_Ingot", "Uranium");
static MyItemType ICE = new MyItemType("MyObjectBuilder_Ore", "Ice");

Boolean includeConnected = false;

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    mainCockpit = GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit;

    oxygenStatusPanels.Add(mainCockpit.GetSurface(1));
    powerStatusPanels.Add(mainCockpit.GetSurface(2));
    cargoStatusPanels.Add(mainCockpit.GetSurface(3));

    ConfigurePanels(oxygenStatusPanels);
    ConfigurePanels(powerStatusPanels);
    ConfigurePanels(cargoStatusPanels);

    RefreshBlocks<IMyPowerProducer>(powerProducers);
}

Boolean Connected(IMyTerminalBlock block) {
    return includeConnected || block.IsSameConstructAs(Me);
}

void RefreshBlocks<T>(List<T> list) where T : class, IMyTerminalBlock {
    list.Clear();
    GridTerminalSystem.GetBlocksOfType<T>(list, Connected);
}

static void ConfigurePanels(List<IMyTextSurface> panels, TextAlignment align = TextAlignment.CENTER, float fontSize = 1.8f) {
    foreach (IMyTextSurface panel in panels) {
        panel.Alignment = align;
        panel.FontSize = fontSize;
        panel.FontColor = Color.DarkSlateGray;
        panel.ContentType = ContentType.TEXT_AND_IMAGE;
    }
}

static void PrintPanels(List<IMyTextSurface> panels, string text, Boolean append = true) {
    foreach (IMyTextSurface panel in panels) panel.WriteText(text, append);
}

static string Percent(float total, float capacity) {
    return $"{ ((total / capacity) * 100).ToString("n0") }%";
}

public void Save() {
    // Called when the program needs to save its state. Use
    // this method to save your state to the Storage field
    // or some other means. 
    // 
    // This method is optional and can be removed if not
    // needed.

}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update100) != 0) {
        DisplayCockpitOxygen(mainCockpit, oxygenStatusPanels);
        DisplayPowerSummary(powerProducers, powerStatusPanels);
        DisplayCargoSummary(cargoStatusPanels);
    }
    updateSource &= ~UpdateType.Update100;
    if (updateSource != UpdateType.None) {
        Echo("Process Arguments...");
    }
}

void DisplayCockpitOxygen(IMyCockpit cockpit, List<IMyTextSurface> displayPanels) {
    float oxygenCapacity = cockpit.OxygenCapacity;
    float oxygenFilledRatio = cockpit.OxygenFilledRatio;
    float oxygenFill = oxygenFilledRatio * oxygenCapacity;
    string oxygenStatus = $"{ oxygenFill.ToString("n2") } / { oxygenCapacity.ToString("n2") }L\n";
    oxygenStatus += $"({ Percent(oxygenFill, oxygenCapacity) })";

    List<IMyGasGenerator> gasGenerators = new List<IMyGasGenerator>();
    GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(gasGenerators, block => {
        if (!Connected(block)) return false;
        if (!block.GetInventory(0).IsConnectedTo(cockpit.GetInventory(0))) return false;
        return true;
    });
    float connectedIce = SearchConnectedItems(gasGenerators, ICE);

    float oxygenTankCapacity = 0f;
    float oxygenTankAmount = 0f;
    List<IMyGasTank> gasTanks = new List<IMyGasTank>();
    GridTerminalSystem.GetBlocksOfType<IMyGasTank>(gasTanks, block => {
        IMyGasTank tank = (IMyGasTank) block;
        if (!Connected(tank)) return false;
        if (tank.BlockDefinition.SubtypeId.ToString().IndexOf("OxygenTank") == -1) return false;
        foreach (IMyGasGenerator generator in gasGenerators) {
            if (!tank.GetInventory(0).IsConnectedTo(generator.GetInventory(0))) return false;
        }
        oxygenTankCapacity += tank.Capacity;
        oxygenTankAmount += tank.Capacity * (float) tank.FilledRatio;
        return true;
    });

    oxygenStatus = $"Oxygen Status:\n{ oxygenStatus }\n\nO2 Tanks: ";
    oxygenStatus += gasTanks.Count == 0?"None\n":$"{ Percent(oxygenTankAmount, oxygenTankCapacity) }\n";

    PrintPanels(displayPanels, oxygenStatus, false);
    PrintPanels(displayPanels, $"O2 Ice: { (connectedIce / 1000).ToString("n2") } Mg\n");
}

void DisplayPowerSummary(List<IMyPowerProducer> powerProducers, List<IMyTextSurface> panels) {
    PrintPanels(panels, $"Power Summary:\n", false);

    float totalFuelAmount = 0f;
    float totalFuelCapacity = 0f;

    float totalChargeAmount = 0f;
    float totalChargeCapacity = 0f;

    float totalUraniumMass = 0f;
    float totalIceMass = 0f;

    float activeMaxPower = 0f;
    float auxMaxPower = 0f;

    float totalCurrentPower = 0f;

    List<IMyPowerProducer> hydrogenEngines = new List<IMyPowerProducer>();
    List<IMyPowerProducer> reactors = new List<IMyPowerProducer>();

    foreach (IMyPowerProducer producer in powerProducers) {
        Boolean functional = true;
        string blockType = producer.BlockDefinition.TypeId.ToString();
        if (blockType.IndexOf("HydrogenEngine") != -1) {
            float[] fuelStats = ReadHydrogenEngineFuel(producer);
            totalFuelAmount += fuelStats[0];
            totalFuelCapacity += fuelStats[1];
            if (fuelStats[0] == 0) functional = false;
            hydrogenEngines.Add(producer);
        } else if (blockType.IndexOf("BatteryBlock") != -1) {
            IMyBatteryBlock battery = (IMyBatteryBlock) producer;
            if (battery.ChargeMode == ChargeMode.Recharge) auxMaxPower += ReadChargingBatteryMaxOutput(battery);
            totalChargeAmount += battery.CurrentStoredPower;
            totalChargeCapacity += battery.MaxStoredPower;
        } else if (blockType.IndexOf("Reactor") != 1) {
            if (producer.GetInventory(0).CurrentMass == 0) functional = false;
            reactors.Add(producer);
        }

        if (producer.Enabled && functional) {
            activeMaxPower += producer.MaxOutput;
            totalCurrentPower += producer.CurrentOutput;
        } else if (functional) {
            auxMaxPower += producer.MaxOutput;
        }
    }

    totalUraniumMass += SearchConnectedItems(reactors, URANIUM_I);

    List<IMyGasGenerator> gasGenerators = new List<IMyGasGenerator>();
    GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(gasGenerators, block => {
        if (!Connected(block)) return false;
        // if not connected to engines return false
        return true;
    });
    totalIceMass += SearchConnectedItems(gasGenerators, ICE);

    List<IMyGasTank> gasTanks = new List<IMyGasTank>();
    GridTerminalSystem.GetBlocksOfType<IMyGasTank>(gasTanks, block => {
        IMyGasTank tank = (IMyGasTank) block;
        if (!Connected(tank)) return false;
        if (tank.BlockDefinition.SubtypeId.ToString().IndexOf("HydrogenTank") == -1) return false;
        foreach (IMyGasGenerator generator in gasGenerators) {
            if (!tank.GetInventory(0).IsConnectedTo(generator.GetInventory(0))) return false;
        }
        totalFuelCapacity += tank.Capacity;
        totalFuelAmount += tank.Capacity * (float) tank.FilledRatio;
        return true;
    });

    PrintPanels(panels, $"{ totalCurrentPower.ToString("n2") } / { activeMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"Aux: { auxMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"Batt/H2: { Percent(totalChargeAmount, totalChargeCapacity) } /");
    PrintPanels(panels, $" { Percent(totalFuelAmount, totalFuelCapacity) }\n");
    PrintPanels(panels, $"H2 Ice: { (totalIceMass / 1000).ToString("n2") } Mg\n");
    PrintPanels(panels, $"U: { totalUraniumMass.ToString("n2") } kg\n");
}

float SearchConnectedItems<T>(List<T> destinationBlocks, MyItemType item,
        Boolean requireAll = false) where T: class, IMyTerminalBlock {
    float foundItems = 0f;
    List<IMyTerminalBlock> connectedBlocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(connectedBlocks, block => {
        if (!Connected(block)) return false;
        if (block.GetInventory(0) == null) return false;
        
        Boolean connectedToAny = false;
        foreach (T dBlock in destinationBlocks) {
            if (!block.GetInventory(0).IsConnectedTo(dBlock.GetInventory(0))) {
                if (requireAll) return false;
            } else {
                connectedToAny = true;
            }
        }

        if (!connectedToAny) return false;

        for (int i=0; i < block.InventoryCount; i++) foundItems += (float) block.GetInventory(i).GetItemAmount(item);

        return true;
    });

    return foundItems;
}

static float[] ReadHydrogenEngineFuel(IMyPowerProducer hydrogenEngine) {
    string[] fuel = hydrogenEngine.DetailedInfo.Split('\n')[3].Split()[2].Split('/');
    fuel[0] = fuel[0].Substring(1, fuel[0].Length - 2);
    fuel[1] = fuel[1].Substring(0, fuel[1].Length - 2);
    float fuelAmount = float.Parse(fuel[0]);
    float fuelCapacity = float.Parse(fuel[1]);

    float[] result = {fuelAmount, fuelCapacity};

    return result;
}

static float ReadChargingBatteryMaxOutput(IMyBatteryBlock battery) {
    string[] maxOutput = battery.DetailedInfo.Split('\n')[1].Split(':')[1].Substring(1).Split();
    return float.Parse(maxOutput[0]) * (maxOutput[1].Equals("MW")?1:0.001f);
}

void DisplayCargoSummary(List<IMyTextSurface> panels) {
    PrintPanels(panels, "Cargo Summary:\n", false);

    float iceStorage = 0f;
    float iceCapacity = 0f;
    float stoneStorage = 0f;
    float stoneCapacity = 0f;
    float oreStorage = 0f;
    float oreCapacity = 0f;
    float ingotStorage = 0f;
    float ingotCapacity = 0f;
    float ammoStorage = 0f;
    float ammoCapacity = 0f;
    float miscStorage = 0f;
    float miscCapacity = 0f;

    List<IMyTerminalBlock> storageBlocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(storageBlocks, block => {
        if (!Connected(block) || !block.HasInventory) return false;
        string blockType = block.BlockDefinition.ToString();
        if (blockType.IndexOf("Tank") != -1) return false;
        if (blockType.IndexOf("Reactor") != -1) return false;

        IMyInventory blockInventory = block.GetInventory(0);
        float current = (float) blockInventory.CurrentVolume;
        float max = (float) blockInventory.MaxVolume;

        if (blockType.IndexOf("CargoContainer") != -1 ||
                blockType.IndexOf("Connector") != -1 ||
                blockType.IndexOf("Cockpit") != -1) {
            iceStorage += current;
            stoneStorage += current;
            oreStorage += current;
            ingotStorage += current;
            ammoStorage += current;
            miscStorage += current;

            iceCapacity += max;
            stoneCapacity += max;
            oreCapacity += max;
            ingotCapacity += max;
            ammoCapacity += max;
            miscCapacity += max;
        } else if (blockType.IndexOf("OxygenGenerator") != -1) {
            iceStorage += current;
            iceCapacity += max;
        } else if (blockType.IndexOf("Gun") != -1 ||
                blockType.IndexOf("Turret") != -1) {
            ammoStorage += current;
            ammoCapacity += max;
        } else if (blockType.IndexOf("Drill") != -1) {
            iceStorage += current;
            stoneStorage += current;
            oreStorage += current;

            iceCapacity += max;
            stoneCapacity += max;
            oreCapacity += max;
        } else if (blockType.IndexOf("SurvivalKit") != -1) {
            stoneStorage += current;
            ingotStorage += current;
            ingotStorage += (float) block.GetInventory(1).CurrentVolume;
            miscStorage += (float) block.GetInventory(1).CurrentVolume;

            stoneCapacity += max;
            ingotCapacity += max;
            ingotCapacity += (float) block.GetInventory(1).MaxVolume;
            miscCapacity += (float) block.GetInventory(1).MaxVolume;
        } else {
            Echo($"Unhandled Storage Block:\n{ blockType }");
        }
        return true;
    });

    PrintPanels(panels, $"Ice/Stone: { Percent(iceStorage, iceCapacity) } / { Percent(stoneStorage, stoneCapacity) }\n");
    PrintPanels(panels, $"Ore/Ingots: { Percent(oreStorage, oreCapacity) } / { Percent(ingotStorage, ingotCapacity) }\n");
    PrintPanels(panels, $"Ammo: { Percent(ammoStorage, ammoCapacity) }\n");
    PrintPanels(panels, $"Misc: { Percent(miscStorage, miscCapacity) }\n");
}
