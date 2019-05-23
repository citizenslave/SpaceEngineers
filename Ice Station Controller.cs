List<IMyShipDrill> drills = new List<IMyShipDrill>();
List<IMyTerminalBlock> cargoContainers = new List<IMyTerminalBlock>();

List<IMyExtendedPistonBase> radialPistons = new List<IMyExtendedPistonBase>();
IMyExtendedPistonBase elevationPiston;
IMyMotorStator drillRotor;

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
    GridTerminalSystem.GetBlocksOfType(radialPistons, block => {
        return block.CubeGrid != Me.CubeGrid && block.IsSameConstructAs(Me);
    });

    List<IMyExtendedPistonBase> tempPistons = new List<IMyExtendedPistonBase>();
    GridTerminalSystem.GetBlocksOfType(tempPistons, block => {
        return block.CubeGrid == Me.CubeGrid;
    });
    if (tempPistons.Count == 1) elevationPiston = tempPistons[0];
    else throw new Exception("Too Many Elevation? Pistons");
    
    List<IMyMotorStator> tempRotors = new List<IMyMotorStator>();
    GridTerminalSystem.GetBlocksOfType(tempRotors, block => {
        return block.IsSameConstructAs(Me);
    });
    if (tempRotors.Count == 1) drillRotor = tempRotors[0];
    else throw new Exception("Too Many Drill? Rotors");

    if (Storage != "") drillExtending = Storage.Equals("EXTENDING");
    else drillExtending = false;
}

Boolean Connected(IMyTerminalBlock block) {
    return block.IsSameConstructAs(Me);
}

public void Save() {
    Storage = drillExtending?"EXTENDING":"RETRACTING";
}

public void Main(string argument, UpdateType updateSource) {
    if (CargoCheck(0.95f)) PauseDrilling();
    else if (!drillsReset) UpdateDrills();
}

void PauseDrilling() {
    ToggleBlocks(drills, false);
    ToggleBlocks(radialPistons, false);
}

void UpdateDrills() {
    ToggleBlocks(drills, true);
    ToggleBlocks(radialPistons, true);
    Boolean display = true;
    foreach (IMyExtendedPistonBase piston in radialPistons) {
        if (piston.CurrentPosition == piston.MaxLimit) {
            if (display) Echo($"Retracting: {piston.CurrentPosition.ToString("n1")}m");
            piston.Velocity = -0.5f;
            drillExtending = false;
        } else if (drillExtending && elevationPiston.CurrentPosition == elevationPiston.MaxLimit) {
            if (display) Echo($"Drilling: {piston.CurrentPosition.ToString("n1")}m");
            piston.Velocity = 0.02f;
        } else if (piston.CurrentPosition == piston.MinLimit && !drillExtending) {
            if (display) Echo($"Advancing: {piston.CurrentPosition.ToString("n1")}m");
            if (elevationPiston.MaxLimit == elevationPiston.HighestPosition) {
                ResetDrills();
                return;
            }
            elevationPiston.MaxLimit += 1f;
            drillExtending = true;
        } else if (!drillExtending) {
            if (display) Echo($"Retracting: {piston.CurrentPosition.ToString("n1")}m");
        } else {
            if (display) Echo($"Advancing: {piston.CurrentPosition.ToString("n1")}m");
        }
        display = false;
    }
    Echo($"Vertical: {elevationPiston.CurrentPosition.ToString("n1")}m");
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
    Echo($"{(currentVolume*1000).ToString("n2")} / {(maxVolume*1000).ToString("n2")} L {(fillRatio*100).ToString("n2")}%");
    return fillRatio >= maxFillRatio;
}

void ToggleBlocks<T>(List<T> blocks, Boolean toggle) where T: class, IMyFunctionalBlock {
    foreach(T block in blocks) block.Enabled = toggle;
}
