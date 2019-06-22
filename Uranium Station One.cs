List<IMyShipDrill> drills = new List<IMyShipDrill>();
List<IMyTerminalBlock> cargoContainers = new List<IMyTerminalBlock>();

List<IMyExtendedPistonBase> radialPistons = new List<IMyExtendedPistonBase>();
List<IMyExtendedPistonBase> elevationPistons = new List<IMyExtendedPistonBase>();
IMyMotorStator drillRotor;

IMyTextSurface statusPanel;

Boolean drillExtending = false;
Boolean drillsReset = false;

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    GridTerminalSystem.GetBlocksOfType(drills, Connected);
    GridTerminalSystem.GetBlocksOfType(cargoContainers, block => {
        if (!Connected(block)) return false;
        if (!block.HasInventory) return false;
        return true;
    });

    GridTerminalSystem.GetBlocksOfType(elevationPistons, block => {
        return block.CubeGrid == Me.CubeGrid;
    });
    Vector3D elevationPistonOrientation = elevationPistons[0].WorldMatrix.Up;
    GridTerminalSystem.GetBlocksOfType(elevationPistons, block => {
        return block.WorldMatrix.Up.Equals(elevationPistonOrientation, 0.001f);
    });

    GridTerminalSystem.GetBlocksOfType(radialPistons, block => {
        return block.CubeGrid != Me.CubeGrid && block.IsSameConstructAs(Me) && !elevationPistons.Contains(block);
    });
    
    List<IMyMotorStator> tempRotors = new List<IMyMotorStator>();
    GridTerminalSystem.GetBlocksOfType(tempRotors, block => {
        return block.IsSameConstructAs(Me);
    });
    if (tempRotors.Count == 1) drillRotor = tempRotors[0];
    else throw new Exception("Too Many Drill? Rotors");

    statusPanel = Me.GetSurface(0);
    statusPanel.ContentType = ContentType.TEXT_AND_IMAGE;

    if (Storage != "") drillExtending = Storage.Equals("EXTENDING");
    else drillExtending = false;
}

void Display(IMyTextSurface panel, string text, Boolean append = true) {
    Echo(text);
    panel.WriteText($"{text}\n", append);
}

Boolean Connected(IMyTerminalBlock block) {
    return block.IsSameConstructAs(Me);
}

public void Save() {
    Storage = drillExtending?"EXTENDING":"RETRACTING";
}

public void Main(string argument, UpdateType updateSource) {
    Display(statusPanel, "", false);
    if (CargoCheck(0.95f)) PauseDrilling();
    else if (!drillsReset) UpdateDrills();
    else {
        Display(statusPanel, "RESET");
        foreach (IMyExtendedPistonBase piston in elevationPistons) {
            piston.MaxLimit = (1f/3f);
            piston.Retract();
        }
        Runtime.UpdateFrequency = UpdateFrequency.None;
        Me.Enabled = false;
    }
}

void PauseDrilling() {
    ToggleBlocks(drills, false);
    ToggleBlocks(radialPistons, false);
    Display(statusPanel, "PAUSED");
}

void UpdateDrills() {
    ToggleBlocks(drills, true);
    ToggleBlocks(radialPistons, true);
    Boolean display = true;
    foreach (IMyExtendedPistonBase piston in radialPistons) {
        if (piston.CurrentPosition == piston.MaxLimit) {
            if (display) Display(statusPanel, $"Retracting: {piston.CurrentPosition.ToString("n1")}m");
            piston.Velocity = -0.5f;
            drillExtending = false;
        } else if (drillExtending && elevationPistons[0].CurrentPosition == elevationPistons[0].MaxLimit) {
            if (display) Display(statusPanel, $"Drilling: {piston.CurrentPosition.ToString("n1")} / {piston.MaxLimit.ToString("n1")}m");
            piston.Velocity = 0.02f;
        } else if (piston.CurrentPosition == piston.MinLimit && !drillExtending) {
            if (display) Display(statusPanel, $"Advancing: {piston.CurrentPosition.ToString("n1")}m");
            if (elevationPistons[0].MaxLimit == elevationPistons[0].HighestPosition) {
                ResetDrills();
                return;
            }
            foreach (IMyExtendedPistonBase ePiston in elevationPistons) ePiston.MaxLimit += (1f/3f);
            drillExtending = true;
        } else if (!drillExtending) {
            if (display) Display(statusPanel, $"Retracting: {piston.CurrentPosition.ToString("n1")}m");
        } else {
            if (display) Display(statusPanel, $"Advancing: {piston.CurrentPosition.ToString("n1")}m");
        }
        display = false;
    }
    Display(statusPanel, $"Vertical: { (elevationPistons[0].CurrentPosition*3).ToString("n1") }m");
}

void ResetDrills() {
    drillsReset = true;
    PauseDrilling();
}

Boolean CargoCheck(float maxFillRatio) {
    float maxVolume = 0f;
    float currentVolume = 0f;

    foreach (IMyTerminalBlock container in cargoContainers) {
        IMyInventory inventory = container.GetInventory(0);
        maxVolume += (float) inventory.MaxVolume;
        currentVolume += (float) inventory.CurrentVolume;
    }

    float fillRatio = currentVolume / maxVolume;
    Display(statusPanel, $"{(currentVolume*1000).ToString("n2")} / {(maxVolume*1000).ToString("n2")} L");
    Display(statusPanel, $"{(fillRatio*100).ToString("n2")}%");
    return fillRatio >= maxFillRatio;
}

void ToggleBlocks<T>(List<T> blocks, Boolean toggle) where T: class, IMyFunctionalBlock {
    foreach(T block in blocks) block.Enabled = toggle;
}
