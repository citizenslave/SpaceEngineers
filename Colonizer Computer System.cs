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

    GridTerminalSystem.GetBlocksOfType<IMyPowerProducer>(powerProducers, producer => {
        return includeConnected || producer.IsSameConstructAs(Me);
    });
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
}

static void DisplayCockpitOxygen(IMyCockpit cockpit, List<IMyTextSurface> displayPanels) {
    float oxygenCapacity = cockpit.OxygenCapacity;
    float oxygenFilledRatio = cockpit.OxygenFilledRatio;
    float oxygenFill = oxygenFilledRatio * oxygenCapacity;
    string oxygenStatus = $"{ oxygenFill.ToString("n2") } / { oxygenCapacity.ToString("n2") }L\n";
    oxygenStatus += $"({ (oxygenFilledRatio*100f).ToString("n2") }%)";
    PrintPanels(displayPanels, $"Oxygen Status:\n{oxygenStatus}\nIce:", false);
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
        if (producer.BlockDefinition.TypeId.ToString().IndexOf("HydrogenEngine") != -1) {
            hydrogenEngineCount++;

            float[] fuelStats = ReadHydrogenEngineFuel(producer);

            totalFuelAmount += fuelStats[0];
            totalFuelCapacity += fuelStats[1];

            if (producer.Enabled) {
                activeMaxPower += producer.MaxOutput;
                totalCurrentPower += producer.CurrentOutput;
            } else {
                auxMaxPower += producer.MaxOutput;
            }
        }
    }

    PrintPanels(panels, $"{ totalCurrentPower.ToString("n1") } / { activeMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"Aux: { auxMaxPower.ToString("n1") } MW\n");
    PrintPanels(panels, $"H2: ({ ((totalFuelAmount / totalFuelCapacity) * 100).ToString("n1") }%)\n");
    PrintPanels(panels, $"U:\n");
    PrintPanels(panels, $"Ice:\n");
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
