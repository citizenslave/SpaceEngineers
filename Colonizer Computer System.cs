IMyCockpit mainCockpit;
List<IMyTextSurface> oxygenStatusPanels = new List<IMyTextSurface>();

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    mainCockpit = GridTerminalSystem.GetBlockWithName("Cockpit") as IMyCockpit;

    oxygenStatusPanels.Add(mainCockpit.GetSurface(1));
    ConfigurePanels(oxygenStatusPanels);
}

void ConfigurePanels(List<IMyTextSurface> panels, TextAlignment align = TextAlignment.CENTER, float fontSize = 2.0f) {
    foreach (IMyTextSurface panel in panels) {
        panel.Alignment = align;
        panel.FontSize = fontSize;
        panel.ContentType = ContentType.TEXT_AND_IMAGE;
    }
}

void PrintPanels(List<IMyTextSurface> panels, string text, Boolean append = true) {
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
    float oxygenCapacity = mainCockpit.OxygenCapacity;
    float oxygenFilledRatio = mainCockpit.OxygenFilledRatio;
    float oxygenFill = oxygenFilledRatio * oxygenCapacity;
    string oxygenStatus = $"{ oxygenFill.ToString("n2") } / { oxygenCapacity.ToString("n2") }L\n";
    oxygenStatus += $"({ (oxygenFilledRatio*100f).ToString("n2") }%)";
    PrintPanels(oxygenStatusPanels, "Oxygen Status:\n", false);
    PrintPanels(oxygenStatusPanels, oxygenStatus);
}

