IMyRadioAntenna antenna;
string CHANNEL = "RSN";

public Program() {
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, AddPrefix);

    List<IMyRadioAntenna> tAntenna = new List<IMyRadioAntenna>();
    GridTerminalSystem.GetBlocksOfType(tAntenna, IsConnected);
    antenna = tAntenna[0];
    antenna.AttachedProgrammableBlock = Me.EntityId;

    IMyBroadcastListener relaySatNet = IGC.RegisterBroadcastListener(CHANNEL);
    relaySatNet.SetMessageCallback(CHANNEL);
}

Boolean AddPrefix(IMyTerminalBlock block) {
    if (!IsConnected(block)) return false;
    string prefix = FindPrefix();
    if (prefix.Equals("")) return false;
    if (block.CustomName.IndexOf(prefix) != 0) block.CustomName = $"{ prefix } - { block.CustomName }";
    return false;
}

string FindPrefix() {
    string[] customData = Me.CustomData.Split('\n');
    foreach(string dataItem in customData) {
        string[] dataItemValues = dataItem.Split('=');
        if (dataItemValues[0].Equals("GridPrefix")) return dataItemValues[1];
    }
    return "";
}

Boolean IsConnected(IMyTerminalBlock block) {
    return block.IsSameConstructAs(Me);
}

public void Save() {
}

public void Main(string argument, UpdateType updateSource) {
    if ((updateSource & UpdateType.Update100) != 0) {
        updateSource &= ~UpdateType.Update100;
        BroadcastRSNStatus();
    }
    if (updateSource != UpdateType.None) {
        if (argument.Equals(CHANNEL)) {
            FetchPacket(CHANNEL);
        }
        string[] parseArgs = argument.Split();
        if (parseArgs[0].ToUpper().Equals("SEND")) {
            if (parseArgs.Length < 2) return;
            IGC.SendBroadcastMessage(parseArgs[1], parseArgs.Length > 2?parseArgs[2]:"TEST", TransmissionDistance.AntennaRelay);
            Echo("did a thing");
        }
    }
}

void BroadcastRSNStatus() {
    float uranium = 0f;
    int ammo = 0;
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(blocks, block => {
        if (!IsConnected(block)) return false;
        if (!block.HasInventory) return false;

        IMyInventory blockInventory = block.GetInventory(0);
        List<MyInventoryItem> items = new List<MyInventoryItem>();
        blockInventory.GetItems(items);
        foreach(MyInventoryItem item in items) {
            if (item.Type.ToString().Contains("Ingot/Uranium")) uranium += (float) item.Amount;
            if (item.Type.ToString().Contains("AmmoMagazine/NATO_25x184mm")) ammo += (int) item.Amount;
        }
        return true;
    });
    IGC.SendBroadcastMessage(CHANNEL,
            $"{ Me.CubeGrid.CustomName }:\nUranium: { uranium.ToString("n2") } kg\nAmmo: { ammo.ToString() }",
            TransmissionDistance.AntennaRelay);
}

void FetchPacket(string channel) {
    IMyBroadcastListener listener = IGC.RegisterBroadcastListener(CHANNEL);
    if (!listener.HasPendingMessage) return;
    MyIGCMessage packet = listener.AcceptMessage();
    Echo($"#{ packet.Tag } ({ packet.Source }): { packet.Data }");
}
