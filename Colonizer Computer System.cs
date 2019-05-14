IMyCockpit mainCockpit;
List<IMyTextSurface> oxygenStatusPanels = new List<IMyTextSurface>();
List<IMyTextSurface> powerStatusPanels = new List<IMyTextSurface>();

List<IMyPowerProducer> powerProducers = new List<IMyPowerProducer>();

Boolean includeConnected = false;

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    mainCockpit = GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit;

    oxygenStatusPanels.Add(mainCockpit.GetSurface(1));
    powerStatusPanels.Add(mainCockpit.GetSurface(2));

    ConfigurePanels(oxygenStatusPanels);
    ConfigurePanels(powerStatusPanels);

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
    }
    updateSource &= ~UpdateType.Update100;
    if (updateSource != UpdateType.None) {
        Echo("Process Arguments...");
    }
}

static void DisplayCockpitOxygen(IMyCockpit cockpit, List<IMyTextSurface> displayPanels) {
    float oxygenCapacity = cockpit.OxygenCapacity;
    float oxygenFilledRatio = cockpit.OxygenFilledRatio;
    float oxygenFill = oxygenFilledRatio * oxygenCapacity;
    string oxygenStatus = $"{ oxygenFill.ToString("n2") } / { oxygenCapacity.ToString("n2") }L\n";
    oxygenStatus += $"({ (oxygenFilledRatio*100f).ToString("n0") }%)";
    PrintPanels(displayPanels, $"Oxygen Status:\n{oxygenStatus}\n\n", false);
    PrintPanels(displayPanels, $"O2 Ice: { 0 } kg\n");
}

static void DisplayPowerSummary(List<IMyPowerProducer> powerProducers, List<IMyTextSurface> panels) {
    PrintPanels(panels, $"Power Summary:\n", false);

    float totalFuelAmount = 0f;
    float totalFuelCapacity = 0f;

    float activeMaxPower = 0f;
    float auxMaxPower = 0f;

    float totalCurrentPower = 0f;

    int hydrogenEngineCount = 0;

    foreach (IMyPowerProducer producer in powerProducers) {
        Boolean functional = true;
        if (producer.BlockDefinition.TypeId.ToString().IndexOf("HydrogenEngine") != -1) {
            hydrogenEngineCount++;

            float[] fuelStats = ReadHydrogenEngineFuel(producer);
            totalFuelAmount += fuelStats[0];
            totalFuelCapacity += fuelStats[1];
            if (fuelStats[0] == 0) functional = false;
        } else if (producer.BlockDefinition.TypeId.ToString().IndexOf("BatteryBlock") != -1) {
            IMyBatteryBlock battery = (IMyBatteryBlock) producer;
            if (battery.ChargeMode == ChargeMode.Recharge) auxMaxPower += ReadChargingBatteryMaxOutput(battery);
        }

        if (producer.Enabled && functional) {
            activeMaxPower += producer.MaxOutput;
            totalCurrentPower += producer.CurrentOutput;
        } else if (functional) {
            auxMaxPower += producer.MaxOutput;
        }
    }

    PrintPanels(panels, $"{ totalCurrentPower.ToString("n1") } / { activeMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"Aux: { auxMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"Batt/H2: { 100 }% / { ((totalFuelAmount / totalFuelCapacity) * 100).ToString("n0") }%\n");
    PrintPanels(panels, $"H2 Ice: { 0 } kg\n");
    PrintPanels(panels, $"U: { 0 } kg\n");
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
