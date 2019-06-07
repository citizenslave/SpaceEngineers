List<IMyShipDrill> drills = new List<IMyShipDrill>();
IMyMotorStator drillRotor;
IMyExtendedPistonBase drillPiston;

string drillState;
string[] DRILL_COMMANDS = { "STOP", "START", "PAUSE" };

public Program()
 {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    drillRotor = GridTerminalSystem.GetBlockWithName("MM Drill Rotor") as IMyMotorStator;
    drillPiston = GridTerminalSystem.GetBlockWithName("MM Drill Piston") as IMyExtendedPistonBase;
    GridTerminalSystem.GetBlocksOfType(drills, block => {
        return block.IsSameConstructAs(Me);
    });

    if (Storage != "") drillState = Storage;
    else UpdateDrillState("STOP");
}

public void Save() {
    Storage = drillState;
}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update100) != 0) {
        updateSource &= ~UpdateType.Update100;
        Echo($"Drill State: { drillState }");
        CheckDrill();
    }
    if (updateSource != UpdateType.None) {
        Echo($"RECIEVED ARGUMENT: { argument }");
        if (!DRILL_COMMANDS.Contains(argument)) Echo("INVALID COMMAND");
        else {
            Echo("UPDATING DRILL STATE...");
            UpdateDrillState(argument);
        }
    }
}

void UpdateDrillState(string command) {
    if (command.Equals("STOP")) {
        if (drillState.Equals("STOPPED") || drillState.Equals("STOPPING")) return;
        drillState = "STOPPING";
        drillPiston.Velocity = -0.1f;
        drillPiston.Enabled = true;
        ToggleBlocks(drills, true);
    } else if (command.Equals("START")) {
        if (drillState.Equals("DRILLING")) return;
        // check clamps and thrusters?
        drillState = "DRILLING";
        ToggleBlocks(drills, true);
        drillPiston.Enabled = true;
        drillPiston.Velocity = 0.02f;
        drillRotor.Enabled = true;
        drillRotor.RotorLock = false;
        drillRotor.TargetVelocityRPM = 6f;
    } else if (command.Equals("PAUSE")) {
        if (drillState.Equals("STOPPED") || drillState.Equals("STOPPING")) return;
        if (drillState.Equals("PAUSED")) {
            drillState = "DRILLING";
            ToggleBlocks(drills, true);
            drillPiston.Enabled = true;
        } else {
            drillState = "PAUSED";
            ToggleBlocks(drills, false);
            drillPiston.Enabled = false;
        }
    }
}

void CheckDrill() {
    if (drillState.Equals("STOPPING")) StopDrills();
    else if (drillState.Equals("STOPPED")) return;
    else if (drillState.Equals("PAUSED") && !IsCargoFull(0.1f)) UpdateDrillState("PAUSE");
    else if (drillState.Equals("DRILLING")) {
        if (IsCargoFull(0.9f)) UpdateDrillState("PAUSE");
        if (drillPiston.CurrentPosition == drillPiston.MaxLimit) UpdateDrillState("STOP");
        Echo($"{ drillPiston.CurrentPosition } / { drillPiston.MaxLimit }m");
    }
}

Boolean IsCargoFull(float maxPercentFull) {
    float maxCargo = 0f;
    float currentCargo = 0f;
    List<IMyTerminalBlock> containers = new List<IMyTerminalBlock>();

    GridTerminalSystem.GetBlocksOfType(containers, block => {
        if (!block.IsSameConstructAs(Me)) return false;
        if (!block.HasInventory) return false;
        if (block.BlockDefinition.SubtypeId.IndexOf("Tank") != -1) return false;
        if (block.BlockDefinition.SubtypeId.IndexOf("Blast Furnace") != -1) return false;
        if (block.BlockDefinition.SubtypeId.IndexOf("BasicAssembler") != -1) return false;
        if (block.BlockDefinition.SubtypeId.IndexOf("LargeBlockSmallGenerator") != -1) return false;
        if (block.BlockDefinition.TypeId.ToString().IndexOf("OxygenGenerator") != -1) return false;

        IMyInventory blockInventory = block.GetInventory(0);
        maxCargo += (float) blockInventory.MaxVolume;
        currentCargo += (float) blockInventory.CurrentVolume;

        return true;
    });

    float percentFull = currentCargo / maxCargo;
    Echo ($"{ (currentCargo*1000).ToString("n2") } / { (maxCargo*1000).ToString("n2") } L ({ (percentFull * 100).ToString("n2") }%)");

    if (percentFull >= maxPercentFull) return true;
    else return false;
}

void StopDrills() {
    Boolean stopped = true;
    if (drillRotor.Angle < MathHelper.ToRadians(0.5f) || drillRotor.Angle >= MathHelper.ToRadians(359.5f)) {
        drillRotor.TargetVelocityRPM = 0f;
        drillRotor.RotorLock = true;
        drillRotor.Enabled = false;
        Echo($"{ MathHelper.ToDegrees(drillRotor.Angle) }");
    } else {
        stopped = false;
        drillRotor.TargetVelocityRPM = (drillRotor.Angle > MathHelper.ToRadians(270f)) ?
                (12f*(float) (1 - (drillRotor.Angle / (2f*Math.PI)))) : 6f;
        Echo($"{ MathHelper.ToDegrees(drillRotor.Angle) }");
    }
    if (drillPiston.CurrentPosition == 0f) {
        drillPiston.Velocity = 0f;
        drillPiston.Enabled = false;
    } else stopped = false;
    if (stopped) {
        drillState = "STOPPED";
        ToggleBlocks(drills, false);
    }
}

static void ToggleBlocks<T>(List<T> blocks, Boolean toggle) where T: class, IMyFunctionalBlock {
    foreach (IMyFunctionalBlock block in blocks) block.Enabled = toggle;
}
